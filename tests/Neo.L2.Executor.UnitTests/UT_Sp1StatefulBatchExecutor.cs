using Neo.Cryptography;
using Neo.Extensions.IO;
using System.Security.Cryptography;
using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Persistence;
using Neo.L2.State;
using Neo.Network.P2P.Payloads;
using Neo.VM;

namespace Neo.L2.Executor.UnitTests;

[TestClass]
public sealed class UT_Sp1StatefulBatchExecutor
{
    [TestMethod]
    public async Task ApplyBatchWithWitness_ValidatesNativeTransitionWithoutCommittingState()
    {
        using var harness = new ExecutorHarness();
        var process = new WritingProcess();
        var executor = harness.CreateExecutor(process);
        var batch = harness.Batch();

        var result = await executor.ApplyBatchWithWitnessAsync(batch);

        Assert.IsTrue(result.WitnessAuthenticated);
        Assert.AreEqual(ExecutionSemanticIds.Sp1StatefulNeoVmV1, result.ExecutionSemanticId);
        Assert.AreEqual(harness.InitialRoot, result.ExecutionResult.PostStateRoot);
        Assert.AreEqual(
            harness.InitialRoot,
            await executor.GetInitialStateRootAsync());
        Assert.AreEqual(harness.InitialRoot, harness.Source.CaptureCurrent().StateRoot);
        CollectionAssert.AreEqual(
            harness.Source.Capture(harness.InitialRoot).Witness.ToArray(),
            result.StateWitness.ToArray());
        Assert.AreEqual(1, process.RunCount);
        Assert.IsNotNull(process.LastRequest);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(harness.ScratchDirectory).Any());
    }

    [TestMethod]
    public async Task EnsureStateCommitted_ReplaysDurableArtifactOnceAndIsIdempotent()
    {
        using var harness = new ExecutorHarness();
        var process = new WritingProcess(AddStateEntry);
        var executor = harness.CreateExecutor(process);
        var batch = harness.Batch();

        var execution = await executor.ApplyBatchWithWitnessAsync(batch);

        Assert.AreNotEqual(harness.InitialRoot, execution.ExecutionResult.PostStateRoot);
        Assert.AreEqual(harness.InitialRoot, harness.Source.CaptureCurrent().StateRoot);
        var artifact = BuildArtifact(batch, execution);
        using var artifactBackend = new InMemoryKeyValueStore();
        using var artifactStore = new KeyValueProofWitnessStore(artifactBackend);
        await artifactStore.CommitAsync(artifact);

        await executor.EnsureStateCommittedAsync(artifactStore, artifact);

        Assert.AreEqual(
            execution.ExecutionResult.PostStateRoot,
            harness.Source.CaptureCurrent().StateRoot);
        Assert.AreEqual(2, process.RunCount);

        await executor.EnsureStateCommittedAsync(artifactStore, artifact);

        Assert.AreEqual(2, process.RunCount, "an already committed transition must not replay");
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(harness.ScratchDirectory).Any());
    }

    [TestMethod]
    public async Task EnsureStateCommitted_RejectsArtifactThatIsNotDurable()
    {
        using var harness = new ExecutorHarness();
        var process = new WritingProcess(AddStateEntry);
        var executor = harness.CreateExecutor(process);
        var batch = harness.Batch();
        var execution = await executor.ApplyBatchWithWitnessAsync(batch);
        var artifact = BuildArtifact(batch, execution);
        using var artifactBackend = new InMemoryKeyValueStore();
        using var artifactStore = new KeyValueProofWitnessStore(artifactBackend);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            executor.EnsureStateCommittedAsync(artifactStore, artifact).AsTask());

        Assert.AreEqual(harness.InitialRoot, harness.Source.CaptureCurrent().StateRoot);
        Assert.AreEqual(1, process.RunCount, "state replay must not start before artifact commit");
    }

    [TestMethod]
    public async Task ApplyBatchWithWitness_RejectsForgedRequestHashWithoutStateMutation()
    {
        using var harness = new ExecutorHarness();
        var process = new WritingProcess(static (_, output) => output with
        {
            RequestPayloadHash = UInt256.Zero,
        });
        var executor = harness.CreateExecutor(process);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            executor.ApplyBatchWithWitnessAsync(harness.Batch()).AsTask());

        Assert.AreEqual(
            harness.InitialRoot,
            harness.Source.Capture(harness.InitialRoot).StateRoot);
    }

    [TestMethod]
    public async Task ApplyBatchWithWitness_ProcessFailureDoesNotCommitState()
    {
        using var harness = new ExecutorHarness();
        var process = new WritingProcess(exitCode: 17, standardError: "execution rejected");
        var executor = harness.CreateExecutor(process);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            executor.ApplyBatchWithWitnessAsync(harness.Batch()).AsTask());

        StringAssert.Contains(exception.Message, "17");
        StringAssert.Contains(exception.Message, "execution rejected");
        Assert.AreEqual(
            harness.InitialRoot,
            harness.Source.Capture(harness.InitialRoot).StateRoot);
    }

    [TestMethod]
    public async Task ApplyBatchWithWitness_RejectsReplacedExecutableBeforeProcessStart()
    {
        using var harness = new ExecutorHarness();
        var process = new WritingProcess();
        var executor = harness.CreateExecutor(process, Enumerable.Repeat((byte)0xAA, 32).ToArray());

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            executor.ApplyBatchWithWitnessAsync(harness.Batch()).AsTask());

        Assert.IsNull(process.LastRequest);
        Assert.AreEqual(
            harness.InitialRoot,
            harness.Source.Capture(harness.InitialRoot).StateRoot);
    }

    [TestMethod]
    public void ApplyBatchAsync_RejectsLegacyIncompleteRequest()
    {
        using var harness = new ExecutorHarness();
        var executor = harness.CreateExecutor(new WritingProcess());

        Assert.ThrowsExactly<NotSupportedException>(() =>
            executor.ApplyBatchAsync(harness.Batch().ToExecutionRequest()).AsTask()
                .GetAwaiter().GetResult());
    }

    [TestMethod]
    public async Task RealNativeExecutor_ExecutesBootstrappedNeoGenesisRetTransaction()
    {
        var executable = Environment.GetEnvironmentVariable("NEO_ZKVM_EXECUTOR");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive(
                "Set NEO_ZKVM_EXECUTOR to the built neo-zkvm-executor release binary");
        using var state = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(state);
        var initialRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
        var source = new Sp1StateWitnessSource(state, initialRoot);
        var scratch = Path.Combine(
            Path.GetTempPath(), "neo-n4-real-sp1-executor-" + Guid.NewGuid().ToString("N"));
        try
        {
            var executor = new Sp1StatefulBatchExecutor(
                source,
                executable!,
                SHA256.HashData(File.ReadAllBytes(executable!)),
                scratch,
                TimeSpan.FromMinutes(2));
            var batch = new SealedBatch(
                chainId: 1099,
                batchNumber: 1,
                firstBlock: 100,
                lastBlock: 100,
                preStateRoot: initialRoot,
                transactions: new[] { (ReadOnlyMemory<byte>)RetTransaction() },
                l1Messages: Array.Empty<CrossChainMessage>(),
                blockContext: new BatchBlockContext
                {
                    L1FinalizedHeight = 1234,
                    FirstBlockTimestamp = 1_750_000_000_000,
                    LastBlockTimestamp = 1_750_000_000_000,
                    SequencerCommitteeHash = Hash(0x33),
                    Network = NeoVMGenesisBootstrap.DefaultBootstrapSettings.Network,
                });

            var result = await executor.ApplyBatchWithWitnessAsync(batch);

            Assert.IsTrue(result.WitnessAuthenticated);
            Assert.AreEqual(initialRoot, result.ExecutionResult.PostStateRoot);
            CollectionAssert.AreEqual("NEO4EFX1"u8.ToArray(), result.Effects.Span[..8].ToArray());
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    private sealed class ExecutorHarness : IDisposable
    {
        private readonly InMemoryKeyValueStore _state = new();
        private readonly string _rootDirectory = Path.Combine(
            Path.GetTempPath(), "neo-n4-sp1-executor-test-" + Guid.NewGuid().ToString("N"));
        private readonly string _executablePath;

        public ExecutorHarness()
        {
            Directory.CreateDirectory(_rootDirectory);
            ScratchDirectory = Path.Combine(_rootDirectory, "scratch");
            Directory.CreateDirectory(ScratchDirectory);
            _executablePath = Path.Combine(_rootDirectory, "neo-zkvm-executor");
            File.WriteAllBytes(_executablePath, [0x00]);
            NeoVMGenesisBootstrap.Run(_state);
            InitialRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(_state);
            Source = new Sp1StateWitnessSource(_state, InitialRoot);
        }

        public string ScratchDirectory { get; }

        public UInt256 InitialRoot { get; }

        public Sp1StateWitnessSource Source { get; }

        public Sp1StatefulBatchExecutor CreateExecutor(
            ISp1NativeExecutionProcess process,
            ReadOnlyMemory<byte>? executableSha256 = null)
            => new(
                Source,
                _executablePath,
                executableSha256 ?? SHA256.HashData(File.ReadAllBytes(_executablePath)),
                ScratchDirectory,
                TimeSpan.FromSeconds(5),
                process);

        public SealedBatch Batch() => new(
            chainId: 1099,
            batchNumber: 1,
            firstBlock: 100,
            lastBlock: 100,
            preStateRoot: InitialRoot,
            transactions: new[] { (ReadOnlyMemory<byte>)new byte[] { 0x01 } },
            l1Messages: Array.Empty<CrossChainMessage>(),
            blockContext: new BatchBlockContext
            {
                L1FinalizedHeight = 1234,
                FirstBlockTimestamp = 1_750_000_000_000,
                LastBlockTimestamp = 1_750_000_000_000,
                SequencerCommitteeHash = Hash(0x33),
                Network = 0x334f_454e,
            });

        public void Dispose()
        {
            _state.Dispose();
            if (Directory.Exists(_rootDirectory))
                Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private sealed class WritingProcess : ISp1NativeExecutionProcess
    {
        private readonly Func<
            Sp1NativeExecutionProcessRequest,
            Sp1NativeExecutionOutputV1,
            Sp1NativeExecutionOutputV1> _mutate;
        private readonly int _exitCode;
        private readonly string _standardError;

        public WritingProcess(
            Func<
                Sp1NativeExecutionProcessRequest,
                Sp1NativeExecutionOutputV1,
                Sp1NativeExecutionOutputV1>? mutate = null,
            int exitCode = 0,
            string standardError = "")
        {
            _mutate = mutate ?? (static (_, output) => output);
            _exitCode = exitCode;
            _standardError = standardError;
        }

        public Sp1NativeExecutionProcessRequest? LastRequest { get; private set; }

        public int RunCount { get; private set; }

        public ValueTask<Sp1NativeExecutionProcessResult> RunAsync(
            Sp1NativeExecutionProcessRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            RunCount++;
            if (_exitCode == 0)
            {
                var output = _mutate(request, BuildOutput(request));
                File.WriteAllBytes(
                    request.OutputPath,
                    Sp1NativeExecutionOutputSerializer.Encode(output));
            }
            return ValueTask.FromResult(new Sp1NativeExecutionProcessResult(
                _exitCode, string.Empty, _standardError));
        }

        private static Sp1NativeExecutionOutputV1 BuildOutput(
            Sp1NativeExecutionProcessRequest request)
        {
            var payloadBytes = File.ReadAllBytes(request.PayloadPath);
            var witnessBytes = File.ReadAllBytes(request.StateWitnessPath);
            var payload = ExecutionPayloadSerializer.Decode(payloadBytes);
            var witness = StateWitnessV1Serializer.Decode(witnessBytes);
            var postStateRoot = KeyedStateMerkleTree.ComputeRoot(witness.Entries.Select(
                static entry => (entry.Key.ToArray(), entry.Value.ToArray())));
            var executionResult = new BatchExecutionResult
            {
                PostStateRoot = postStateRoot,
                TxRoot = Hash(0x11),
                ReceiptRoot = Hash(0x22),
                WithdrawalRoot = UInt256.Zero,
                L2ToL1MessageRoot = UInt256.Zero,
                L2ToL2MessageRoot = UInt256.Zero,
                GasConsumed = 0,
            };
            var publicInputs = new PublicInputs
            {
                ChainId = payload.ChainId,
                BatchNumber = payload.BatchNumber,
                PreStateRoot = payload.PreStateRoot,
                PostStateRoot = executionResult.PostStateRoot,
                TxRoot = executionResult.TxRoot,
                ReceiptRoot = executionResult.ReceiptRoot,
                WithdrawalRoot = executionResult.WithdrawalRoot,
                L2ToL1MessageRoot = executionResult.L2ToL1MessageRoot,
                L2ToL2MessageRoot = executionResult.L2ToL2MessageRoot,
                L1MessageHash = StateRootCalculator.HashL1Messages(payload.L1Messages),
                DACommitment = ExecutionPayloadSerializer.ComputeCommitment(payloadBytes),
                BlockContextHash = StateRootCalculator.HashBlockContext(payload.BlockContext),
            };
            return new Sp1NativeExecutionOutputV1
            {
                RequestPayloadHash = new UInt256(Crypto.Hash256(payloadBytes)),
                RequestStateWitnessHash = new UInt256(Crypto.Hash256(witnessBytes)),
                ExecutionSemanticId = ExecutionSemanticIds.Sp1StatefulNeoVmV1,
                ExecutionResult = executionResult,
                Effects = GoldenEffects(),
                PostStateWitness = witnessBytes,
                PublicInputHash = StateRootCalculator.HashPublicInputs(publicInputs),
            };
        }

        private static byte[] GoldenEffects()
        {
            var path = Path.Combine(
                AppContext.BaseDirectory, "Fixtures", "native_execution_output_v1.hex");
            var bytes = Convert.FromHexString(File.ReadAllText(path).Trim());
            return Sp1NativeExecutionOutputSerializer.Decode(bytes).Effects.ToArray();
        }
    }

    private static ProofWitnessArtifactV1 BuildArtifact(
        SealedBatch batch,
        ProofWitnessExecutionResult execution)
    {
        var payload = batch.ToExecutionPayload();
        var payloadBytes = ExecutionPayloadSerializer.Encode(payload);
        var publicInputs = BuildPublicInputs(payload, execution.ExecutionResult, payloadBytes);
        return ProofWitnessArtifactV1.Create(
            ProofType.Zk,
            WitnessProofSystem.Sp1,
            Hash(0x44),
            execution.ExecutionSemanticId,
            execution.WitnessAuthenticated,
            batch.ChainId,
            batch.BatchNumber,
            batch.FirstBlock,
            batch.LastBlock,
            payload,
            execution.StateWitness,
            execution.ExecutionResult,
            execution.Effects,
            new DAReceipt
            {
                Commitment = publicInputs.DACommitment,
                Pointer = new byte[] { 0x01 },
                Evidence = new byte[] { 0x02 },
                Kind = DAReceiptKind.ExternalPublication,
                Layer = DAMode.External,
            },
            publicInputs);
    }

    private static Sp1NativeExecutionOutputV1 AddStateEntry(
        Sp1NativeExecutionProcessRequest request,
        Sp1NativeExecutionOutputV1 output)
    {
        var witness = StateWitnessV1Serializer.Decode(
            File.ReadAllBytes(request.StateWitnessPath));
        var key = "neo-n4/test/state-transition/v1"u8.ToArray();
        if (witness.Entries.Any(entry => entry.Key.Span.SequenceEqual(key)))
            throw new InvalidOperationException("test transition key already exists");
        var entries = witness.Entries.Append(new StateWitnessEntryV1
        {
            Key = key,
            Value = new byte[] { 0x7a },
        }).ToArray();
        Array.Sort(entries, static (left, right) =>
            left.Key.Span.SequenceCompareTo(right.Key.Span));
        var postStateWitness = StateWitnessV1Serializer.Encode(witness with
        {
            Entries = entries,
        });
        var postStateRoot = KeyedStateMerkleTree.ComputeRoot(entries.Select(
            static entry => (entry.Key.ToArray(), entry.Value.ToArray())));
        var executionResult = output.ExecutionResult with
        {
            PostStateRoot = postStateRoot,
        };
        var payloadBytes = File.ReadAllBytes(request.PayloadPath);
        var payload = ExecutionPayloadSerializer.Decode(payloadBytes);
        var publicInputs = BuildPublicInputs(payload, executionResult, payloadBytes);
        return output with
        {
            ExecutionResult = executionResult,
            PostStateWitness = postStateWitness,
            PublicInputHash = StateRootCalculator.HashPublicInputs(publicInputs),
        };
    }

    private static PublicInputs BuildPublicInputs(
        ExecutionPayloadV1 payload,
        BatchExecutionResult executionResult,
        ReadOnlySpan<byte> payloadBytes)
        => new()
        {
            ChainId = payload.ChainId,
            BatchNumber = payload.BatchNumber,
            PreStateRoot = payload.PreStateRoot,
            PostStateRoot = executionResult.PostStateRoot,
            TxRoot = executionResult.TxRoot,
            ReceiptRoot = executionResult.ReceiptRoot,
            WithdrawalRoot = executionResult.WithdrawalRoot,
            L2ToL1MessageRoot = executionResult.L2ToL1MessageRoot,
            L2ToL2MessageRoot = executionResult.L2ToL2MessageRoot,
            L1MessageHash = StateRootCalculator.HashL1Messages(payload.L1Messages),
            DACommitment = ExecutionPayloadSerializer.ComputeCommitment(payloadBytes),
            BlockContextHash = StateRootCalculator.HashBlockContext(payload.BlockContext),
        };

    private static UInt256 Hash(byte value)
    {
        var bytes = new byte[UInt256.Length];
        bytes[0] = value;
        return new UInt256(bytes);
    }

    private static byte[] RetTransaction()
    {
        var transaction = new Transaction
        {
            Version = 0,
            Nonce = 1,
            SystemFee = 0,
            NetworkFee = 0,
            ValidUntilBlock = 5000,
            Script = new byte[] { (byte)OpCode.RET },
            Signers = new[]
            {
                new Signer { Account = UInt160.Zero, Scopes = WitnessScope.None },
            },
            Attributes = Array.Empty<TransactionAttribute>(),
            Witnesses = new[]
            {
                new Witness
                {
                    InvocationScript = ReadOnlyMemory<byte>.Empty,
                    VerificationScript = ReadOnlyMemory<byte>.Empty,
                },
            },
        };
        return transaction.ToArray();
    }
}
