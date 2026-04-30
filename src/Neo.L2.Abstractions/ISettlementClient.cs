namespace Neo.L2;

/// <summary>
/// L2-side client that submits sealed batches to <c>NeoHub.SettlementManager</c> (or, when
/// <see cref="L2ChainConfig.GatewayEnabled"/> is true, to Neo Gateway for aggregation).
/// </summary>
/// <remarks>
/// See doc.md §3.2 (SettlementManager), §4 (Neo Gateway), and §15.1 (transaction flow).
/// </remarks>
public interface ISettlementClient
{
    /// <summary>
    /// Submit <paramref name="commitment"/> and the (raw) public-input bundle to the L1
    /// settlement target. The call must be idempotent on <c>(ChainId, BatchNumber)</c>.
    /// </summary>
    /// <returns>The L1 transaction hash that carried the submission.</returns>
    ValueTask<UInt256> SubmitBatchAsync(
        L2BatchCommitment commitment,
        PublicInputs publicInputs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the canonical (finalized) state root recorded by NeoHub for a given chain.
    /// </summary>
    ValueTask<UInt256> GetCanonicalStateRootAsync(
        uint chainId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up the lifecycle status of a previously submitted batch.
    /// </summary>
    ValueTask<BatchStatus> GetBatchStatusAsync(
        uint chainId,
        ulong batchNumber,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Lifecycle of a batch submitted to NeoHub.
/// </summary>
public enum BatchStatus : byte
{
    /// <summary>NeoHub has no record of the batch.</summary>
    Unknown = 0,

    /// <summary>Submitted but not yet verified or finalized.</summary>
    Pending = 1,

    /// <summary>Inside the optimistic challenge window.</summary>
    Challengeable = 2,

    /// <summary>Verified and recorded as canonical state.</summary>
    Finalized = 3,

    /// <summary>Reverted due to fraud proof or governance action.</summary>
    Reverted = 4,
}
