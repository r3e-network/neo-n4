using System.Buffers.Binary;

namespace Neo.L2.Bridge.External;

/// <summary>
/// Canonical wire format for the <c>proofBytes</c> argument of
/// <c>NeoHub.MpcCommitteeVerifier.VerifyInboundMessage</c>. Watchers
/// produce this; the contract decodes it.
/// </summary>
/// <remarks>
/// Layout (little-endian):
/// <code>
/// [2B sigCount]
/// [sigCount × ([keyLen B pubkey][64B sig])]
/// </code>
/// where <c>keyLen</c> is 33 for secp256k1 (Ethereum / Tron) or 32 for
/// ed25519 (Solana). The signing curve is determined by the committee's
/// registered <c>curveTag</c>, NOT by the proof bytes themselves —
/// callers MUST encode with a length matching the committee.
/// </remarks>
public sealed record MpcCommitteePayload
{
    /// <summary>Hard cap on signer count on Decode (defensive — production
    /// committees run 7-21; 256 is the contract's MaxCommitteeSize ceiling
    /// times an order-of-magnitude safety factor).</summary>
    public const int MaxSigners = 256;

    /// <summary>secp256k1 — Ethereum / Tron watchers. 33-byte compressed pubkey.</summary>
    public const byte CurveSecp256k1 = 1;
    /// <summary>ed25519 — Solana watchers. 32-byte raw pubkey.</summary>
    public const byte CurveEd25519 = 2;

    /// <summary>The committee curve, determining pubkey length on encode.</summary>
    public required byte CurveTag { get; init; }

    /// <summary>Per-signer (pubkey, signature) pairs. Pubkey length must match
    /// <see cref="CurveTag"/>; signature is always 64 bytes (raw r||s for
    /// secp256k1, R||s for ed25519).</summary>
    public required IReadOnlyList<MpcSignature> Signatures { get; init; }

    /// <summary>Encode to canonical bytes for the contract's
    /// <c>proofBytes</c> argument.</summary>
    public byte[] Encode()
    {
        ArgumentNullException.ThrowIfNull(Signatures);
        if (CurveTag is not (CurveSecp256k1 or CurveEd25519))
            throw new InvalidOperationException(
                $"CurveTag {CurveTag} must be 1 (secp256k1) or 2 (ed25519)");
        if (Signatures.Count > MaxSigners)
            throw new InvalidOperationException(
                $"Signatures.Count {Signatures.Count} exceeds MaxSigners {MaxSigners}");

        var keyLen = CurveTag == CurveSecp256k1 ? 33 : 32;
        var size = 2 + Signatures.Count * (keyLen + 64);
        var buffer = new byte[size];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), checked((ushort)Signatures.Count));
        var pos = 2;
        for (var i = 0; i < Signatures.Count; i++)
        {
            var s = Signatures[i] ?? throw new ArgumentException(
                $"Signatures[{i}] is null", nameof(Signatures));
            if (s.PublicKey.Length != keyLen)
                throw new ArgumentException(
                    $"Signatures[{i}].PublicKey length {s.PublicKey.Length} != {keyLen} for CurveTag {CurveTag}",
                    nameof(Signatures));
            if (s.Signature.Length != 64)
                throw new ArgumentException(
                    $"Signatures[{i}].Signature length {s.Signature.Length} != 64",
                    nameof(Signatures));
            s.PublicKey.Span.CopyTo(buffer.AsSpan(pos, keyLen));
            pos += keyLen;
            s.Signature.Span.CopyTo(buffer.AsSpan(pos, 64));
            pos += 64;
        }
        if (pos != size) throw new InvalidOperationException("MpcCommitteePayload encode length mismatch");
        return buffer;
    }

    /// <summary>Decode canonical bytes back into the record. Used by tooling
    /// that inspects on-chain proof submissions; the contract decodes inline
    /// for gas-cost reasons.</summary>
    public static MpcCommitteePayload Decode(ReadOnlySpan<byte> bytes, byte curveTag)
    {
        if (bytes.Length < 2)
            throw new ArgumentException(
                $"proof bytes length {bytes.Length} < 2 (header)", nameof(bytes));
        if (curveTag is not (CurveSecp256k1 or CurveEd25519))
            throw new ArgumentException(
                $"curveTag {curveTag} must be 1 (secp256k1) or 2 (ed25519)", nameof(curveTag));

        var sigCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes[..2]);
        if (sigCount > MaxSigners)
            throw new ArgumentException(
                $"sigCount {sigCount} exceeds MaxSigners {MaxSigners}", nameof(bytes));

        var keyLen = curveTag == CurveSecp256k1 ? 33 : 32;
        var perSig = keyLen + 64;
        var expected = 2 + sigCount * perSig;
        if (bytes.Length != expected)
            throw new ArgumentException(
                $"proof bytes length {bytes.Length} != expected {expected} (header + sigCount × (keyLen + 64))",
                nameof(bytes));

        var sigs = new MpcSignature[sigCount];
        var pos = 2;
        for (var i = 0; i < sigCount; i++)
        {
            sigs[i] = new MpcSignature
            {
                PublicKey = bytes.Slice(pos, keyLen).ToArray(),
                Signature = bytes.Slice(pos + keyLen, 64).ToArray(),
            };
            pos += perSig;
        }
        return new MpcCommitteePayload { CurveTag = curveTag, Signatures = sigs };
    }
}

/// <summary>One (pubkey, signature) pair from a committee member.</summary>
public sealed record MpcSignature
{
    /// <summary>Compressed secp256k1 (33B) or raw ed25519 (32B) public key.</summary>
    public required ReadOnlyMemory<byte> PublicKey { get; init; }

    /// <summary>Raw signature (64B). For secp256k1 this is r||s; for ed25519 this is R||s.</summary>
    public required ReadOnlyMemory<byte> Signature { get; init; }

    /// <inheritdoc />
    public bool Equals(MpcSignature? other)
    {
        return other is not null
            && PublicKey.Span.SequenceEqual(other.PublicKey.Span)
            && Signature.Span.SequenceEqual(other.Signature.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(PublicKey.Span);
        hash.AddBytes(Signature.Span);
        return hash.ToHashCode();
    }
}
