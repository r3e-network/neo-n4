using Neo.Cryptography.ECC;
using Neo.L2.State;

namespace Neo.L2.Proving.Attestation;

/// <summary>
/// Canonical signing context for a Stage 0 committee attestation: the exact 40-byte message
/// every committee member signs, and the signer ordering an attestation payload must carry.
/// </summary>
/// <remarks>
/// See doc.md §3.2 (Pillar 3, <c>NeoHub.ZkVerifier</c> multisig route) and §7.5 (Stage 0).
/// <para>
/// <b>Message (40 bytes):</b> ASCII <c>"N4ATTST1"</c> (8 bytes) || <c>publicInputHash</c>
/// (32 bytes). <c>publicInputHash</c> is
/// <see cref="StateRootCalculator.HashPublicInputs"/> — the same 32 bytes
/// <c>BatchSerializer.Encode</c> writes at commitment offset 284, which is where
/// <c>NeoHub.ZkVerifier</c> reads it before calling
/// <c>CryptoLib.VerifyWithECDsa(message, pubkey, signature, secp256r1SHA256)</c>.
/// </para>
/// <para>
/// The <c>"N4ATTST1"</c> prefix domain-separates attestation signatures from the optimistic
/// sequencer claim, which signs the 352-byte public-input preimage: no signature is valid in
/// both contexts even when one key serves both roles.
/// </para>
/// <para>
/// <b>Signer order:</b> signatures are listed strictly ascending by the byte-lexicographic
/// order of the 33-byte compressed public key (<see cref="ComparePublicKeys"/>). This is NOT
/// <see cref="ECPoint.CompareTo"/> (which orders by X, then Y); the contract deduplicates
/// signers by rejecting any non-increasing encoded key.
/// </para>
/// </remarks>
public static class AttestationMessage
{
    /// <summary>Length of the domain tag.</summary>
    public const int DomainSize = 8;

    /// <summary>Total signed-message length (<see cref="DomainSize"/> + 32).</summary>
    public const int Size = DomainSize + 32;

    /// <summary>Maximum signers per attestation and maximum committee size (on-chain cap).</summary>
    public const int MaxSigners = 64;

    /// <summary>ASCII <c>"N4ATTST1"</c>.</summary>
    public static ReadOnlySpan<byte> Domain => "N4ATTST1"u8;

    /// <summary>Build the 40-byte message a committee member signs for <paramref name="publicInputHash"/>.</summary>
    public static byte[] Build(UInt256 publicInputHash)
    {
        ArgumentNullException.ThrowIfNull(publicInputHash);
        var message = new byte[Size];
        Domain.CopyTo(message);
        publicInputHash.GetSpan().CopyTo(message.AsSpan(DomainSize));
        return message;
    }

    /// <summary>Build the 40-byte message for the canonical hash of <paramref name="inputs"/>.</summary>
    public static byte[] Build(PublicInputs inputs) => Build(StateRootCalculator.HashPublicInputs(inputs));

    /// <summary>
    /// Compare two keys by the byte-lexicographic order of their 33-byte compressed encoding —
    /// the order signatures must appear in within an attestation payload.
    /// </summary>
    public static int ComparePublicKeys(ECPoint left, ECPoint right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.EncodePoint(true).AsSpan().SequenceCompareTo(right.EncodePoint(true));
    }
}
