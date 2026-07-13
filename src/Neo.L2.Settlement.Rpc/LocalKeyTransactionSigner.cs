using Neo.Extensions.VM;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.VM;
using Neo.Wallets;

namespace Neo.L2.Settlement.Rpc;

/// <summary>Single-key transaction signer for local operator wallets and test environments.</summary>
/// <remarks>
/// See doc.md §14.2. Production custody systems should implement
/// <see cref="INeoTransactionSigner"/> and keep private keys inside their HSM or KMS.
/// </remarks>
public sealed class LocalKeyTransactionSigner : INeoTransactionSigner, IDisposable
{
    private readonly KeyPair _key;
    private bool _disposed;

    /// <summary>Constructs an owned signer by copying the supplied private key.</summary>
    public LocalKeyTransactionSigner(KeyPair key, WitnessScope scope = WitnessScope.CalledByEntry)
    {
        ArgumentNullException.ThrowIfNull(key);
        _key = new KeyPair(key.PrivateKey.ToArray());
        Scope = scope;
        var verificationScript = Contract.CreateSignatureRedeemScript(_key.PublicKey);
        Account = verificationScript.ToScriptHash();
    }

    /// <inheritdoc />
    public UInt160 Account { get; }

    /// <inheritdoc />
    public WitnessScope Scope { get; }

    /// <inheritdoc />
    public Witness CreatePlaceholderWitness()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return CreateWitness(new byte[64]);
    }

    /// <inheritdoc />
    public ValueTask<Witness> SignAsync(
        Transaction transaction,
        uint network,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(transaction);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(CreateWitness(transaction.Sign(_key, network)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _key.PrivateKey.AsSpan().Clear();
        _disposed = true;
    }

    private Witness CreateWitness(byte[] signature)
    {
        using var builder = new ScriptBuilder();
        builder.EmitPush(signature);
        return new Witness
        {
            InvocationScript = builder.ToArray(),
            VerificationScript = Contract.CreateSignatureRedeemScript(_key.PublicKey),
        };
    }
}
