namespace Neo.L2;

/// <summary>
/// L1 write/read-back boundary that completes forced-inclusion consumption after settlement
/// finality.
/// </summary>
/// <remarks>
/// See doc.md §15.1 and §15.4. Implementations must read
/// <c>SettlementManager.getFinalizedTxRoot</c>, idempotently submit permissionless
/// <c>ForcedInclusion.consume</c> with the persisted inclusion proof, and return only after an L1
/// read confirms consumption. Merely hiding a nonce in local memory does not satisfy this contract.
/// </remarks>
public interface IForcedInclusionFinalizationClient
{
    /// <summary>
    /// Submit and confirm consumption of all forced-inclusion nonces committed by a finalized
    /// batch. Partial prior success must be safe to retry.
    /// </summary>
    ValueTask ConsumeAndConfirmAsync(
        uint chainId,
        ulong batchNumber,
        IReadOnlyList<ForcedInclusionConsumptionProof> proofs,
        CancellationToken cancellationToken = default);
}

/// <summary>Canonical proof material for permissionless forced-inclusion consumption.</summary>
/// <remarks>See doc.md §15.4.</remarks>
public sealed record ForcedInclusionConsumptionProof
{
    /// <summary>Per-chain forced-inclusion nonce.</summary>
    public required ulong Nonce { get; init; }

    /// <summary>Zero-based transaction leaf index in the finalized batch transaction root.</summary>
    public required uint LeafIndex { get; init; }

    /// <summary>Hash256 of the exact encoded forced transaction.</summary>
    public required UInt256 TxHash { get; init; }

    /// <summary>Merkle siblings from leaf to root.</summary>
    public required IReadOnlyList<UInt256> Siblings { get; init; }

    /// <inheritdoc />
    public bool Equals(ForcedInclusionConsumptionProof? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Nonce == other.Nonce
            && LeafIndex == other.LeafIndex
            && TxHash.Equals(other.TxHash)
            && Siblings.SequenceEqual(other.Siblings);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Nonce);
        hash.Add(LeafIndex);
        hash.Add(TxHash);
        foreach (var sibling in Siblings) hash.Add(sibling);
        return hash.ToHashCode();
    }
}
