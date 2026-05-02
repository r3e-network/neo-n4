using Neo.L2;
using Neo.L2.State;

namespace Neo.Plugins.L2Gateway;

/// <summary>
/// Reference aggregator: collects commitments and emits a trivial pass-through aggregation
/// over them. The "aggregated proof" is the concatenation of constituent proofs prefixed by
/// a 4B count and a 4B per-proof length — easy to deserialize and verify against the
/// individual chain verifiers, but no recursive ZK reduction.
/// </summary>
/// <remarks>
/// This is the right backend for Phase 5 dev/devnet integration; for Phase 5 production,
/// swap for a real aggregation prover (SP1 recursion, Halo2 accumulation, or a custom STARK
/// fold).
/// </remarks>
public sealed class PassThroughAggregator : IGatewayAggregator
{
    private readonly Lock _gate = new();
    private readonly List<L2BatchCommitment> _pending = new();

    /// <summary>Constant backend id for this trivial aggregation strategy.</summary>
    public const byte BackendId = 0xFF;

    /// <inheritdoc />
    public int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    /// <inheritdoc />
    public void Submit(L2BatchCommitment commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        lock (_gate) _pending.Add(commitment);
    }

    /// <inheritdoc />
    public AggregatedCommitment? Aggregate()
    {
        L2BatchCommitment[] snapshot;
        lock (_gate)
        {
            if (_pending.Count == 0) return null;
            snapshot = _pending.ToArray();
            _pending.Clear();
        }

        // Compute global message root over the L2-to-L2 roots of every constituent.
        var l2L2Roots = new UInt256[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++) l2L2Roots[i] = snapshot[i].L2ToL2MessageRoot;
        var globalRoot = MerkleTree.ComputeRoot(l2L2Roots);

        var proof = ConcatenateProofs(snapshot);

        return new AggregatedCommitment
        {
            Constituents = snapshot,
            GlobalMessageRoot = globalRoot,
            AggregatedProof = proof,
            BackendId = BackendId,
        };
    }

    private static byte[] ConcatenateProofs(L2BatchCommitment[] batches)
    {
        // 4B count + (4B len + len bytes) per constituent.
        // Use checked arithmetic so a pathological N × proofLen that overflows int (~2 GiB)
        // surfaces as an OverflowException naming the operation, rather than wrapping to a
        // negative size and throwing OverflowException from `new byte[neg]` deep in the
        // allocator with no link back to the cause.
        var totalSize = 4;
        for (var i = 0; i < batches.Length; i++)
            totalSize = checked(totalSize + 4 + batches[i].Proof.Length);
        var buffer = new byte[totalSize];
        var span = buffer.AsSpan();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), batches.Length);
        var pos = 4;
        for (var i = 0; i < batches.Length; i++)
        {
            var proof = batches[i].Proof;
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), proof.Length);
            pos += 4;
            proof.Span.CopyTo(span.Slice(pos));
            pos += proof.Length;
        }
        return buffer;
    }
}
