using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Neo.Cryptography;
using Neo.SmartContract;
using Neo.VM;
using Neo.VM.Types;
using Array = Neo.VM.Types.Array;

namespace Neo.L2.Executor.Effects;

/// <summary>Version tag for canonical per-transaction execution effects.</summary>
/// <remarks>See <c>doc.md</c> §7.2 and §8.1.</remarks>
public enum CanonicalEffectsVersion : byte
{
    /// <summary>Domain-separated storage and event encodings defined by executor <c>SPEC.md</c>.</summary>
    V1 = 1,
}

/// <summary>Canonical storage operation committed by a successful transaction.</summary>
/// <remarks>See <c>doc.md</c> §7.2 and §8.4.</remarks>
public enum CanonicalStorageOperation : byte
{
    /// <summary>Insert or replace a value.</summary>
    Put = 1,

    /// <summary>Delete an existing value.</summary>
    Delete = 2,
}

/// <summary>One canonical storage transition, including its complete before and after images.</summary>
/// <remarks>See <c>doc.md</c> §7.2 and §8.4.</remarks>
public sealed record CanonicalStorageChange
{
    /// <summary>Complete storage key bytes, including any contract namespace prefix.</summary>
    public required ReadOnlyMemory<byte> Key { get; init; }

    /// <summary>Operation applied to the key.</summary>
    public required CanonicalStorageOperation Operation { get; init; }

    /// <summary>Value before execution, or <c>null</c> when the key was absent.</summary>
    public ReadOnlyMemory<byte>? OldValue { get; init; }

    /// <summary>Value after execution, or <c>null</c> for a delete.</summary>
    public ReadOnlyMemory<byte>? NewValue { get; init; }

    /// <inheritdoc />
    public bool Equals(CanonicalStorageChange? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Operation == other.Operation
            && Key.Span.SequenceEqual(other.Key.Span)
            && OptionalBytesEqual(OldValue, other.OldValue)
            && OptionalBytesEqual(NewValue, other.NewValue);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Operation);
        hash.AddBytes(Key.Span);
        AddOptionalBytes(ref hash, OldValue);
        AddOptionalBytes(ref hash, NewValue);
        return hash.ToHashCode();
    }

    internal CanonicalStorageChange Copy() => new()
    {
        Key = Key.ToArray(),
        Operation = Operation,
        OldValue = CopyOptional(OldValue),
        NewValue = CopyOptional(NewValue),
    };

    private static ReadOnlyMemory<byte>? CopyOptional(ReadOnlyMemory<byte>? value)
    {
        if (!value.HasValue) return null;
        return new ReadOnlyMemory<byte>(value.Value.ToArray());
    }

    private static bool OptionalBytesEqual(ReadOnlyMemory<byte>? left, ReadOnlyMemory<byte>? right)
        => left.HasValue == right.HasValue
            && (!left.HasValue || left.Value.Span.SequenceEqual(right!.Value.Span));

    private static void AddOptionalBytes(ref HashCode hash, ReadOnlyMemory<byte>? value)
    {
        hash.Add(value.HasValue);
        if (value.HasValue) hash.AddBytes(value.Value.Span);
    }
}

/// <summary>One ordered notification with its complete canonical Neo stack-state bytes.</summary>
/// <remarks>See <c>doc.md</c> §7.2 and §8.1.</remarks>
public sealed record CanonicalExecutionEvent
{
    /// <summary>Script hash that emitted the notification.</summary>
    public required UInt160 ScriptHash { get; init; }

    /// <summary>UTF-8 event name.</summary>
    public required string EventName { get; init; }

    /// <summary>Canonical <see cref="BinarySerializer"/> encoding of the complete event state.</summary>
    public required ReadOnlyMemory<byte> State { get; init; }

    /// <inheritdoc />
    public bool Equals(CanonicalExecutionEvent? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ScriptHash.Equals(other.ScriptHash)
            && string.Equals(EventName, other.EventName, StringComparison.Ordinal)
            && State.Span.SequenceEqual(other.State.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ScriptHash);
        hash.Add(EventName, StringComparer.Ordinal);
        hash.AddBytes(State.Span);
        return hash.ToHashCode();
    }

    internal CanonicalExecutionEvent Copy() => new()
    {
        ScriptHash = ScriptHash,
        EventName = EventName,
        State = State.ToArray(),
    };
}

