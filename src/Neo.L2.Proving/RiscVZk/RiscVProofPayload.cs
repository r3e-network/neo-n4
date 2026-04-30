using System.Buffers.Binary;

namespace Neo.L2.Proving.RiscVZk;

/// <summary>
/// Stage 2 (ZK) proof payload — wraps the raw zkVM proof bytes (e.g. SP1 Groth16) plus the
/// proof system identifier, so NeoHub.VerifierRegistry can route to the right backend.
/// </summary>
/// <remarks>
/// See doc.md §7.5 (Stage 2) and §8 (Proof system).
/// </remarks>
public sealed record RiscVProofPayload
{
    /// <summary>Wire-format version (currently <c>1</c>).</summary>
    public const byte Version = 1;

    /// <summary>Identifier of the underlying proof system (e.g. SP1, RISC-Zero).</summary>
    public required ProofSystem ProofSystem { get; init; }

    /// <summary>Raw proof bytes produced by the prover backend.</summary>
    public required ReadOnlyMemory<byte> ProofBytes { get; init; }

    /// <summary>Verification key identifier (used by the verifier to look up VK material).</summary>
    public required UInt256 VerificationKeyId { get; init; }

    /// <summary>Encode to canonical bytes for embedding in <see cref="L2BatchCommitment.Proof"/>.</summary>
    public byte[] Encode()
    {
        var size = 1 + 1 + 32 + 4 + ProofBytes.Length;
        var buffer = new byte[size];
        var span = buffer.AsSpan();
        span[0] = Version;
        span[1] = (byte)ProofSystem;
        VerificationKeyId.GetSpan().CopyTo(span.Slice(2, 32));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(34, 4), ProofBytes.Length);
        ProofBytes.Span.CopyTo(span.Slice(38));
        return buffer;
    }

    /// <summary>Decode a canonical RISC-V ZK proof payload.</summary>
    public static RiscVProofPayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 38) throw new ArgumentException("Buffer too small", nameof(bytes));
        if (bytes[0] != Version) throw new InvalidDataException($"Unsupported RiscV proof version {bytes[0]}");

        var system = (ProofSystem)bytes[1];
        var vk = new UInt256(bytes.Slice(2, 32));
        var len = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(34, 4));
        if (len < 0 || 38 + len != bytes.Length)
            throw new InvalidDataException($"Bad proof length {len}");
        var proof = bytes.Slice(38, len).ToArray();
        return new RiscVProofPayload { ProofSystem = system, ProofBytes = proof, VerificationKeyId = vk };
    }
}

/// <summary>Backend that produced a Stage 2 proof.</summary>
public enum ProofSystem : byte
{
    /// <summary>Sentinel — payload is malformed.</summary>
    Unknown = 0,

    /// <summary>Succinct SP1 (RISC-V zkVM, Groth16 / Plonk wrapper).</summary>
    Sp1 = 1,

    /// <summary>RISC-Zero zkVM.</summary>
    RiscZero = 2,

    /// <summary>Halo2 (KZG-based, no trusted setup).</summary>
    Halo2 = 3,

    /// <summary>Custom Neo Axiom verifier.</summary>
    Axiom = 4,
}
