using Neo.L2;
using Neo.L2.Telemetry;
using Neo.Plugins.L2;

namespace Neo.Plugins.L2Batch.UnitTests;

[TestClass]
public class UT_L2BatchPlugin
{
    private static L2BatchCommitment SampleCommitment() => new()
    {
        ChainId = 1001,
        BatchNumber = 1,
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
    public void DispatchSealed_OneSubscriberThrows_OthersStillFire()
    {
        // Regression for iter 170: a buggy OnBatchSealed subscriber would previously
        // surface its exception to Neo's Blockchain.Committed via standard .NET event
        // semantics (first-throw aborts further dispatch). Now isolated so each
        // subscriber's failure is contained.
        var fired = new bool[3];
        EventHandler<L2BatchCommitment>? handler = null;
        handler += (_, _) => fired[0] = true;
        handler += (_, _) => throw new InvalidOperationException("buggy subscriber");
        handler += (_, _) => fired[2] = true;

        var metrics = new InMemoryMetrics();
        L2BatchPlugin.DispatchSealed(this, handler, SampleCommitment(), metrics);

        Assert.IsTrue(fired[0], "subscriber 0 must fire");
        Assert.IsTrue(fired[2], "subscriber 2 must fire even after subscriber 1 threw");
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.BatchSealedSubscriberFailures),
            "subscriber failures must be counted");
    }

    [TestMethod]
    public void DispatchSealed_NoSubscribers_DoesNotThrow()
    {
        var metrics = new InMemoryMetrics();
        L2BatchPlugin.DispatchSealed(this, handler: null, SampleCommitment(), metrics);
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.BatchSealedSubscriberFailures));
    }

    [TestMethod]
    public void DispatchSealed_MultipleThrowsAllCounted()
    {
        EventHandler<L2BatchCommitment>? handler = null;
        for (var i = 0; i < 4; i++)
            handler += (_, _) => throw new InvalidOperationException("nope");

        var metrics = new InMemoryMetrics();
        L2BatchPlugin.DispatchSealed(this, handler, SampleCommitment(), metrics);
        Assert.AreEqual(4, metrics.GetCounter(MetricNames.BatchSealedSubscriberFailures));
    }

    [TestMethod]
    public void WithMetrics_RejectsNullMetrics()
    {
        // Pin L2BatchPlugin.cs:40. Symmetric to other plugin WithMetrics pins.
        using var plugin = new L2BatchPlugin();
        Assert.ThrowsExactly<ArgumentNullException>(() => plugin.WithMetrics(null!));
    }

    [TestMethod]
    public void Plugin_NameAndDescription_AreNonEmpty()
    {
        // Surfaced in plugin host startup logs; pin so a refactor doesn't accidentally
        // empty either. Same convention as UT_L2BridgePlugin / UT_L2GatewayPlugin /
        // UT_L2ProverPlugin.
        using var plugin = new L2BatchPlugin();
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(plugin.Description));
    }
}
