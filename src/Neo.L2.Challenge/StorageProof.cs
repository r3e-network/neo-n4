using System.Buffers.Binary;

namespace Neo.L2.Challenge;

/// <summary>
/// One storage-key inclusion proof carried by a v3 <see cref="FraudProofPayload"/>.
/// Together with the v1 header's <c>PreStateRoot</c> + <c>ClaimedPostStateRoot</c> /
/// <c>ReplayedPostStateRoot</c>, an L1 fraud verifier (or off-chain re-execution
/// service) has enough data to verify the disputed transaction's storage reads + writes
/// without trusting the challenger's replay.
/// </summary>
/// <remarks>
/// Each instance describes a single storage key the disputed tx touched. The verifier:
/// <list type="number">
///   <item><description>Computes pre-leaf-hash = <c>Hash256(keyLen||key||preValueLen||preValue)</c></description></item>
///   <item><description>Folds with <see cref="PreSiblings"/> + <see cref="LeafIndex"/> →
///     re-derives the pre-state root; compares to <see cref="FraudProofPayload.PreStateRoot"/></description></item>
///   <item><description>Same for post side: post-leaf-hash with <see cref="PostValue"/>, fold with
///     <see cref="PostSiblings"/> + <see cref="LeafIndex"/>, compare to
///     <c>ClaimedPostStateRoot</c> (sequencer's claim) or <c>ReplayedPostStateRoot</c>
///     (challenger's claim)</description></item>
/// </list>
/// <para>
/// Caps prevent unbounded payloads. <see cref="MaxKeyBytes"/> + <see cref="MaxValueBytes"/>
/// + <see cref="MaxSiblingDepth"/> are validated by <see cref="FraudProofPayload"/>'s
/// encode/decode.
/// </para>
/// </remarks>
public sealed record StorageProof
{
    /// <summary>Cap on a single storage key's length.</summary>
    public const int MaxKeyBytes = 256;

    /// <summary>Cap on a single value's length (pre or post).</summary>
    public const int MaxValueBytes = 4096;

    /// <summary>Cap on Merkle tree depth (= number of siblings per side).</summary>
    public const int MaxSiblingDepth = 64;

    /// <summary>The storage key the disputed transaction touched.</summary>
    public required ReadOnlyMemory<byte> Key { get; init; }

    /// <summary>The value at <see cref="Key"/> before the tx ran.</summary>
    public required ReadOnlyMemory<byte> PreValue { get; init; }

    /// <summary>The value at <see cref="Key"/> after the tx ran.</summary>
    public required ReadOnlyMemory<byte> PostValue { get; init; }

    /// <summary>Position of the key's leaf in the Merkle tree (same in pre + post when the tx only modifies existing keys).</summary>
    public required ulong LeafIndex { get; init; }

    /// <summary>Sibling hashes from leaf to root in the pre-state Merkle tree. Length = tree depth.</summary>
    public IReadOnlyList<UInt256> PreSiblings { get; init; } = Array.Empty<UInt256>();

    /// <summary>Sibling hashes from leaf to root in the post-state Merkle tree. Length = tree depth.</summary>
    public IReadOnlyList<UInt256> PostSiblings { get; init; } = Array.Empty<UInt256>();

    /// <summary>Encoded byte size for this single proof.</summary>
    public int EncodedSize =>
        2 + Key.Length +
        4 + PreValue.Length +
        4 + PostValue.Length +
        8 +
        1 + 32 * PreSiblings.Count +
        1 + 32 * PostSiblings.Count;

