namespace Neo.L2.State;

/// <summary>
/// Canonical wire encoding for <see cref="MerkleProof"/>. The L1 NeoHub.SharedBridge contract
/// reads this format off the wire when verifying user withdrawal proofs, so the byte layout is
/// part of the off-chain ↔ on-chain contract — do not break it without coordinating the
/// matching contract change.
/// </summary>
/// <remarks>
/// Layout (all little-endian):
/// <code>
/// offset  size  field
/// 0       32    Leaf hash
/// 32      4     LeafIndex (uint32)
/// 36      8     PathBitmap (uint64)
/// 44      4     SiblingCount (uint32)
/// 48      32*N  Siblings (in leaf-to-root order)
/// </code>
/// Total: 48 + 32 × SiblingCount bytes.
/// </remarks>
public static class MerkleProofSerializer
{
    /// <summary>Maximum supported tree depth (matches <see cref="MerkleTree.Verify"/>).</summary>
    public const int MaxDepth = 64;

    /// <summary>Fixed-size header before the sibling array.</summary>
    public const int HeaderSize = 32 + 4 + 8 + 4;

    /// <summary>Encode <paramref name="proof"/> as canonical bytes.</summary>
    public static byte[] Encode(MerkleProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        // Defense-in-depth: Leaf and Siblings entries are UInt256 (reference type); a
        // null would crash in GetSpan() with no link back to the caller. Same iter-154+
        // hashing-primitive null-guard pattern. Sibling-entry check sits in the loop.
        ArgumentNullException.ThrowIfNull(proof.Leaf);
        ArgumentNullException.ThrowIfNull(proof.Siblings);
        if (proof.LeafIndex < 0)
            throw new ArgumentException("LeafIndex must be non-negative", nameof(proof));
        if (proof.Siblings.Count > MaxDepth)
            throw new ArgumentException($"SiblingCount {proof.Siblings.Count} exceeds MaxDepth {MaxDepth}", nameof(proof));

        var bytes = new byte[HeaderSize + 32 * proof.Siblings.Count];
        var span = bytes.AsSpan();

        proof.Leaf.GetSpan().CopyTo(span[..32]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[32..36], (uint)proof.LeafIndex);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span[36..44], proof.PathBitmap);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[44..48], (uint)proof.Siblings.Count);

        var cursor = HeaderSize;
        for (var i = 0; i < proof.Siblings.Count; i++)
        {
            var sibling = proof.Siblings[i] ?? throw new ArgumentException(
                $"Siblings[{i}] is null", nameof(proof));
            sibling.GetSpan().CopyTo(span[cursor..(cursor + 32)]);
            cursor += 32;
        }
        return bytes;
    }

    /// <summary>Decode a previously-<see cref="Encode"/>d proof. Throws on truncated or oversized input.</summary>
    public static MerkleProof Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
            throw new ArgumentException($"MerkleProof needs at least {HeaderSize} bytes, got {bytes.Length}", nameof(bytes));

        var leaf = new UInt256(bytes[..32]);
        var leafIndex = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes[32..36]);
        var pathBitmap = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes[36..44]);
        var siblingCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes[44..48]);

        if (siblingCount > MaxDepth)
            throw new ArgumentException($"SiblingCount {siblingCount} exceeds MaxDepth {MaxDepth}", nameof(bytes));
        // The encoder writes leafIndex as a uint via `(uint)proof.LeafIndex` after a
        // `LeafIndex < 0` check (Encode line 33), so honest output is in [0, int.MaxValue].
        // A malicious or corrupt input could carry leafIndex > int.MaxValue; the (int)cast
        // below would silently wrap to negative, and downstream MerkleTree.Verify's
        // `(uint)idx >= (uint)LeafCount` check evaluates `(uint)(-1) = 0xFFFFFFFF` which
        // happens to bounds-fail correctly — but only for that path. Defense-in-depth:
        // reject at decode time so the bad bytes never become a typed MerkleProof.
        if (leafIndex > int.MaxValue)
            throw new ArgumentException(
                $"LeafIndex {leafIndex} exceeds int.MaxValue (encoder produces signed-positive)", nameof(bytes));

        var expected = HeaderSize + 32 * (int)siblingCount;
        if (bytes.Length != expected)
            throw new ArgumentException($"MerkleProof: expected {expected} bytes for siblingCount={siblingCount}, got {bytes.Length}", nameof(bytes));

        var siblings = new UInt256[siblingCount];
        var cursor = HeaderSize;
        for (var i = 0; i < siblingCount; i++)
        {
            siblings[i] = new UInt256(bytes.Slice(cursor, 32));
            cursor += 32;
        }

        return new MerkleProof
        {
            Leaf = leaf,
            LeafIndex = (int)leafIndex,
            Siblings = siblings,
            PathBitmap = pathBitmap,
        };
    }
}
