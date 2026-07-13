using System.Runtime.CompilerServices;
using Neo.L2.Executor.Effects;
using Neo.L2.Persistence;

namespace Neo.L2.Executor.State;

/// <summary>
/// Read-through transaction overlay for an <see cref="IL2KeyValueStore"/>. Mutations remain
/// isolated until <see cref="Commit"/> and are discarded by <see cref="Rollback"/>.
/// </summary>
/// <remarks>See <c>doc.md</c> §7.3 and §8.1.</remarks>
public sealed class ExecutionStateTransaction : IL2KeyValueStore
{
    private static readonly ConditionalWeakTable<IL2KeyValueStore, object> CommitGates = new();

    private readonly IL2KeyValueStore _backing;
    private readonly SortedDictionary<byte[], PendingChange> _changes = new(LexicographicByteArrayComparer.Instance);
    private readonly object _gate = new();
    private TransactionStatus _status;

    /// <summary>Create an isolated transaction over <paramref name="backing"/>.</summary>
    public ExecutionStateTransaction(IL2KeyValueStore backing)
    {
        ArgumentNullException.ThrowIfNull(backing);
        _backing = backing;
    }

    /// <summary>Number of keys visible through the overlay.</summary>
    public long Count => EnumeratePrefix(ReadOnlySpan<byte>.Empty).LongCount();

    /// <summary>True after storage changes have been committed.</summary>
    public bool IsCommitted
    {
        get { lock (_gate) return _status == TransactionStatus.Committed; }
    }

    /// <inheritdoc />
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (key.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(key), "key must be non-empty");
        lock (_gate)
        {
            EnsureActive();
            var keyBytes = key.ToArray();
            if (!_changes.TryGetValue(keyBytes, out var change))
            {
                change = new PendingChange(_backing.Get(key), value.ToArray());
                _changes.Add(keyBytes, change);
            }
            else
            {
                change.CurrentValue = value.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public bool TryPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (key.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(key), "key must be non-empty");
        lock (_gate)
        {
            EnsureActive();
            if (GetCore(key) is not null) return false;
            Put(key, value);
            return true;
        }
    }

    /// <inheritdoc />
    public bool CompareExchange(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> expectedValue,
        ReadOnlySpan<byte> newValue)
    {
        if (key.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(key), "key must be non-empty");
        lock (_gate)
        {
            EnsureActive();
            var current = GetCore(key);
            if (current is null || !current.AsSpan().SequenceEqual(expectedValue)) return false;
            Put(key, newValue);
            return true;
        }
    }

    /// <inheritdoc />
    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        lock (_gate)
        {
            EnsureReadable();
            return GetCore(key)?.ToArray();
        }
    }

