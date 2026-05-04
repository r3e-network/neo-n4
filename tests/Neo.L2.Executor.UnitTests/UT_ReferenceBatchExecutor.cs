using System.Numerics;
using Neo.L2.Executor.Receipts;

namespace Neo.L2.Executor.UnitTests;

[TestClass]
public class UT_ReferenceBatchExecutor
{
    private static BatchBlockContext SampleContext() => new()
    {
        L1FinalizedHeight = 100,
        FirstBlockTimestamp = 1_700_000_000_000,
        LastBlockTimestamp = 1_700_000_005_000,
        SequencerCommitteeHash = UInt256.Parse("0x" + new string('1', 64)),
        Network = 0x4F454E,
    };

    private sealed class StubL1MessageProcessor : IL1MessageProcessor
    {
        public List<CrossChainMessage> Applied { get; } = new();
        public ValueTask ApplyAsync(CrossChainMessage message, CancellationToken cancellationToken = default)
        {
            Applied.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubEffectsCollector : IBatchEffectsCollector
    {
        private readonly Dictionary<UInt256, BatchEffects> _effects;
        public StubEffectsCollector(IDictionary<UInt256, BatchEffects> effects) => _effects = new(effects);
        public BatchEffects GetEffects(UInt256 txHash) =>
            _effects.TryGetValue(txHash, out var e) ? e : BatchEffects.Empty;
    }

    [TestMethod]
    public async Task EmptyBatch_ProducesAllZeroRootsExceptPostState()
    {
        var executor = new ReferenceBatchExecutor(
            new ReferenceTransactionExecutor(),
            new DerivedPostStateRootOracle());

        var result = await executor.ApplyBatchAsync(new BatchExecutionRequest
        {
            ChainId = 1001,
            BatchNumber = 1,
            PreStateRoot = UInt256.Zero,
            Transactions = Array.Empty<ReadOnlyMemory<byte>>(),
            L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
            BlockContext = SampleContext(),
        });

        Assert.AreEqual(UInt256.Zero, result.TxRoot);
        Assert.AreEqual(UInt256.Zero, result.ReceiptRoot);
        Assert.AreEqual(UInt256.Zero, result.WithdrawalRoot);
        Assert.AreEqual(UInt256.Zero, result.L2ToL1MessageRoot);
        Assert.AreEqual(UInt256.Zero, result.L2ToL2MessageRoot);
        Assert.AreEqual(0L, result.GasConsumed);
        // Post-state oracle hashes pre + receipt + ctx → non-zero even when receipt is zero.
        Assert.AreNotEqual(UInt256.Zero, result.PostStateRoot);
    }

    [TestMethod]
    public async Task SingleTransaction_ProducesNonZeroRoots()
    {
        var executor = new ReferenceBatchExecutor(
            new ReferenceTransactionExecutor(),
            new DerivedPostStateRootOracle());

        var result = await executor.ApplyBatchAsync(new BatchExecutionRequest
        {
            ChainId = 1001,
            BatchNumber = 1,
            PreStateRoot = UInt256.Zero,
            Transactions = new ReadOnlyMemory<byte>[] { new byte[] { 0xAA, 0xBB } },
            L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
            BlockContext = SampleContext(),
        });

        Assert.AreNotEqual(UInt256.Zero, result.TxRoot);
        Assert.AreNotEqual(UInt256.Zero, result.ReceiptRoot);
    }

    [TestMethod]
    public async Task L1Messages_AppliedInOrder()
    {
        var processor = new StubL1MessageProcessor();
        var executor = new ReferenceBatchExecutor(
            new ReferenceTransactionExecutor(),
            new DerivedPostStateRootOracle(),
            processor);

        var msgs = new[]
        {
            new CrossChainMessage { SourceChainId = 0, TargetChainId = 1001, Nonce = 1, Sender = UInt160.Zero, Receiver = UInt160.Zero, MessageType = MessageType.Deposit, Payload = ReadOnlyMemory<byte>.Empty, MessageHash = UInt256.Parse("0x" + new string('1', 64)) },
            new CrossChainMessage { SourceChainId = 0, TargetChainId = 1001, Nonce = 2, Sender = UInt160.Zero, Receiver = UInt160.Zero, MessageType = MessageType.Deposit, Payload = ReadOnlyMemory<byte>.Empty, MessageHash = UInt256.Parse("0x" + new string('2', 64)) },
        };

        await executor.ApplyBatchAsync(new BatchExecutionRequest
        {
            ChainId = 1001,
            BatchNumber = 1,
            PreStateRoot = UInt256.Zero,
            Transactions = Array.Empty<ReadOnlyMemory<byte>>(),
            L1MessagesConsumed = msgs,
            BlockContext = SampleContext(),
        });

        Assert.AreEqual(2, processor.Applied.Count);
        Assert.AreEqual(1UL, processor.Applied[0].Nonce);
        Assert.AreEqual(2UL, processor.Applied[1].Nonce);
    }

    [TestMethod]
    public async Task TransactionEffects_PopulateBatchTrees()
    {
        // Construct a transaction whose hash we can predict, then attach effects to it.
        var txBytes = new ReadOnlyMemory<byte>(new byte[] { 0x10, 0x20, 0x30 });
        var txHash = new UInt256(Cryptography.Crypto.Hash256(txBytes.Span));

        var withdrawal = new WithdrawalRequest
        {
            EmittingContract = UInt160.Zero,
            L2Sender = UInt160.Parse("0x" + new string('a', 40)),
            L1Recipient = UInt160.Parse("0x" + new string('b', 40)),
            L2Asset = UInt160.Parse("0x" + new string('c', 40)),
            Amount = new BigInteger(123),
            Nonce = 1,
        };
        var msgL1 = new CrossChainMessage
        {
            SourceChainId = 1001, TargetChainId = 0, Nonce = 1,
            Sender = UInt160.Zero, Receiver = UInt160.Zero,
            MessageType = MessageType.Withdraw,
            Payload = ReadOnlyMemory<byte>.Empty,
            MessageHash = UInt256.Parse("0x" + new string('5', 64)),
        };
        var msgL2 = msgL1 with { TargetChainId = 2002, MessageHash = UInt256.Parse("0x" + new string('6', 64)) };

        var effects = new StubEffectsCollector(new Dictionary<UInt256, BatchEffects>
        {
            [txHash] = new BatchEffects
            {
                Withdrawals = new[] { withdrawal },
                Messages = new[] { msgL1, msgL2 },
            },
        });

        var executor = new ReferenceBatchExecutor(
            new ReferenceTransactionExecutor(effects),
            new DerivedPostStateRootOracle());

        var result = await executor.ApplyBatchAsync(new BatchExecutionRequest
        {
            ChainId = 1001,
            BatchNumber = 1,
            PreStateRoot = UInt256.Zero,
            Transactions = new[] { txBytes },
            L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
            BlockContext = SampleContext(),
        });

        Assert.AreNotEqual(UInt256.Zero, result.WithdrawalRoot);
        Assert.AreNotEqual(UInt256.Zero, result.L2ToL1MessageRoot);
        Assert.AreNotEqual(UInt256.Zero, result.L2ToL2MessageRoot);
    }

    [TestMethod]
    public async Task Determinism_SameInputsProduceSameOutputs()
    {
        var executor = new ReferenceBatchExecutor(
            new ReferenceTransactionExecutor(),
            new DerivedPostStateRootOracle());

        BatchExecutionRequest Build() => new()
        {
            ChainId = 1001,
            BatchNumber = 5,
            PreStateRoot = UInt256.Parse("0x" + new string('a', 64)),
            Transactions = new ReadOnlyMemory<byte>[] { new byte[] { 1, 2 }, new byte[] { 3 } },
            L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
            BlockContext = SampleContext(),
        };

        var r1 = await executor.ApplyBatchAsync(Build());
        var r2 = await executor.ApplyBatchAsync(Build());

        Assert.AreEqual(r1.TxRoot, r2.TxRoot);
        Assert.AreEqual(r1.ReceiptRoot, r2.ReceiptRoot);
        Assert.AreEqual(r1.PostStateRoot, r2.PostStateRoot);
    }

    [TestMethod]
    public async Task FailedTransaction_EffectsAreSuppressed()
    {
        // Regression: previously a failed transaction's emitted withdrawals + L2→* messages
        // were still added to the batch trees. Per L2 semantics, a failed tx reverts all
        // its state changes — including emitted effects. A buggy executor that leaks effects
        // from a failed tx would silently produce a withdrawal-tree commitment that doesn't
        // match the (correct) ReceiptRoot, surfacing only at L1 settlement when the
        // inclusion proof for the leaked withdrawal is checked against the user's actual
        // state (which never debited the funds).
        var receiptTx = UInt256.Parse("0x" + new string('5', 64));
        var withdrawal = new WithdrawalRequest
        {
            EmittingContract = UInt160.Zero,
            L2Sender = UInt160.Parse("0x" + new string('a', 40)),
            L1Recipient = UInt160.Parse("0x" + new string('b', 40)),
            L2Asset = UInt160.Parse("0x" + new string('c', 40)),
            Amount = new BigInteger(1000),
            Nonce = 1,
        };

        var executor = new ReferenceBatchExecutor(
            new FailingExecutor(receiptTx, new[] { withdrawal }, Array.Empty<CrossChainMessage>()),
            new DerivedPostStateRootOracle());

        var result = await executor.ApplyBatchAsync(new BatchExecutionRequest
        {
            ChainId = 1001, BatchNumber = 1, PreStateRoot = UInt256.Zero,
            Transactions = new ReadOnlyMemory<byte>[] { new byte[] { 0x00 } },
            L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
            BlockContext = SampleContext(),
        });

        Assert.AreEqual(UInt256.Zero, result.WithdrawalRoot, "failed tx must not leak withdrawals");
    }

    private sealed class FailingExecutor : ITransactionExecutor
    {
        private readonly UInt256 _txHash;
        private readonly IReadOnlyList<WithdrawalRequest> _withdrawals;
        private readonly IReadOnlyList<CrossChainMessage> _messages;
        public FailingExecutor(UInt256 txHash, IReadOnlyList<WithdrawalRequest> w, IReadOnlyList<CrossChainMessage> m)
        { _txHash = txHash; _withdrawals = w; _messages = m; }

        public ValueTask<TransactionExecutionResult> ExecuteAsync(
            ReadOnlyMemory<byte> serializedTx, BatchBlockContext batchContext, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new TransactionExecutionResult
            {
                TxHash = _txHash,
                Receipt = new Receipt
                {
                    TxHash = _txHash,
                    Success = false,  // ← failed
                    GasConsumed = 100,
                    StorageDeltaHash = UInt256.Zero,
                    EventsHash = UInt256.Zero,
                },
                // Buggy executor leaks effects from a failed tx.
                Withdrawals = _withdrawals,
                Messages = _messages,
            });
    }

    [TestMethod]
    public async Task ApplyBatchAsync_BuggyTxExecutorReturnsNull_SurfacesContractViolation()
    {
        // Regression for iter 173: a buggy ITransactionExecutor that returns null would
        // propagate as a confusing NRE deep in the for loop. Now surfaced as
        // InvalidOperationException naming the contract method.
        var executor = new ReferenceBatchExecutor(
            new NullReturningTxExecutor(),
            new DerivedPostStateRootOracle());
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await executor.ApplyBatchAsync(new BatchExecutionRequest
            {
                ChainId = 1001, BatchNumber = 1, PreStateRoot = UInt256.Zero,
                Transactions = new ReadOnlyMemory<byte>[] { new byte[] { 0x01 } },
                L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
                BlockContext = SampleContext(),
            }));
        StringAssert.Contains(ex.Message, "ExecuteAsync");
    }

