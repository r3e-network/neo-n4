using System.Buffers.Binary;
using Neo.Cryptography;

namespace Neo.L2.Challenge;

/// <summary>
/// Off-chain reference verifier for v3 <see cref="FraudProofPayload"/>s. Demonstrates the
/// algorithm a future on-chain re-execution-capable fraud verifier would mirror: for each
/// <see cref="StorageProof"/> in the payload, re-derive the pre/post Merkle roots from the
/// proof's leaf-hash + siblings + leafIndex and check them against the payload's
/// <see cref="FraudProofPayload.PreStateRoot"/> and
/// <see cref="FraudProofPayload.ReplayedPostStateRoot"/>.
/// </summary>
/// <remarks>
/// <para>
/// Successful verification proves that the challenger's storage proofs are consistent with
/// their replayed post-state — i.e. starting from <c>PreStateRoot</c> and applying just the
/// proof's claimed pre→post value changes for the disputed key produces a tree whose root
/// matches the challenger's <c>ReplayedPostStateRoot</c>. Combined with the structural
/// claim that <c>ClaimedPostStateRoot != ReplayedPostStateRoot</c>, this gives a v3 fraud
/// proof.
/// </para>
/// <para>
/// What this verifier does NOT do: re-execute the disputed transaction. That last step
/// requires running NeoVM on L1 with restricted state (an <c>ApplicationEngine</c> instance
/// seeded only with the storage proofs' pre-values), which is downstream multi-iteration
/// work. This helper proves "the challenger's storage manifests are well-formed and
/// consistent" — the operator-level fraud-arbitration step then re-executes off-chain (or
/// in a future on-chain v3 verifier contract) and compares.
/// </para>
/// <para>
/// Leaf-hash composition matches <c>Neo.L2.Executor.State.KeyedStateStore.HashEntry</c>:
/// <c>Hash256(int32LE(keyLen) || key || int32LE(valueLen) || value)</c>. Sibling-folding
/// matches <c>Neo.L2.State.MerkleTree</c>: <c>Hash256(left || right)</c>, with the leaf-bit
/// of <c>leafIndex</c> at level <c>i</c> determining whether <c>current</c> is left (bit=0)
/// or right (bit=1) of the sibling at that level.
/// </para>
/// </remarks>
public static class V3StorageProofVerifier
{
    /// <summary>Outcome of a v3 verification pass.</summary>
    public enum Verdict
    {
        /// <summary>All storage proofs verify against the payload's pre/post roots.</summary>
        Verified,

        /// <summary>Payload is not v3 (no storage proofs).</summary>
        NotV3,

        /// <summary>Payload claims no real discrepancy (claimed == replayed).</summary>
        NoDiscrepancy,

        /// <summary>One or more storage proofs' pre-derived root != payload's PreStateRoot.</summary>
        PreStateRootMismatch,

        /// <summary>One or more storage proofs' post-derived root != ReplayedPostStateRoot.</summary>
        ReplayedPostStateRootMismatch,
    }

    /// <summary>
    /// Verify a v3 <see cref="FraudProofPayload"/>.
    /// Returns <see cref="Verdict.Verified"/> on full success; otherwise the specific
    /// failure reason (so callers can log the structural failure mode).
    /// </summary>
    public static Verdict Verify(FraudProofPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.IsV3) return Verdict.NotV3;

        // Same structural check as the v1/v2 verifier: the challenger's whole point is
        // that their replay produced a different post-state. Without this check, a
        // challenger could submit a payload with claimed == replayed and verify
        // successfully — pointless dispute.
        if (Equals(payload.ClaimedPostStateRoot, payload.ReplayedPostStateRoot))
            return Verdict.NoDiscrepancy;

        // Per-proof verification: pre-derived root MUST equal PreStateRoot, post-derived
        // root MUST equal ReplayedPostStateRoot.
        foreach (var proof in payload.StorageProofs)
        {
            var preLeaf = HashEntry(proof.Key.Span, proof.PreValue.Span);
            var preRoot = FoldMerkleProof(preLeaf, proof.PreSiblings, proof.LeafIndex);
            if (!preRoot.Equals(payload.PreStateRoot))
                return Verdict.PreStateRootMismatch;

            var postLeaf = HashEntry(proof.Key.Span, proof.PostValue.Span);
            var postRoot = FoldMerkleProof(postLeaf, proof.PostSiblings, proof.LeafIndex);
            if (!postRoot.Equals(payload.ReplayedPostStateRoot))
                return Verdict.ReplayedPostStateRootMismatch;
        }

        return Verdict.Verified;
    }

    /// <summary>
    /// Compute the canonical leaf hash for a (key, value) pair — same shape as
    /// <c>KeyedStateStore.HashEntry</c>: <c>Hash256(int32LE(keyLen) || key ||
    /// int32LE(valueLen) || value)</c>.
    /// </summary>
    public static UInt256 HashEntry(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var size = checked(4 + key.Length + 4 + value.Length);
        Span<byte> buffer = size <= 256 ? stackalloc byte[size] : new byte[size];
        var pos = 0;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(pos, 4), key.Length); pos += 4;
        key.CopyTo(buffer.Slice(pos, key.Length)); pos += key.Length;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(pos, 4), value.Length); pos += 4;
        value.CopyTo(buffer.Slice(pos, value.Length));
        return new UInt256(Crypto.Hash256(buffer));
    }

    /// <summary>
    /// Fold a leaf hash with its sibling list at each level using the leaf-index bits to
    /// decide left/right ordering. Mirrors <c>Neo.L2.State.MerkleTree</c>'s composition
    /// and the on-chain <c>SettlementManager.VerifyWithdrawalLeafWithProof</c> algorithm.
    /// </summary>
    public static UInt256 FoldMerkleProof(UInt256 leafHash, IReadOnlyList<UInt256> siblings, ulong leafIndex)
    {
        ArgumentNullException.ThrowIfNull(leafHash);
        ArgumentNullException.ThrowIfNull(siblings);

        var current = leafHash.GetSpan().ToArray();  // 32 bytes
        var index = leafIndex;
        for (var i = 0; i < siblings.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(siblings[i]);
            var sibling = siblings[i].GetSpan().ToArray();
            var combined = new byte[64];
            if ((index & 1UL) == 0UL)
            {
                Array.Copy(current, 0, combined, 0, 32);
                Array.Copy(sibling, 0, combined, 32, 32);
            }
            else
            {
                Array.Copy(sibling, 0, combined, 0, 32);
                Array.Copy(current, 0, combined, 32, 32);
            }
            current = Crypto.Hash256(combined);
            index >>= 1;
        }
        return new UInt256(current);
    }
}
