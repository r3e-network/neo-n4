namespace Neo.Plugins.L2Batch.UnitTests;

/// <summary>
/// Tests for <see cref="BatchSealer"/> — the pure batch-accumulation state machine that
/// lives behind <see cref="L2BatchPlugin"/>. Drives all three seal triggers (block-count,
/// tx-count, age) and verifies the metric contract.
/// </summary>
[TestClass]
public class UT_BatchSealer
{
    [TestMethod]
    public void Sealer_AccumulatesBlocks_UntilBlockCountTrigger()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 3,
            MaxTransactionsPerBatch = 100_000,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var metrics = new InMemoryMetrics();
        long now = 0;
        var sealer = new BatchSealer(settings, metrics, () => now);

        Assert.IsNull(sealer.OnBlockCommit(10, 1000, 11, NoTxs()), "1st block: no seal");
        Assert.IsNull(sealer.OnBlockCommit(11, 1100, 11, NoTxs()), "2nd block: no seal");
        var sealed_ = sealer.OnBlockCommit(12, 1200, 11, NoTxs());
        Assert.IsNotNull(sealed_, "3rd block hits MaxBlocksPerBatch — seals");
        Assert.AreEqual(1u, sealed_!.BatchNumber);
        Assert.AreEqual(10u, (uint)sealed_.FirstBlock);
        Assert.AreEqual(12u, (uint)sealed_.LastBlock);
        Assert.AreEqual(1L, metrics.GetCounter(MetricNames.BatchesSealed));
        Assert.AreEqual(1, metrics.GetHistogram(MetricNames.BatchSealLatencyMs).Count);
    }

    [TestMethod]
    public void Sealer_SealsOnTxCountTrigger()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1_000,
            MaxTransactionsPerBatch = 5,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var metrics = new InMemoryMetrics();
        var sealer = new BatchSealer(settings, metrics, () => 0L);

        Assert.IsNull(sealer.OnBlockCommit(1, 1000, 11, MakeTxs(3)));
        var sealed_ = sealer.OnBlockCommit(2, 1100, 11, MakeTxs(3));
        Assert.IsNotNull(sealed_, "6 txs > MaxTransactionsPerBatch=5 — seals");
        Assert.AreEqual(6, (int)metrics.GetGauge(MetricNames.BatchTxCount));
    }

    [TestMethod]
    public void Sealer_SealsOnAgeTrigger()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1_000,
            MaxTransactionsPerBatch = 100_000,
            MaxBatchAgeMillis = 5_000,
            Enabled = true,
        };
        var metrics = new InMemoryMetrics();
        long now = 0;
        var sealer = new BatchSealer(settings, metrics, () => now);

        Assert.IsNull(sealer.OnBlockCommit(1, 1000, 11, NoTxs()), "no seal at t=0");
        now = 4_999;
        Assert.IsNull(sealer.OnBlockCommit(2, 2000, 11, NoTxs()), "still under age threshold");
        now = 5_001;
        var sealed_ = sealer.OnBlockCommit(3, 3000, 11, NoTxs());
        Assert.IsNotNull(sealed_, "now >= MaxBatchAgeMillis — seals");
    }

    [TestMethod]
    public void Sealer_BatchNumberMonotonic_AcrossSeals()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1,
            MaxTransactionsPerBatch = 100,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var sealer = new BatchSealer(settings, new InMemoryMetrics(), () => 0L);

        var b1 = sealer.OnBlockCommit(10, 1000, 11, NoTxs())!;
        var b2 = sealer.OnBlockCommit(11, 2000, 11, NoTxs())!;
        var b3 = sealer.OnBlockCommit(12, 3000, 11, NoTxs())!;

        Assert.AreEqual(1u, b1.BatchNumber);
        Assert.AreEqual(2u, b2.BatchNumber);
        Assert.AreEqual(3u, b3.BatchNumber);
        Assert.AreEqual(10u, (uint)b1.FirstBlock);
        Assert.AreEqual(11u, (uint)b2.FirstBlock);
        Assert.AreEqual(12u, (uint)b3.FirstBlock);
    }

    [TestMethod]
    public void Sealer_StartsFreshBatchAfterSeal()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1,
            MaxTransactionsPerBatch = 100,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var sealer = new BatchSealer(settings, new InMemoryMetrics(), () => 0L);

        Assert.IsFalse(sealer.HasOpenBatch);
        sealer.OnBlockCommit(1, 1000, 11, NoTxs()); // seals immediately (MaxBlocksPerBatch=1)
        Assert.IsFalse(sealer.HasOpenBatch, "post-seal: builder reset");
        Assert.AreEqual(0, sealer.InProgressTxCount);
    }

    [TestMethod]
    public void Sealer_NoOpMetricsByDefault_DoesNotThrow()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1,
            MaxTransactionsPerBatch = 100,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var sealer = new BatchSealer(settings, NoOpMetrics.Instance, () => 0L);
        var sealed_ = sealer.OnBlockCommit(1, 1000, 11, NoTxs());
        Assert.IsNotNull(sealed_);
    }

    [TestMethod]
    public void Sealer_TxCountGauge_TracksMostRecentSeal()
    {
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1,
            MaxTransactionsPerBatch = 100,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var metrics = new InMemoryMetrics();
        var sealer = new BatchSealer(settings, metrics, () => 0L);

        sealer.OnBlockCommit(1, 1000, 11, MakeTxs(2));
        Assert.AreEqual(2, (int)metrics.GetGauge(MetricNames.BatchTxCount));
        sealer.OnBlockCommit(2, 2000, 11, MakeTxs(7));
        Assert.AreEqual(7, (int)metrics.GetGauge(MetricNames.BatchTxCount), "gauge replaces, not accumulates");
        Assert.AreEqual(2L, metrics.GetCounter(MetricNames.BatchesSealed), "counter increments");
    }

    [TestMethod]
    public void Sealer_WithMetrics_PreservesBatchNumberingAndBuilderState()
    {
        // Regression: previously L2BatchPlugin.WithMetrics nulled _sealer, dropping
        // _nextBatchNumber, _lastPostStateRoot, and any in-progress builder. Replaying
        // batch 1 after the rewire would collide with whatever was already submitted.
        // Now WithMetrics swaps the sink in-place on the existing sealer.
        var settings = new L2BatchSettings
        {
            ChainId = 1001,
            MaxBlocksPerBatch = 1,
            MaxTransactionsPerBatch = 100,
            MaxBatchAgeMillis = int.MaxValue,
            Enabled = true,
        };
        var initial = new InMemoryMetrics();
        var sealer = new BatchSealer(settings, initial, () => 0L);

        var b1 = sealer.OnBlockCommit(10, 1000, 11, NoTxs())!;
        Assert.AreEqual(1u, b1.BatchNumber);

        // Mid-flight rewire to a new sink.
        var second = new InMemoryMetrics();
        sealer.WithMetrics(second);

        // Batch numbering must continue from 2, not reset to 1.
        var b2 = sealer.OnBlockCommit(11, 2000, 11, NoTxs())!;
        Assert.AreEqual(2u, b2.BatchNumber, "batch numbering survives the rewire");
        Assert.AreEqual(1, second.GetCounter(MetricNames.BatchesSealed), "post-rewire seal hits new sink");
        Assert.AreEqual(1, initial.GetCounter(MetricNames.BatchesSealed), "pre-rewire seal stayed on old sink");
    }

    [TestMethod]
    public void Settings_ValidatePositive_RejectsZero()
    {
        // Regression: previously MaxBlocksPerBatch: 0 (or any other Max*: 0) made
        // BatchSealer.ShouldSeal return true on every block — every block became its own
        // batch, producing degenerate per-block batches that each carry full settlement /
        // proving overhead. The misconfig surfaces as a runaway L1 submission rate hours
        // later instead of at plugin load.
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            L2BatchSettings.ValidatePositive(0, "MaxBlocksPerBatch"));
        StringAssert.Contains(ex.Message, "MaxBlocksPerBatch");
    }

    [TestMethod]
    public void Settings_ValidatePositive_RejectsNegative()
    {
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            L2BatchSettings.ValidatePositive(-5, "MaxTransactionsPerBatch"));
        StringAssert.Contains(ex.Message, "MaxTransactionsPerBatch");
        StringAssert.Contains(ex.Message, "-5");
    }

    [TestMethod]
    public void Settings_ValidatePositive_AcceptsOne()
    {
        // Boundary partner: 1 is the smallest valid threshold. A user explicitly opting
        // into per-block batches must be allowed (seal-every-block is degenerate but
        // not incoherent — useful for tests, devnet diagnostics).
        Assert.AreEqual(1, L2BatchSettings.ValidatePositive(1, "MaxBlocksPerBatch"));
        Assert.AreEqual(50, L2BatchSettings.ValidatePositive(50, "MaxBlocksPerBatch"));
    }

    [TestMethod]
    public void Constructor_ValidatesSettingsPositivity()
    {
        // Regression for iter 191: previously the BatchSealer ctor accepted
        // L2BatchSettings constructed via init-setters that bypassed
        // L2BatchSettings.From's validation — e.g. `new L2BatchSettings {
        // MaxBlocksPerBatch = 0 }`. With Max*=0, ShouldSeal returns true on every block,
        // producing degenerate per-block batches and a runaway L1 submission rate.
        // Now caught at the ctor.
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            new BatchSealer(new L2BatchSettings { MaxBlocksPerBatch = 0 }, new InMemoryMetrics()));
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            new BatchSealer(new L2BatchSettings { MaxTransactionsPerBatch = -1 }, new InMemoryMetrics()));
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            new BatchSealer(new L2BatchSettings { MaxBatchAgeMillis = 0 }, new InMemoryMetrics()));

        // Default settings (50/5000/30000) must still work.
        new BatchSealer(new L2BatchSettings(), new InMemoryMetrics());
    }

    [TestMethod]
    public void Constructor_RejectsNullSettings()
        => Assert.ThrowsExactly<ArgumentNullException>(
            () => new BatchSealer(null!, new InMemoryMetrics()));

    [TestMethod]
    public void Constructor_RejectsNullMetrics()
        => Assert.ThrowsExactly<ArgumentNullException>(
            () => new BatchSealer(new L2BatchSettings(), null!));

    [TestMethod]
    public void WithMetrics_RejectsNullMetrics()
    {
        // Pin BatchSealer.cs:34. Symmetric to other plugin WithMetrics pins.
        var sealer = new BatchSealer(new L2BatchSettings(), new InMemoryMetrics());
        Assert.ThrowsExactly<ArgumentNullException>(() => sealer.WithMetrics(null!));
    }

    [TestMethod]
    public void OnBlockCommit_RejectsNullRawTransactions()
    {
        // Pin BatchSealer.cs:74. Companion to RejectsNullTransactionInList — the per-entry
        // guard catches null entries within a non-null IEnumerable; this guard catches a
        // null IEnumerable itself.
        var sealer = new BatchSealer(new L2BatchSettings(), new InMemoryMetrics());
        Assert.ThrowsExactly<ArgumentNullException>(
            () => sealer.OnBlockCommit(1, 1000, 11, null!));
    }

    [TestMethod]
    public void OnBlockCommit_RejectsNullTransactionInList()
    {
        // Regression for iter 181: previously the implicit byte[] → ReadOnlyMemory<byte>
        // conversion silently turned null into Empty, so a null tx in the list would be
        // folded into the batch's tx tree as an empty leaf — a deterministic-replay
        // nightmare since the commitment would not match what re-execution produces.
        // Now caught at the foreach with the bad index named.
        var sealer = new BatchSealer(new L2BatchSettings(), new InMemoryMetrics());
        var bad = new byte[]?[] { new byte[] { 0x01 }, null, new byte[] { 0x02 } };
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => sealer.OnBlockCommit(1, 1000, 11, bad!));
        StringAssert.Contains(ex.Message, "[1]");
    }

    // ---- Forced-inclusion drain (censorship resistance) ----

    private static L2BatchSettings ForcedSettings(int maxBlocks = 5) => new()
    {
        ChainId = 1001,
        MaxBlocksPerBatch = maxBlocks,
        MaxTransactionsPerBatch = 100_000,
        MaxBatchAgeMillis = int.MaxValue,
        Enabled = true,
    };

    private static (ulong, UInt256, ReadOnlyMemory<byte>) Forced(
        ulong nonce,
        byte[] transaction) => (
            nonce,
            new UInt256(Neo.Cryptography.Crypto.Hash256(transaction)),
            transaction);

    private static (ulong, UInt256, ReadOnlyMemory<byte>)[] TwoForced()
    {
        return
        [
            Forced(1UL, new byte[] { 0xF1, 0x01 }),
            Forced(2UL, new byte[] { 0xF2, 0x02 }),
        ];
    }

    [TestMethod]
    public void ForcedInclusion_PrependsAtBatchStart_WithoutConsuming_DrainsOncePerBatch()
    {
        var drainCalls = 0;
        var sealer = new BatchSealer(ForcedSettings(maxBlocks: 5), new InMemoryMetrics(), () => 0L,
            forcedDrain: _ => { drainCalls++; return TwoForced(); });

        // First block of a fresh batch: 2 forced txs prepended + 1 block tx = 3.
        Assert.IsNull(sealer.OnBlockCommit(1, 1000, 11, MakeTxs(1)));
        Assert.AreEqual(3, sealer.InProgressTxCount, "2 forced + 1 block tx");
        Assert.AreEqual(1, drainCalls);

        // Second block of the SAME batch: forced source not re-polled; only the block tx is added.
        Assert.IsNull(sealer.OnBlockCommit(2, 1100, 11, MakeTxs(1)));
        Assert.AreEqual(4, sealer.InProgressTxCount, "only the new block tx is added");
        Assert.AreEqual(1, drainCalls, "forced source polled once per batch, at batch start");
    }

    [TestMethod]
    public void ForcedInclusion_ForcedTxsComeFirst_InSealedPayload()
    {
        // Seal on the first block and assert the immutable payload order directly.
        var f1 = new byte[] { 0xF1, 0x01 };
        var f2 = new byte[] { 0xF2, 0x02 };
        var b1 = new byte[] { 0x00, 0xCA, 0xFE }; // MakeTxs(1)[0]
        var sealer = new BatchSealer(ForcedSettings(maxBlocks: 1), new InMemoryMetrics(), () => 0L,
            forcedDrain: _ => new[] { Forced(1UL, f1), Forced(2UL, f2) });

        var sealed_ = sealer.OnBlockCommit(1, 1000, 11, MakeTxs(1));
        Assert.IsNotNull(sealed_);
        Assert.AreEqual(3, sealed_!.Transactions.Count);
        CollectionAssert.AreEqual(f1, sealed_.Transactions[0].ToArray());
        CollectionAssert.AreEqual(f2, sealed_.Transactions[1].ToArray());
        CollectionAssert.AreEqual(b1, sealed_.Transactions[2].ToArray());
        Assert.AreEqual(2, sealed_.ForcedInclusions.Count);
        Assert.AreEqual(1UL, sealed_.ForcedInclusions[0].Nonce);
        Assert.AreEqual(0U, sealed_.ForcedInclusions[0].LeafIndex);
        Assert.AreEqual(2UL, sealed_.ForcedInclusions[1].Nonce);
        Assert.AreEqual(1U, sealed_.ForcedInclusions[1].LeafIndex);
        var txRoot = Neo.L2.State.StateRootCalculator.ComputeTxRoot(
            sealed_.Transactions
                .Select(transaction => new UInt256(
                    Neo.Cryptography.Crypto.Hash256(transaction.Span)))
                .ToArray());
        foreach (var proof in sealed_.ForcedInclusions)
        {
            Assert.IsTrue(new Neo.L2.State.MerkleProof
            {
                Leaf = proof.TxHash,
                LeafIndex = checked((int)proof.LeafIndex),
                Siblings = proof.Siblings,
                PathBitmap = proof.LeafIndex,
            }.Verify(txRoot));
        }
    }

    [TestMethod]
    public void ForcedInclusion_NoSource_BehaviorUnchanged()
    {
        var sealer = new BatchSealer(ForcedSettings(maxBlocks: 1), new InMemoryMetrics(), () => 0L);
        var b1 = new byte[] { 0x00, 0xCA, 0xFE };
        var sealed_ = sealer.OnBlockCommit(1, 1000, 11, MakeTxs(1));
        Assert.IsNotNull(sealed_);
        Assert.AreEqual(1, sealed_!.Transactions.Count);
        CollectionAssert.AreEqual(b1, sealed_.Transactions[0].ToArray());
    }

    [TestMethod]
    public void ForcedInclusion_EmptyForcedTx_Rejected()
    {
        var sealer = new BatchSealer(ForcedSettings(maxBlocks: 5), new InMemoryMetrics(), () => 0L,
            forcedDrain: _ => new[]
            {
                (1UL, UInt256.Zero, ReadOnlyMemory<byte>.Empty),
            });
        Assert.ThrowsExactly<InvalidOperationException>(() => sealer.OnBlockCommit(1, 1000, 11, NoTxs()));
    }

    [TestMethod]
    public void ForcedInclusion_RejectsSourceTxHashMismatch()
    {
        var sealer = new BatchSealer(
            ForcedSettings(maxBlocks: 1),
            new InMemoryMetrics(),
            () => 0L,
            forcedDrain: _ => new[]
            {
                (1UL, UInt256.Zero, (ReadOnlyMemory<byte>)new byte[] { 0x01 }),
            });
        Assert.ThrowsExactly<InvalidOperationException>(
            () => sealer.OnBlockCommit(1, 1000, 11, NoTxs()));
    }

    [TestMethod]
    public void Sealer_UsesRealL1MessagesAndFullBlockContext()
    {
        var messageWithoutHash = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 1001,
            Nonce = 9,
            Sender = new UInt160(new byte[UInt160.Length]),
            Receiver = new UInt160(Enumerable.Repeat((byte)1, UInt160.Length).ToArray()),
            MessageType = MessageType.Deposit,
            Payload = new byte[] { 0x44 },
            MessageHash = UInt256.Zero,
        };
        var message = messageWithoutHash with
        {
            MessageHash = Neo.L2.State.MessageHasher.HashMessage(messageWithoutHash),
        };
        var committee = new UInt256(Enumerable.Repeat((byte)0x55, UInt256.Length).ToArray());
        var sealer = new BatchSealer(
            ForcedSettings(maxBlocks: 2),
            new InMemoryMetrics(),
            () => 0L,
            l1MessageDrain: _ => new[] { message },
            l1FinalizedHeight: () => 777,
            sequencerCommitteeHash: () => committee);

        Assert.IsNull(sealer.OnBlockCommit(10, 1000, 11, NoTxs()));
        var sealedBatch = sealer.OnBlockCommit(11, 2000, 11, NoTxs());

        Assert.IsNotNull(sealedBatch);
        Assert.AreEqual(message, sealedBatch.L1Messages.Single());
        Assert.AreEqual(777u, sealedBatch.BlockContext.L1FinalizedHeight);
        Assert.AreEqual(1000UL, sealedBatch.BlockContext.FirstBlockTimestamp);
        Assert.AreEqual(2000UL, sealedBatch.BlockContext.LastBlockTimestamp);
        Assert.AreEqual(committee, sealedBatch.BlockContext.SequencerCommitteeHash);
        Assert.AreEqual(11u, sealedBatch.BlockContext.Network);
    }

    [TestMethod]
    public void AcknowledgeExecution_AdvancesNextBatchPreStateRoot()
    {
        var sealer = new BatchSealer(
            ForcedSettings(maxBlocks: 1), new InMemoryMetrics(), () => 0L);
        var first = sealer.OnBlockCommit(1, 1000, 11, NoTxs())!;
        var postStateRoot = new UInt256(
            Enumerable.Repeat((byte)0x66, UInt256.Length).ToArray());
        sealer.AcknowledgeExecution(first.BatchNumber, postStateRoot);

        var second = sealer.OnBlockCommit(2, 2000, 11, NoTxs())!;
        Assert.AreEqual(postStateRoot, second.PreStateRoot);
        sealer.AcknowledgeExecution(first.BatchNumber, postStateRoot);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => sealer.AcknowledgeExecution(3, postStateRoot));
    }

    private static IEnumerable<byte[]> NoTxs() => Array.Empty<byte[]>();

    private static IEnumerable<byte[]> MakeTxs(int n)
    {
        for (var i = 0; i < n; i++)
            yield return new byte[] { (byte)i, 0xCA, 0xFE };
    }
}
