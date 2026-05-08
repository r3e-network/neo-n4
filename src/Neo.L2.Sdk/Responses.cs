using Neo;
using Neo.L2;
using Neo.L2.Batch;

namespace Neo.L2.Sdk;

/// <summary>
/// Typed view of the <c>getl2batch</c> response — every field is decoded; the
/// <see cref="EncodedWireFormat"/> field is the canonical bytes emitted by
/// <see cref="BatchSerializer"/>, kept for re-publish flows where bit-for-bit
/// fidelity matters.
/// </summary>
public sealed record L2BatchView(
    uint ChainId,
    ulong BatchNumber,
    ulong FirstBlock,
    ulong LastBlock,
    UInt256 PreStateRoot,
    UInt256 PostStateRoot,
    UInt256 TxRoot,
    UInt256 ReceiptRoot,
    UInt256 WithdrawalRoot,
    UInt256 L2ToL1MessageRoot,
    UInt256 L2ToL2MessageRoot,
    UInt256 DACommitment,
    UInt256 PublicInputHash,
    ProofType ProofType,
    byte[] Proof,
    byte[] EncodedWireFormat);

/// <summary>Typed view of the <c>getl2batchstatus</c> response.</summary>
public sealed record BatchStatusResponse(
    uint ChainId,
    ulong BatchNumber,
    BatchStatus Status,
    string StatusName);

/// <summary>Typed view of the <c>getl1depositstatus</c> response.</summary>
public sealed record DepositStatusResponse(
    uint SourceChainId,
    ulong Nonce,
    bool ConsumedOnL2,
    ulong? IncludedInBatch);

/// <summary>Typed view of the <c>getsecuritylevel</c> response.</summary>
public sealed record SecurityLevelResponse(
    uint ChainId,
    SecurityLevel Level);

/// <summary>
/// Typed view of the <c>getsecuritylabel</c> response — full doc.md §16.2 label.
/// </summary>
public sealed record SecurityLabelResponse(
    uint ChainId,
    SecurityLevel SecurityLevel,
    DAMode DAMode,
    bool GatewayEnabled,
    SequencerModel Sequencer,
    ExitModel Exit);