/// <summary>
/// Immutable storage and notification material used both to build a receipt and to expose
/// successful transaction effects to downstream collectors.
/// </summary>
/// <remarks>See <c>doc.md</c> §7.2, §7.3, and §8.1.</remarks>
public sealed record CanonicalExecutionEffects
{
    /// <summary>Effect-free transaction material.</summary>
    public static CanonicalExecutionEffects Empty { get; } = Create(
        System.Array.Empty<CanonicalStorageChange>(),
        System.Array.Empty<NotifyEventArgs>());

    /// <summary>Canonical storage transitions in lexicographic full-key order.</summary>
    public required IReadOnlyList<CanonicalStorageChange> StorageChanges { get; init; }

    /// <summary>Canonical events in execution order.</summary>
    public required IReadOnlyList<CanonicalExecutionEvent> Events { get; init; }

    /// <summary>Immutable notification objects from the same event source used by <see cref="Events"/>.</summary>
    public required IReadOnlyList<NotifyEventArgs> Notifications { get; init; }

    /// <summary>V1 hash of <see cref="StorageChanges"/>; empty changes map to <see cref="UInt256.Zero"/>.</summary>
    public required UInt256 StorageHash { get; init; }

    /// <summary>V1 hash of <see cref="Events"/>; empty events map to <see cref="UInt256.Zero"/>.</summary>
    public required UInt256 EventsHash { get; init; }

    /// <summary>Create immutable canonical effects from committed storage transitions and notifications.</summary>
    public static CanonicalExecutionEffects Create(
        IReadOnlyList<CanonicalStorageChange> storageChanges,
        IReadOnlyList<NotifyEventArgs> notifications,
        CanonicalEffectsVersion version = CanonicalEffectsVersion.V1)
    {
        ArgumentNullException.ThrowIfNull(storageChanges);
        ArgumentNullException.ThrowIfNull(notifications);

        var canonicalStorage = storageChanges
            .Select(static change =>
            {
                ArgumentNullException.ThrowIfNull(change);
                return change.Copy();
            })
            .OrderBy(static change => change.Key.ToArray(), LexicographicByteArrayComparer.Instance)
            .ToArray();

        var immutableNotifications = new NotifyEventArgs[notifications.Count];
        var canonicalEvents = new CanonicalExecutionEvent[notifications.Count];
        for (var index = 0; index < notifications.Count; index++)
        {
            var notification = notifications[index]
                ?? throw new ArgumentException("notifications cannot contain null", nameof(notifications));
            ArgumentNullException.ThrowIfNull(notification.ScriptHash);
            ArgumentNullException.ThrowIfNull(notification.EventName);
            ArgumentNullException.ThrowIfNull(notification.State);

            var state = (Array)notification.State.DeepCopy(asImmutable: true);
            var immutable = new NotifyEventArgs(
                notification.ScriptContainer,
                notification.ScriptHash,
                notification.EventName,
                state);
            immutableNotifications[index] = immutable;
            canonicalEvents[index] = new CanonicalExecutionEvent
            {
                ScriptHash = immutable.ScriptHash,
                EventName = immutable.EventName,
                State = BinarySerializer.Serialize(
                    immutable.State,
                    ApplicationEngine.MaxNotificationSize,
                    ExecutionEngineLimits.Default.MaxStackSize),
            };
        }

        return new CanonicalExecutionEffects
        {
            StorageChanges = canonicalStorage,
            Events = canonicalEvents,
            Notifications = immutableNotifications,
            StorageHash = CanonicalEffectsHasher.HashStorage(canonicalStorage, version),
            EventsHash = CanonicalEffectsHasher.HashEvents(canonicalEvents, version),
        };
    }
}

