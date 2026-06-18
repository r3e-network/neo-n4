namespace Neo.Plugins.L2Gateway.UnitTests;

/// <summary>
/// Tests for <see cref="L2GatewayPlugin"/> — the Phase 5 plugin that owns the active
/// <see cref="IGatewayAggregator"/> and forwards sealed L2 batches into it. Boundary
/// + lifecycle pins; the deep aggregation behavior is tested in
/// <c>UT_BinaryTreeAggregator</c> and <c>UT_PassThroughAggregator</c>.
/// </summary>
[TestClass]
public class UT_L2GatewayPlugin
{
    private static L2BatchCommitment SampleCommitment(uint chainId = 1001, ulong batchNumber = 1) => new()
    {
        ChainId = chainId,
        BatchNumber = batchNumber,
        FirstBlock = 0,
        LastBlock = 0,
        PreStateRoot = UInt256.Zero,
        PostStateRoot = UInt256.Zero,
        TxRoot = UInt256.Zero,
        ReceiptRoot = UInt256.Zero,
        WithdrawalRoot = UInt256.Zero,
        L2ToL1MessageRoot = UInt256.Zero,
        L2ToL2MessageRoot = UInt256.Zero,
        DACommitment = UInt256.Zero,
        PublicInputHash = UInt256.Zero,
        ProofType = ProofType.None,
        Proof = ReadOnlyMemory<byte>.Empty,
    };

    [TestMethod]
    public void Constructor_DoesNotThrow()
    {
        using var plugin = new L2GatewayPlugin();
    }

    [TestMethod]
    public void DefaultAggregator_IsPassThrough()
    {
        // Pre-Configure default — pinned so a refactor of the field initializer doesn't
        // silently break tests / devnet setups that construct the plugin and immediately
        // call ReceiveBatch without UseAggregator.
        using var plugin = new L2GatewayPlugin();
        Assert.IsInstanceOfType(plugin.Aggregator, typeof(PassThroughAggregator));
    }

    [TestMethod]
    public void UseAggregator_OverridesDefault()
    {
        using var plugin = new L2GatewayPlugin();
        var custom = new BinaryTreeAggregator();
        plugin.UseAggregator(custom);
        Assert.AreSame(custom, plugin.Aggregator);
    }

