using Neo.Cryptography;
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
/// Use <see cref="FromWif"/> / <see cref="FromEnvironmentVariable"/> only for local and
/// testnet operator tooling — never store WIF in plugin configuration.
/// </remarks>
public sealed class LocalKeyTransactionSigner : INeoTransactionSigner, IDisposable
{
    /// <summary>Default environment variable used by neo-stack operator commands.</summary>
    public const string DefaultOperatorWifEnvironmentVariable = "NEO_N4_OPERATOR_WIF";

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

    /// <summary>
    /// Import a compressed Neo WIF private key into an owned signer.
    /// The temporary key material is cleared before return.
    /// </summary>
    public static LocalKeyTransactionSigner FromWif(
        string wif,
        WitnessScope scope = WitnessScope.CalledByEntry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wif);
        var payload = wif.Base58CheckDecode();
        try
        {
            if (payload.Length != 34 || payload[0] != 0x80 || payload[33] != 0x01)
                throw new FormatException("WIF payload is not a compressed Neo private key.");
            var key = new KeyPair(payload[1..33]);
            try
            {
                return new LocalKeyTransactionSigner(key, scope);
            }
            finally
            {
                key.PrivateKey.AsSpan().Clear();
            }
        }
        finally
        {
            payload.AsSpan().Clear();
        }
    }

    /// <summary>
    /// Import WIF from an environment variable (default <see cref="DefaultOperatorWifEnvironmentVariable"/>).
    /// </summary>
    public static LocalKeyTransactionSigner FromEnvironmentVariable(
        string environmentVariableName = DefaultOperatorWifEnvironmentVariable,
        WitnessScope scope = WitnessScope.CalledByEntry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariableName);
        var wif = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(wif))
            throw new InvalidOperationException(
                $"{environmentVariableName} is not set or empty — required for local WIF signing");
        return FromWif(wif, scope);
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
