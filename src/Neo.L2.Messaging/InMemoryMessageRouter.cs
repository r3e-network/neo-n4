using Neo.L2.Persistence;
using Neo.L2.State;

namespace Neo.L2.Messaging;

/// <summary>
/// <see cref="IMessageRouter"/> with an in-memory inbox/outbox and a pluggable
/// <see cref="IL2KeyValueStore"/> for the finalized-proof map. Production wires
/// <see cref="RocksDbKeyValueStore"/> so finalized message proofs survive node
/// restarts and remain queryable via <see cref="GetMessageProofAsync"/>.
/// </summary>
/// <remarks>
/// Inbox + outbox are transient — on restart they're re-drained from L1. The
/// finalized-proof map is durability-critical: once a batch's messages finalize, RPC
/// callers expect to query their inclusion proofs indefinitely.
/// </remarks>
public sealed class InMemoryMessageRouter : IMessageRouter, IDisposable
{
    private readonly L1MessageInbox _inbox;
    private readonly L2Outbox _outbox;
    private readonly IL2KeyValueStore _finalized;
    private readonly bool _ownsFinalized;
    private bool _disposed;

    /// <summary>The L1 inbox feeding inbound messages.</summary>
    public L1MessageInbox Inbox => _inbox;

    /// <summary>The current-batch outbox.</summary>
    public L2Outbox Outbox => _outbox;

    /// <summary>Construct with optional pre-configured inbox/outbox. Finalized proofs
    /// default to an in-memory KV backing — production passes a RocksDB backing via the
    /// alternate ctor.</summary>
    public InMemoryMessageRouter(L1MessageInbox? inbox = null, L2Outbox? outbox = null)
        : this(inbox, outbox, new InMemoryKeyValueStore(), ownsFinalized: true) { }

    /// <summary>
    /// Construct with a caller-supplied <see cref="IL2KeyValueStore"/> for the
    /// finalized-proof map. Production wires <see cref="RocksDbKeyValueStore"/> here so
    /// finalized message proofs survive node restarts.
    /// </summary>
    public InMemoryMessageRouter(
        L1MessageInbox? inbox,
        L2Outbox? outbox,
        IL2KeyValueStore finalized,
        bool ownsFinalized = false)
    {
        ArgumentNullException.ThrowIfNull(finalized);
        _inbox = inbox ?? new L1MessageInbox();
        _outbox = outbox ?? new L2Outbox();
        _finalized = finalized;
        _ownsFinalized = ownsFinalized;
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
        // Null UInt256 would NRE inside the underlying store's lookup; surface
        // it at the API boundary like the iter-148/149 sweep.
        ArgumentNullException.ThrowIfNull(messageHash);
        var bytes = _finalized.Get(messageHash.GetSpan());
        if (bytes is null)
            return new ValueTask<ReadOnlyMemory<byte>?>((ReadOnlyMemory<byte>?)null);
        return new ValueTask<ReadOnlyMemory<byte>?>(new ReadOnlyMemory<byte>(bytes));
    }

    /// <summary>
    /// Mark a message as finalized in a particular batch and remember the proof bytes for later
    /// retrieval by <see cref="GetMessageProofAsync"/>. Used by tests / settlement plugins.
    /// </summary>
    /// <remarks>
    /// Defensive copy via <see cref="IL2KeyValueStore.Put"/>'s contract: the underlying KV
    /// store materializes its own copy of the bytes so a caller who reuses a scratch buffer
    /// or mutates their copy after passing it in cannot silently corrupt the stored proof.
    /// </remarks>
    public void RecordFinalized(UInt256 messageHash, ReadOnlyMemory<byte> proofBytes)
    {
        ArgumentNullException.ThrowIfNull(messageHash);
        _finalized.Put(messageHash.GetSpan(), proofBytes.Span);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsFinalized) _finalized.Dispose();
    }
}
