using Neo.L2.Bridge;

namespace Neo.L2.Messaging;

/// <summary>
/// Helpers for composing the batcher's synchronous L1-message drain from one or more
/// production sources (SharedBridge deposits, MessageRouter inbox, tests).
/// </summary>
/// <remarks>
/// See doc.md §15.1 / §15.2. Every drain must fail closed on null results. SharedBridge deposits
/// and MessageRouter inbox entries both arrive with <c>sourceChainId = 0</c> while advancing
/// independent per-target-chain counters, and the L2 applies them through separate native
/// contracts, so a combined drain may legitimately carry equal nonces from the two families.
/// What it may not carry is the same message twice, two deposits claiming one
/// <c>(sourceChainId, nonce)</c> slot, or an order that is not total — the merged sequence feeds
/// <c>l1MessageHash</c>, so it must be reproducible across seal retries.
/// </remarks>
public static class L1MessageDrain
{
    /// <summary>
    /// Combine one or more drains into a single <c>Func&lt;int, IReadOnlyList&lt;CrossChainMessage&gt;&gt;</c>
    /// suitable for <c>L2BatchPlugin.WithSealingInputs</c> / <c>BatchSealer</c>.
    /// </summary>
    public static Func<int, IReadOnlyList<CrossChainMessage>> Combine(
        params Func<int, IReadOnlyList<CrossChainMessage>>[] drains)
    {
        ArgumentNullException.ThrowIfNull(drains);
        if (drains.Length == 0)
            throw new ArgumentException("at least one L1 message drain is required", nameof(drains));
        for (var i = 0; i < drains.Length; i++)
        {
            if (drains[i] is null)
                throw new ArgumentNullException(nameof(drains), $"drains[{i}] is null");
        }

        // Capture a defensive copy so later mutation of the caller's array cannot
        // change the sealed composition root.
        var snapshot = drains.ToArray();
        return max => DrainAll(snapshot, max);
    }

    /// <summary>
    /// Adapter for SharedBridge deposits used from the sealer's sync drain boundary.
    /// Calls <see cref="ISharedBridgeDepositSource.ScanAsync"/> then
    /// <see cref="ISharedBridgeDepositSource.Drain"/> so newly finalized L1 deposits
    /// are discovered at seal time without a separate operator poll loop — same pattern as
    /// <c>RpcForcedInclusionSource.DrainAsync</c> scanning events before returning entries.
    /// Optional proactive <c>ScanAsync</c> calls remain safe and idempotent.
    /// </summary>
    public static Func<int, IReadOnlyList<CrossChainMessage>> FromDeposits(
        ISharedBridgeDepositSource deposits)
    {
        ArgumentNullException.ThrowIfNull(deposits);
        return max =>
        {
            // Scan discovers + materializes; Drain reserves. In-memory sources no-op Scan.
            deposits.ScanAsync().AsTask().GetAwaiter().GetResult();
            return deposits.Drain(max)
                ?? throw new InvalidOperationException(
                    "SharedBridge deposit Drain returned null");
        };
    }

    /// <summary>
    /// Adapter for an async MessageRouter dequeue used from the sealer's sync drain boundary.
    /// Blocks the sealer thread only for the awaited RPC fanout — same contract as other
    /// production pollers called from commit-path composition.
    /// </summary>
    public static Func<int, IReadOnlyList<CrossChainMessage>> FromRouter(
        IMessageRouter router,
        uint chainId)
    {
        ArgumentNullException.ThrowIfNull(router);
        if (chainId == 0)
            throw new ArgumentOutOfRangeException(nameof(chainId), "chain id 0 is reserved for L1");
        return max => router.DequeueL1MessagesAsync(chainId, max)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    private static IReadOnlyList<CrossChainMessage> DrainAll(
        Func<int, IReadOnlyList<CrossChainMessage>>[] drains,
        int max)
    {
        if (max < 0)
            throw new ArgumentOutOfRangeException(nameof(max));
        if (max == 0)
            return Array.Empty<CrossChainMessage>();

        var merged = new List<CrossChainMessage>();
        for (var d = 0; d < drains.Length; d++)
        {
            var batch = drains[d](max)
                ?? throw new InvalidOperationException($"L1 message drain[{d}] returned null");
            if (batch.Count > max)
                throw new InvalidOperationException(
                    $"L1 message drain[{d}] returned {batch.Count}, maximum is {max}");
            for (var i = 0; i < batch.Count; i++)
            {
                var message = batch[i]
                    ?? throw new InvalidOperationException(
                        $"L1 message drain[{d}] returned null at index {i}");
                merged.Add(message);
            }
        }

        if (merged.Count == 0)
            return Array.Empty<CrossChainMessage>();

        merged.Sort(Compare);

        var seenMessages = new HashSet<CrossChainMessage>();
        var seenDepositSlots = new HashSet<(uint, ulong)>();
        for (var i = 0; i < merged.Count; i++)
        {
            var message = merged[i];
            if (!seenMessages.Add(message))
                throw new InvalidOperationException(
                    $"duplicate L1 inbox message (sourceChainId={message.SourceChainId}, " +
                    $"nonce={message.Nonce}, type={message.MessageType}) returned by combined drains");

            // SharedBridge deposits and MessageRouter inbox entries both carry sourceChainId=0 but
            // advance independent per-target-chain counters, and the L2 applies them through
            // separate native contracts. Only the deposit slot is claimed by (sourceChainId, nonce).
            if (message.MessageType == MessageType.Deposit
                && !seenDepositSlots.Add((message.SourceChainId, message.Nonce)))
                throw new InvalidOperationException(
                    $"two distinct SharedBridge deposits claim slot ({message.SourceChainId}, " +
                    $"{message.Nonce}) across combined drains");
        }

        if (merged.Count <= max)
            return merged;
        return merged.GetRange(0, max);
    }

    /// <summary>
    /// Total ordering over the L1 inbox. <c>(SourceChainId, Nonce)</c> alone does not order a
    /// combined drain, and <c>List.Sort</c> is unstable, so every remaining field participates
    /// — otherwise two seals of identical inputs could emit different sequences and therefore
    /// different <c>l1MessageHash</c> values.
    /// </summary>
    private static int Compare(CrossChainMessage a, CrossChainMessage b)
    {
        var byChain = a.SourceChainId.CompareTo(b.SourceChainId);
        if (byChain != 0) return byChain;
        var byNonce = a.Nonce.CompareTo(b.Nonce);
        if (byNonce != 0) return byNonce;
        var byTarget = a.TargetChainId.CompareTo(b.TargetChainId);
        if (byTarget != 0) return byTarget;
        var byType = a.MessageType.CompareTo(b.MessageType);
        if (byType != 0) return byType;
        var bySender = a.Sender.GetSpan().SequenceCompareTo(b.Sender.GetSpan());
        if (bySender != 0) return bySender;
        var byReceiver = a.Receiver.GetSpan().SequenceCompareTo(b.Receiver.GetSpan());
        if (byReceiver != 0) return byReceiver;
        var byPayload = a.Payload.Span.SequenceCompareTo(b.Payload.Span);
        if (byPayload != 0) return byPayload;
        // Two messages that tie on every field above are still distinct records if their hashes
        // differ, and List.Sort is unstable — so the hash breaks the tie to keep one total order.
        return a.MessageHash.GetSpan().SequenceCompareTo(b.MessageHash.GetSpan());
    }
}
