using System.Buffers.Binary;
using Neo.Cryptography;
using Neo.L2.Persistence;
using Neo.L2.State;

namespace Neo.L2.Executor.State;

/// <summary>
/// Sorted-key state store with a deterministic Merkle root computed over
/// <c>Hash256(keyLen || key || valueLen || value)</c> leaves in lexicographic key order.
/// Backed by an <see cref="IL2KeyValueStore"/> — production wires
/// <see cref="RocksDbKeyValueStore"/> for state that survives node restarts; tests +
/// devnets use the default <see cref="InMemoryKeyValueStore"/>.
/// </summary>
/// <remarks>
/// Pragmatic stand-in for an MPT state-root oracle. The Neo MPTTrie source isn't reachable
/// from the local checkout, so this class provides a deterministic, verifiable state root that
/// any L2 implementation can produce identically — sufficient for the proving spec to commit
/// to and for tests / devnet to exercise.
/// <para>
/// Production deployments swap this for a proper MPT or sparse Merkle tree; the wire-format
/// of <see cref="L2BatchCommitment.PostStateRoot"/> stays a 32-byte hash either way.
/// </para>
/// </remarks>
public sealed class KeyedStateStore : IDisposable
{
    private readonly IL2KeyValueStore _backing;
    private readonly bool _ownsBacking;
    private bool _disposed;

    /// <summary>Construct with a default in-memory backing.</summary>
    public KeyedStateStore() : this(new InMemoryKeyValueStore(), ownsBacking: true) { }

    /// <summary>
    /// Construct against a caller-supplied <see cref="IL2KeyValueStore"/> — production
    /// wires <see cref="RocksDbKeyValueStore"/> here for state that survives node restarts.
    /// </summary>
    public KeyedStateStore(IL2KeyValueStore backing, bool ownsBacking = false)
    {
        ArgumentNullException.ThrowIfNull(backing);
        _backing = backing;
        _ownsBacking = ownsBacking;
    }

    /// <summary>Number of stored entries.</summary>
    public int Count => checked((int)_backing.Count);

    /// <summary>Insert or replace a value.</summary>
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(key.Length);
        _backing.Put(key, value);
    }

    /// <summary>Delete a key. Returns <c>true</c> if it existed.</summary>
    public bool Delete(ReadOnlySpan<byte> key) => _backing.Delete(key);

    /// <summary>Read a value, or empty span if missing.</summary>
    public ReadOnlyMemory<byte> Get(ReadOnlySpan<byte> key)
        => _backing.Get(key) ?? Array.Empty<byte>();

    /// <summary>True if a key is present.</summary>
    public bool Contains(ReadOnlySpan<byte> key) => _backing.Contains(key);

    /// <summary>
    /// Compute the deterministic Merkle root of the current store contents.
    /// Returns <see cref="UInt256.Zero"/> when the store is empty.
    /// </summary>
    public UInt256 ComputeRoot()
    {
        // Materialize entries in lex-key order (IL2KeyValueStore guarantees this).
        var entries = _backing.EnumeratePrefix(ReadOnlySpan<byte>.Empty).ToList();
        if (entries.Count == 0) return UInt256.Zero;
        var leaves = new UInt256[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            leaves[i] = HashEntry(entries[i].Key, entries[i].Value);
        return Neo.L2.State.MerkleTree.ComputeRoot(leaves);
    }

    /// <summary>Canonical leaf hash for a (key, value) pair.</summary>
    public static UInt256 HashEntry(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        // checked: key + value are caller-supplied and unbounded; a near-int.MaxValue
        // value would wrap to negative, surfacing only as a confusing
        // OverflowException from `new byte[neg]`.
        var size = checked(4 + key.Length + 4 + value.Length);
        Span<byte> buffer = size <= 256 ? stackalloc byte[size] : new byte[size];
        var pos = 0;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(pos, 4), key.Length); pos += 4;
        key.CopyTo(buffer.Slice(pos, key.Length)); pos += key.Length;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(pos, 4), value.Length); pos += 4;
        value.CopyTo(buffer.Slice(pos, value.Length)); pos += value.Length;
        return new UInt256(Crypto.Hash256(buffer));
    }

    /// <summary>Iterate the store in sorted-key order (test/debug helper).</summary>
    /// <remarks>
    /// Defensive copy: every yielded byte[] is a fresh clone produced by the backing
    /// <see cref="IL2KeyValueStore"/>. Caller mutations cannot reach the underlying store.
    /// Same iter-176 pattern as before, now enforced uniformly across InMemory + RocksDB
    /// backends.
    /// </remarks>
    public IEnumerable<(byte[] Key, byte[] Value)> EnumerateSorted()
        => _backing.EnumeratePrefix(ReadOnlySpan<byte>.Empty);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsBacking) _backing.Dispose();
    }
}
