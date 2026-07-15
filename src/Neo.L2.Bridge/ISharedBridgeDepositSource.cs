namespace Neo.L2.Bridge;

/// <summary>
/// Production L1 deposit inbox for one L2 chain. Discovers
/// <c>NeoHub.SharedBridge.DepositEnqueued</c> events, materializes the canonical
/// <see cref="CrossChainMessage"/> for each deposit, and drains them into the next batch.
/// </summary>
/// <remarks>
/// See doc.md §9.1 / §15.2. SharedBridge stores deposits under its own per-chain nonce
/// space and does not enqueue <c>NeoHub.MessageRouter</c>; operators must wire this
/// source (or an equivalent) as the batcher's L1 message drain for asset deposits.
/// </remarks>
public interface ISharedBridgeDepositSource
{
    /// <summary>L2 chain id this source serves.</summary>
    uint ChainId { get; }

    /// <summary>
    /// Scan finalized L1 blocks, persist newly observed deposit nonces, and warm the
    /// in-memory message cache from <c>GetDeposit</c>.
    /// </summary>
    ValueTask<int> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Return up to <paramref name="maxMessages"/> ready deposit messages in nonce order
    /// without removing them. Call <see cref="ConfirmConsumed"/> after the batch that
    /// included them is durably sealed.
    /// </summary>
    IReadOnlyList<CrossChainMessage> Peek(int maxMessages);

    /// <summary>
    /// Mark a deposit nonce as consumed by the local batcher so subsequent
    /// <see cref="Peek"/> calls skip it. Authoritative L2 replay protection remains on
    /// the native bridge consumed-set.
    /// </summary>
    void ConfirmConsumed(ulong nonce);
}
