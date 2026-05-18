namespace Neo.L2.Audit.UnitTests;

/// <summary>
/// Tests for <see cref="DAAvailabilityCheck"/> — flags batches whose DA payload has been
/// dropped/garbage-collected from the configured DA layer.
/// </summary>
[TestClass]
public class UT_DAAvailabilityCheck
{
    private static L2BatchCommitment Mk(ulong batchNumber, UInt256 daCommitment)
    {
        return new L2BatchCommitment
        {
            ChainId = 1001,
            BatchNumber = batchNumber,
            FirstBlock = batchNumber * 10,
            LastBlock = batchNumber * 10 + 9,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = daCommitment,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Multisig,
            Proof = new byte[] { 0xAA },
        };
    }

    /// <summary>Stub IDAWriter that says yes/no per receipt.</summary>
    private sealed class StubDA : IDAWriter
    {
        private readonly HashSet<UInt256> _available;
        public StubDA(params UInt256[] available) { _available = new(available); }
        public DAMode Mode => DAMode.External;
        public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
        public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken ct = default)
            => new(_available.Contains(receipt.Commitment));
    }

    private sealed class PointerAwareNeoFsStubDA : IDAWriter
    {
        private readonly UInt256 _commitment;
        private readonly byte[] _pointer;

        public PointerAwareNeoFsStubDA(UInt256 commitment, byte[] pointer)
        {
            _commitment = commitment;
            _pointer = pointer;
        }

        public DAMode Mode => DAMode.NeoFS;
        public ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
        public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken ct = default)
            => new(receipt.Commitment.Equals(_commitment) && receipt.Pointer.Span.SequenceEqual(_pointer));
    }

    private static UInt256 Commit(byte b)
    {
        var bytes = new byte[32];
        bytes[0] = b;
        return new UInt256(bytes);
    }

    [TestMethod]
    public async Task AllAvailable_PassesWithSummaryFinding()
    {
        var c1 = Commit(1); var c2 = Commit(2);
        var check = new DAAvailabilityCheck(new StubDA(c1, c2));
        var batches = new[] { Mk(1, c1), Mk(2, c2) };

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(1, findings.Count);
        Assert.IsTrue(findings[0].Passed);
        StringAssert.Contains(findings[0].Detail, "2 batches");
    }

    [TestMethod]
    public async Task OneMissing_FailsWithDetail()
    {
        var c1 = Commit(1); var c2 = Commit(2); var c3 = Commit(3);
        // c2 dropped from DA — middle batch has lost its payload.
        var check = new DAAvailabilityCheck(new StubDA(c1, c3));
        var batches = new[] { Mk(1, c1), Mk(2, c2), Mk(3, c3) };

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(1, findings.Count);
        Assert.IsFalse(findings[0].Passed);
        Assert.AreEqual(2UL, findings[0].BatchNumber);
        StringAssert.Contains(findings[0].Detail, "no longer available");
        StringAssert.Contains(findings[0].Detail, "External");
    }

    [TestMethod]
    public async Task ZeroCommitment_SkippedNotFlagged()
    {
        // Legacy "no DA" sentinel — older batches before DA wiring landed should not
        // gate the audit. Pin this so a refactor that drops the skip would fail
        // ancient devnet histories with confusing "DA unavailable" findings.
        var check = new DAAvailabilityCheck(new StubDA(/* nothing available */));
        var batches = new[] { Mk(1, UInt256.Zero), Mk(2, UInt256.Zero) };

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(1, findings.Count);
        Assert.IsTrue(findings[0].Passed, "all-zero-commitment batches should pass (skipped)");
        StringAssert.Contains(findings[0].Detail, "skipped");
    }

    [TestMethod]
    public async Task MixedZeroAndReal_OnlyRealAreChecked()
    {
        var c2 = Commit(2);
        var check = new DAAvailabilityCheck(new StubDA(c2));
        var batches = new[] { Mk(1, UInt256.Zero), Mk(2, c2) };

        var findings = await check.RunAsync(batches);

        Assert.AreEqual(1, findings.Count);
        Assert.IsTrue(findings[0].Passed);
        StringAssert.Contains(findings[0].Detail, "1 batches");
        StringAssert.Contains(findings[0].Detail, "skipped");
    }

    [TestMethod]
    public async Task NeoFsReceiptResolver_PreservesPointerForAvailability()
    {
        // NeoFS availability requires the object pointer returned by PublishAsync, not
        // just the content commitment. The auditor accepts an optional resolver so a
        // live pipeline can pass the stored DA receipt and avoid false negatives.
        var commitment = Commit(7);
        var pointer = new byte[] { 0x01, 0x02, 0x03 };
        var writer = new PointerAwareNeoFsStubDA(commitment, pointer);
        var receipt = new DAReceipt
        {
            Commitment = commitment,
            Pointer = pointer,
            Layer = DAMode.NeoFS,
        };
        var check = new DAAvailabilityCheck(
            writer,
            batch => batch.BatchNumber == 1 ? receipt : null);

        var findings = await check.RunAsync(new[] { Mk(1, commitment) });

        Assert.AreEqual(1, findings.Count);
        Assert.IsTrue(findings[0].Passed);
        StringAssert.Contains(findings[0].Detail, "NeoFS");
    }

    [TestMethod]
    public async Task ReceiptResolverCommitmentMismatch_FailsClearly()
    {
        var c1 = Commit(1);
        var wrong = new DAReceipt
        {
            Commitment = Commit(9),
            Pointer = ReadOnlyMemory<byte>.Empty,
            Layer = DAMode.External,
        };
        var check = new DAAvailabilityCheck(new StubDA(c1), _ => wrong);

        var findings = await check.RunAsync(new[] { Mk(1, c1) });

        Assert.AreEqual(1, findings.Count);
        Assert.IsFalse(findings[0].Passed);
        StringAssert.Contains(findings[0].Detail, "does not match");
    }

    [TestMethod]
    public void Constructor_RejectsNullDA()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new DAAvailabilityCheck(null!));
    }

    [TestMethod]
    public async Task RunAsync_RejectsNullBatches()
    {
        var check = new DAAvailabilityCheck(new StubDA());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await check.RunAsync(null!));
    }

    [TestMethod]
    public void NameIsStable()
    {
        // Pin the canonical name — used by ChainAuditor to attribute findings to a check.
        // Renaming this would break operator dashboards / log filters that query by name.
        Assert.AreEqual("da_availability", new DAAvailabilityCheck(new StubDA()).Name);
    }
}
