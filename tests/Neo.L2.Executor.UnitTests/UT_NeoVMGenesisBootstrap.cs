using System;
using System.Threading.Tasks;
using Neo;
using Neo.Extensions.IO;
using Neo.L2;
using Neo.L2.Executor;
using Neo.L2.Persistence;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.Executor.UnitTests;

[TestClass]
public class UT_NeoVMGenesisBootstrap
{
    private static readonly BatchBlockContext Ctx = new()
    {
        L1FinalizedHeight = 1,
        FirstBlockTimestamp = 1_700_000_000_000UL,
        LastBlockTimestamp = 1_700_000_005_000UL,
        SequencerCommitteeHash = UInt256.Zero,
        Network = 0x4F454E,
    };

    [TestMethod]
    public void Run_PopulatesNativeContractStorage()
    {
        // PHASE C0 SUCCESS: Run() actually writes native-contract state to the
        // underlying KV store. The earlier "cache propagation gap" was actually
        // an IsInitialized false-positive that short-circuited Run() before
        // bootstrap; the fix probes the storage directly instead of via a
        // gas=0 ApplicationEngine.Create.
        using var store = new InMemoryKeyValueStore();
        Assert.AreEqual(0L, store.Count, "store starts empty");
        NeoVMGenesisBootstrap.Run(store);
        Assert.IsTrue(store.Count > 0,
            $"after bootstrap, store must have native-contract state (count={store.Count})");
        Assert.IsTrue(NeoVMGenesisBootstrap.IsInitialized(store),
            "IsInitialized must return true after a successful Run");
    }

    [TestMethod]
    public async Task BootstrappedStore_RunsRealNeoVMScript_HALT()
    {
        // Phase C0 SUCCESS: after genesis bootstrap, ApplicationEngineTransactionExecutor
        // runs a real PUSH1 script through Neo VM and gets HALT + Success receipt.
        // This is the smoking-gun proof that the entire pipeline works:
        //   IL2KeyValueStore → L2DataCacheAdapter → ApplicationEngine → Receipt
        using var store = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(store);

        // Executor MUST use the same ProtocolSettings as bootstrap — Network and
        // StandbyCommittee both affect native-contract storage keying.
        var executor = new ApplicationEngineTransactionExecutor(
            store, settings: NeoVMGenesisBootstrap.DefaultBootstrapSettings);
        var script = new byte[] { (byte)OpCode.PUSH1 };
        var tx = new Transaction
        {
            Version = 0, Nonce = 1, SystemFee = 0, NetworkFee = 0, ValidUntilBlock = 100,
            Script = script,
            Signers = new[] { new Signer { Account = UInt160.Zero, Scopes = WitnessScope.None } },
            Attributes = Array.Empty<TransactionAttribute>(),
            Witnesses = new[] { new Witness { InvocationScript = ReadOnlyMemory<byte>.Empty, VerificationScript = ReadOnlyMemory<byte>.Empty } },
        };
        var serialized = tx.ToArray();

        var result = await executor.ExecuteAsync(serialized, Ctx);
        Assert.IsTrue(result.Receipt.Success,
            $"PUSH1 must HALT against bootstrapped state — got Success={result.Receipt.Success}");
        Assert.IsTrue(result.Receipt.GasConsumed > 0, "HALT execution consumed gas");
    }

    [TestMethod]
    public void Run_Idempotent_DoesntDoubleInit()
    {
        // A second Run call must short-circuit via IsInitialized rather than corrupting
        // state. Pin so a refactor that drops the IsInitialized guard surfaces here.
        using var store = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(store);
        var keysAfterFirst = store.Count;

        NeoVMGenesisBootstrap.Run(store);
        Assert.AreEqual(keysAfterFirst, store.Count, "second Run must not add additional state");
    }

    [TestMethod]
    public void Run_NullStore_Rejects()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => NeoVMGenesisBootstrap.Run(null!));
    }
}
