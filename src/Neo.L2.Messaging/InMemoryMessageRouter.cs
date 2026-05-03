using Neo.L2.State;

namespace Neo.L2.Messaging;

/// <summary>
/// In-memory <see cref="IMessageRouter"/> for tests and devnet boot. Production nodes use the
/// plugin-backed implementation that talks to NeoHub.
/// </summary>
public sealed class InMemoryMessageRouter : IMessageRouter
{
    private readonly L1MessageInbox _inbox;
    private readonly L2Outbox _outbox;
    // ConcurrentDictionary because GetMessageProofAsync can be called from RPC handler
    // threads while RecordFinalized is being driven from the settlement-pipeline thread —
    // a plain Dictionary would have a small but real chance of corruption / NRE under
    // the race. Cost is negligible for the in-memory test backend.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<UInt256, FinalizedEntry> _finalized = new();

    /// <summary>The L1 inbox feeding inbound messages.</summary>
    public L1MessageInbox Inbox => _inbox;

    /// <summary>The current-batch outbox.</summary>
    public L2Outbox Outbox => _outbox;

    /// <summary>Construct with optional pre-configured inbox/outbox (used in tests).</summary>
    public InMemoryMessageRouter(L1MessageInbox? inbox = null, L2Outbox? outbox = null)
    {
        _inbox = inbox ?? new L1MessageInbox();
        _outbox = outbox ?? new L2Outbox();
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<CrossChainMessage>> DequeueL1MessagesAsync(
        uint chainId, int maxMessages, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<IReadOnlyList<CrossChainMessage>>(_inbox.Dequeue(maxMessages));
    }

    /// <inheritdoc />
    public ValueTask EnqueueOutboundAsync(IReadOnlyList<CrossChainMessage> messages, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(messages);
        foreach (var m in messages) _outbox.Add(m);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<ReadOnlyMemory<byte>?> GetMessageProofAsync(UInt256 messageHash, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Null UInt256 would NRE inside ConcurrentDictionary's hash lookup; surface
        // it at the API boundary like the iter-148/149 sweep.
        ArgumentNullException.ThrowIfNull(messageHash);
        if (!_finalized.TryGetValue(messageHash, out var entry))
            return new ValueTask<ReadOnlyMemory<byte>?>((ReadOnlyMemory<byte>?)null);
        return new ValueTask<ReadOnlyMemory<byte>?>(entry.ProofBytes);
    }

    /// <summary>
    /// Mark a message as finalized in a particular batch and remember the proof bytes for later
    /// retrieval by <see cref="GetMessageProofAsync"/>. Used by tests / settlement plugins.
    /// </summary>
    /// <remarks>
    /// Defensive copy: ReadOnlyMemory&lt;byte&gt; gives immutability semantics for the *view*,
    /// but the underlying array can still be mutated through other references. Without
    /// the copy, a caller who reuses a scratch buffer or mutates their copy after
    /// passing it in would silently corrupt the stored proof. Same pattern as
    /// InMemoryL2RpcStore.RecordWithdrawalProof.
    /// </remarks>
    public void RecordFinalized(UInt256 messageHash, ReadOnlyMemory<byte> proofBytes)
    {
        ArgumentNullException.ThrowIfNull(messageHash);
        _finalized[messageHash] = new FinalizedEntry(proofBytes.ToArray());
    }

    private sealed record FinalizedEntry(ReadOnlyMemory<byte> ProofBytes);
}
