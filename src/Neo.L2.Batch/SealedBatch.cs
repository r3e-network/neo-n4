using Neo.Cryptography;
using Neo.L2.State;

namespace Neo.L2.Batch;

/// <summary>
/// Immutable executable batch produced before execution, DA publication, proving, or settlement.
/// </summary>
/// <remarks>
/// See doc.md §7.2, §8.1, and §15.1. All variable byte fields are deep-copied so the
/// execution payload cannot change after sealing.
/// </remarks>
public sealed class SealedBatch
{
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> _transactions;
    private readonly IReadOnlyList<CrossChainMessage> _l1Messages;
    private readonly IReadOnlyList<ForcedInclusionConsumptionProof> _forcedInclusions;

    /// <summary>L2 chain identifier.</summary>
    public uint ChainId { get; }

    /// <summary>Monotonic batch number.</summary>
    public ulong BatchNumber { get; }

    /// <summary>Inclusive first L2 block.</summary>
    public ulong FirstBlock { get; }

    /// <summary>Inclusive last L2 block.</summary>
    public ulong LastBlock { get; }

    /// <summary>Canonical state root before execution.</summary>
    public UInt256 PreStateRoot { get; }

    /// <summary>Ordered canonical transaction bytes.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Transactions => _transactions;

    /// <summary>Ordered L1 inbox messages consumed before transactions.</summary>
    public IReadOnlyList<CrossChainMessage> L1Messages => _l1Messages;

    /// <summary>Forced-inclusion transaction positions and Merkle proof material.</summary>
    public IReadOnlyList<ForcedInclusionConsumptionProof> ForcedInclusions => _forcedInclusions;

    /// <summary>Forced-inclusion nonces durably tracked from sealing through L1 consumption.</summary>
    public IReadOnlyList<ulong> ForcedInclusionNonces =>
        _forcedInclusions.Select(static proof => proof.Nonce).ToArray();

    /// <summary>Deterministic block context consumed by execution.</summary>
    public BatchBlockContext BlockContext { get; }