    private sealed class NullReturningTxExecutor : ITransactionExecutor
    {
        public ValueTask<TransactionExecutionResult> ExecuteAsync(
            ReadOnlyMemory<byte> serializedTx, BatchBlockContext batchContext, CancellationToken cancellationToken = default)
            => new ValueTask<TransactionExecutionResult>((TransactionExecutionResult)null!);
    }

    [TestMethod]
    public void Constructor_RejectsNullTxExecutor()
    {
        // Pin ReferenceBatchExecutor.cs:30. Without it the first ExecuteAsync NREs with
        // no link to the bad ctor arg.
        var oracle = new DerivedPostStateRootOracle();
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new ReferenceBatchExecutor(null!, oracle));
    }

    [TestMethod]
    public void Constructor_RejectsNullPostStateRootOracle()
    {
        // Pin ReferenceBatchExecutor.cs:31.
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new ReferenceBatchExecutor(new ReferenceTransactionExecutor(), null!));
    }

    [TestMethod]
    public async Task ApplyBatchAsync_RejectsNullRequest()
    {
        // Pin ReferenceBatchExecutor.cs:42.
        var executor = new ReferenceBatchExecutor(new ReferenceTransactionExecutor(), new DerivedPostStateRootOracle());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await executor.ApplyBatchAsync(null!));
    }

    [TestMethod]
    public async Task DerivedPostStateRootOracle_RejectsNullPreStateRoot()
    {
        // Regression for iter 169: previously a null UInt256 input would NRE deep inside
        // GetSpan() with no link to the bad caller. Same iter-156 hashing-primitive
        // null-guard pattern, applied at the oracle's API boundary.
        var oracle = new DerivedPostStateRootOracle();
        var ctx = SampleContext();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await oracle.ResolveAsync(null!, UInt256.Zero, ctx));
    }

    [TestMethod]
    public async Task DerivedPostStateRootOracle_RejectsNullReceiptRoot()
    {
        var oracle = new DerivedPostStateRootOracle();
        var ctx = SampleContext();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await oracle.ResolveAsync(UInt256.Zero, null!, ctx));
    }

    [TestMethod]
    public async Task DerivedPostStateRootOracle_RejectsNullBlockContext()
    {
        var oracle = new DerivedPostStateRootOracle();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await oracle.ResolveAsync(UInt256.Zero, UInt256.Zero, null!));
    }

    [TestMethod]
    public async Task ApplyBatchAsync_RejectsNullL1MessageEntry()
    {
        // Regression for iter 193: a null entry in L1MessagesConsumed would propagate
        // to _l1Processor.ApplyAsync with no clear "[index] is null" diagnostic. Now
        // surfaces at the source with the bad index.
        var executor = new ReferenceBatchExecutor(
            new ReferenceTransactionExecutor(),
            new DerivedPostStateRootOracle(),
            new NoopL1Processor());
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await executor.ApplyBatchAsync(new BatchExecutionRequest
            {
                ChainId = 1001, BatchNumber = 1, PreStateRoot = UInt256.Zero,
                Transactions = Array.Empty<ReadOnlyMemory<byte>>(),
                L1MessagesConsumed = new CrossChainMessage?[] { null }!,
                BlockContext = SampleContext(),
            }));
        StringAssert.Contains(ex.Message, "[0]");
    }

    private sealed class NoopL1Processor : IL1MessageProcessor
    {
        public ValueTask ApplyAsync(CrossChainMessage message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    [TestMethod]
    public void Receipt_HashStableAcrossInstances()
    {
        Receipt Mk() => new()
        {
            TxHash = UInt256.Parse("0x" + new string('1', 64)),
            Success = true,
            GasConsumed = 1000,
            StorageDeltaHash = UInt256.Parse("0x" + new string('2', 64)),
            EventsHash = UInt256.Parse("0x" + new string('3', 64)),
        };
        Assert.AreEqual(Mk().Hash(), Mk().Hash());
    }

    [TestMethod]
    public void Receipt_Hash_RejectsNullTxHash()
    {
        // Pin Receipt.cs:36. Same iter-154+ hashing-primitive defense pattern. Without
        // the guard Hash() NREs inside TxHash.GetSpan().
        var bad = new Receipt
        {
            TxHash = null!, Success = true, GasConsumed = 1,
            StorageDeltaHash = UInt256.Zero, EventsHash = UInt256.Zero,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => bad.Hash());
    }

    [TestMethod]
    public void Receipt_Hash_RejectsNullStorageDeltaHash()
    {
        var bad = new Receipt
        {
            TxHash = UInt256.Zero, Success = true, GasConsumed = 1,
            StorageDeltaHash = null!, EventsHash = UInt256.Zero,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => bad.Hash());
    }

    [TestMethod]
    public void Receipt_Hash_RejectsNullEventsHash()
    {
        var bad = new Receipt
        {
            TxHash = UInt256.Zero, Success = true, GasConsumed = 1,
            StorageDeltaHash = UInt256.Zero, EventsHash = null!,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => bad.Hash());
    }
}
