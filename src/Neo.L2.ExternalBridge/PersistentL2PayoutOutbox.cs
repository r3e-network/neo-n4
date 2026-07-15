using System.Text;
using Neo.L2.Messaging;
using Neo.L2.Persistence;

namespace Neo.L2.ExternalBridge;

/// <summary>Crash-safe relay state. Persisted values must never be renumbered.</summary>
/// <remarks>See <c>doc.md</c> §11.3 and §17.</remarks>
public enum L2PayoutRelayState : byte
{
    /// <summary>Finalized L1 queue event is durable locally.</summary>
    Enqueued = 1,

    /// <summary>Exact signed L2 credit transaction is durable before broadcast.</summary>
    CreditPrepared = 2,

    /// <summary>L2 native bridge confirms the exact message hash and one-time credit.</summary>
    Credited = 3,

    /// <summary>Exact signed L1 acknowledgement transaction is durable before broadcast.</summary>
    AcknowledgementPrepared = 4,

    /// <summary>L1 adapter confirms the exact L2 transaction hash.</summary>
    Acknowledged = 5,

    /// <summary>Automatic retries are exhausted and operator intervention is required.</summary>
    Poisoned = 6,
}

/// <summary>Durable checkpoint for one canonical payout.</summary>
/// <remarks>See <c>doc.md</c> §11.3 and §17.</remarks>
public sealed record L2PayoutRelayCheckpoint
{
    /// <summary>Exact payout instruction.</summary>
    public required L2PayoutInstruction Instruction { get; init; }

    /// <summary>Current durable state.</summary>
    public required L2PayoutRelayState State { get; init; }

    /// <summary>Canonical signed Neo transaction bytes prepared for target-L2 credit.</summary>
    public required ReadOnlyMemory<byte> PreparedCreditTransaction { get; init; }

    /// <summary>Confirmed target-L2 transaction hash.</summary>
    public required UInt256 L2TransactionHash { get; init; }

    /// <summary>Canonical signed Neo transaction bytes prepared for L1 acknowledgement.</summary>
    public required ReadOnlyMemory<byte> PreparedAcknowledgementTransaction { get; init; }

    /// <summary>Consecutive processing failures.</summary>
    public required int RetryCount { get; init; }

    /// <summary>Bounded operator-visible failure.</summary>
    public string? LastError { get; init; }

