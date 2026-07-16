using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;
using Neo.L2.State;

namespace Neo.L2.Messaging;

/// <summary>
/// Production <see cref="IMessageRouter"/> backed by a real Neo L1 RPC endpoint
/// for the inbound (L1→L2) side, with local outbox staging and a pluggable
/// finalized-proof store for the outbound side.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Inbound (L1→L2)</strong>: <see cref="DequeueL1MessagesAsync"/> optionally
/// scans finalized <c>L1ToL2Enqueued</c> events through
/// <see cref="RpcMessageRouterEventScanner"/>, then polls
/// <c>NeoHub.MessageRouter.GetL1ToL2(chainId, nonce)</c> + <c>IsConsumed(hash)</c>
/// for each known nonce. Genesis nonce seed and <see cref="RegisterInboundNonce"/>
/// remain migration/recovery hooks.
/// </para>
/// <para>
/// <strong>Outbound (L2-internal)</strong>: <see cref="EnqueueOutboundAsync"/>
/// stages outbound messages in a local <see cref="L2Outbox"/> the L2 batcher
/// consumes when sealing. Doesn't talk to L1.
/// </para>
/// <para>
/// <strong>Proofs</strong>: <see cref="GetMessageProofAsync"/> looks up
/// finalized inclusion proofs in a caller-supplied
/// <see cref="IL2KeyValueStore"/> (typically the same RocksDb-backed store the
/// L2 RPC plugin writes to when batches finalize).
/// </para>
/// </remarks>
public sealed class RpcMessageRouter : IMessageRouter, IDisposable
{
    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _routerHash;
    private readonly uint _chainId;
    private readonly TimeSpan _cacheTtl;
    private readonly bool _ownsRpc;
    private readonly bool _ownsFinalized;
    private readonly RpcMessageRouterEventScanner? _eventScanner;
    private readonly L2Outbox _outbox = new();
    private readonly IL2KeyValueStore _finalized;
    private readonly ConcurrentDictionary<ulong, byte> _knownNonces = new();
    private readonly ConcurrentDictionary<ulong, byte> _locallyConsumed = new();
    private readonly Lock _cacheGate = new();
    private List<CrossChainMessage>? _cachedInbound;
    private DateTime _cacheUntilUtc = DateTime.MinValue;
    private bool _disposed;