    /// <summary>Encode to canonical bytes at <paramref name="span"/>; returns the number of bytes written.</summary>
    public int Encode(Span<byte> span)
    {
        ValidateCaps();
        var pos = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(pos, 2), (ushort)Key.Length); pos += 2;
        Key.Span.CopyTo(span.Slice(pos, Key.Length)); pos += Key.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), (uint)PreValue.Length); pos += 4;
        PreValue.Span.CopyTo(span.Slice(pos, PreValue.Length)); pos += PreValue.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), (uint)PostValue.Length); pos += 4;
        PostValue.Span.CopyTo(span.Slice(pos, PostValue.Length)); pos += PostValue.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), LeafIndex); pos += 8;
        span[pos++] = (byte)PreSiblings.Count;
        foreach (var s in PreSiblings) { s.GetSpan().CopyTo(span.Slice(pos, 32)); pos += 32; }
        span[pos++] = (byte)PostSiblings.Count;
        foreach (var s in PostSiblings) { s.GetSpan().CopyTo(span.Slice(pos, 32)); pos += 32; }
        return pos;
    }

    /// <summary>Decode a single canonical storage proof at <paramref name="bytes"/>; returns (proof, bytesConsumed).</summary>
    public static (StorageProof proof, int consumed) Decode(ReadOnlySpan<byte> bytes)
    {
        var pos = 0;
        if (bytes.Length < pos + 2) throw new ArgumentException("storage proof: truncated keyLen");
        var keyLen = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(pos, 2)); pos += 2;
        if (keyLen > MaxKeyBytes) throw new ArgumentException($"storage proof: key length {keyLen} > {MaxKeyBytes}");
        if (bytes.Length < pos + keyLen) throw new ArgumentException("storage proof: truncated key");
        var key = bytes.Slice(pos, keyLen).ToArray(); pos += keyLen;

        if (bytes.Length < pos + 4) throw new ArgumentException("storage proof: truncated preValueLen");
        var preLen = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(pos, 4)); pos += 4;
        if (preLen > MaxValueBytes) throw new ArgumentException($"storage proof: preValue length {preLen} > {MaxValueBytes}");
        if (bytes.Length < pos + preLen) throw new ArgumentException("storage proof: truncated preValue");
        var preVal = bytes.Slice(pos, (int)preLen).ToArray(); pos += (int)preLen;

        if (bytes.Length < pos + 4) throw new ArgumentException("storage proof: truncated postValueLen");
        var postLen = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(pos, 4)); pos += 4;
        if (postLen > MaxValueBytes) throw new ArgumentException($"storage proof: postValue length {postLen} > {MaxValueBytes}");
        if (bytes.Length < pos + postLen) throw new ArgumentException("storage proof: truncated postValue");
        var postVal = bytes.Slice(pos, (int)postLen).ToArray(); pos += (int)postLen;

        if (bytes.Length < pos + 8) throw new ArgumentException("storage proof: truncated leafIndex");
        var leafIndex = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(pos, 8)); pos += 8;

        if (bytes.Length < pos + 1) throw new ArgumentException("storage proof: truncated preSiblingCount");
        var preSibCount = bytes[pos++];
        if (preSibCount > MaxSiblingDepth) throw new ArgumentException($"storage proof: preSiblings {preSibCount} > {MaxSiblingDepth}");
        var preSiblings = new UInt256[preSibCount];
        for (var i = 0; i < preSibCount; i++)
        {
            if (bytes.Length < pos + 32) throw new ArgumentException($"storage proof: truncated preSibling[{i}]");
            preSiblings[i] = new UInt256(bytes.Slice(pos, 32)); pos += 32;
        }

        if (bytes.Length < pos + 1) throw new ArgumentException("storage proof: truncated postSiblingCount");
        var postSibCount = bytes[pos++];
        if (postSibCount > MaxSiblingDepth) throw new ArgumentException($"storage proof: postSiblings {postSibCount} > {MaxSiblingDepth}");
        var postSiblings = new UInt256[postSibCount];
        for (var i = 0; i < postSibCount; i++)
        {
            if (bytes.Length < pos + 32) throw new ArgumentException($"storage proof: truncated postSibling[{i}]");
            postSiblings[i] = new UInt256(bytes.Slice(pos, 32)); pos += 32;
        }

        return (new StorageProof
        {
            Key = key,
            PreValue = preVal,
            PostValue = postVal,
            LeafIndex = leafIndex,
            PreSiblings = preSiblings,
            PostSiblings = postSiblings,
        }, pos);
    }

    private void ValidateCaps()
    {
        if (Key.Length > MaxKeyBytes)
            throw new InvalidOperationException($"Key length {Key.Length} > MaxKeyBytes ({MaxKeyBytes})");
        if (PreValue.Length > MaxValueBytes)
            throw new InvalidOperationException($"PreValue length {PreValue.Length} > MaxValueBytes ({MaxValueBytes})");
        if (PostValue.Length > MaxValueBytes)
            throw new InvalidOperationException($"PostValue length {PostValue.Length} > MaxValueBytes ({MaxValueBytes})");
        if (PreSiblings.Count > MaxSiblingDepth)
            throw new InvalidOperationException($"PreSiblings count {PreSiblings.Count} > MaxSiblingDepth ({MaxSiblingDepth})");
        if (PostSiblings.Count > MaxSiblingDepth)
            throw new InvalidOperationException($"PostSiblings count {PostSiblings.Count} > MaxSiblingDepth ({MaxSiblingDepth})");
    }

    /// <inheritdoc />
    public bool Equals(StorageProof? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (LeafIndex != other.LeafIndex) return false;
        if (!Key.Span.SequenceEqual(other.Key.Span)) return false;
        if (!PreValue.Span.SequenceEqual(other.PreValue.Span)) return false;
        if (!PostValue.Span.SequenceEqual(other.PostValue.Span)) return false;
        if (PreSiblings.Count != other.PreSiblings.Count) return false;
        for (var i = 0; i < PreSiblings.Count; i++)
            if (!Equals(PreSiblings[i], other.PreSiblings[i])) return false;
        if (PostSiblings.Count != other.PostSiblings.Count) return false;
        for (var i = 0; i < PostSiblings.Count; i++)
            if (!Equals(PostSiblings[i], other.PostSiblings[i])) return false;
        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(LeafIndex);
        hc.Add(Key.Length);
        hc.Add(PreValue.Length);
        hc.Add(PostValue.Length);
        hc.Add(PreSiblings.Count);
        hc.Add(PostSiblings.Count);
        return hc.ToHashCode();
    }
}
