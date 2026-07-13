using System.Buffers.Binary;
using System.Text;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Persistence;

namespace Neo.Plugins.L2Gateway;

/// <summary>Durable lifecycle of a Gateway constituent or aggregate publication.</summary>
/// <remarks>See doc.md §4 and §17. Values are persisted; do not renumber.</remarks>
public enum GatewayOutboxState : byte
{
    /// <summary>Finalized constituent waiting for aggregation.</summary>
    Sealed = 1,

    /// <summary>Constituents have been transactionally attached to an aggregate attempt.</summary>
    Proving = 2,

    /// <summary>The exact terminal proof is persisted.</summary>
    Proved = 3,

    /// <summary>Submission may have reached L1 and must be reconciled before retry.</summary>
    Submitted = 4,

    /// <summary>Automatic retries are exhausted; operator recovery is required.</summary>
    Poisoned = 5,

    /// <summary>L1 confirmation was reconciled and the constituent is terminal.</summary>
    Confirmed = 6,
}

/// <summary>Persisted Gateway publication checkpoint.</summary>
/// <remarks>See doc.md §4 (Neo Gateway) and §17 (operator recovery).</remarks>
public sealed record GatewayPublicationCheckpoint
{
    /// <summary>Epoch bound into the terminal proof.</summary>
    public required ulong BatchEpoch { get; init; }

    /// <summary>Exact aggregate, including all canonical constituents.</summary>
    public required AggregatedCommitment Commitment { get; init; }

    /// <summary>Exact terminal proof statement.</summary>
    public required GatewayProofBinding Binding { get; init; }

    /// <summary>Terminal proof bytes; empty only before proving succeeds.</summary>
    public required ReadOnlyMemory<byte> Proof { get; init; }

    /// <summary>Current lifecycle state.</summary>
    public required GatewayOutboxState State { get; init; }

    /// <summary>Consecutive automatic failures for this attempt.</summary>
    public required int RetryCount { get; init; }

    /// <summary>UTC Unix milliseconds when this aggregate attempt started.</summary>
    public required long StartedAtUnixMilliseconds { get; init; }

    /// <summary>Bounded operator-visible error message from the last failure.</summary>
    public string? LastError { get; init; }
}

/// <summary>Recovery snapshot loaded atomically before the Gateway starts accepting work.</summary>
/// <remarks>See doc.md §4 (Neo Gateway).</remarks>
public sealed record GatewayOutboxRecovery
{
    /// <summary>Sealed constituents that must be rehydrated into the aggregator.</summary>
    public required IReadOnlyList<L2BatchCommitment> Sealed { get; init; }

    /// <summary>Unconfirmed aggregate publication, if one exists.</summary>
    public GatewayPublicationCheckpoint? Publication { get; init; }
}

/// <summary>Operator-facing durable queue state.</summary>
/// <remarks>See doc.md §17 (threat model and recovery).</remarks>
public sealed record GatewayOutboxStatus
{
    /// <summary>Number of non-confirmed constituent entries.</summary>
    public required int QueueDepth { get; init; }

    /// <summary>Current publication state, or <c>null</c> when no aggregate is active.</summary>
    public GatewayOutboxState? PublicationState { get; init; }

    /// <summary>Current consecutive retry count.</summary>
    public required int RetryCount { get; init; }

    /// <summary>Last persisted failure, if any.</summary>
    public string? LastError { get; init; }

    /// <summary>Age of the current publication in milliseconds.</summary>
    public required long ConfirmationLagMilliseconds { get; init; }
}

/// <summary>
/// Crash-safe Gateway outbox over the repository's canonical <see cref="IL2KeyValueStore"/>.
/// </summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). Production passes a dedicated
/// <see cref="RocksDbKeyValueStore"/>. Every constituent is keyed by
/// <c>(chainId,batchNumber)</c>. The aggregate checkpoint is written before constituent states,
/// and confirmation states are written before deleting the checkpoint; either partial ordering is
/// therefore recoverable after a crash without dropping or duplicating logical work.
/// </remarks>
public sealed class PersistentGatewayOutbox : IDisposable
{
    private static readonly byte[] ItemPrefix = "neo4:gateway:item:"u8.ToArray();
    private static readonly byte[] PublicationKey = "neo4:gateway:publication"u8.ToArray();
    private const byte FormatVersion = 1;
    private const int MaxConstituents = 4096;
    private const int MaxAggregateProofBytes = 16 * 1024 * 1024;
    private const int MaxTerminalProofBytes = 1 * 1024 * 1024;
    private const int MaxErrorBytes = 4096;
    private const int MaxCheckpointBytes = 64 * 1024 * 1024;
    private readonly Lock _gate = new();
    private readonly IL2KeyValueStore _store;
    private readonly bool _ownsStore;
    private bool _disposed;

