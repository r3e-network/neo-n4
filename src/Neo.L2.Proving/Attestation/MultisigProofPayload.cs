using System.Buffers.Binary;
using Neo.Cryptography.ECC;

namespace Neo.L2.Proving.Attestation;

/// <summary>
/// Canonical wire format for a Stage 0 multisig attestation proof. The proof bytes that go
/// into <see cref="L2BatchCommitment.Proof"/> are <see cref="Encode"/>'s output.
/// </summary>
/// <remarks>
/// Layout (little-endian):
/// <c>[1B version=1] [2B signerCount] (per signer: [33B compressed-secp256r1-pubkey] [64B sig])</c>.
/// The 64-byte signature is the raw r||s encoding produced by <c>Crypto.Sign</c>.
/// </remarks>
public sealed record MultisigProofPayload
{
    /// <summary>Wire-format version (currently <c>1</c>).</summary>
    public const byte Version = 1;

    /// <summary>Hard cap on signer count on Decode (defensive — production multisigs run 7-21 signers).</summary>
    public const int MaxSigners = 256;

    /// <summary>Per-signer public-key + signature pairs, in canonical (sorted) order.</summary>
    public required IReadOnlyList<SignerSignature> Signatures { get; init; }

    /// <summary>Encode to canonical bytes for embedding in <see cref="L2BatchCommitment.Proof"/>.</summary>
    public byte[] Encode()
    {
        // Defense-in-depth: collection or any entry/PublicKey could be null even with
        // `required` — `required` only forces "must be set," not "non-null."
        ArgumentNullException.ThrowIfNull(Signatures);
        // Symmetry with Decode: Decode rejects > MaxSigners, so refuse to Encode bytes
        // the round-trip would later fail on.
        if (Signatures.Count > MaxSigners)
            throw new InvalidOperationException(
                $"Signatures.Count {Signatures.Count} exceeds MaxSigners {MaxSigners}");
        var size = 1 + 2 + Signatures.Count * (33 + 64);
        var buffer = new byte[size];
        var span = buffer.AsSpan();
        span[0] = Version;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(1, 2), checked((ushort)Signatures.Count));
        var pos = 3;
        for (var i = 0; i < Signatures.Count; i++)
        {
            var s = Signatures[i] ?? throw new ArgumentException(
                $"Signatures[{i}] is null", nameof(Signatures));
            ArgumentNullException.ThrowIfNull(s.PublicKey);
            // Per-signer Signature length must be exactly 64 — Span.CopyTo with a
            // shorter source silently zero-pads the destination, producing an Encode
            // that's structurally valid but semantically wrong (signature won't verify).
            if (s.Signature.Length != 64)
                throw new ArgumentException(
                    $"Signatures[{i}].Signature length {s.Signature.Length} != 64", nameof(Signatures));
            s.PublicKey.GetSpan().CopyTo(span.Slice(pos, 33));
            pos += 33;
            s.Signature.Span.CopyTo(span.Slice(pos, 64));
            pos += 64;
        }
        if (pos != size) throw new InvalidOperationException("MultisigProofPayload encode length mismatch");
        return buffer;
    }

    /// <summary>Decode a canonical multisig proof payload.</summary>
    public static MultisigProofPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 3) throw new ArgumentException("Buffer too small", nameof(bytes));
        if (bytes[0] != Version) throw new InvalidDataException($"Unsupported multisig proof version {bytes[0]}");

        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(1, 2));
        if (count > MaxSigners)
            throw new InvalidDataException($"signerCount {count} exceeds MaxSigners {MaxSigners}");
        var expected = 3 + count * (33 + 64);
        if (bytes.Length != expected)
            throw new InvalidDataException($"Buffer size {bytes.Length} != expected {expected} for {count} signers");

        var sigs = new SignerSignature[count];
        var pos = 3;
        for (var i = 0; i < count; i++)
        {
            var pk = ECPoint.DecodePoint(bytes.Slice(pos, 33), ECCurve.Secp256r1);
            pos += 33;
            var sig = bytes.Slice(pos, 64).ToArray();
            pos += 64;
            sigs[i] = new SignerSignature { PublicKey = pk, Signature = sig };
        }
        return new MultisigProofPayload { Signatures = sigs };
    }
}

/// <summary>A single signer's contribution to a multisig attestation.</summary>
public sealed record SignerSignature
{
    /// <summary>Compressed secp256r1 public key (33 bytes).</summary>
    public required ECPoint PublicKey { get; init; }

    /// <summary>Raw 64-byte signature (r||s) over the canonical public-input bytes.</summary>
    public required ReadOnlyMemory<byte> Signature { get; init; }
}