    /// <inheritdoc />
    public bool Equals(L2PayoutRelayCheckpoint? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Instruction == other.Instruction
            && State == other.State
            && PreparedCreditTransaction.Span.SequenceEqual(other.PreparedCreditTransaction.Span)
            && L2TransactionHash == other.L2TransactionHash
            && PreparedAcknowledgementTransaction.Span.SequenceEqual(
                other.PreparedAcknowledgementTransaction.Span)
            && RetryCount == other.RetryCount
            && string.Equals(LastError, other.LastError, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Instruction);
        hash.Add((byte)State);
        hash.AddBytes(PreparedCreditTransaction.Span);
        hash.Add(L2TransactionHash);
        hash.AddBytes(PreparedAcknowledgementTransaction.Span);
        hash.Add(RetryCount);
        hash.Add(LastError, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>Durable relay write-ahead log over the repository-standard key-value store.</summary>
/// <remarks>
/// See <c>doc.md</c> §11.3 and §17. Production supplies <see cref="RocksDbKeyValueStore"/>.
/// Signed transactions are persisted before broadcast, and confirmations are persisted before the
/// next cross-chain side effect, so every crash window is recoverable by exact-hash reconciliation.
/// </remarks>
public sealed class PersistentL2PayoutOutbox : IDisposable
{
    private static readonly byte[] ItemPrefix = "neo4:l2-payout-relay:item:v1:"u8.ToArray();
    private const byte EncodingVersion = 1;
    private const int MaxTransactionBytes = 1024 * 1024;
    private const int MaxErrorBytes = 4096;
    private readonly Lock _gate = new();
    private readonly IL2KeyValueStore _store;
    private readonly bool _ownsStore;
    private bool _disposed;

    /// <summary>Construct over a caller-owned store.</summary>
    public PersistentL2PayoutOutbox(IL2KeyValueStore store, bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _ownsStore = ownsStore;
    }

    /// <summary>Persist a finalized adapter instruction; exact event replays are idempotent.</summary>
    public bool Enqueue(L2PayoutInstruction instruction)
    {
        ValidateInstruction(instruction);
        var checkpoint = NewCheckpoint(instruction);
        var key = BuildKey(instruction.Adapter, instruction.Message.MessageHash);
        var encoded = Encode(checkpoint);
        lock (_gate)
        {
            ThrowIfDisposed();
            var existingBytes = _store.Get(key);
            if (existingBytes is not null)
            {
                var existing = Decode(existingBytes);
                if (existing.Instruction != instruction)
                    throw new InvalidDataException(
                        "Payout message hash already exists with a different canonical instruction.");
                return false;
            }
            if (!_store.TryPut(key, encoded))
                throw new InvalidOperationException("Concurrent payout enqueue changed durable state.");
            return true;
        }
    }

    /// <summary>Load all non-terminal work in adapter sequence order.</summary>
    public IReadOnlyList<L2PayoutRelayCheckpoint> LoadPending()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return _store.EnumeratePrefix(ItemPrefix)
                .Select(pair => DecodeAndValidateKey(pair.Key, pair.Value))
                .Where(checkpoint => checkpoint.State != L2PayoutRelayState.Acknowledged)
                .OrderBy(checkpoint => checkpoint.Instruction.Adapter.ToString(), StringComparer.Ordinal)
                .ThenBy(checkpoint => checkpoint.Instruction.Sequence)
                .ToArray();
        }
    }

    /// <summary>Persist the exact signed L2 transaction before broadcasting it.</summary>
    public L2PayoutRelayCheckpoint SavePreparedCredit(
        L2PayoutRelayCheckpoint checkpoint,
        ReadOnlyMemory<byte> transaction)
    {
        if (checkpoint.State != L2PayoutRelayState.Enqueued)
            throw new InvalidOperationException("Credit can only be prepared from Enqueued state.");
        ValidateTransaction(transaction, nameof(transaction));
        return Save(checkpoint, checkpoint with
        {
            State = L2PayoutRelayState.CreditPrepared,
            PreparedCreditTransaction = transaction.ToArray(),
            LastError = null,
        });
    }

    /// <summary>Persist exact target-L2 confirmation before preparing L1 acknowledgement.</summary>
    public L2PayoutRelayCheckpoint MarkCredited(
        L2PayoutRelayCheckpoint checkpoint,
        UInt256 l2TransactionHash)
    {
        ArgumentNullException.ThrowIfNull(l2TransactionHash);
        if (l2TransactionHash == UInt256.Zero)
            throw new ArgumentException("L2 transaction hash must not be zero.", nameof(l2TransactionHash));
        if (checkpoint.State is not (L2PayoutRelayState.Enqueued or L2PayoutRelayState.CreditPrepared))
            throw new InvalidOperationException("Payout is not awaiting L2 credit.");
        return Save(checkpoint, checkpoint with
        {
            State = L2PayoutRelayState.Credited,
            L2TransactionHash = l2TransactionHash,
            PreparedCreditTransaction = ReadOnlyMemory<byte>.Empty,
            LastError = null,
        });
    }

    /// <summary>Persist the exact signed L1 acknowledgement before broadcasting it.</summary>
    public L2PayoutRelayCheckpoint SavePreparedAcknowledgement(
        L2PayoutRelayCheckpoint checkpoint,
        ReadOnlyMemory<byte> transaction)
    {
        if (checkpoint.State != L2PayoutRelayState.Credited)
            throw new InvalidOperationException("Acknowledgement can only be prepared after credit.");
        ValidateTransaction(transaction, nameof(transaction));
        return Save(checkpoint, checkpoint with
        {
            State = L2PayoutRelayState.AcknowledgementPrepared,
            PreparedAcknowledgementTransaction = transaction.ToArray(),
            LastError = null,
        });
    }

    /// <summary>Persist terminal L1 acknowledgement.</summary>
    public L2PayoutRelayCheckpoint MarkAcknowledged(L2PayoutRelayCheckpoint checkpoint)
    {
        if (checkpoint.State is not (L2PayoutRelayState.Credited
            or L2PayoutRelayState.AcknowledgementPrepared))
        {
            throw new InvalidOperationException("Payout is not awaiting acknowledgement.");
        }
        return Save(checkpoint, checkpoint with
        {
            State = L2PayoutRelayState.Acknowledged,
            PreparedAcknowledgementTransaction = ReadOnlyMemory<byte>.Empty,
            LastError = null,
        });
    }

    /// <summary>Persist a bounded retry failure and poison after the configured limit.</summary>
    public L2PayoutRelayCheckpoint RecordFailure(
        L2PayoutRelayCheckpoint checkpoint,
        Exception exception,
        int maximumRetries)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (maximumRetries <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRetries));
        checkpoint = LoadCurrent(checkpoint.Instruction);
        var retryCount = checked(checkpoint.RetryCount + 1);
        var error = exception.Message;
        if (Encoding.UTF8.GetByteCount(error) > MaxErrorBytes)
        {
            var bytes = Encoding.UTF8.GetBytes(error);
            error = Encoding.UTF8.GetString(bytes, 0, MaxErrorBytes);
        }
        return Save(checkpoint, checkpoint with
        {
            State = retryCount >= maximumRetries
                ? L2PayoutRelayState.Poisoned
                : checkpoint.State,
            RetryCount = retryCount,
            LastError = error,
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsStore) _store.Dispose();
        }
    }

    private L2PayoutRelayCheckpoint Save(
        L2PayoutRelayCheckpoint previous,
        L2PayoutRelayCheckpoint next)
    {
        ValidateCheckpoint(next);
        var key = BuildKey(previous.Instruction.Adapter, previous.Instruction.Message.MessageHash);
        lock (_gate)
        {
            ThrowIfDisposed();
            var currentBytes = _store.Get(key)
                ?? throw new InvalidDataException("Payout checkpoint disappeared from durable state.");
            var current = Decode(currentBytes);
            if (current != previous)
                throw new InvalidOperationException("Payout checkpoint changed concurrently.");
            var nextBytes = Encode(next);
            if (!_store.CompareExchange(key, currentBytes, nextBytes))
                throw new InvalidOperationException("Payout checkpoint compare-exchange failed.");
            return next;
        }
    }

    private L2PayoutRelayCheckpoint LoadCurrent(L2PayoutInstruction instruction)
    {
        var key = BuildKey(instruction.Adapter, instruction.Message.MessageHash);
        lock (_gate)
        {
            ThrowIfDisposed();
            var bytes = _store.Get(key)
                ?? throw new InvalidDataException("Payout checkpoint disappeared from durable state.");
            var checkpoint = Decode(bytes);
            if (checkpoint.Instruction != instruction)
                throw new InvalidDataException("Payout checkpoint identity changed in durable state.");
            return checkpoint;
        }
    }

    private static L2PayoutRelayCheckpoint NewCheckpoint(L2PayoutInstruction instruction) => new()
    {
        Instruction = instruction,
        State = L2PayoutRelayState.Enqueued,
        PreparedCreditTransaction = ReadOnlyMemory<byte>.Empty,
        L2TransactionHash = UInt256.Zero,
        PreparedAcknowledgementTransaction = ReadOnlyMemory<byte>.Empty,
        RetryCount = 0,
    };

    private static byte[] BuildKey(UInt160 adapter, UInt256 messageHash)
    {
        var key = new byte[ItemPrefix.Length + UInt160.Length + UInt256.Length];
        ItemPrefix.CopyTo(key, 0);
        adapter.GetSpan().CopyTo(key.AsSpan(ItemPrefix.Length, UInt160.Length));
        messageHash.GetSpan().CopyTo(
            key.AsSpan(ItemPrefix.Length + UInt160.Length, UInt256.Length));
        return key;
    }

    private static byte[] Encode(L2PayoutRelayCheckpoint checkpoint)
    {
        ValidateCheckpoint(checkpoint);
        var message = checkpoint.Instruction.CanonicalMessageBytes.ToArray();
        var credit = checkpoint.PreparedCreditTransaction.ToArray();
        var acknowledgement = checkpoint.PreparedAcknowledgementTransaction.ToArray();
        var error = checkpoint.LastError is null
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(checkpoint.LastError);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(EncodingVersion);
        writer.Write((byte)checkpoint.State);
        writer.Write(checkpoint.Instruction.Sequence);
        writer.Write(checkpoint.Instruction.Adapter.GetSpan());
        writer.Write(checkpoint.Instruction.NeoAsset.GetSpan());
        WriteBytes(writer, message);
        WriteBytes(writer, credit);
        writer.Write(checkpoint.L2TransactionHash.GetSpan());
        WriteBytes(writer, acknowledgement);
        writer.Write(checkpoint.RetryCount);
        WriteBytes(writer, error);
        writer.Flush();
        return stream.ToArray();
    }

    private static L2PayoutRelayCheckpoint Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadByte() != EncodingVersion)
                throw new InvalidDataException("Unsupported payout relay checkpoint version.");
            var state = ParseState(reader.ReadByte());
            var sequence = reader.ReadUInt64();
            var adapter = new UInt160(ReadExact(reader, UInt160.Length));
            var neoAsset = new UInt160(ReadExact(reader, UInt160.Length));
            var messageBytes = ReadBytes(reader, ExternalMessageHasher.FixedPrefixSize + ExternalMessageHasher.MaxPayloadSize);
            var credit = ReadBytes(reader, MaxTransactionBytes);
            var l2TransactionHash = new UInt256(ReadExact(reader, UInt256.Length));
            var acknowledgement = ReadBytes(reader, MaxTransactionBytes);
            var retryCount = reader.ReadInt32();
            var errorBytes = ReadBytes(reader, MaxErrorBytes);
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Payout relay checkpoint has trailing bytes.");
            var decodedMessage = ExternalMessageHasher.DecodeCanonical(messageBytes);
            var instruction = L2PayoutInstruction.Decode(
                sequence, adapter, neoAsset, decodedMessage.MessageHash, messageBytes,
                decodedMessage.NeoChainId);
            var checkpoint = new L2PayoutRelayCheckpoint
            {
                Instruction = instruction,
                State = state,
                PreparedCreditTransaction = credit,
                L2TransactionHash = l2TransactionHash,
                PreparedAcknowledgementTransaction = acknowledgement,
                RetryCount = retryCount,
                LastError = errorBytes.Length == 0 ? null : Encoding.UTF8.GetString(errorBytes),
            };
            ValidateCheckpoint(checkpoint);
            return checkpoint;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Payout relay checkpoint is truncated.", exception);
        }
    }

    private static L2PayoutRelayCheckpoint DecodeAndValidateKey(byte[] key, byte[] value)
    {
        var checkpoint = Decode(value);
        var expected = BuildKey(
            checkpoint.Instruction.Adapter, checkpoint.Instruction.Message.MessageHash);
        if (!key.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException("Payout relay key does not match checkpoint identity.");
        return checkpoint;
    }

    private static void ValidateCheckpoint(L2PayoutRelayCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ValidateInstruction(checkpoint.Instruction);
        if (checkpoint.RetryCount < 0)
            throw new InvalidDataException("Payout retry count is negative.");
        ValidateOptionalTransaction(checkpoint.PreparedCreditTransaction, "prepared credit");
        ValidateOptionalTransaction(
            checkpoint.PreparedAcknowledgementTransaction, "prepared acknowledgement");
        if (checkpoint.State == L2PayoutRelayState.CreditPrepared
            && checkpoint.PreparedCreditTransaction.IsEmpty)
            throw new InvalidDataException("CreditPrepared checkpoint has no transaction.");
        if (checkpoint.State is (L2PayoutRelayState.Credited
            or L2PayoutRelayState.AcknowledgementPrepared
            or L2PayoutRelayState.Acknowledged)
            && checkpoint.L2TransactionHash == UInt256.Zero)
            throw new InvalidDataException("Credited payout has no L2 transaction hash.");
        if (checkpoint.State == L2PayoutRelayState.AcknowledgementPrepared
            && checkpoint.PreparedAcknowledgementTransaction.IsEmpty)
            throw new InvalidDataException("AcknowledgementPrepared checkpoint has no transaction.");
        if (checkpoint.LastError is not null
            && Encoding.UTF8.GetByteCount(checkpoint.LastError) > MaxErrorBytes)
            throw new InvalidDataException("Payout checkpoint error exceeds limit.");
    }

    private static void ValidateInstruction(L2PayoutInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var decoded = L2PayoutInstruction.Decode(
            instruction.Sequence,
            instruction.Adapter,
            instruction.NeoAsset,
            instruction.Message.MessageHash,
            instruction.CanonicalMessageBytes,
            instruction.Message.NeoChainId);
        if (decoded != instruction)
            throw new InvalidDataException("Payout instruction fields do not match canonical bytes.");
    }

    private static void ValidateOptionalTransaction(ReadOnlyMemory<byte> bytes, string name)
    {
        if (bytes.Length > MaxTransactionBytes)
            throw new InvalidDataException($"{name} transaction exceeds {MaxTransactionBytes} bytes.");
    }

    private static void ValidateTransaction(ReadOnlyMemory<byte> bytes, string name)
    {
        if (bytes.IsEmpty) throw new ArgumentException("Transaction must not be empty.", name);
        ValidateOptionalTransaction(bytes, name);
    }

    private static L2PayoutRelayState ParseState(byte value)
    {
        var state = (L2PayoutRelayState)value;
        if (state is < L2PayoutRelayState.Enqueued or > L2PayoutRelayState.Poisoned)
            throw new InvalidDataException($"Unsupported payout relay state {value}.");
        return state;
    }

    private static void WriteBytes(BinaryWriter writer, byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static byte[] ReadBytes(BinaryReader reader, int maximumLength)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > maximumLength)
            throw new InvalidDataException($"Payout relay field length {length} is invalid.");
        return ReadExact(reader, length);
    }

    private static byte[] ReadExact(BinaryReader reader, int length)
    {
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return bytes;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
