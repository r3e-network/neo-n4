namespace Neo.L2.Bridge.External;

/// <summary>
/// Abstraction the off-chain watcher daemon plugs in for committee-member
/// signing. The watcher implements this against its key-management backend
/// (HSM, KMS, file-based dev keys, etc.) — the framework never sees the
/// private key material.
/// </summary>
/// <remarks>
/// The signer's curve must match what
/// <c>NeoHub.MpcCommitteeVerifier.RegisterCommittee</c> recorded for the
/// target <c>externalChainId</c>: secp256k1 for Eth/Tron, ed25519 for
/// Solana.
/// </remarks>
public interface IExternalBridgeSigner
{
    /// <summary>The signer's public key, in the committee's canonical
    /// encoding (33B compressed secp256k1 or 32B ed25519).</summary>
    ReadOnlyMemory<byte> PublicKey { get; }

    /// <summary>Curve identifier — must match <c>CurveTag</c> the committee
    /// was registered with.</summary>
    byte CurveTag { get; }

    /// <summary>Sign the canonical <c>ExternalCrossChainMessage</c> bytes
    /// (NOT the messageHash — the verifier hashes internally per its
    /// curveHash). Returns a 64-byte signature (r||s for secp256k1,
    /// R||s for ed25519).</summary>
    /// <param name="canonicalMessageBytes">The fixed-prefix + payload bytes
    /// of the message, without the trailing <c>messageHash</c> field — same
    /// bytes <c>ExternalMessageHasher.HashMessage</c> hashes over.</param>
    /// <param name="cancellationToken">Cooperative cancellation; lets the
    /// daemon abort an in-flight HSM call on shutdown.</param>
    ValueTask<ReadOnlyMemory<byte>> SignAsync(
        ReadOnlyMemory<byte> canonicalMessageBytes,
        CancellationToken cancellationToken = default);
}
