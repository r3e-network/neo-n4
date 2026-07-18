using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Neo;
using Neo.Cryptography;
using Neo.L2.ForcedInclusion;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.ForcedInclusion.UnitTests;

[TestClass]
public class UT_RpcForcedInclusionSource
{
    private const uint TestChainId = 4242;
    private static readonly UInt160 RegistryHash = UInt160.Parse("0x" + new string('a', 40));
    private const string Endpoint = "http://l1.example:30332";

    private static byte[] EncodeEntry(UInt160 sender, UInt256 txHash, byte[] tx, uint deadline)
    {
        var buf = new byte[20 + 32 + 4 + tx.Length + 4];
        var span = buf.AsSpan();
        sender.GetSpan().CopyTo(span.Slice(0, 20));
        txHash.GetSpan().CopyTo(span.Slice(20, 32));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(52, 4), tx.Length);
        tx.CopyTo(span.Slice(56, tx.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(56 + tx.Length, 4), deadline);
        return buf;
    }

    private static UInt256 TxHash(byte[] transaction)
        => new(Crypto.Hash256(transaction));

    private static (RpcForcedInclusionSource src, StubRpcHandler stub, JsonRpcClient rpc) Build(
        TimeSpan? cacheTtl = null,
        params ulong[] genesisNonces)
    {
        var stub = new StubRpcHandler();
        var http = new HttpClient(stub);
        var rpc = new JsonRpcClient(new Uri(Endpoint), http);
        var src = new RpcForcedInclusionSource(
            rpc, RegistryHash, TestChainId, genesisNonces,
            cacheTtl: cacheTtl ?? TimeSpan.Zero);
        return (src, stub, rpc);
    }

    [TestMethod]
    public async Task Drain_RoundTripsTwoNonces_DeadlineOrdered()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var transaction1 = new byte[] { 0xAA };
        var transaction2 = new byte[] { 0xBB };
        var entry1 = EncodeEntry(sender, TxHash(transaction1), transaction1, deadline: 200);
        var entry2 = EncodeEntry(sender, TxHash(transaction2), transaction2, deadline: 100); // earlier deadline

        var (src, stub, rpc) = Build(genesisNonces: new ulong[] { 1, 2 });
        using var _ = rpc;

        stub.Register((m, h, p) =>
        {
            if (m == "isConsumed") return StubRpcHandler.Boolean(false);
            if (m == "getEntry")
            {
                var nonce = ulong.Parse(p[1]!["value"]!.AsString());
                return StubRpcHandler.ByteArrayBase64(nonce == 1 ? entry1 : entry2);
            }
            return null;
        });

