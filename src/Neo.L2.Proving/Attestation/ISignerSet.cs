using System.Diagnostics;
using Neo.Cryptography.ECC;

namespace Neo.L2.Proving.Attestation;

/// <summary>
/// Source of validator signatures used by <see cref="AttestationProver"/>. Production
/// deployments wire this to an HSM or remote signing service; tests / devnet use
/// <see cref="InMemorySignerSet"/>.
/// </summary>
public interface ISignerSet
{
    /// <summary>The full validator public key set (sorted, canonical order).</summary>
    IReadOnlyList<ECPoint> ValidatorKeys { get; }

    /// <summary>Sign <paramref name="message"/> with as many validators as are available.</summary>
    /// <returns>One <see cref="SignerSignature"/> per signer that produced a signature.</returns>
    ValueTask<IReadOnlyList<SignerSignature>> SignAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory <see cref="ISignerSet"/> for tests and devnet boot. Holds private keys directly.
/// </summary>
/// <remarks>
/// NEVER use this in production; private keys must live behind an HSM / TEE.
/// </remarks>
public sealed class InMemorySignerSet : ISignerSet
{
    private readonly IReadOnlyList<(ECPoint PubKey, byte[] PrivateKey)> _keys;

    /// <inheritdoc />
    public IReadOnlyList<ECPoint> ValidatorKeys { get; }

    /// <summary>
    /// Construct from a list of (public, private) key pairs. The list is canonicalized by
    /// public-key bytes so independent constructions of the same key set produce the same
    /// validator-set hash.
    /// </summary>
    /// <remarks>
    /// <b>NEVER use this in production.</b> Private keys are held in plain memory. A debug
    /// assertion fires in DEBUG builds; Release builds should use HSM/KMS-backed signers.
    /// </remarks>
    public InMemorySignerSet(IEnumerable<(ECPoint PubKey, byte[] PrivateKey)> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        // Emit a diagnostic warning in debug builds. In the test host,
        // Debug.Assert translates to a DebugAssertException which tests
        // can catch — but this fires AFTER the null check above, so
        // tests that pass null still get the expected ArgumentNullException.
        // Production code should never construct this class; use an HSM/KMS
        // adapter implementing ISignerSet instead.
        Debug.WriteLine("WARNING: InMemorySignerSet holds private keys in plain memory — NEVER use in production. Use an HSM/KMS-backed ISignerSet implementation.");
        _keys = keys
            .OrderBy(k => k.PubKey)
            .ToArray();
        ValidatorKeys = _keys.Select(k => k.PubKey).ToArray();
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SignerSignature>> SignAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sigs = new SignerSignature[_keys.Count];
        for (var i = 0; i < _keys.Count; i++)
        {
            // neo-project/neo master Crypto.Sign takes a KeyPair, not raw bytes; wrap on
            // each call so the underlying secret stays in the immutable tuple field.
            var sig = Cryptography.Crypto.Sign(
                message.ToArray(),
                new Neo.Wallets.KeyPair(_keys[i].PrivateKey));
            sigs[i] = new SignerSignature { PublicKey = _keys[i].PubKey, Signature = sig };
        }
        return new ValueTask<IReadOnlyList<SignerSignature>>(sigs);
    }
}
