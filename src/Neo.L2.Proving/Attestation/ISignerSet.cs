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
    public InMemorySignerSet(IEnumerable<(ECPoint PubKey, byte[] PrivateKey)> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
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
            var sig = Cryptography.Crypto.Sign(message.ToArray(), _keys[i].PrivateKey);
            sigs[i] = new SignerSignature { PublicKey = _keys[i].PubKey, Signature = sig };
        }
        return new ValueTask<IReadOnlyList<SignerSignature>>(sigs);
    }
}
