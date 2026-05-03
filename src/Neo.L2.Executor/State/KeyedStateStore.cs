using System.Buffers.Binary;
using Neo.Cryptography;
using Neo.L2.State;

namespace Neo.L2.Executor.State;

/// <summary>
/// Sorted-key in-memory state store with a deterministic Merkle root computed over
/// <c>Hash256(keyLen || key || valueLen || value)</c> leaves in lexicographic key order.
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
public sealed class KeyedStateStore
{
    // Lexicographic key ordering keeps the Merkle root deterministic across insert orders.
    private readonly SortedDictionary<byte[], byte[]> _data = new(ByteArrayComparer.Lexicographic);

    /// <summary>Number of stored entries.</summary>
    public int Count => _data.Count;

    /// <summary>Insert or replace a value.</summary>
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(key.Length);
        _data[key.ToArray()] = value.ToArray();
    }

    /// <summary>Delete a key. Returns <c>true</c> if it existed.</summary>
    public bool Delete(ReadOnlySpan<byte> key)
    {
        return _data.Remove(key.ToArray());
    }

    /// <summary>Read a value, or empty span if missing.</summary>
    public ReadOnlyMemory<byte> Get(ReadOnlySpan<byte> key)
    {
        return _data.TryGetValue(key.ToArray(), out var value) ? value : Array.Empty<byte>();
    }

    /// <summary>True if a key is present.</summary>
    public bool Contains(ReadOnlySpan<byte> key) => _data.ContainsKey(key.ToArray());

    /// <summary>
    /// Compute the deterministic Merkle root of the current store contents.
    /// Returns <see cref="UInt256.Zero"/> when the store is empty.
    /// </summary>
    public UInt256 ComputeRoot()
    {
        if (_data.Count == 0) return UInt256.Zero;
        var leaves = new UInt256[_data.Count];
        var i = 0;
        foreach (var (key, value) in _data)
        {
            leaves[i++] = HashEntry(key, value);
        }
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
    /// Defensive copy: <c>_data</c> stores raw <c>byte[]</c> references that callers
    /// could mutate. Without the per-entry copy, a debug consumer that touched the
    /// returned bytes would silently corrupt the store's keys/values — Put copies
    /// (iter-167 pattern), Get returns immutable <c>ReadOnlyMemory</c>, but this
    /// helper would otherwise be the lone hole.
    /// </remarks>
    public IEnumerable<(byte[] Key, byte[] Value)> EnumerateSorted()
    {
        foreach (var entry in _data)
            yield return ((byte[])entry.Key.Clone(), (byte[])entry.Value.Clone());
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Lexicographic = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var n = Math.Min(x.Length, y.Length);
            for (var i = 0; i < n; i++)
            {
                if (x[i] != y[i]) return x[i] < y[i] ? -1 : 1;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
