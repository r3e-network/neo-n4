namespace Neo.L2.Batch;

/// <summary>Proof backend encoded in a <see cref="ProofWitnessArtifactV1"/>.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. The numeric values intentionally match the Stage-2 proof
/// envelope so an artifact can be routed without reinterpretation.
/// </remarks>
public enum WitnessProofSystem : byte
{
    /// <summary>Explicit non-ZK compatibility profile.</summary>
    None = 0,

    /// <summary>Succinct SP1 RISC-V zkVM.</summary>
    Sp1 = 1,

    /// <summary>RISC Zero RISC-V zkVM.</summary>
    RiscZero = 2,

    /// <summary>Halo2 proof system.</summary>
    Halo2 = 3,

    /// <summary>Neo Axiom proof system.</summary>
    Axiom = 4,
}

/// <summary>
/// Canonical execution payload published to the configured DA layer before a proof witness
/// artifact is committed.
/// </summary>
/// <remarks>
/// See doc.md §7.2, §7.4, and §8. The payload deliberately excludes the DA commitment so its
/// encoded bytes can be content-addressed without a self-reference.
/// </remarks>
public sealed record ExecutionPayloadV1
{
    /// <summary>Wire-format version.</summary>
    public const ushort Version = 1;

    /// <summary>L2 chain identifier.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Batch sequence number.</summary>
    public required ulong BatchNumber { get; init; }

    /// <summary>Inclusive first L2 block in the batch.</summary>
    public required ulong FirstBlock { get; init; }

    /// <summary>Inclusive last L2 block in the batch.</summary>
    public required ulong LastBlock { get; init; }

    /// <summary>State root before execution starts.</summary>
    public required UInt256 PreStateRoot { get; init; }

    /// <summary>Deterministic block context consumed by the executor.</summary>
    public required BatchBlockContext BlockContext { get; init; }

    /// <summary>Ordered L1 inbox messages consumed by the batch.</summary>
    public required IReadOnlyList<CrossChainMessage> L1Messages { get; init; }

    /// <summary>
    /// Forced-inclusion nonces plus finalized transaction-root inclusion proofs.
    /// </summary>
    public IReadOnlyList<ForcedInclusionConsumptionProof> ForcedInclusions { get; init; }
        = Array.Empty<ForcedInclusionConsumptionProof>();

    /// <summary>Ordered canonical transaction bytes.</summary>
    public required IReadOnlyList<ReadOnlyMemory<byte>> Transactions { get; init; }

    /// <inheritdoc />
    public bool Equals(ExecutionPayloadV1? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (ChainId != other.ChainId
            || BatchNumber != other.BatchNumber
            || FirstBlock != other.FirstBlock
            || LastBlock != other.LastBlock
            || !PreStateRoot.Equals(other.PreStateRoot)
            || !BlockContext.Equals(other.BlockContext)
            || L1Messages.Count != other.L1Messages.Count
            || ForcedInclusions.Count != other.ForcedInclusions.Count
            || Transactions.Count != other.Transactions.Count)
            return false;

        for (var i = 0; i < L1Messages.Count; i++)
            if (!L1Messages[i].Equals(other.L1Messages[i])) return false;
        for (var i = 0; i < ForcedInclusions.Count; i++)
            if (!ForcedInclusions[i].Equals(other.ForcedInclusions[i])) return false;
        for (var i = 0; i < Transactions.Count; i++)
            if (!Transactions[i].Span.SequenceEqual(other.Transactions[i].Span)) return false;
        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ChainId);
        hash.Add(BatchNumber);
        hash.Add(FirstBlock);
        hash.Add(LastBlock);
        hash.Add(PreStateRoot);
        hash.Add(BlockContext);
        foreach (var message in L1Messages) hash.Add(message);
        foreach (var forcedInclusion in ForcedInclusions) hash.Add(forcedInclusion);
        foreach (var transaction in Transactions) hash.AddBytes(transaction.Span);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Durable, content-addressed input to a proof job, including the exact DA payload, state
/// witness, execution result, effects, DA receipt, and canonical public inputs.
/// </summary>
/// <remarks>
/// See doc.md §7.5 and §8. The content hash is Hash256 over a versioned domain separator and
/// every encoded byte preceding <see cref="ContentHash"/>.
/// </remarks>
public sealed record ProofWitnessArtifactV1
{
    /// <summary>Wire-format version.</summary>
    public const ushort Version = 1;

    /// <summary>Settlement proof stage that consumes this artifact.</summary>
    public required ProofType ProofType { get; init; }

    /// <summary>Proof backend that must consume this artifact.</summary>
    public required WitnessProofSystem ProofSystem { get; init; }

    /// <summary>Identifier of the verification key expected by the terminal verifier.</summary>
    public required UInt256 VerificationKeyId { get; init; }

