using System.Security.Cryptography;
using Neo.L2.Batch;
using Neo.L2.Executor;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Persistence;
using Neo.L2.Proving.RiscVZk;

namespace Neo.Plugins.L2Settlement.UnitTests;

[TestClass]
public sealed class UT_Sp1SettlementExecutionStack
{
    [TestMethod]
    public void Create_BindsConcreteExecutorProverProfileAndGenesisRoot()
    {
        using var harness = new StackHarness();

        var stack = Sp1SettlementExecutionStack.Create(
            1099,
            harness.State,
            harness.InitialRoot,
            harness.ExecutablePath,
            harness.ExecutableSha256,
            harness.ScratchDirectory,
            harness.QueueDirectory,
            Hash(0x44));

        Assert.IsInstanceOfType<Sp1StatefulBatchExecutor>(stack.Executor);
        Assert.IsInstanceOfType<Sp1BatchProofProver>(stack.Prover);
        Assert.AreEqual(ProofType.Zk, stack.Profile.ProofType);
        Assert.AreEqual(WitnessProofSystem.Sp1, stack.Profile.ProofSystem);
        Assert.AreEqual(Hash(0x44), stack.Profile.VerificationKeyId);
        Assert.AreEqual(
            harness.InitialRoot,
            stack.Executor.GetInitialStateRootAsync().AsTask().GetAwaiter().GetResult());
    }

    [TestMethod]
    public async Task Create_DefersCurrentStateValidationToArtifactRecovery()
    {
        using var harness = new StackHarness();
        harness.State.Put("drift"u8, "value"u8);

        var stack = Sp1SettlementExecutionStack.Create(
            1099,
            harness.State,
            harness.InitialRoot,
            harness.ExecutablePath,
            harness.ExecutableSha256,
            harness.ScratchDirectory,
            harness.QueueDirectory,
            Hash(0x44));

        Assert.AreEqual(harness.InitialRoot, await stack.Executor.GetInitialStateRootAsync());
        Assert.AreNotEqual(harness.InitialRoot, await stack.Executor.GetCurrentStateRootAsync());
    }

    private sealed class StackHarness : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "neo-n4-sp1-stack-" + Guid.NewGuid().ToString("N"));

        public StackHarness()
        {
            Directory.CreateDirectory(_root);
            ScratchDirectory = Path.Combine(_root, "scratch");
            QueueDirectory = Path.Combine(_root, "queue");
            ExecutablePath = Path.Combine(_root, "neo-zkvm-executor");
            File.WriteAllBytes(ExecutablePath, [0x01]);
            ExecutableSha256 = SHA256.HashData(File.ReadAllBytes(ExecutablePath));
            NeoVMGenesisBootstrap.Run(State);
            InitialRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(State);
        }

        public InMemoryKeyValueStore State { get; } = new();

        public UInt256 InitialRoot { get; }

        public string ExecutablePath { get; }

        public byte[] ExecutableSha256 { get; }

        public string ScratchDirectory { get; }

        public string QueueDirectory { get; }

        public void Dispose()
        {
            State.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private static UInt256 Hash(byte value)
    {
        var bytes = new byte[UInt256.Length];
        bytes[0] = value;
        return new UInt256(bytes);
    }
}
