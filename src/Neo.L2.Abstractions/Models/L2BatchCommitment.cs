namespace Neo.L2;

/// <summary>
/// State commitment that an L2 submits to <c>NeoHub.SettlementManager</c> after sealing a batch.
/// All cryptographic roots in this object are the public anchors that NeoHub records and that
/// the verifier checks against, so every field MUST be reproducible from the L2's deterministic
/// batch executor.
/// </summary>
/// <remarks>
/// See doc.md §3.2 (SettlementManager) and §7.2 (Batcher).
/// </remarks>
public sealed record L2BatchCommitment
{
    /// <summary>Source chain identifier (matches <see cref="L2ChainConfig.ChainId"/>).</summary>
    public required uint ChainId { get; init; }

    /// <summary>Monotonically increasing batch index for the chain.</summary>
    public required ulong BatchNumber { get; init; }

    /// <summary>Inclusive index of the first L2 block packed into this batch.</summary>
    public required ulong FirstBlock { get; init; }

    /// <summary>Inclusive index of the last L2 block packed into this batch.</summary>
    public required ulong LastBlock { get; init; }

    /// <summary>State root before any transaction in this batch is applied.</summary>
    public required UInt256 PreStateRoot { get; init; }

    /// <summary>State root after every transaction in this batch is applied.</summary>
    public required UInt256 PostStateRoot { get; init; }

    /// <summary>Merkle root of the ordered transactions contained in this batch.</summary>
    public required UInt256 TxRoot { get; init; }

    /// <summary>Merkle root of the receipts produced by transactions in this batch.</summary>
    public required UInt256 ReceiptRoot { get; init; }

    /// <summary>Merkle root of withdrawal records produced by this batch.</summary>
    public required UInt256 WithdrawalRoot { get; init; }

    /// <summary>Merkle root of L2 → L1 messages emitted in this batch.</summary>
    public required UInt256 L2ToL1MessageRoot { get; init; }

    /// <summary>Merkle root of L2 → L2 messages emitted in this batch.</summary>
    public required UInt256 L2ToL2MessageRoot { get; init; }

    /// <summary>Commitment to the data published on the chosen DA layer for this batch.</summary>
    public required UInt256 DACommitment { get; init; }

    /// <summary>Hash of the canonical public-input encoding (used for ZK / aggregated verification).</summary>
    public required UInt256 PublicInputHash { get; init; }

    /// <summary>Discriminator for the <see cref="Proof"/> bytes.</summary>
    public required ProofType ProofType { get; init; }

    /// <summary>Proof bytes whose interpretation depends on <see cref="ProofType"/>.</summary>
    public required ReadOnlyMemory<byte> Proof { get; init; }
}
