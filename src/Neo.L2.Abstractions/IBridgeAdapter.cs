namespace Neo.L2;

/// <summary>
/// L2-side bridge adapter. Translates between native L2 contract calls and the canonical
/// asset accounting that <c>NeoHub.SharedBridge</c> records on L1.
/// </summary>
/// <remarks>
/// See doc.md §3.2 (SharedBridge), §11 (Bridge), §15.2 (deposit), §15.3 (withdrawal).
/// </remarks>
public interface IBridgeAdapter
{
    /// <summary>
    /// Apply a verified L1 → L2 deposit message: mint the bridged asset on L2 (or release it
    /// from escrow). Idempotent on <c>(SourceChainId, Nonce)</c>.
    /// </summary>
    ValueTask ApplyDepositAsync(
        CrossChainMessage depositMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Record an L2 → L1 withdrawal so that the next batch's withdrawal Merkle tree includes it.
    /// </summary>
    /// <returns>The leaf hash that was inserted, for client tracking.</returns>
    ValueTask<UInt256> EnqueueWithdrawalAsync(
        WithdrawalRequest withdrawal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up the canonical L1 asset hash for a given L2-side asset, using the local cache of
    /// <c>NeoHub.TokenRegistry</c>.
    /// </summary>
    ValueTask<UInt160?> ResolveCanonicalAsync(
        UInt160 l2Asset,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A withdrawal request emitted on L2, waiting to be sealed into a batch's withdrawal Merkle tree.
/// </summary>
public sealed record WithdrawalRequest
{
    /// <summary>L2 contract that emitted the withdrawal (typically <c>L2BridgeContract</c>).</summary>
    public required UInt160 EmittingContract { get; init; }

    /// <summary>Address that initiated the withdrawal on L2.</summary>
    public required UInt160 L2Sender { get; init; }

    /// <summary>L1 address that should receive the asset.</summary>
    public required UInt160 L1Recipient { get; init; }

    /// <summary>L2-side asset hash being withdrawn.</summary>
    public required UInt160 L2Asset { get; init; }

    /// <summary>
    /// Canonical L1 payout amount in the L1 asset's smallest unit. For assets whose
    /// L2 decimals differ from L1, the L2 native bridge converts the burned L2 amount
    /// before emitting the withdrawal record.
    /// </summary>
    public required System.Numerics.BigInteger Amount { get; init; }

    /// <summary>Per-(chain, sender) monotonic nonce for replay protection.</summary>
    public required ulong Nonce { get; init; }
}