    /// <summary>Exact VM/state-transition semantic executed and proved.</summary>
    public required UInt256 ExecutionSemanticId { get; init; }

    /// <summary>True only when the executor authenticated the state witness.</summary>
    public required bool ExecutionWitnessAuthenticated { get; init; }

    /// <summary>L2 chain identifier.</summary>
    public required uint ChainId { get; init; }

    /// <summary>Batch sequence number.</summary>
    public required ulong BatchNumber { get; init; }

    /// <summary>Inclusive first L2 block in the batch.</summary>
    public required ulong FirstBlock { get; init; }

    /// <summary>Inclusive last L2 block in the batch.</summary>
    public required ulong LastBlock { get; init; }

    /// <summary>Payload whose exact encoded bytes are published to DA.</summary>
    public required ExecutionPayloadV1 ExecutionPayload { get; init; }

    /// <summary>Versioned state read/write and code witness consumed by the execution kernel.</summary>
    public required ReadOnlyMemory<byte> StateWitness { get; init; }

    /// <summary>Six canonical roots and total gas produced by deterministic execution.</summary>
    public required BatchExecutionResult ExecutionResult { get; init; }

    /// <summary>Canonical receipts, withdrawals, and outbound-message effects.</summary>
    public required ReadOnlyMemory<byte> Effects { get; init; }

    /// <summary>Receipt returned by the DA writer for the encoded execution payload.</summary>
    public required DAReceipt DAReceipt { get; init; }

    /// <summary>Canonical 332-byte settlement public-input bundle.</summary>
    public required PublicInputs PublicInputs { get; init; }

    /// <summary>Hash256 of the versioned artifact domain and all preceding bytes.</summary>
    public required UInt256 ContentHash { get; init; }

    /// <summary>Create an artifact and calculate its canonical content hash.</summary>
    public static ProofWitnessArtifactV1 Create(
        ProofType proofType,
        WitnessProofSystem proofSystem,
        UInt256 verificationKeyId,
        UInt256 executionSemanticId,
        bool executionWitnessAuthenticated,
        uint chainId,
        ulong batchNumber,
        ulong firstBlock,
        ulong lastBlock,
        ExecutionPayloadV1 executionPayload,
        ReadOnlyMemory<byte> stateWitness,
        BatchExecutionResult executionResult,
        ReadOnlyMemory<byte> effects,
        DAReceipt daReceipt,
        PublicInputs publicInputs)
    {
        var artifact = new ProofWitnessArtifactV1
        {
            ProofType = proofType,
            ProofSystem = proofSystem,
            VerificationKeyId = verificationKeyId,
            ExecutionSemanticId = executionSemanticId,
            ExecutionWitnessAuthenticated = executionWitnessAuthenticated,
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = firstBlock,
            LastBlock = lastBlock,
            ExecutionPayload = executionPayload,
            StateWitness = stateWitness,
            ExecutionResult = executionResult,
            Effects = effects,
            DAReceipt = daReceipt,
            PublicInputs = publicInputs,
            ContentHash = UInt256.Zero,
        };
        return artifact with
        {
            ContentHash = ProofWitnessArtifactSerializer.ComputeContentHash(artifact),
        };
    }

    /// <inheritdoc />
    public bool Equals(ProofWitnessArtifactV1? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ProofSystem == other.ProofSystem
            && ProofType == other.ProofType
            && VerificationKeyId.Equals(other.VerificationKeyId)
            && ExecutionSemanticId.Equals(other.ExecutionSemanticId)
            && ExecutionWitnessAuthenticated == other.ExecutionWitnessAuthenticated
            && ChainId == other.ChainId
            && BatchNumber == other.BatchNumber
            && FirstBlock == other.FirstBlock
            && LastBlock == other.LastBlock
            && ExecutionPayload.Equals(other.ExecutionPayload)
            && StateWitness.Span.SequenceEqual(other.StateWitness.Span)
            && ExecutionResult.Equals(other.ExecutionResult)
            && Effects.Span.SequenceEqual(other.Effects.Span)
            && DAReceipt.Equals(other.DAReceipt)
            && PublicInputs.Equals(other.PublicInputs)
            && ContentHash.Equals(other.ContentHash);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProofType);
        hash.Add(ProofSystem);
        hash.Add(VerificationKeyId);
        hash.Add(ExecutionSemanticId);
        hash.Add(ExecutionWitnessAuthenticated);
        hash.Add(ChainId);
        hash.Add(BatchNumber);
        hash.Add(FirstBlock);
        hash.Add(LastBlock);
        hash.Add(ExecutionPayload);
        hash.AddBytes(StateWitness.Span);
        hash.Add(ExecutionResult);
        hash.AddBytes(Effects.Span);
        hash.Add(DAReceipt);
        hash.Add(PublicInputs);
        hash.Add(ContentHash);
        return hash.ToHashCode();
    }
}
