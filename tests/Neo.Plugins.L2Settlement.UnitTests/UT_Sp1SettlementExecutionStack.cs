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

    [TestMethod]
    public void CreateFromChainDirectory_BindsGenesisAndProverLayout()
    {
        using var harness = new StackHarness();
        var chainDir = Path.Combine(Path.GetTempPath(), "neo-n4-sp1-chain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            WriteSettlementConfig(chainDir, ProofType.Zk, chainId: 20260716);
            File.WriteAllText(Path.Combine(chainDir, L2GenesisManifest.RelativePath), $$"""
                {
                  "schemaVersion": 1,
                  "chainId": 20260716,
                  "initialStateRoot": "{{harness.InitialRoot}}"
                }
                """);

            var stack = Sp1SettlementExecutionStack.CreateFromChainDirectory(
                chainDir,
                harness.State,
                harness.ExecutablePath,
                harness.ExecutableSha256,
                Hash(0x55));

            Assert.AreEqual(20260716u, stack.Profile.ChainId);
            Assert.AreEqual(ProofType.Zk, stack.Profile.ProofType);
            Assert.AreEqual(harness.InitialRoot, stack.Profile.GenesisStateRoot);
            Assert.AreEqual(Hash(0x55), stack.Profile.VerificationKeyId);
            Assert.IsTrue(Directory.Exists(Path.Combine(
                chainDir, Sp1SettlementExecutionStack.RelativeExecutorScratchDir)));
            Assert.IsTrue(Directory.Exists(Path.Combine(
                chainDir, Sp1SettlementExecutionStack.RelativeProverQueueDir)));
            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(chainDir, Sp1SettlementExecutionStack.RelativeProverQueueDir)),
                Path.GetFullPath(stack.Prover.QueueDirectory));
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateFromChainDirectory_MultisigConfig_FailsClosed()
    {
        using var harness = new StackHarness();
        var chainDir = Path.Combine(Path.GetTempPath(), "neo-n4-sp1-ms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            WriteSettlementConfig(chainDir, ProofType.Multisig, chainId: 20260716);
            File.WriteAllText(Path.Combine(chainDir, L2GenesisManifest.RelativePath), $$"""
                { "initialStateRoot": "{{harness.InitialRoot}}" }
                """);
            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                Sp1SettlementExecutionStack.CreateFromChainDirectory(
                    chainDir,
                    harness.State,
                    harness.ExecutablePath,
                    harness.ExecutableSha256,
                    Hash(0x55)));
            StringAssert.Contains(ex.Message, "ProofType=Zk");
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateFromChainDirectory_MissingGenesis_FailsClosed()
    {
        using var harness = new StackHarness();
        var chainDir = Path.Combine(Path.GetTempPath(), "neo-n4-sp1-nog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            WriteSettlementConfig(chainDir, ProofType.Zk, chainId: 20260716);
            Assert.ThrowsExactly<FileNotFoundException>(() =>
                Sp1SettlementExecutionStack.CreateFromChainDirectory(
                    chainDir,
                    harness.State,
                    harness.ExecutablePath,
                    harness.ExecutableSha256,
                    Hash(0x55)));
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void OpenStateFromChainDirectory_OpensBootstrapLayout()
    {
        var chainDir = Path.Combine(Path.GetTempPath(), "neo-n4-sp1-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            var statePath = Path.Combine(chainDir, Sp1SettlementExecutionStack.RelativeStateDir);
            Directory.CreateDirectory(statePath);
            // Seed an empty RocksDB so OpenState can reopen it.
            using (var seed = new RocksDbKeyValueStore(statePath))
            {
                seed.Put("k"u8, "v"u8);
            }

            using var reopened = Sp1SettlementExecutionStack.OpenStateFromChainDirectory(chainDir);
            CollectionAssert.AreEqual("v"u8.ToArray(), reopened.Get("k"u8)!.ToArray());
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void OpenStateFromChainDirectory_MissingState_FailsClosed()
    {
        var chainDir = Path.Combine(Path.GetTempPath(), "neo-n4-sp1-nostate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            var ex = Assert.ThrowsExactly<DirectoryNotFoundException>(
                () => Sp1SettlementExecutionStack.OpenStateFromChainDirectory(chainDir));
            StringAssert.Contains(ex.Message, "bootstrap-genesis");
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateFromChainDirectory_WithOpenState_BindsEndToEnd()
    {
        using var harness = new StackHarness();
        var chainDir = Path.Combine(Path.GetTempPath(), "neo-n4-sp1-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            WriteSettlementConfig(chainDir, ProofType.Zk, chainId: 20260716);
            File.WriteAllText(Path.Combine(chainDir, L2GenesisManifest.RelativePath), $$"""
                { "initialStateRoot": "{{harness.InitialRoot}}" }
                """);
            var statePath = Path.Combine(chainDir, Sp1SettlementExecutionStack.RelativeStateDir);
            Directory.CreateDirectory(statePath);
            using (var seed = new RocksDbKeyValueStore(statePath))
            {
                seed.Put("pin"u8, "1"u8);
            }

            // OpenState after seed dispose; Create uses harness in-memory state (genesis bindings).
            using var opened = Sp1SettlementExecutionStack.OpenStateFromChainDirectory(chainDir);
            CollectionAssert.AreEqual("1"u8.ToArray(), opened.Get("pin"u8)!);

            var stack = Sp1SettlementExecutionStack.CreateFromChainDirectory(
                chainDir,
                harness.State,
                harness.ExecutablePath,
                harness.ExecutableSha256,
                Hash(0x66));
            Assert.AreEqual(harness.InitialRoot, stack.Profile.GenesisStateRoot);
            Assert.AreEqual(20260716u, stack.Profile.ChainId);
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    private static void WriteSettlementConfig(string chainDir, ProofType proofType, uint chainId)
    {
        var dir = Path.Combine(chainDir, "Plugins", "Neo.Plugins.L2Settlement");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.json"), $$"""
            {
              "PluginConfiguration": {
                "ChainId": {{chainId}},
                "L1RpcEndpoint": "http://127.0.0.1:10332",
                "ExpectedNetwork": 860833102,
                "SettlementManagerHash": "0x1111111111111111111111111111111111111111",
                "ForcedInclusionHash": "0x2222222222222222222222222222222222222222",
                "ProofType": {{(byte)proofType}},
                "Enabled": false
              }
            }
            """);
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
