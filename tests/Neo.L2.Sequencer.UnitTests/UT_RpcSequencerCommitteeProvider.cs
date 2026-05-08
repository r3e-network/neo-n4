using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Neo.Cryptography.ECC;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.Sequencer.UnitTests;

[TestClass]
public class UT_RpcSequencerCommitteeProvider
{
    private const uint TestChainId = 4242;
    private static readonly UInt160 RegistryHash = UInt160.Parse("0x" + new string('a', 40));
    private const string Endpoint = "http://l1.example:30332";

    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (ECCurve.Secp256r1.G * priv, priv);
    }

    private static (RpcSequencerCommitteeProvider provider, StubRpcHandler stub, JsonRpcClient rpc) BuildProvider(
        params ECPoint[] genesisKeys)
    {
        var stub = new StubRpcHandler();
        var http = new HttpClient(stub);
        var rpc = new JsonRpcClient(new Uri(Endpoint), http);
        var provider = new RpcSequencerCommitteeProvider(
            rpc, RegistryHash, TestChainId, genesisKeys, cacheTtl: TimeSpan.Zero);
        return (provider, stub, rpc);
    }

    [TestMethod]
    public async Task GetActiveCommittee_RoundTripsAcrossThreeKnownKeys()
    {
        var k1 = GenKey(1).pub;
        var k2 = GenKey(2).pub;
        var k3 = GenKey(3).pub;
        var addr = UInt160.Parse("0x" + new string('1', 40));

        var (provider, stub, rpc) = BuildProvider(k1, k2, k3);
        using var _ = rpc;
        // All three keys report status=1 (Active) + the same address.
        stub.Register((m, h, p) => m switch
        {
            "getStatus" => StubRpcHandler.Integer(1),
            "getSequencerAddress" => StubRpcHandler.ByteArrayBase64(addr.GetSpan().ToArray()),
            _ => null,
        });

        var members = await provider.GetActiveCommitteeAsync();

        Assert.AreEqual(3, members.Count);
        var pubs = members.Select(m => m.PublicKey).ToHashSet();
        Assert.IsTrue(pubs.Contains(k1));
        Assert.IsTrue(pubs.Contains(k2));
        Assert.IsTrue(pubs.Contains(k3));
        Assert.IsTrue(members.All(m => m.Status == 1));
        Assert.IsTrue(members.All(m => m.L1Address == addr));
    }

    [TestMethod]
    public async Task GetActiveCommittee_DropsKeysWithStatusZero_WithoutFailingTheCall()
    {
        // A key the operator knew about but L1 says is unregistered (status=0) must
        // be silently dropped from the committee — no exception, no NRE downstream.
        var k1 = GenKey(10).pub;
        var k2 = GenKey(20).pub;
        var addr = UInt160.Parse("0x" + new string('2', 40));

        var (provider, stub, rpc) = BuildProvider(k1, k2);
        using var _ = rpc;
        stub.Register((m, h, p) =>
        {
            if (m == "getSequencerAddress") return StubRpcHandler.ByteArrayBase64(addr.GetSpan().ToArray());
            if (m == "getStatus")
            {
                // Decode pubkey from contractArgs[1] to decide the answer per-key.
                var pkB64 = p[1]!["value"]!.AsString();
                var pkBytes = Convert.FromBase64String(pkB64);
                // Match k1's first byte (k1's pubkey starts with a known compressed prefix).
                // Simpler: inspect the second byte of the encoded pubkey since that varies by seed.
                return pkBytes[1] == k1.EncodePoint(true)[1]
                    ? StubRpcHandler.Integer(1)  // k1 is Active
                    : StubRpcHandler.Integer(0); // k2 is unregistered
            }
            return null;
        });

        var members = await provider.GetActiveCommitteeAsync();
        Assert.AreEqual(1, members.Count);
        Assert.AreEqual(k1, members[0].PublicKey);
    }

    [TestMethod]
    public async Task RegisterKnownKey_AddsToFutureCommittees_AndInvalidatesCache()
    {
        var k1 = GenKey(40).pub;
        var k2 = GenKey(50).pub;
        var addr = UInt160.Parse("0x" + new string('3', 40));

        var stub = new StubRpcHandler();
        var http = new HttpClient(stub);
        var rpc = new JsonRpcClient(new Uri(Endpoint), http);
        // Use a positive cache TTL so we can detect cache invalidation.
        var provider = new RpcSequencerCommitteeProvider(
            rpc, RegistryHash, TestChainId, new[] { k1 },
            cacheTtl: TimeSpan.FromMinutes(10));
        using var _ = rpc;

        stub.Register((m, h, p) => m switch
        {
            "getStatus" => StubRpcHandler.Integer(1),
            "getSequencerAddress" => StubRpcHandler.ByteArrayBase64(addr.GetSpan().ToArray()),
            _ => null,
        });

        var first = await provider.GetActiveCommitteeAsync();
        Assert.AreEqual(1, first.Count);

        // RegisterKnownKey must invalidate the cache so the next call sees the new key
        // (otherwise event-driven additions would be ignored until the TTL expires).
        Assert.IsTrue(provider.RegisterKnownKey(k2));
        Assert.IsFalse(provider.RegisterKnownKey(k2), "second call returns false (already known)");

        var second = await provider.GetActiveCommitteeAsync();
        Assert.AreEqual(2, second.Count);
    }

    [TestMethod]
    public async Task IsRegistered_AlwaysHitsL1_DoesNotShortcutFromKnownKeysSet()
    {
        // Pin: operator queries `IsRegistered` and the L1 contract is the source of truth,
        // not the local known-keys set. Otherwise an operator who hasn't yet wired the
        // event-watcher would silently mis-report registration.
        var k = GenKey(99).pub;
        // Construct provider WITHOUT k in the genesis set — yet L1 reports it registered.
        var (provider, stub, rpc) = BuildProvider();
        using var _ = rpc;
        stub.Register((m, h, p) => m switch
        {
            "isRegistered" => StubRpcHandler.Boolean(true),
            _ => null,
        });
        Assert.IsTrue(await provider.IsRegisteredAsync(k));
    }

    [TestMethod]
    public async Task GetMaxCommitteeSize_RpcRoundTrip_AndCachedAcrossCalls()
    {
        var stub = new StubRpcHandler();
        var http = new HttpClient(stub);
        var rpc = new JsonRpcClient(new Uri(Endpoint), http);
        // Long cache so we can pin the call-count behavior.
        var provider = new RpcSequencerCommitteeProvider(
            rpc, RegistryHash, TestChainId, Array.Empty<ECPoint>(),
            cacheTtl: TimeSpan.FromMinutes(10));
        using var _ = rpc;

        stub.Register((m, h, p) => m == "getMaxCommitteeSize" ? StubRpcHandler.Integer(21) : null);

        var first = await provider.GetMaxCommitteeSizeAsync();
        var second = await provider.GetMaxCommitteeSizeAsync();
        Assert.AreEqual(21, first);
        Assert.AreEqual(21, second);
        Assert.AreEqual(1, stub.Captured.Count, "second call must be served from cache");
    }

    [TestMethod]
    public async Task InvalidateCache_ForcesNextCallToRefetch()
    {
        var k = GenKey(60).pub;
        var addr = UInt160.Parse("0x" + new string('4', 40));
        var stub = new StubRpcHandler();
        var http = new HttpClient(stub);
        var rpc = new JsonRpcClient(new Uri(Endpoint), http);
        var provider = new RpcSequencerCommitteeProvider(
            rpc, RegistryHash, TestChainId, new[] { k },
            cacheTtl: TimeSpan.FromMinutes(10));
        using var _ = rpc;
        stub.Register((m, h, p) => m switch
        {
            "getStatus" => StubRpcHandler.Integer(1),
            "getSequencerAddress" => StubRpcHandler.ByteArrayBase64(addr.GetSpan().ToArray()),
            _ => null,
        });

        await provider.GetActiveCommitteeAsync();
        var firstFanoutCount = stub.Captured.Count;
        await provider.GetActiveCommitteeAsync();
        Assert.AreEqual(firstFanoutCount, stub.Captured.Count, "cache should serve the second call");

        provider.InvalidateCache();
        await provider.GetActiveCommitteeAsync();
        Assert.IsTrue(stub.Captured.Count > firstFanoutCount, "InvalidateCache must trigger fresh fanout");
    }
}
