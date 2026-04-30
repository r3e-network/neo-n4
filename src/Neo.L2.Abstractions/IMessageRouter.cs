namespace Neo.L2;

/// <summary>
/// L2-side cross-chain message router. Reads inbound messages from the L1 inbox queue and
/// publishes outbound messages destined for L1 or other L2s.
/// </summary>
/// <remarks>
/// See doc.md §3.2 (MessageRouter), §10 (Neo Connect), and §15 (key flows).
/// The L1 contract counterpart lives in <c>NeoHub.MessageRouter</c>.
/// </remarks>
public interface IMessageRouter
{
    /// <summary>
    /// Drain the L1 → L2 inbox queue up to <paramref name="maxMessages"/>. The returned messages
    /// are consumed once and must be included in the next L2 batch.
    /// </summary>
    /// <param name="chainId">L2 chain identifier of the calling node.</param>
    /// <param name="maxMessages">Upper bound on messages to fetch in this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<CrossChainMessage>> DequeueL1MessagesAsync(
        uint chainId,
        int maxMessages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist outbound messages emitted during the current batch. The router computes
    /// <see cref="L2BatchCommitment.L2ToL1MessageRoot"/> and
    /// <see cref="L2BatchCommitment.L2ToL2MessageRoot"/> from the staged set when sealing.
    /// </summary>
    ValueTask EnqueueOutboundAsync(
        IReadOnlyList<CrossChainMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Produce a Merkle inclusion proof for <paramref name="messageHash"/> against a previously
    /// finalized batch root.
    /// </summary>
    /// <returns>The proof bytes, or <c>null</c> if the message is not yet in a finalized batch.</returns>
    ValueTask<ReadOnlyMemory<byte>?> GetMessageProofAsync(
        UInt256 messageHash,
        CancellationToken cancellationToken = default);
}