    /// <inheritdoc />
    public bool Delete(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty) return false;
        lock (_gate)
        {
            EnsureActive();
            var current = GetCore(key);
            if (current is null) return false;

            var keyBytes = key.ToArray();
            if (!_changes.TryGetValue(keyBytes, out var change))
            {
                _changes.Add(
                    keyBytes,
                    new PendingChange(current, null));
            }
            else if (change.OriginalValue is null)
            {
                _changes.Remove(keyBytes);
            }
            else
            {
                change.CurrentValue = null;
            }
            return true;
        }
    }

    /// <inheritdoc />
    public bool Contains(ReadOnlySpan<byte> key)
    {
        lock (_gate)
        {
            EnsureReadable();
            return GetCore(key) is not null;
        }
    }

    /// <inheritdoc />
    public IEnumerable<(byte[] Key, byte[] Value)> EnumeratePrefix(ReadOnlySpan<byte> prefix)
    {
        var prefixBytes = prefix.ToArray();
        lock (_gate)
        {
            EnsureReadable();
            var merged = new SortedDictionary<byte[], byte[]>(LexicographicByteArrayComparer.Instance);
            foreach (var (key, value) in _backing.EnumeratePrefix(prefixBytes))
                merged[key] = value;
            foreach (var (key, change) in _changes)
            {
                if (!key.AsSpan().StartsWith(prefixBytes)) continue;
                if (change.CurrentValue is null) merged.Remove(key);
                else merged[key] = change.CurrentValue.ToArray();
            }
            return merged.Select(static entry => (entry.Key.ToArray(), entry.Value.ToArray())).ToArray();
        }
    }

    /// <summary>Snapshot canonical net transitions without committing them.</summary>
    public IReadOnlyList<CanonicalStorageChange> GetChanges()
    {
        lock (_gate)
        {
            EnsureReadable();
            return _changes
                .Where(static entry => !OptionalBytesEqual(
                    entry.Value.OriginalValue,
                    CopyOptional(entry.Value.CurrentValue)))
                .Select(static entry => new CanonicalStorageChange
                {
                    Key = entry.Key.ToArray(),
                    Operation = Classify(entry.Value),
                    OldValue = CopyOptional(entry.Value.OriginalValue),
                    NewValue = CopyOptional(entry.Value.CurrentValue),
                })
                .ToArray();
        }
    }

    /// <summary>
    /// Apply all staged transitions under a per-store commit gate. A failed store operation is
    /// compensated with the captured before images before the exception is rethrown.
    /// </summary>
    public void Commit()
    {
        lock (_gate)
        {
            EnsureActive();
            var changes = GetChanges();
            var commitGate = CommitGates.GetValue(_backing, static _ => new object());
            lock (commitGate)
            {
                ValidateBeforeImages(changes);
                var applied = new List<CanonicalStorageChange>(changes.Count);
                try
                {
                    foreach (var change in changes)
                    {
                        Apply(change);
                        applied.Add(change);
                    }
                }
                catch (Exception commitError)
                {
                    try
                    {
                        for (var index = applied.Count - 1; index >= 0; index--)
                            Restore(applied[index]);
                    }
                    catch (Exception rollbackError)
                    {
                        throw new AggregateException(
                            "execution state commit and compensating rollback both failed",
                            commitError,
                            rollbackError);
                    }
                    throw;
                }
            }
            _status = TransactionStatus.Committed;
        }
    }

    /// <summary>Discard every staged transition.</summary>
    public void Rollback()
    {
        lock (_gate)
        {
            if (_status != TransactionStatus.Active) return;
            _changes.Clear();
            _status = TransactionStatus.RolledBack;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Rollback();

    private byte[]? GetCore(ReadOnlySpan<byte> key)
    {
        var keyBytes = key.ToArray();
        return _changes.TryGetValue(keyBytes, out var change)
            ? change.CurrentValue
            : _backing.Get(key);
    }

    private void ValidateBeforeImages(IReadOnlyList<CanonicalStorageChange> changes)
    {
        foreach (var change in changes)
        {
            var current = _backing.Get(change.Key.Span);
            if (!OptionalBytesEqual(current, change.OldValue))
                throw new InvalidOperationException(
                    $"execution state changed concurrently for key {Convert.ToHexString(change.Key.Span)}");
        }
    }

    private void Apply(CanonicalStorageChange change)
    {
        if (change.Operation != CanonicalStorageOperation.Delete)
            _backing.Put(change.Key.Span, change.NewValue!.Value.Span);
        else
            _backing.Delete(change.Key.Span);
    }

    private void Restore(CanonicalStorageChange change)
    {
        if (change.OldValue.HasValue)
            _backing.Put(change.Key.Span, change.OldValue.Value.Span);
        else
            _backing.Delete(change.Key.Span);
    }

    private static bool OptionalBytesEqual(byte[]? current, ReadOnlyMemory<byte>? expected)
        => (current is null) == !expected.HasValue
            && (current is null || current.AsSpan().SequenceEqual(expected!.Value.Span));

    private static ReadOnlyMemory<byte>? CopyOptional(byte[]? value)
    {
        if (value is null) return null;
        return new ReadOnlyMemory<byte>(value.ToArray());
    }

    private void EnsureActive()
    {
        if (_status != TransactionStatus.Active)
            throw new InvalidOperationException($"execution state transaction is {_status}");
    }

    private void EnsureReadable()
    {
        if (_status == TransactionStatus.RolledBack)
            throw new InvalidOperationException("execution state transaction was rolled back");
    }

    private static CanonicalStorageOperation Classify(PendingChange change)
    {
        if (change.OriginalValue is null) return CanonicalStorageOperation.Add;
        if (change.CurrentValue is null) return CanonicalStorageOperation.Delete;
        return CanonicalStorageOperation.Update;
    }

    private sealed class PendingChange(byte[]? originalValue, byte[]? currentValue)
    {
        public byte[]? OriginalValue { get; } = originalValue?.ToArray();
        public byte[]? CurrentValue { get; set; } = currentValue?.ToArray();
    }

    private enum TransactionStatus : byte
    {
        Active,
        Committed,
        RolledBack,
    }
}