    /// <summary>
    /// Construct.
    /// </summary>
    /// <param name="rpc">L1 JSON-RPC client.</param>
    /// <param name="routerHash">Deployed <c>NeoHub.MessageRouter</c> hash.</param>
    /// <param name="chainId">L2 chain id this router serves (for inbox lookups).</param>
    /// <param name="knownInboundNonces">
    /// Optional migration/recovery seed; production discovery comes from
    /// <paramref name="eventScanner"/>.
    /// </param>
    /// <param name="finalized">
    /// Caller-supplied finalized-proof store. Operators wire
    /// <see cref="RocksDbKeyValueStore"/> here so messages remain queryable across
    /// restarts. Pass <see langword="null"/> to use an in-memory store
    /// (acceptable for devnet but proofs vanish on restart).
    /// </param>
    /// <param name="ownsFinalized">If true, <see cref="Dispose"/> disposes <paramref name="finalized"/>.</param>
    /// <param name="cacheTtl">Inbound-cache TTL (default 5s).</param>
    /// <param name="ownsRpc">If true, <see cref="Dispose"/> disposes the rpc client.</param>
    /// <param name="eventScanner">Optional durable finalized-event scanner; production supplies one.</param>
    public RpcMessageRouter(
        JsonRpcClient rpc,
        UInt160 routerHash,
        uint chainId,
        IEnumerable<ulong> knownInboundNonces,
        IL2KeyValueStore? finalized = null,
        bool ownsFinalized = true,
        TimeSpan? cacheTtl = null,
        bool ownsRpc = false,
        RpcMessageRouterEventScanner? eventScanner = null)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(routerHash);
        ArgumentNullException.ThrowIfNull(knownInboundNonces);
        _rpc = rpc;
        _routerHash = routerHash;
        _chainId = chainId;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(5);
        _ownsRpc = ownsRpc;
        _eventScanner = eventScanner;
        if (finalized is null)
        {
            _finalized = new InMemoryKeyValueStore();
            _ownsFinalized = true;
        }
        else
        {
            _finalized = finalized;
            _ownsFinalized = ownsFinalized;
        }
        foreach (var n in knownInboundNonces)
            RegisterInboundNonce(n);
        if (_eventScanner is not null)
        {
            foreach (var nonce in _eventScanner.LoadTrackedNonces())
                RegisterDiscoveredNonce(nonce);
        }
    }

    /// <summary>
    /// Add an L1→L2 nonce to the known set. Production scanners call this for each
    /// discovered event; operators may also seed nonces for migration/recovery.
    /// </summary>
    public bool RegisterInboundNonce(ulong nonce)
    {
        _eventScanner?.TrackNonce(nonce);
        return RegisterDiscoveredNonce(nonce);
    }

    /// <summary>Force a fresh L1 fanout on the next <see cref="DequeueL1MessagesAsync"/>.</summary>
    public void InvalidateInboundCache()
    {
        lock (_cacheGate)
        {
            _cachedInbound = null;
            _cacheUntilUtc = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Record a finalized message proof. Operators wire this to the L2 settlement
    /// plugin's "batch finalized" callback so future
    /// <see cref="GetMessageProofAsync"/> calls return the proof bytes for any
    /// message in that batch.
    /// </summary>
    public void RecordFinalizedProof(UInt256 messageHash, ReadOnlyMemory<byte> proofBytes)
    {
        ArgumentNullException.ThrowIfNull(messageHash);
        _finalized.Put(messageHash.GetSpan(), proofBytes.ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CrossChainMessage>> DequeueL1MessagesAsync(
        uint chainId, int maxMessages, CancellationToken cancellationToken = default)
    {
        if (chainId != _chainId)
            throw new ArgumentException($"chainId {chainId} differs from local {_chainId}", nameof(chainId));
        if (maxMessages < 0) throw new ArgumentOutOfRangeException(nameof(maxMessages));
        cancellationToken.ThrowIfCancellationRequested();
        if (maxMessages == 0) return Array.Empty<CrossChainMessage>();

        if (_eventScanner is not null)
        {
            // Newly registered nonces invalidate the fanout cache via RegisterDiscoveredNonce.
            await _eventScanner.ScanAsync(
                    nonce => { RegisterDiscoveredNonce(nonce); },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        List<CrossChainMessage>? cached;
        lock (_cacheGate)
        {
            cached = _cachedInbound is not null && DateTime.UtcNow < _cacheUntilUtc
                ? new List<CrossChainMessage>(_cachedInbound)
                : null;
        }
        if (cached is null)
        {
            cached = await FetchInboundAsync(cancellationToken).ConfigureAwait(false);
            lock (_cacheGate)
            {
                _cachedInbound = cached;
                _cacheUntilUtc = DateTime.UtcNow + _cacheTtl;
            }
        }

        // Filter locally-consumed nonces (the L2 batcher's MarkConsumed-equivalent).
        // Order: nonce ascending — replay protection is per-source-chain monotonic.
        var dequeued = cached
            .Where(m => !_locallyConsumed.ContainsKey(m.Nonce))
            .OrderBy(m => m.Nonce)
            .Take(maxMessages)
            .ToArray();

        // Mark dequeued nonces locally consumed. The L2 batcher will include them in
        // the next batch; subsequent DequeueL1MessagesAsync calls must not return
        // them again (matches the in-memory L1MessageInbox.Dequeue contract).
        foreach (var m in dequeued) _locallyConsumed[m.Nonce] = 1;
        return dequeued;
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
        ArgumentNullException.ThrowIfNull(messageHash);
        var bytes = _finalized.Get(messageHash.GetSpan());
        if (bytes is null)
            return new ValueTask<ReadOnlyMemory<byte>?>((ReadOnlyMemory<byte>?)null);
        return new ValueTask<ReadOnlyMemory<byte>?>(new ReadOnlyMemory<byte>(bytes));
    }

    /// <summary>The L2-internal outbox; the batcher consumes its contents when sealing.</summary>
    public L2Outbox Outbox => _outbox;

    private bool RegisterDiscoveredNonce(ulong nonce)
    {
        var added = _knownNonces.TryAdd(nonce, 1);
        if (added) InvalidateInboundCache();
        return added;
    }

    private async Task<List<CrossChainMessage>> FetchInboundAsync(CancellationToken ct)
    {
        var snapshot = _knownNonces.Keys
            .Where(n => !_locallyConsumed.ContainsKey(n))
            .ToArray();
        var fetchTasks = snapshot.Select(n => FetchOneAsync(n, ct)).ToArray();
        var results = await Task.WhenAll(fetchTasks).ConfigureAwait(false);
        return results.Where(r => r is not null).Cast<CrossChainMessage>().ToList();
    }

    private async Task<CrossChainMessage?> FetchOneAsync(ulong nonce, CancellationToken ct)
    {
        var raw = await RpcContractReader.InvokeReadAsync(_rpc, _routerHash, "getL1ToL2", new object[] { _chainId, nonce }, ct).ConfigureAwait(false);
        var bytes = RpcContractReader.ParseByteArray(raw);
        if (bytes.Length == 0) return null; // not stored — operator's known set is ahead of L1
        var msg = DecodeMessage(bytes);

        // Cross-check L1's IsConsumed: the contract's at-most-once gate. Skip messages
        // that the L1 already considers consumed (settlement-driven cleanup).
        var consumedRaw = await RpcContractReader.InvokeReadAsync(_rpc, _routerHash, "isConsumed", new object[] { msg.MessageHash }, ct).ConfigureAwait(false);
        if (RpcContractReader.ParseBoolean(consumedRaw))
        {
            // Drop durable track for L1-retired messages so the store does not grow forever.
            _eventScanner?.ForgetNonce(nonce);
            _knownNonces.TryRemove(nonce, out _);
            return null;
        }
        return msg;
    }

    /// <summary>
    /// Decode the canonical L1→L2 message encoding (matches
    /// <c>NeoHub.MessageRouter.EncodeMessage</c>):
    /// <c>[4B sourceChainId LE][4B targetChainId LE][8B nonce LE][20B sender][20B receiver][1B messageType][4B payloadLen LE][payload]</c>.
    /// </summary>
    public static CrossChainMessage DecodeMessage(ReadOnlySpan<byte> bytes)
    {
        const int FixedHeader = 4 + 4 + 8 + 20 + 20 + 1 + 4;
        if (bytes.Length < FixedHeader)
            throw new InvalidDataException($"L1→L2 message too short ({bytes.Length} < {FixedHeader})");
        var sourceChainId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4));
        var targetChainId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4));
        var nonce = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8, 8));
        var sender = new UInt160(bytes.Slice(16, 20));
        var receiver = new UInt160(bytes.Slice(36, 20));
        var messageType = (MessageType)bytes[56];
        var payloadLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(57, 4));
        if (payloadLen < 0)
            throw new InvalidDataException($"L1→L2 message payloadLen {payloadLen} negative");
        if (bytes.Length != FixedHeader + payloadLen)
            throw new InvalidDataException(
                $"L1→L2 message size {bytes.Length} != expected {FixedHeader + payloadLen} for payloadLen={payloadLen}");
        var payload = bytes.Slice(FixedHeader, payloadLen).ToArray();

        // Recompute the canonical hash via MessageBuilder/MessageHasher so the decoded
        // record matches what the on-chain ledger committed to. A drift between the
        // contract's hash convention and the off-chain hasher would break inclusion
        // proofs; we deliberately recompute rather than trust an off-wire hash.
        var unhashed = new CrossChainMessage
        {
            SourceChainId = sourceChainId,
            TargetChainId = targetChainId,
            Nonce = nonce,
            Sender = sender,
            Receiver = receiver,
            MessageType = messageType,
            Payload = payload,
            MessageHash = UInt256.Zero,
        };
        var hash = MessageHasher.HashMessage(unhashed);
        return unhashed with { MessageHash = hash };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _eventScanner?.Dispose();
        if (_ownsRpc) _rpc.Dispose();
        if (_ownsFinalized) _finalized.Dispose();
    }
}