    /// <summary>Create an immutable sealed batch.</summary>
    public SealedBatch(
        uint chainId,
        ulong batchNumber,
        ulong firstBlock,
        ulong lastBlock,
        UInt256 preStateRoot,
        IReadOnlyList<ReadOnlyMemory<byte>> transactions,
        IReadOnlyList<CrossChainMessage> l1Messages,
        BatchBlockContext blockContext,
        IReadOnlyList<ForcedInclusionConsumptionProof>? forcedInclusions = null)
    {
        ArgumentNullException.ThrowIfNull(preStateRoot);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(l1Messages);
        ArgumentNullException.ThrowIfNull(blockContext);
        ArgumentNullException.ThrowIfNull(blockContext.SequencerCommitteeHash);
        if (lastBlock < firstBlock)
            throw new ArgumentOutOfRangeException(
                nameof(lastBlock), "lastBlock must not precede firstBlock");

        ChainId = chainId;
        BatchNumber = batchNumber;
        FirstBlock = firstBlock;
        LastBlock = lastBlock;
        PreStateRoot = new UInt256(preStateRoot.GetSpan());
        BlockContext = blockContext with
        {
            SequencerCommitteeHash = new UInt256(
                blockContext.SequencerCommitteeHash.GetSpan()),
        };

        var transactionCopies = new ReadOnlyMemory<byte>[transactions.Count];
        for (var index = 0; index < transactions.Count; index++)
            transactionCopies[index] = transactions[index].ToArray();
        _transactions = Array.AsReadOnly(transactionCopies);

        var forcedSource = forcedInclusions
            ?? Array.Empty<ForcedInclusionConsumptionProof>();
        var forcedCopies = new ForcedInclusionConsumptionProof[forcedSource.Count];
        if (forcedCopies.Length > transactionCopies.Length)
            throw new ArgumentException(
                "forced-inclusion nonce count exceeds transaction count",
                nameof(forcedInclusions));
        var transactionHashes = transactionCopies
            .Select(static transaction => new UInt256(Crypto.Hash256(transaction.Span)))
            .ToArray();
        var transactionTree = new Neo.L2.State.MerkleTree(transactionHashes);
        var nonces = new HashSet<ulong>();
        for (var index = 0; index < forcedSource.Count; index++)
        {
            var proof = forcedSource[index]
                ?? throw new ArgumentException(
                    $"forcedInclusions[{index}] is null", nameof(forcedInclusions));
            ArgumentNullException.ThrowIfNull(proof.TxHash);
            ArgumentNullException.ThrowIfNull(proof.Siblings);
            if (proof.LeafIndex != (uint)index)
                throw new ArgumentException(
                    "forced-inclusion transactions must occupy the leading transaction positions",
                    nameof(forcedInclusions));
            if (!nonces.Add(proof.Nonce))
                throw new ArgumentException(
                    "forced-inclusion nonces must be unique within a batch",
                    nameof(forcedInclusions));
            if (!proof.TxHash.Equals(transactionHashes[index]))
                throw new ArgumentException(
                    $"forced-inclusion tx hash {index} does not match encoded transaction",
                    nameof(forcedInclusions));
            var canonicalProof = transactionTree.GetProof(index);
            if (!canonicalProof.Siblings.SequenceEqual(proof.Siblings))
                throw new ArgumentException(
                    $"forced-inclusion proof {index} is not canonical for the batch transaction root",
                    nameof(forcedInclusions));
            forcedCopies[index] = proof with
            {
                TxHash = new UInt256(proof.TxHash.GetSpan()),
                Siblings = Array.AsReadOnly(proof.Siblings
                    .Select(static sibling => new UInt256(sibling.GetSpan()))
                    .ToArray()),
            };
        }
        if (forcedCopies.Length != nonces.Count)
            throw new ArgumentException(
                "forced-inclusion nonces must be unique within a batch",
                nameof(forcedInclusions));
        _forcedInclusions = Array.AsReadOnly(forcedCopies);

        var messageCopies = new CrossChainMessage[l1Messages.Count];
        for (var index = 0; index < l1Messages.Count; index++)
        {
            var message = l1Messages[index]
                ?? throw new ArgumentException(
                    $"l1Messages[{index}] is null", nameof(l1Messages));
            ArgumentNullException.ThrowIfNull(message.Sender);
            ArgumentNullException.ThrowIfNull(message.Receiver);
            ArgumentNullException.ThrowIfNull(message.MessageHash);
            messageCopies[index] = message with
            {
                Sender = new UInt160(message.Sender.GetSpan()),
                Receiver = new UInt160(message.Receiver.GetSpan()),
                Payload = message.Payload.ToArray(),
                MessageHash = new UInt256(message.MessageHash.GetSpan()),
            };
        }
        _l1Messages = Array.AsReadOnly(messageCopies);
    }

    /// <summary>Build the exact executor request represented by this sealed batch.</summary>
    public BatchExecutionRequest ToExecutionRequest() => new()
    {
        ChainId = ChainId,
        BatchNumber = BatchNumber,
        PreStateRoot = PreStateRoot,
        Transactions = Transactions,
        L1MessagesConsumed = L1Messages,
        BlockContext = BlockContext,
    };

    /// <summary>Build the exact versioned payload published to DA.</summary>
    public ExecutionPayloadV1 ToExecutionPayload() => new()
    {
        ChainId = ChainId,
        BatchNumber = BatchNumber,
        FirstBlock = FirstBlock,
        LastBlock = LastBlock,
        PreStateRoot = PreStateRoot,
        BlockContext = BlockContext,
        L1Messages = L1Messages,
        ForcedInclusions = ForcedInclusions,
        Transactions = Transactions,
    };
}

/// <summary>
/// Durable hand-off used by the batch plugin to persist a sealed batch before advancing its
/// pre-state root.
/// </summary>
/// <remarks>See doc.md §7.2, §7.5, and §15.1.</remarks>
public interface ISealedBatchSink
{
    /// <summary>
    /// Execute, publish, and atomically persist the batch, returning the validated post-state root.
    /// </summary>
    ValueTask<UInt256> PersistAsync(
        SealedBatch batch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Return forced-inclusion nonces already tracked by durable artifacts.
    /// </summary>
    ValueTask<IReadOnlyCollection<ulong>> GetTrackedForcedInclusionNoncesAsync(
        uint chainId,
        CancellationToken cancellationToken = default);
}
