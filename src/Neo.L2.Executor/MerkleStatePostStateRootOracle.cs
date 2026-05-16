using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Neo.L2.Persistence;
using Neo.L2.State;

namespace Neo.L2.Executor;

/// <summary>
/// Production <see cref="IPostStateRootOracle"/>: computes the post-batch state
/// root as the canonical <see cref="KeyedStateMerkleTree"/> over every key-value
/// pair in the L2's <see cref="IL2KeyValueStore"/> at batch-end time. The tree
/// composition matches the on-chain <c>SettlementManager.VerifyStateLeafWithProof</c>
/// reconstructor byte-for-byte (Neo classic Hash256 with odd-leaf duplication).
/// For tests that don't need a state store, see <see cref="DerivedPostStateRootOracle"/>.
/// </summary>
/// <remarks>
/// <para>
/// Same primitive as ZKsync / Polygon zkEVM / Optimism use for state roots — a
/// binary Merkle tree over sorted (key, value) pairs, hashed pairwise up to a
/// single root. Determinism: same KV state → byte-identical root, regardless of
/// insertion order. Inclusion proofs available via <see cref="Prove"/> for
/// users querying L2 state.
/// </para>
/// <para>
/// O(N) per batch where N = total keys in the store. Production deployments
/// with large state should swap in an incremental MPT-style provider (which
/// only re-hashes paths to changed keys); this implementation prioritizes
/// simplicity + cryptographic correctness over big-N performance.
/// </para>
/// </remarks>
public sealed class MerkleStatePostStateRootOracle : IPostStateRootOracle
{
    private readonly IL2KeyValueStore _state;

    /// <summary>Construct against the L2's KV state store.</summary>
    public MerkleStatePostStateRootOracle(IL2KeyValueStore state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    /// <inheritdoc />
    public ValueTask<UInt256> ResolveAsync(
        UInt256 preStateRoot,
        UInt256 receiptRoot,
        BatchBlockContext blockContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // preStateRoot, receiptRoot, blockContext are passed through but the
        // production oracle ignores them — the post-state root is a function
        // of the live KV state only. (The other inputs go into the public-input
        // bundle separately.) A drift here would silently disconnect the receipt
        // commitment from the state commitment, masking a real bug.
        var pairs = _state.EnumeratePrefix(ReadOnlySpan<byte>.Empty)
            .Select(kv => (kv.Key, kv.Value));
        var root = KeyedStateMerkleTree.ComputeRoot(pairs);
        return new ValueTask<UInt256>(root);
    }

    /// <summary>
    /// Build a Merkle inclusion proof for a specific key in the current state.
    /// Returns <see langword="null"/> if the key isn't present.
    /// </summary>
    public IReadOnlyList<UInt256>? Prove(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var sorted = _state.EnumeratePrefix(ReadOnlySpan<byte>.Empty)
            .OrderBy(kv => kv.Key, ByteSeqComparer.Instance)
            .ToArray();
        var index = -1;
        for (var i = 0; i < sorted.Length; i++)
        {
            if (sorted[i].Key.AsSpan().SequenceEqual(key))
            {
                index = i;
                break;
            }
        }
        if (index < 0) return null;
        return KeyedStateMerkleTree.Prove(sorted.Select(kv => (kv.Key, kv.Value)), index);
    }

    /// <summary>Total leaves in the current state Merkle tree.</summary>
    public int LeafCount => (int)_state.Count;

    private sealed class ByteSeqComparer : IComparer<byte[]>
    {
        public static readonly ByteSeqComparer Instance = new();
        public int Compare(byte[]? x, byte[]? y)
        {
            if (x is null || y is null) return (x is null ? 0 : 1) - (y is null ? 0 : 1);
            var min = Math.Min(x.Length, y.Length);
            for (var i = 0; i < min; i++) { var d = x[i] - y[i]; if (d != 0) return d; }
            return x.Length - y.Length;
        }
    }
}
