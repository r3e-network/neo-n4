namespace Neo.L2;

/// <summary>
/// Fail-closed validation for the in-memory <see cref="L2BatchBlock"/> execution timeline
/// (<see cref="BatchExecutionRequest.BlockTimeline"/>). Inconsistent timelines are a fatal
/// protocol error: a batcher whose timeline does not describe the sealed batch must not seal,
/// and an executor that receives one must not execute (doc.md §7.2, §8.1).
/// </summary>
public static class BlockTimelineValidator
{
    /// <summary>
    /// Validate the timeline against the transaction count and, when supplied, the batch's block
    /// range and block context. Empty timelines are accepted only for transaction-free batches.
    /// </summary>
    public static void Validate(
        IReadOnlyList<L2BatchBlock>? timeline,
        int transactionCount,
        ulong? firstBlock = null,
        ulong? lastBlock = null,
        BatchBlockContext? blockContext = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (timeline.Count == 0)
        {
            if (transactionCount != 0)
                throw new InvalidOperationException(
                    "batch execution requires a per-block timeline; got none for a batch with transactions");
            return;
        }

        for (var index = 0; index < timeline.Count; index++)
        {
            var entry = timeline[index]
                ?? throw new InvalidOperationException($"BlockTimeline[{index}] is null");
            if (entry.TransactionCount < 0)
                throw new InvalidOperationException(
                    $"BlockTimeline[{index}] carries a negative transaction count");
            if (index > 0)
            {
                var previous = timeline[index - 1]
                    ?? throw new InvalidOperationException($"BlockTimeline[{index - 1}] is null");
                if (entry.BlockIndex != previous.BlockIndex + 1)
                    throw new InvalidOperationException(
                        $"BlockTimeline[{index}] block index {entry.BlockIndex} is not contiguous after {previous.BlockIndex}");
                if (entry.BlockTimestamp < previous.BlockTimestamp)
                    throw new InvalidOperationException(
                        $"BlockTimeline[{index}] timestamp {entry.BlockTimestamp} precedes the previous block's {previous.BlockTimestamp}");
            }
            if (blockContext is not null
                && (entry.BlockTimestamp < blockContext.FirstBlockTimestamp
                    || entry.BlockTimestamp > blockContext.LastBlockTimestamp))
                throw new InvalidOperationException(
                    $"BlockTimeline[{index}] timestamp {entry.BlockTimestamp} is outside the block context range " +
                    $"[{blockContext.FirstBlockTimestamp}, {blockContext.LastBlockTimestamp}]");
        }

        long attributed = 0;
        foreach (var entry in timeline)
        {
            attributed = checked(attributed + entry!.TransactionCount);
        }
        if (attributed != transactionCount)
            throw new InvalidOperationException(
                $"BlockTimeline attributes {attributed} transactions but the batch carries {transactionCount}");

        if (firstBlock is not null && timeline[0]!.BlockIndex != firstBlock.Value)
            throw new InvalidOperationException(
                $"BlockTimeline starts at block {timeline[0]!.BlockIndex}, expected the batch's first block {firstBlock.Value}");
        if (lastBlock is not null
            && timeline.Count != checked((long)(lastBlock.Value - timeline[0]!.BlockIndex + 1)))
            throw new InvalidOperationException(
                $"BlockTimeline covers {timeline.Count} blocks, expected {lastBlock.Value - timeline[0]!.BlockIndex + 1} for the batch's block range");
    }
}