    /// <summary>Construct over a caller-supplied store; production supplies RocksDB.</summary>
    public PersistentGatewayOutbox(IL2KeyValueStore store, bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _ownsStore = ownsStore;
    }

    /// <summary>
    /// Persist a sealed constituent before exposing it to the in-memory aggregator.
    /// Exact duplicates are idempotent; conflicting reuse of the same key fails closed.
    /// </summary>
    /// <returns><c>true</c> only when a new sealed item was inserted.</returns>
    public bool Enqueue(L2BatchCommitment commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        var canonical = BatchSerializer.Encode(commitment);
        var key = BuildItemKey(commitment.ChainId, commitment.BatchNumber);
        lock (_gate)
        {
            ThrowIfDisposed();
            var existingBytes = _store.Get(key);
            if (existingBytes is not null)
            {
                var existing = DecodeItem(existingBytes);
                if (!BatchSerializer.Encode(existing.Commitment).AsSpan().SequenceEqual(canonical))
                {
                    throw new InvalidOperationException(
                        $"Gateway batch ({commitment.ChainId}, {commitment.BatchNumber}) " +
                        "already exists with different canonical bytes");
                }
                return false;
            }

            _store.Put(key, EncodeItem(GatewayOutboxState.Sealed, canonical));
            return true;
        }
    }

    /// <summary>
    /// Rehydrate sealed work and the single active publication. Orphaned <c>Proving</c> items are
    /// restored to <c>Sealed</c>; later states without their checkpoint are treated as corruption.
    /// </summary>
    public GatewayOutboxRecovery Recover()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var publicationBytes = _store.Get(PublicationKey);
            var publication = publicationBytes is null ? null : DecodeCheckpoint(publicationBytes);
            var active = publication?.Commitment.Constituents.ToDictionary(ItemIdentity);
            var seenActive = new HashSet<(uint ChainId, ulong BatchNumber)>();
            var sealedItems = new List<L2BatchCommitment>();

            foreach (var (key, value) in _store.EnumeratePrefix(ItemPrefix))
            {
                var item = DecodeItem(value);
                var expectedKey = BuildItemKey(item.Commitment.ChainId, item.Commitment.BatchNumber);
                if (!key.AsSpan().SequenceEqual(expectedKey))
                    throw new InvalidDataException("Gateway outbox item key does not match its commitment");

                var identity = ItemIdentity(item.Commitment);
                if (active is not null && active.TryGetValue(identity, out var activeCommitment))
                {
                    AssertSameCommitment(item.Commitment, activeCommitment);
                    seenActive.Add(identity);
                    if (item.State != publication!.State)
                    {
                        _store.Put(key, EncodeItem(
                            publication.State,
                            BatchSerializer.Encode(item.Commitment)));
                    }
                    continue;
                }

                switch (item.State)
                {
                    case GatewayOutboxState.Sealed:
                        sealedItems.Add(item.Commitment);
                        break;
                    case GatewayOutboxState.Proving:
                        _store.Put(key, EncodeItem(
                            GatewayOutboxState.Sealed,
                            BatchSerializer.Encode(item.Commitment)));
                        sealedItems.Add(item.Commitment);
                        break;
                    case GatewayOutboxState.Confirmed:
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Gateway item {identity} is {item.State} without an active publication checkpoint");
                }
            }

            if (active is not null && seenActive.Count != active.Count)
                throw new InvalidDataException("Gateway publication references a missing constituent item");

