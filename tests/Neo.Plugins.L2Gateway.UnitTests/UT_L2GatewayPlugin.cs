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
}
