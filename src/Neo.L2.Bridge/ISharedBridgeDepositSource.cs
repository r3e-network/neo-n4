namespace Neo.L2.Bridge;

/// <summary>
/// Production L1 deposit inbox for one L2 chain. Discovers
/// <c>NeoHub.SharedBridge.DepositEnqueued</c> events, materializes the canonical
/// <see cref="CrossChainMessage"/> for each deposit, and drains them into the next batch.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle (mirrors forced-inclusion reservation / finality ownership):
/// </para>
/// <list type="number">
///   <item><see cref="ScanAsync"/> discovers and materializes deposits.</item>
///   <item><see cref="Drain"/> reserves deposits for the batcher (must not re-offer them).</item>
///   <item><see cref="ConfirmConsumed"/> permanently retires a nonce after the sealed batch
///   that included it has been durably persisted.</item>
///   <item><see cref="ReleaseReservations"/> returns reserved nonces to the ready set if
///   durable persistence fails before acknowledgement.</item>
/// </list>
/// See doc.md §9.1 / §15.2. SharedBridge does not enqueue <c>NeoHub.MessageRouter</c>.
/// </remarks>
public interface ISharedBridgeDepositSource
{
    /// <summary>L2 chain id this source serves.</summary>
    uint ChainId { get; }

    /// <summary>
    /// Scan finalized L1 blocks, persist newly observed deposit nonces, and warm the
    /// ready set from <c>GetDeposit</c> (no-op for pure in-memory sources).
    /// </summary>
    ValueTask<int> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Non-mutating view of ready (not reserved, not consumed) deposits.
    /// </summary>
    IReadOnlyList<CrossChainMessage> Peek(int maxMessages);

    /// <summary>
    /// Reserve and return up to <paramref name="maxMessages"/> ready deposits in nonce order
    /// for inclusion in the next batch. Reserved deposits must not reappear until
    /// <see cref="ConfirmConsumed"/> or <see cref="ReleaseReservations"/>.
    /// </summary>
    IReadOnlyList<CrossChainMessage> Drain(int maxMessages);

    /// <summary>
    /// Permanently retire a deposit nonce after the batch that included it was durably sealed.
    /// Idempotent for already-consumed nonces.
    /// </summary>
    void ConfirmConsumed(ulong nonce);

    /// <summary>
    /// Return previously drained reservations to the ready set after a failed seal/persist.
    /// Nonces that were never reserved are ignored.
    /// </summary>
    void ReleaseReservations(IEnumerable<ulong> nonces);
}