    [TestMethod]
    public void UseAggregator_RejectsNull()
    {
        using var plugin = new L2GatewayPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.UseAggregator(null!));
    }

    [TestMethod]
    public void ReceiveBatch_RejectsNull()
    {
        // Pin L2GatewayPlugin.cs:36. Surface null at the API boundary instead of relying
        // on the aggregator's own internal guard, so the operator sees the L2GatewayPlugin
        // call site in the stack rather than the deeper aggregator's "commitment" arg name.
        using var plugin = new L2GatewayPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.ReceiveBatch(null!));
    }

    [TestMethod]
    public void ReceiveBatch_ForwardsToAggregator()
    {
        using var plugin = new L2GatewayPlugin();
        plugin.ReceiveBatch(SampleCommitment());
        Assert.AreEqual(1, plugin.Aggregator.PendingCount);
    }

    [TestMethod]
    public void PullAggregate_ReturnsNullWhenEmpty()
    {
        using var plugin = new L2GatewayPlugin();
        Assert.IsNull(plugin.PullAggregate());
    }

    [TestMethod]
    public void PullAggregate_ReturnsResultAfterBatchSubmitted()
    {
        using var plugin = new L2GatewayPlugin();
        plugin.ReceiveBatch(SampleCommitment(batchNumber: 1));
        plugin.ReceiveBatch(SampleCommitment(batchNumber: 2));
        var aggregated = plugin.PullAggregate();
        Assert.IsNotNull(aggregated);
        Assert.AreEqual(2, aggregated.Constituents.Count);
    }

    [TestMethod]
    public void Plugin_NameAndDescription_AreNonEmpty()
    {
        using var plugin = new L2GatewayPlugin();
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Description));
        StringAssert.Contains(plugin.Name, "L2Gateway");
    }

    // ---------------------------------------------------------------------------------------------
    // IGlobalRootPublisher wiring — connects the off-chain aggregator to the on-chain proof-gated
    // PublishGlobalRoot path. These tests exercise the full SubmitBatch → Aggregate → publish
    // flow with the NoOpGlobalRootPublisher (which records the call), so the wiring is proven
    // without an L1 RPC.
    // ---------------------------------------------------------------------------------------------

    private static readonly ReadOnlyMemory<byte> SampleVkId = new byte[32] {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20,
    };

    [TestMethod]
    public void DefaultGlobalRootPublisher_IsNoOp()
    {
        using var plugin = new L2GatewayPlugin();
        Assert.IsInstanceOfType(plugin.GlobalRootPublisher, typeof(NoOpGlobalRootPublisher));
        Assert.AreEqual(ReadOnlyMemory<byte>.Empty, plugin.GlobalRootVerificationKeyId);
    }

    [TestMethod]
    public void UseGlobalRootPublisher_OverridesDefault_AndRejectsNull()
    {
        using var plugin = new L2GatewayPlugin();
        var custom = new NoOpGlobalRootPublisher();  // stand-in for an RPC-backed impl
        plugin.UseGlobalRootPublisher(custom);
        Assert.AreSame(custom, plugin.GlobalRootPublisher);

        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.UseGlobalRootPublisher(null!));
    }

    [TestMethod]
    public void SetGlobalRootVerificationKeyId_RejectsWrongLength_AndDefensivelyCopies()
    {
        using var plugin = new L2GatewayPlugin();
        Assert.ThrowsExactly<ArgumentException>(() => plugin.SetGlobalRootVerificationKeyId(new byte[31]));
        Assert.ThrowsExactly<ArgumentException>(() => plugin.SetGlobalRootVerificationKeyId(new byte[33]));

        plugin.SetGlobalRootVerificationKeyId(SampleVkId);
        Assert.AreEqual(32, plugin.GlobalRootVerificationKeyId.Length);

        // Defensive copy: mutating the original buffer the caller passed must NOT retroactively
        // change the id the plugin forwards to the publisher.
        var mutable = new byte[32];
        SampleVkId.Span.CopyTo(mutable);
        plugin.SetGlobalRootVerificationKeyId(mutable);
        mutable[0] = 0xFF;
        Assert.AreEqual(0x01, plugin.GlobalRootVerificationKeyId.Span[0],
            "the plugin must hold its own copy — caller mutation must not leak in");
    }

    [TestMethod]
    public async Task PublishAggregateAsync_ReturnsZero_WhenNothingPending()
    {
        using var plugin = new L2GatewayPlugin();
        plugin.SetGlobalRootVerificationKeyId(SampleVkId);
        var noOp = (NoOpGlobalRootPublisher)plugin.GlobalRootPublisher;

        var tx = await plugin.PublishAggregateAsync(batchEpoch: 7);
        Assert.AreEqual(UInt256.Zero, tx);
        Assert.AreEqual(0, noOp.CallCount, "no pending batches → publisher must NOT be called");
    }

    [TestMethod]
    public async Task PublishAggregateAsync_PublishesAggregate_WithEpochAndGlobalRoot()
    {
        // The key end-to-end wiring test: submit batches → PublishAggregateAsync drains the
        // aggregator and forwards the aggregated global root + epoch to the publisher. The
        // NoOpGlobalRootPublisher records the call so we assert the plugin actually invoked it
        // with the aggregator's output.
        using var plugin = new L2GatewayPlugin();
        plugin.UseAggregator(new BinaryTreeAggregator());  // produces a real aggregate
        plugin.SetGlobalRootVerificationKeyId(SampleVkId);
        var noOp = (NoOpGlobalRootPublisher)plugin.GlobalRootPublisher;

        plugin.ReceiveBatch(SampleCommitment(chainId: 1001, batchNumber: 1));
        plugin.ReceiveBatch(SampleCommitment(chainId: 2002, batchNumber: 1));
        Assert.AreEqual(2, plugin.Aggregator.PendingCount);

        await plugin.PublishAggregateAsync(batchEpoch: 42);

        Assert.AreEqual(1, noOp.CallCount, "publisher must be called exactly once");
        Assert.AreEqual(42UL, noOp.LastEpoch, "epoch must be forwarded to the publisher");
        Assert.IsNotNull(noOp.LastGlobalRoot, "global root must be forwarded");
        var forwardedRoot = (UInt256)noOp.LastGlobalRoot!;
        Assert.IsFalse(forwardedRoot.Equals(UInt256.Zero),
            "the aggregated global root must be non-zero (BinaryTreeAggregator hashes its leaves)");
        // Pending drained by the publish.
        Assert.AreEqual(0, plugin.Aggregator.PendingCount);
    }

    [TestMethod]
    public async Task PublishAggregateAsync_Faults_WhenVkIdNotConfigured()
    {
        using var plugin = new L2GatewayPlugin();
        plugin.UseAggregator(new BinaryTreeAggregator());
        plugin.ReceiveBatch(SampleCommitment());

        // No SetGlobalRootVerificationKeyId call → publish must fault loudly rather than silently
        // submitting a publish with an empty VK id (which the on-chain contract would reject
        // with "verification key id must be 32 bytes" anyway — surface it earlier, at the plugin).
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await plugin.PublishAggregateAsync(batchEpoch: 1));
    }

    [TestMethod]
    public async Task PublishAggregateAsync_UsesSwappedPublisher()
    {
        // Operator injects a custom publisher (the RPC-backed production impl). Confirm the
        // plugin routes through it rather than the default NoOp.
        using var plugin = new L2GatewayPlugin();
        plugin.UseAggregator(new BinaryTreeAggregator());
        plugin.SetGlobalRootVerificationKeyId(SampleVkId);
        var custom = new NoOpGlobalRootPublisher();
        plugin.UseGlobalRootPublisher(custom);

        plugin.ReceiveBatch(SampleCommitment());
        await plugin.PublishAggregateAsync(batchEpoch: 9);

        Assert.AreEqual(1, custom.CallCount, "the swapped-in publisher must receive the call");
    }
}
