using Neo.Network.P2P.Payloads;

namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// Signs a Neo transaction witness without coupling the settlement client to a wallet,
/// HSM, KMS, or remote custody implementation.
/// </summary>
/// <remarks>See doc.md §14.2 (operator tooling and wallet integration).</remarks>
public interface INeoTransactionSigner
{
    /// <summary>Account placed in the transaction signer list.</summary>
    UInt160 Account { get; }

    /// <summary>Witness scope used for the transaction signer.</summary>
    WitnessScope Scope { get; }

    /// <summary>
    /// Creates a structurally accurate unsigned witness for network-fee estimation.
    /// </summary>
    Witness CreatePlaceholderWitness();

    /// <summary>Creates the final witness for an immutable transaction.</summary>
    ValueTask<Witness> SignAsync(
        Transaction transaction,
        uint network,
        CancellationToken cancellationToken = default);
}
