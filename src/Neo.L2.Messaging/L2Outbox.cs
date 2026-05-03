using Neo.L2.State;
using Neo.L2.Telemetry;

namespace Neo.L2.Messaging;

/// <summary>
/// Per-batch outbox that splits emitted messages by destination class and produces the two
/// Merkle roots required by <see cref="L2BatchCommitment.L2ToL1MessageRoot"/> and
/// <see cref="L2BatchCommitment.L2ToL2MessageRoot"/>.
/// </summary>
/// <remarks>
/// Anything with <see cref="CrossChainMessage.TargetChainId"/> equal to <c>0</c> is treated
/// as L2 → L1; any other target is treated as L2 → L2.
/// </remarks>
public sealed class L2Outbox
{
    /// <summary>Special value that names Neo L1 in <see cref="CrossChainMessage.TargetChainId"/>.</summary>
    public const uint L1ChainId = 0;

    private readonly MessageTree _l2ToL1 = new();
    private readonly MessageTree _l2ToL2 = new();
    private readonly IL2Metrics _metrics;

    /// <summary>Construct, optionally wired to a metrics sink.</summary>
    public L2Outbox(IL2Metrics? metrics = null)
    {
        _metrics = metrics ?? NoOpMetrics.Instance;
    }

    /// <summary>Number of L2 → L1 messages staged.</summary>
    public int L2ToL1Count => _l2ToL1.Count;

    /// <summary>Number of L2 → L2 messages staged.</summary>
    public int L2ToL2Count => _l2ToL2.Count;

    /// <summary>Add a message and return the underlying tree's index.</summary>
    public int Add(CrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var idx = message.TargetChainId == L1ChainId
            ? _l2ToL1.Add(message)
            : _l2ToL2.Add(message);
        // SafeIncrementCounter: the message is committed to the tree before the metric
        // fires; a metric throw must not surface as a caller-visible "Add failed", which
        // would leave the caller out of sync with the message tree's actual contents.
        _metrics.SafeIncrementCounter(MetricNames.MessagesEmitted);
        return idx;
    }

    /// <summary>Merkle root of L2 → L1 messages (goes into <c>L2BatchCommitment.L2ToL1MessageRoot</c>).</summary>
    public UInt256 L2ToL1Root => _l2ToL1.Root;

    /// <summary>Merkle root of L2 → L2 messages (goes into <c>L2BatchCommitment.L2ToL2MessageRoot</c>).</summary>
    public UInt256 L2ToL2Root => _l2ToL2.Root;

    /// <summary>Get the tree of L2 → L1 messages (e.g. to generate inclusion proofs).</summary>
    public MessageTree L2ToL1Tree => _l2ToL1;

    /// <summary>Get the tree of L2 → L2 messages.</summary>
    public MessageTree L2ToL2Tree => _l2ToL2;
}