            return new GatewayOutboxRecovery
            {
                Sealed = sealedItems
                    .OrderBy(static item => item.ChainId)
                    .ThenBy(static item => item.BatchNumber)
                    .ToArray(),
                Publication = publication,
            };
        }
    }

    /// <summary>
    /// Persist the aggregate checkpoint first, then transition all exact constituents. This write
    /// order makes a crash between operations recoverable from the checkpoint plus sealed items.
    /// </summary>
    public void SavePublication(GatewayPublicationCheckpoint checkpoint)
    {
        ValidateCheckpoint(checkpoint);
        lock (_gate)
        {
            ThrowIfDisposed();
            _store.Put(PublicationKey, EncodeCheckpoint(checkpoint));
            foreach (var commitment in checkpoint.Commitment.Constituents)
            {
                var key = BuildItemKey(commitment.ChainId, commitment.BatchNumber);
                var existingBytes = _store.Get(key)
                    ?? throw new InvalidDataException(
                        $"Gateway publication constituent ({commitment.ChainId}, {commitment.BatchNumber}) is not sealed");
                var existing = DecodeItem(existingBytes);
                AssertSameCommitment(existing.Commitment, commitment);
                _store.Put(key, EncodeItem(checkpoint.State, BatchSerializer.Encode(commitment)));
            }
        }
    }

    /// <summary>
    /// Mark every constituent confirmed before deleting the publication checkpoint. A crash during
    /// this sequence leaves the checkpoint available for idempotent L1 reconciliation.
    /// </summary>
    public void MarkConfirmed(GatewayPublicationCheckpoint checkpoint)
    {
        ValidateCheckpoint(checkpoint);
        lock (_gate)
        {
            ThrowIfDisposed();
            var storedBytes = _store.Get(PublicationKey);
            if (storedBytes is not null)
            {
                var stored = DecodeCheckpoint(storedBytes);
                AssertSamePublication(stored, checkpoint);
            }

            foreach (var commitment in checkpoint.Commitment.Constituents)
            {
                var key = BuildItemKey(commitment.ChainId, commitment.BatchNumber);
                var existingBytes = _store.Get(key)
                    ?? throw new InvalidDataException("Gateway confirmed constituent is missing");
                var existing = DecodeItem(existingBytes);
                AssertSameCommitment(existing.Commitment, commitment);
                _store.Put(key, EncodeItem(
                    GatewayOutboxState.Confirmed,
                    BatchSerializer.Encode(commitment)));
            }
            _store.Delete(PublicationKey);
        }
    }

    /// <summary>Read operator-facing queue, retry, poison, and confirmation-lag state.</summary>
    public GatewayOutboxStatus GetStatus(long? nowUnixMilliseconds = null)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var queueDepth = 0;
            foreach (var (_, value) in _store.EnumeratePrefix(ItemPrefix))
            {
                if (DecodeItem(value).State != GatewayOutboxState.Confirmed) queueDepth++;
            }
            var bytes = _store.Get(PublicationKey);
            var checkpoint = bytes is null ? null : DecodeCheckpoint(bytes);
            var now = nowUnixMilliseconds ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new GatewayOutboxStatus
            {
                QueueDepth = queueDepth,
                PublicationState = checkpoint?.State,
                RetryCount = checkpoint?.RetryCount ?? 0,
                LastError = checkpoint?.LastError,
                ConfirmationLagMilliseconds = checkpoint is null
                    ? 0
                    : Math.Max(0, now - checkpoint.StartedAtUnixMilliseconds),
            };
        }
    }

    private static byte[] BuildItemKey(uint chainId, ulong batchNumber)
    {
        var key = new byte[ItemPrefix.Length + 12];
        ItemPrefix.CopyTo(key, 0);
        BinaryPrimitives.WriteUInt32BigEndian(key.AsSpan(ItemPrefix.Length, 4), chainId);
        BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(ItemPrefix.Length + 4, 8), batchNumber);
        return key;
    }

    private static byte[] EncodeItem(GatewayOutboxState state, byte[] commitment)
    {
        var bytes = new byte[2 + commitment.Length];
        bytes[0] = FormatVersion;
        bytes[1] = (byte)state;
        commitment.CopyTo(bytes, 2);
        return bytes;
    }

    private static (GatewayOutboxState State, L2BatchCommitment Commitment) DecodeItem(byte[] bytes)
    {
        if (bytes.Length < 2 + BatchSerializer.CommitmentFixedSize)
            throw new InvalidDataException("Gateway outbox item is truncated");
        if (bytes[0] != FormatVersion)
            throw new InvalidDataException($"unsupported Gateway outbox item version {bytes[0]}");
        var state = ParseState(bytes[1]);
        return (state, BatchSerializer.Decode(bytes.AsSpan(2)));
    }

    private static byte[] EncodeCheckpoint(GatewayPublicationCheckpoint checkpoint)
    {
        var errorBytes = checkpoint.LastError is null
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(checkpoint.LastError);
        if (errorBytes.Length > MaxErrorBytes)
            errorBytes = errorBytes.AsSpan(0, MaxErrorBytes).ToArray();
        var binding = GatewayProofBindingSerializer.Encode(checkpoint.Binding);
        var proof = checkpoint.Proof.ToArray();
        var aggregateProof = checkpoint.Commitment.AggregatedProof.ToArray();
        var encodedConstituents = new List<byte[]>(checkpoint.Commitment.Constituents.Count);
        var encodedSize = 2 + 8 + 4 + 8
            + 4 + errorBytes.Length
            + 4 + binding.Length
            + 4 + proof.Length
            + 32 + 32 + 1
            + 4 + aggregateProof.Length
            + 4;
        foreach (var constituent in checkpoint.Commitment.Constituents)
        {
            var canonical = BatchSerializer.Encode(constituent);
            encodedSize = checked(encodedSize + 4 + canonical.Length);
            if (encodedSize > MaxCheckpointBytes)
                throw new ArgumentException("Gateway checkpoint exceeds 64 MiB", nameof(checkpoint));
            encodedConstituents.Add(canonical);
        }

        using var stream = new MemoryStream(encodedSize);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatVersion);
        writer.Write((byte)checkpoint.State);
        writer.Write(checkpoint.BatchEpoch);
        writer.Write(checkpoint.RetryCount);
        writer.Write(checkpoint.StartedAtUnixMilliseconds);
        WriteBytes(writer, errorBytes);
        WriteBytes(writer, binding);
        WriteBytes(writer, proof);
        writer.Write(checkpoint.Commitment.GlobalMessageRoot.GetSpan());
        writer.Write(checkpoint.Commitment.ConstituentCommitmentsRoot.GetSpan());
        writer.Write(checkpoint.Commitment.BackendId);
        WriteBytes(writer, aggregateProof);
        writer.Write(checkpoint.Commitment.Constituents.Count);
        foreach (var constituent in encodedConstituents) WriteBytes(writer, constituent);
        writer.Flush();
        return stream.ToArray();
    }

    private static GatewayPublicationCheckpoint DecodeCheckpoint(byte[] bytes)
    {
        if (bytes.Length > MaxCheckpointBytes)
            throw new InvalidDataException("Gateway publication checkpoint exceeds 64 MiB");
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var version = reader.ReadByte();
            if (version != FormatVersion)
                throw new InvalidDataException($"unsupported Gateway checkpoint version {version}");
            var state = ParseState(reader.ReadByte());
            var epoch = reader.ReadUInt64();
            var retryCount = reader.ReadInt32();
            var started = reader.ReadInt64();
            var errorBytes = ReadBytes(reader, MaxErrorBytes, "last error");
            var bindingBytes = ReadBytes(
                reader,
                GatewayProofBindingSerializer.EncodedSize,
                "proof binding",
                exactLength: GatewayProofBindingSerializer.EncodedSize);
            var proof = ReadBytes(reader, MaxTerminalProofBytes, "terminal proof");
            var globalRoot = new UInt256(reader.ReadBytes(32));
            var constituentRoot = new UInt256(reader.ReadBytes(32));
            var backend = reader.ReadByte();
            var aggregateProof = ReadBytes(reader, MaxAggregateProofBytes, "aggregate proof");
            var constituentCount = reader.ReadInt32();
            if (constituentCount is < 1 or > MaxConstituents)
                throw new InvalidDataException($"invalid Gateway constituent count {constituentCount}");
            var constituents = new L2BatchCommitment[constituentCount];
            for (var i = 0; i < constituents.Length; i++)
            {
                var canonical = ReadBytes(
                    reader,
                    BatchSerializer.CommitmentFixedSize + MaxTerminalProofBytes,
                    $"constituent {i}");
                constituents[i] = BatchSerializer.Decode(canonical);
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Gateway checkpoint has trailing bytes");

            var checkpoint = new GatewayPublicationCheckpoint
            {
                BatchEpoch = epoch,
                Commitment = new AggregatedCommitment
                {
                    Constituents = constituents,
                    GlobalMessageRoot = globalRoot,
                    ConstituentCommitmentsRoot = constituentRoot,
                    AggregatedProof = aggregateProof,
                    BackendId = backend,
                },
                Binding = GatewayProofBindingSerializer.Decode(bindingBytes),
                Proof = proof,
                State = state,
                RetryCount = retryCount,
                StartedAtUnixMilliseconds = started,
                LastError = errorBytes.Length == 0 ? null : Encoding.UTF8.GetString(errorBytes),
            };
            ValidateCheckpoint(checkpoint);
            return checkpoint;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Gateway publication checkpoint is truncated", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Gateway publication checkpoint is malformed", exception);
        }
    }

    private static void ValidateCheckpoint(GatewayPublicationCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(checkpoint.Commitment);
        ArgumentNullException.ThrowIfNull(checkpoint.Binding);
        if (checkpoint.State is GatewayOutboxState.Sealed or GatewayOutboxState.Confirmed)
            throw new ArgumentException("publication checkpoint must be an active state", nameof(checkpoint));
        if (checkpoint.RetryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(checkpoint), "retry count must be non-negative");
        if (checkpoint.StartedAtUnixMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(checkpoint), "started timestamp must be positive");
        if (checkpoint.Commitment.Constituents.Count is < 1 or > MaxConstituents)
            throw new ArgumentOutOfRangeException(nameof(checkpoint), "invalid constituent count");
        if (checkpoint.Commitment.AggregatedProof.Length > MaxAggregateProofBytes)
            throw new ArgumentException("aggregate proof exceeds outbox limit", nameof(checkpoint));
        if (checkpoint.Proof.Length > MaxTerminalProofBytes)
            throw new ArgumentException("terminal proof exceeds outbox limit", nameof(checkpoint));
        if ((checkpoint.State is GatewayOutboxState.Proved or GatewayOutboxState.Submitted)
            && checkpoint.Proof.IsEmpty)
        {
            throw new ArgumentException("proved/submitted checkpoint requires proof bytes", nameof(checkpoint));
        }
        if (checkpoint.BatchEpoch != checkpoint.Binding.BatchEpoch)
            throw new ArgumentException("checkpoint epoch does not match binding", nameof(checkpoint));

        GatewayProofBindingSerializer.ValidateCanonicalConstituentOrder(
            checkpoint.Commitment.Constituents);
        var expected = GatewayProofBindingSerializer.Create(
            checkpoint.Binding.MessageRouter,
            checkpoint.Binding.ReplayDomain,
            checkpoint.BatchEpoch,
            checkpoint.Commitment,
            checkpoint.Binding.ProofSystem,
            checkpoint.Binding.VerificationKeyId);
        if (!GatewayProofBindingSerializer.ComputeHash(expected)
            .Equals(GatewayProofBindingSerializer.ComputeHash(checkpoint.Binding)))
        {
            throw new ArgumentException("checkpoint binding does not match aggregate", nameof(checkpoint));
        }
    }

    private static void AssertSamePublication(
        GatewayPublicationCheckpoint left,
        GatewayPublicationCheckpoint right)
    {
        if (left.BatchEpoch != right.BatchEpoch
            || !GatewayProofBindingSerializer.ComputeHash(left.Binding)
                .Equals(GatewayProofBindingSerializer.ComputeHash(right.Binding)))
        {
            throw new InvalidDataException("Gateway publication checkpoint identity changed");
        }
    }

    private static void AssertSameCommitment(L2BatchCommitment left, L2BatchCommitment right)
    {
        if (!BatchSerializer.Encode(left).AsSpan().SequenceEqual(BatchSerializer.Encode(right)))
            throw new InvalidDataException("Gateway constituent canonical bytes changed");
    }

    private static (uint ChainId, ulong BatchNumber) ItemIdentity(L2BatchCommitment commitment) =>
        (commitment.ChainId, commitment.BatchNumber);

    private static GatewayOutboxState ParseState(byte value)
    {
        var state = (GatewayOutboxState)value;
        if (state is < GatewayOutboxState.Sealed or > GatewayOutboxState.Confirmed)
            throw new InvalidDataException($"unknown Gateway outbox state {value}");
        return state;
    }

    private static void WriteBytes(BinaryWriter writer, byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static byte[] ReadBytes(
        BinaryReader reader,
        int maximumLength,
        string field,
        int? exactLength = null)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > maximumLength || exactLength is not null && length != exactLength)
            throw new InvalidDataException($"invalid Gateway {field} length {length}");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException($"Gateway {field} is truncated");
        return bytes;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PersistentGatewayOutbox));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsStore) _store.Dispose();
    }
}