/// <summary>Versioned canonical encoder and Hash256 implementation for receipt effects.</summary>
/// <remarks>See <c>doc.md</c> §7.2, §8.1, and §8.3.</remarks>
public static class CanonicalEffectsHasher
{
    private static readonly byte[] StorageDomain = Encoding.ASCII.GetBytes("NEO.L2.EFFECTS.STORAGE");
    private static readonly byte[] EventsDomain = Encoding.ASCII.GetBytes("NEO.L2.EFFECTS.EVENTS");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Hash canonical storage transitions, mapping an empty set to <see cref="UInt256.Zero"/>.</summary>
    public static UInt256 HashStorage(
        IReadOnlyList<CanonicalStorageChange> changes,
        CanonicalEffectsVersion version = CanonicalEffectsVersion.V1)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0) return UInt256.Zero;
        return new UInt256(Crypto.Hash256(SerializeStorage(changes, version)));
    }

    /// <summary>Hash ordered canonical events, mapping an empty sequence to <see cref="UInt256.Zero"/>.</summary>
    public static UInt256 HashEvents(
        IReadOnlyList<CanonicalExecutionEvent> events,
        CanonicalEffectsVersion version = CanonicalEffectsVersion.V1)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0) return UInt256.Zero;
        return new UInt256(Crypto.Hash256(SerializeEvents(events, version)));
    }

    /// <summary>Serialize storage transitions using the selected canonical version.</summary>
    public static byte[] SerializeStorage(
        IReadOnlyList<CanonicalStorageChange> changes,
        CanonicalEffectsVersion version = CanonicalEffectsVersion.V1)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (version != CanonicalEffectsVersion.V1)
            throw new ArgumentOutOfRangeException(nameof(version), version, "unsupported canonical effects version");

        var ordered = changes
            .Select(static change => change ?? throw new ArgumentException("changes cannot contain null"))
            .OrderBy(static change => change.Key.ToArray(), LexicographicByteArrayComparer.Instance)
            .ToArray();
        var writer = new ArrayBufferWriter<byte>();
        WriteBytes(writer, StorageDomain);
        WriteByte(writer, (byte)version);
        WriteUInt32(writer, checked((uint)ordered.Length));

        byte[]? previousKey = null;
        foreach (var change in ordered)
        {
            ValidateStorageChange(change);
            var key = change.Key.ToArray();
            if (previousKey is not null && previousKey.AsSpan().SequenceEqual(key))
                throw new ArgumentException("storage changes contain duplicate full keys", nameof(changes));
            previousKey = key;

            WriteLengthPrefixedBytes(writer, key);
            WriteByte(writer, (byte)change.Operation);
            WriteOptionalBytes(writer, change.OldValue);
            WriteOptionalBytes(writer, change.NewValue);
        }

        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Serialize events in execution order using the selected canonical version.</summary>
    public static byte[] SerializeEvents(
        IReadOnlyList<CanonicalExecutionEvent> events,
        CanonicalEffectsVersion version = CanonicalEffectsVersion.V1)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (version != CanonicalEffectsVersion.V1)
            throw new ArgumentOutOfRangeException(nameof(version), version, "unsupported canonical effects version");

        var writer = new ArrayBufferWriter<byte>();
        WriteBytes(writer, EventsDomain);
        WriteByte(writer, (byte)version);
        WriteUInt32(writer, checked((uint)events.Count));
        foreach (var @event in events)
        {
            ArgumentNullException.ThrowIfNull(@event);
            ArgumentNullException.ThrowIfNull(@event.ScriptHash);
            ArgumentNullException.ThrowIfNull(@event.EventName);
            WriteBytes(writer, @event.ScriptHash.GetSpan());
            WriteLengthPrefixedBytes(writer, StrictUtf8.GetBytes(@event.EventName));
            WriteLengthPrefixedBytes(writer, @event.State.Span);
        }

        return writer.WrittenSpan.ToArray();
    }

    private static void ValidateStorageChange(CanonicalStorageChange change)
    {
        if (change.Key.IsEmpty)
            throw new ArgumentException("canonical storage key cannot be empty", nameof(change));
        if (!Enum.IsDefined(change.Operation))
            throw new ArgumentOutOfRangeException(nameof(change), change.Operation, "unknown storage operation");
        if (change.Operation == CanonicalStorageOperation.Put && !change.NewValue.HasValue)
            throw new ArgumentException("put must carry a new value", nameof(change));
        if (change.Operation == CanonicalStorageOperation.Delete && change.NewValue.HasValue)
            throw new ArgumentException("delete cannot carry a new value", nameof(change));
        if (change.Operation == CanonicalStorageOperation.Delete && !change.OldValue.HasValue)
            throw new ArgumentException("delete must bind the value being removed", nameof(change));
    }

    private static void WriteOptionalBytes(IBufferWriter<byte> writer, ReadOnlyMemory<byte>? value)
    {
        WriteByte(writer, value.HasValue ? (byte)1 : (byte)0);
        if (value.HasValue) WriteLengthPrefixedBytes(writer, value.Value.Span);
    }

    private static void WriteLengthPrefixedBytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        WriteUInt32(writer, checked((uint)value.Length));
        WriteBytes(writer, value);
    }

    private static void WriteUInt32(IBufferWriter<byte> writer, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(writer.GetSpan(sizeof(uint)), value);
        writer.Advance(sizeof(uint));
    }

    private static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }

    private static void WriteBytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        value.CopyTo(writer.GetSpan(value.Length));
        writer.Advance(value.Length);
    }
}
