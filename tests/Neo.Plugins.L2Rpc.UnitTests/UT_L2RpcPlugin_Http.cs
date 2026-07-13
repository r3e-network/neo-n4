using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Neo.Cryptography.ECC;
using Neo.Json;
using Neo.Plugins;
using Neo.Plugins.RpcServer;

namespace Neo.Plugins.L2Rpc.UnitTests;

/// <summary>
/// Real HTTP coverage for the L2 adapter and plugin lifecycle. The test starts the
/// official Kestrel-backed <see cref="RpcServerPlugin"/> and never calls an adapter
/// method directly.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class UT_L2RpcPlugin_Http
{
    private const uint Network = 0x4E34_5250;
    private const uint ChainId = 1001;

    [TestMethod]
    public async Task PluginLifecycle_RealHttp_ExposesAllTenMethodsAndFailsClosed()
    {
        RuntimeHelpers.RunClassConstructor(typeof(NeoSystem).TypeHandle);
        DisposeAndClearAutoLoadedPlugins();

        var port = ReserveLoopbackPort();
        var configPath = WriteRpcServerConfiguration(port);
        var rpcPlugin = new RpcServerPlugin();
        var plugin = new TestableL2RpcPlugin();
        NeoSystem? system = null;

        try
        {
            system = new NeoSystem(CreateProtocolSettings());
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };

            Assert.IsFalse(plugin.IsRegistered(Network));

            var unavailable = await CallAsync(client, "getsecuritylevel", [ChainId]);
            AssertRpcError(unavailable, RpcError.MethodNotFound.Code);

            using var store = CreatePopulatedStore();
            system.AddService(store);

            Assert.IsTrue(plugin.IsRegistered(Network));

            plugin.Load(system);

            await AssertAllTenMethodsAsync(client);
            await AssertErrorBoundariesAsync(client);

            using var replacement = new InMemoryL2RpcStore(2002, SecurityLevel.Sidechain);
            system.AddService(replacement);

            var originalStillAuthoritative = await CallAsync(client, "getsecuritylevel", [ChainId]);
            Assert.IsNull(originalStillAuthoritative["error"]);

            plugin.Dispose();
            plugin.Dispose();
            var disposed = await CallAsync(client, "getsecuritylevel", [ChainId]);
            Assert.IsNotNull(disposed["error"], "disposed registrations must fail closed instead of serving stale data");
        }
        finally
        {
            system?.Dispose();
            plugin.Dispose();
            rpcPlugin.Dispose();
            Plugin.Plugins.Remove(plugin);
            Plugin.Plugins.Remove(rpcPlugin);
            DisposeAndClearAutoLoadedPlugins();
            DeleteRpcServerConfiguration(configPath);
        }
    }

    [TestMethod]
    public void PluginLifecycle_DifferentSystemSameNetwork_IsRejectedDeterministically()
    {
        RuntimeHelpers.RunClassConstructor(typeof(NeoSystem).TypeHandle);
        DisposeAndClearAutoLoadedPlugins();

        using var first = new NeoSystem(CreateProtocolSettings());
        using var second = new NeoSystem(CreateProtocolSettings());
        using var store = new InMemoryL2RpcStore(ChainId, SecurityLevel.Validium);
        first.AddService(store);
        var registrar = new RecordingRegistrar();
        var plugin = new TestableL2RpcPlugin(registrar);

        try
        {
            plugin.Load(first);
            Assert.ThrowsExactly<InvalidOperationException>(() => plugin.Load(second));
            CollectionAssert.AreEqual(new[] { Network }, registrar.Networks);
        }
        finally
        {
            plugin.Dispose();
            Plugin.Plugins.Remove(plugin);
            DisposeAndClearAutoLoadedPlugins();
        }
    }

    private static InMemoryL2RpcStore CreatePopulatedStore()
    {
        var store = new InMemoryL2RpcStore(ChainId, SecurityLevel.Validium)
        {
            DAMode = DAMode.NeoFS,
            GatewayEnabled = true,
            Sequencer = SequencerModel.DbftCommittee,
            Exit = ExitModel.Permissionless,
        };

        var first = SampleBatch(1);
        var latest = SampleBatch(2) with
        {
            PostStateRoot = UInt256.Parse("0x" + new string('a', 64)),
        };
        store.AddBatch(first, BatchStatus.Challengeable);
        store.AddBatch(latest, BatchStatus.Pending);
        store.Finalize(2);

        var withdrawal = UInt256.Parse("0x" + new string('b', 64));
        var message = UInt256.Parse("0x" + new string('c', 64));
        store.RecordWithdrawalProof(withdrawal, [0xDE, 0xAD]);
        store.RecordMessageProof(message, [0xBE, 0xEF]);
        store.RecordDeposit(new DepositStatus(77, 9, true, 2));

        var l1Asset = UInt160.Parse("0x" + new string('1', 40));
        var l2Asset = UInt160.Parse("0x" + new string('2', 40));
        store.RegisterAsset(l1Asset, l2Asset);
        return store;
    }

    private static async Task AssertAllTenMethodsAsync(HttpClient client)
    {
        var batch = await CallAsync(client, "getl2batch", [ChainId, 1UL]);
        Assert.AreEqual(1UL, (ulong)batch["result"]!["batchNumber"]!.AsNumber());

        var status = await CallAsync(client, "getl2batchstatus", [ChainId, 1UL]);
        Assert.AreEqual("Challengeable", status["result"]!["statusName"]!.AsString());

        var stateRoot = await CallAsync(client, "getl2stateroot", [ChainId]);
        Assert.AreEqual("0x" + new string('a', 64), stateRoot["result"]!.AsString());

        var withdrawal = await CallAsync(client, "getl2withdrawalproof",
            [ChainId, "0x" + new string('b', 64)]);
        Assert.AreEqual("DEAD", withdrawal["result"]!.AsString());

        var message = await CallAsync(client, "getl2messageproof",
            [ChainId, "0x" + new string('c', 64)]);
        Assert.AreEqual("BEEF", message["result"]!.AsString());

        var deposit = await CallAsync(client, "getl1depositstatus", [77U, 9UL]);
        Assert.IsTrue(deposit["result"]!["consumedOnL2"]!.AsBoolean());

        var canonical = await CallAsync(client, "getcanonicalasset", ["0x" + new string('2', 40)]);
        Assert.AreEqual("0x" + new string('1', 40), canonical["result"]!.AsString());

        var bridged = await CallAsync(client, "getbridgedasset", ["0x" + new string('1', 40)]);
        Assert.AreEqual("0x" + new string('2', 40), bridged["result"]!.AsString());

        var level = await CallAsync(client, "getsecuritylevel", [ChainId]);
        Assert.AreEqual("Validium", level["result"]!["levelName"]!.AsString());

        var label = await CallAsync(client, "getsecuritylabel", [ChainId]);
        Assert.IsTrue(label["result"]!["gatewayEnabled"]!.AsBoolean());
        Assert.AreEqual("NeoFS", label["result"]!["daModeName"]!.AsString());
    }

    private static async Task AssertErrorBoundariesAsync(HttpClient client)
    {
        var nullBatch = await CallAsync(client, "getl2batch", [ChainId, 999UL]);
        Assert.IsNull(nullBatch["error"]);
        Assert.AreEqual(JToken.Null, nullBatch["result"]);

        var chainMismatch = await CallAsync(client, "getl2batchstatus", [9999U, 1UL]);
        Assert.IsNotNull(chainMismatch["error"]);

        var missing = await CallAsync(client, "getl2batch", [ChainId]);
        Assert.IsNotNull(missing["error"]);

        var invalidHash = await CallAsync(client, "getl2messageproof", [ChainId, "not-a-hash"]);
        Assert.IsNotNull(invalidHash["error"]);
    }

    private static async Task<JObject> CallAsync(HttpClient client, string method, object?[] parameters)
    {
        var body = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method,
            @params = parameters,
        });
        using var response = await client.PostAsync("", new StringContent(body, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JToken.Parse(json) as JObject
            ?? throw new InvalidDataException($"RPC response was not a JSON object: {json}");
    }

    private static void AssertRpcError(JObject response, int expectedCode)
    {
        Assert.IsNotNull(response["error"]);
        Assert.AreEqual(expectedCode, (int)response["error"]!["code"]!.AsNumber());
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string WriteRpcServerConfiguration(int port)
    {
        var directory = Path.Combine(Plugin.PluginsDirectory, "RpcServer");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "RpcServer.json");
        File.WriteAllText(path, $$"""
            {
              "PluginConfiguration": {
                "UnhandledExceptionPolicy": "Ignore",
                "Servers": [
                  {
                    "BindAddress": "127.0.0.1",
                    "Port": {{port}},
                    "EnableCors": false
                  }
                ]
              }
            }
            """);
        return path;
    }

    private static void DeleteRpcServerConfiguration(string path)
    {
        try
        {
            File.Delete(path);
            var directory = Path.GetDirectoryName(path)!;
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch
        {
        }
    }

    private static ProtocolSettings CreateProtocolSettings() => ProtocolSettings.Default with
    {
        Network = Network,
        StandbyCommittee =
        [
            ECPoint.Parse(
                "0278ed78c917797b637a7ed6e7a9d94e8c408444c41ee4c0a0f310a256b9271eda",
                ECCurve.Secp256r1),
        ],
        ValidatorsCount = 1,
        SeedList = [],
    };

    private static void DisposeAndClearAutoLoadedPlugins()
    {
        foreach (var loaded in Plugin.Plugins.ToArray())
        {
            try { loaded.Dispose(); }
            catch { }
        }
        Plugin.Plugins.Clear();
    }

    private static L2BatchCommitment SampleBatch(ulong batchNumber) => new()
    {
        ChainId = ChainId,
        BatchNumber = batchNumber,
        FirstBlock = 100,
        LastBlock = 200,
        PreStateRoot = UInt256.Parse("0x" + new string('1', 64)),
        PostStateRoot = UInt256.Parse("0x" + new string('2', 64)),
        TxRoot = UInt256.Parse("0x" + new string('3', 64)),
        ReceiptRoot = UInt256.Parse("0x" + new string('4', 64)),
        WithdrawalRoot = UInt256.Parse("0x" + new string('5', 64)),
        L2ToL1MessageRoot = UInt256.Parse("0x" + new string('6', 64)),
        L2ToL2MessageRoot = UInt256.Parse("0x" + new string('7', 64)),
        DACommitment = UInt256.Parse("0x" + new string('8', 64)),
        PublicInputHash = UInt256.Parse("0x" + new string('9', 64)),
        ProofType = ProofType.Multisig,
        Proof = new byte[] { 0xAA },
    };

    private sealed class RecordingRegistrar : IL2RpcMethodRegistrar
    {
        internal List<uint> Networks { get; } = [];

        public void RegisterMethods(object handler, uint network) => Networks.Add(network);
    }

    private sealed class TestableL2RpcPlugin : L2RpcPlugin
    {
        internal TestableL2RpcPlugin() { }

        internal TestableL2RpcPlugin(IL2RpcMethodRegistrar registrar) : base(registrar) { }

        internal void Load(NeoSystem system) => OnSystemLoaded(system);
    }
}