        var drained = await src.DrainAsync(max: 10);
        Assert.AreEqual(2, drained.Count);
        // deadline 100 (nonce 2) sorts before deadline 200 (nonce 1)
        Assert.AreEqual(2UL, drained[0].Nonce);
        Assert.AreEqual(100u, drained[0].DeadlineUnixSeconds);
        Assert.AreEqual(1UL, drained[1].Nonce);
        Assert.AreEqual(200u, drained[1].DeadlineUnixSeconds);
    }

    [TestMethod]
    public async Task Drain_DropsEntriesL1HasMarkedConsumed()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var transaction = new byte[] { 0x01 };
        var entry = EncodeEntry(sender, TxHash(transaction), transaction, deadline: 100);

        var (src, stub, rpc) = Build(genesisNonces: new ulong[] { 7 });
        using var _ = rpc;

        stub.Register((m, h, p) =>
        {
            if (m == "isConsumed") return StubRpcHandler.Boolean(true);  // L1 already consumed
            if (m == "getEntry") return StubRpcHandler.ByteArrayBase64(entry);
            return null;
        });

        var drained = await src.DrainAsync(max: 10);
        Assert.AreEqual(0, drained.Count);
    }

    [TestMethod]
    public async Task Drain_RespectsMaxCap()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var (src, stub, rpc) = Build(genesisNonces: new ulong[] { 1, 2, 3, 4, 5 });
        using var _ = rpc;
        stub.Register((m, h, p) =>
        {
            if (m == "isConsumed") return StubRpcHandler.Boolean(false);
            if (m == "getEntry")
            {
                var nonce = ulong.Parse(p[1]!["value"]!.AsString());
                var transaction = new byte[] { (byte)nonce };
                return StubRpcHandler.ByteArrayBase64(EncodeEntry(sender, TxHash(transaction), transaction, (uint)(100 + nonce)));
            }
            return null;
        });
        var drained = await src.DrainAsync(max: 3);
        Assert.AreEqual(3, drained.Count);
    }

    [TestMethod]
    public async Task ConfirmConsumed_RequiresL1ConfirmationAndExcludesNonce()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var (src, stub, rpc) = Build(genesisNonces: new ulong[] { 1, 2 });
        using var _ = rpc;
        var consumed = false;
        stub.Register((m, h, p) =>
        {
            if (m == "isConsumed")
            {
                var nonce = ulong.Parse(p[1]!["value"]!.AsString());
                return StubRpcHandler.Boolean(consumed && nonce == 1);
            }
            if (m == "getEntry")
            {
                var nonce = ulong.Parse(p[1]!["value"]!.AsString());
                var transaction = new byte[] { (byte)nonce };
                return StubRpcHandler.ByteArrayBase64(EncodeEntry(sender, TxHash(transaction), transaction, (uint)(100 + nonce)));
            }
            return null;
        });

        var first = await src.DrainAsync(max: 10);
        Assert.AreEqual(2, first.Count);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await src.ConfirmConsumedAsync(1));
        consumed = true;
        await src.ConfirmConsumedAsync(1);
        var second = await src.DrainAsync(max: 10);
        Assert.AreEqual(1, second.Count);
        Assert.AreEqual(2UL, second[0].Nonce);
    }

    [TestMethod]
    public async Task RegisterNonce_AddsToDrain_AndInvalidatesCache()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var (src, stub, rpc) = Build(cacheTtl: TimeSpan.FromMinutes(10), genesisNonces: new ulong[] { 1 });
        using var _ = rpc;
        stub.Register((m, h, p) =>
        {
            if (m == "isConsumed") return StubRpcHandler.Boolean(false);
            if (m == "getEntry")
            {
                var nonce = ulong.Parse(p[1]!["value"]!.AsString());
                var transaction = new byte[] { (byte)nonce };
                return StubRpcHandler.ByteArrayBase64(EncodeEntry(sender, TxHash(transaction), transaction, (uint)(100 + nonce)));
            }
            return null;
        });

        var first = await src.DrainAsync(max: 10);
        Assert.AreEqual(1, first.Count);

        Assert.IsTrue(src.RegisterNonce(2));
        Assert.IsFalse(src.RegisterNonce(2)); // already known
        Assert.IsTrue(src.KnownNonceCount >= 1);

        var second = await src.DrainAsync(max: 10);
        Assert.AreEqual(2, second.Count);
    }

    [TestMethod]
    public async Task HasOverdueEntry_DetectsDeadlinePastNow()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var (src, stub, rpc) = Build(genesisNonces: new ulong[] { 1 });
        using var _ = rpc;
        stub.Register((m, h, p) =>
        {
            if (m == "isConsumed") return StubRpcHandler.Boolean(false);
            if (m == "getEntry")
            {
                var transaction = new byte[] { 0x01 };
                return StubRpcHandler.ByteArrayBase64(EncodeEntry(sender, TxHash(transaction), transaction, deadline: 100));
            }
            return null;
        });

        Assert.IsFalse(await src.HasOverdueEntryAsync(nowUnixSeconds: 50), "deadline ahead of now is not overdue");
        // deadline <= now is overdue, matching InMemoryForcedInclusionSource, CensorshipDetector,
        // and the on-chain ReportCensorship boundary (now == deadline counts as overdue).
        Assert.IsTrue(await src.HasOverdueEntryAsync(nowUnixSeconds: 100), "deadline at exactly now is overdue");
        Assert.IsTrue(await src.HasOverdueEntryAsync(nowUnixSeconds: 101), "deadline before now is overdue");
        // Soft cache reuses the drain snapshot without another L1 scan.
        Assert.IsTrue(src.HasOverdueCachedEntry(nowUnixSeconds: 100));
        Assert.IsFalse(src.HasOverdueCachedEntry(nowUnixSeconds: 50));
    }

    [TestMethod]
    public void HasOverdueCachedEntry_FalseWhenCacheEmpty()
    {
        var (src, _, rpc) = Build(genesisNonces: Array.Empty<ulong>());
        using var _ = rpc;
        Assert.IsFalse(src.HasOverdueCachedEntry(nowUnixSeconds: 1_000_000));
    }

    [TestMethod]
    public void DecodeEntry_RejectsTruncated()
    {
        var truncated = new byte[40]; // less than 60-byte header+trailer
        Assert.ThrowsExactly<InvalidDataException>(() => RpcForcedInclusionSource.DecodeEntry(0, truncated));
    }

    [TestMethod]
    public void DecodeEntry_RejectsTxLenInconsistentWithLength()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var transaction = new byte[] { 0xAA, 0xBB };
        var bytes = EncodeEntry(sender, TxHash(transaction), transaction, deadline: 200);
        // Tamper: write a wrong txLen so total length disagrees.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52, 4), 99);
        Assert.ThrowsExactly<InvalidDataException>(() => RpcForcedInclusionSource.DecodeEntry(0, bytes));
    }

    [TestMethod]
    public void DecodeEntry_RoundTripsKnownEncoding()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var tx = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var hash = TxHash(tx);
        var encoded = EncodeEntry(sender, hash, tx, deadline: 12345);

        var decoded = RpcForcedInclusionSource.DecodeEntry(nonce: 7, encoded);

        Assert.AreEqual(7UL, decoded.Nonce);
        Assert.AreEqual(sender, decoded.Sender);
        Assert.AreEqual(hash, decoded.TxHash);
        CollectionAssert.AreEqual(tx, decoded.SerializedTx.ToArray());
        Assert.AreEqual(12345u, decoded.DeadlineUnixSeconds);
    }

    [TestMethod]
    public void DecodeEntry_RejectsEncodedTransactionHashMismatch()
    {
        var sender = UInt160.Parse("0x" + new string('1', 40));
        var encoded = EncodeEntry(
            sender,
            UInt256.Zero,
            new byte[] { 0x01 },
            deadline: 12345);

        Assert.ThrowsExactly<InvalidDataException>(
            () => RpcForcedInclusionSource.DecodeEntry(7, encoded));
    }

    [TestMethod]
    public void OpenFromChainDirectory_CreatesDurableStoreUnderLayout()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-fi-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var stub = new StubRpcHandler();
            var http = new HttpClient(stub);
            var rpc = new JsonRpcClient(new Uri(Endpoint), http);
            using var source = RpcForcedInclusionSource.OpenFromChainDirectory(
                dir, rpc, RegistryHash, TestChainId, startHeight: 10, ownsRpc: true);
            Assert.AreEqual(TestChainId, source.ChainId);
            Assert.IsTrue(Directory.Exists(Path.Combine(
                dir, NeoHubDeployReport.RelativeForcedInclusionEventStoreDir)));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void OpenFromChainDirectory_ZeroStartHeight_FailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-fi-zero-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var stub = new StubRpcHandler();
            using var http = new HttpClient(stub);
            using var rpc = new JsonRpcClient(new Uri(Endpoint), http);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                RpcForcedInclusionSource.OpenFromChainDirectory(
                    dir, rpc, RegistryHash, TestChainId, startHeight: 0));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
