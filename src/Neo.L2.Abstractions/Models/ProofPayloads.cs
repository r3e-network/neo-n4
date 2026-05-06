namespace Neo.L2;

/// <summary>
/// Public-input bundle that NeoHub verifiers consume. The exact serialization is defined per
/// <see cref="ProofType"/>, but every verifier must commit to all fields below.
/// </summary>
/// <remarks>
/// See doc.md §8.3.
/// </remarks>
public sealed record PublicInputs
{
    /// <summary>L2 chain identifier.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Batch sequence number.</summary>
    public required ulong BatchNumber { get; init; }

    /// <summary>State root before the batch.</summary>
    public required UInt256 PreStateRoot { get; init; }

    /// <summary>State root after the batch.</summary>
    public required UInt256 PostStateRoot { get; init; }

    /// <summary>Merkle root of transactions in the batch.</summary>
    public required UInt256 TxRoot { get; init; }

    /// <summary>Merkle root of receipts produced by the batch.</summary>
    public required UInt256 ReceiptRoot { get; init; }

    /// <summary>Merkle root of withdrawal records.</summary>
    public required UInt256 WithdrawalRoot { get; init; }

    /// <summary>Merkle root of L2 → L1 messages.</summary>
    public required UInt256 L2ToL1MessageRoot { get; init; }

    /// <summary>Merkle root of L2 → L2 messages.</summary>
    public required UInt256 L2ToL2MessageRoot { get; init; }

    /// <summary>Hash of the sorted L1 inbox messages consumed by the batch.</summary>
    public required UInt256 L1MessageHash { get; init; }

    /// <summary>Commitment to data published on the chosen DA layer.</summary>
    public required UInt256 DACommitment { get; init; }

    /// <summary>Hash of <see cref="BatchBlockContext"/>.</summary>
    public required UInt256 BlockContextHash { get; init; }
}

/// <summary>
/// Request handed to a prover. The prover materializes <see cref="L2BatchCommitment.Proof"/>.
/// </summary>
public sealed record ProofRequest
{
    /// <summary>The public-input bundle.</summary>
    public required PublicInputs PublicInputs { get; init; }

    /// <summary>
    /// Witness data the prover needs (execution trace, storage read/write witness, native contract
    /// state witness, ordered transactions, DA data, contract bytecode).
    /// Format depends on the prover backend.
    /// </summary>
    public required ReadOnlyMemory<byte> Witness { get; init; }

    /// <summary>The proof system the caller wants used.</summary>
    public required ProofType Kind { get; init; }

    /// <inheritdoc />
    public bool Equals(ProofRequest? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return PublicInputs.Equals(other.PublicInputs)
            && Kind == other.Kind
            && Witness.Span.SequenceEqual(other.Witness.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PublicInputs);
        hash.Add(Kind);
        hash.AddBytes(Witness.Span);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Output of a successful proof generation.
/// </summary>
/// <remarks>
/// <para>Provers must honor two contracts that callers (notably <c>L2SettlementPlugin</c>)
/// assert at the prove boundary:</para>
/// <list type="bullet">
///   <item><description><see cref="Kind"/> equals the <c>Kind</c> of the originating
///   <see cref="ProofRequest"/>. A prover that returns a different kind is a bug.</description></item>
///   <item><description><see cref="PublicInputHash"/> equals
///   <c>StateRootCalculator.HashPublicInputs(request.PublicInputs)</c>. Proving for one
///   set of inputs while reporting another is an inconsistency that downstream verifiers
///   would catch but at the cost of a wasted submit round-trip.</description></item>
/// </list>
/// </remarks>
public sealed record ProofResult
{
    /// <summary>Bytes that go into <see cref="L2BatchCommitment.Proof"/>.</summary>
    public required ReadOnlyMemory<byte> Proof { get; init; }

    /// <summary>The proof kind. Must match <c>ProofRequest.Kind</c>. Mirrors <see cref="L2BatchCommitment.ProofType"/>.</summary>
    public required ProofType Kind { get; init; }

    /// <summary>Hash of the canonical encoding of <see cref="PublicInputs"/>. Must equal <c>StateRootCalculator.HashPublicInputs(request.PublicInputs)</c>.</summary>
    public required UInt256 PublicInputHash { get; init; }

    /// <inheritdoc />
    public bool Equals(ProofResult? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind
            && PublicInputHash.Equals(other.PublicInputHash)
            && Proof.Span.SequenceEqual(other.Proof.Span);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(PublicInputHash);
        hash.AddBytes(Proof.Span);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Outcome of verifying a proof against its public inputs.
/// </summary>
public readonly record struct ProofVerificationResult(bool Valid, string? FailureReason)
{
    /// <summary>Successful verification.</summary>
    public static ProofVerificationResult Ok { get; } = new(true, null);

    /// <summary>Construct a failure with a human-readable reason.</summary>
    public static ProofVerificationResult Fail(string reason) => new(false, reason);
}
