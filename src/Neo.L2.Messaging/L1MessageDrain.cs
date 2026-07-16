using Neo.L2.Bridge;

namespace Neo.L2.Messaging;

/// <summary>
/// Helpers for composing the batcher's synchronous L1-message drain from one or more
/// production sources (SharedBridge deposits, MessageRouter inbox, tests).
/// </summary>
/// <remarks>
/// See doc.md §15.1 / §15.2. Every drain must fail closed on null results. Combined drains
/// sort by <c>(SourceChainId, Nonce)</c> and reject colliding keys so two independent L1
/// nonce spaces cannot silently overwrite each other under <c>sourceChainId = 0</c>.
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

        merged.Sort(static (a, b) =>
        {
            var chain = a.SourceChainId.CompareTo(b.SourceChainId);
            return chain != 0 ? chain : a.Nonce.CompareTo(b.Nonce);
        });

        var seen = new HashSet<(uint, ulong)>();
        for (var i = 0; i < merged.Count; i++)
        {
            var key = (merged[i].SourceChainId, merged[i].Nonce);
            if (!seen.Add(key))
                throw new InvalidOperationException(
                    $"duplicate L1 message key ({key.Item1},{key.Item2}) across combined drains — " +
                    "SharedBridge deposit nonces and MessageRouter nonces must not collide under sourceChainId=0");
        }

        if (merged.Count <= max)
            return merged;
        return merged.GetRange(0, max);
    }
}
