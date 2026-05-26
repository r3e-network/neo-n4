namespace Neo.L2.Batch;

/// <summary>
/// In-progress batch state: blocks, transactions, withdrawals, and messages collected while
/// the L2 sequencer accumulates work. <see cref="BatchBuilder"/> mutates this; once sealed it
/// is converted to an immutable <see cref="L2BatchCommitment"/>.
/// </summary>
/// <remarks>
/// See doc.md §7.2 (Batcher inputs/outputs).
/// </remarks>
public sealed class L2Batch
{
    /// <summary>L2 chain identifier.</summary>
    public uint ChainId { get; }

    /// <summary>Sequence number of this batch on its chain.</summary>
    public ulong BatchNumber { get; }

    /// <summary>State root the executor must start from.</summary>
    public UInt256 PreStateRoot { get; }

    /// <summary>Inclusive index of the first L2 block.</summary>
    public ulong FirstBlock { get; private set; }

    /// <summary>Inclusive index of the last L2 block; equals <see cref="FirstBlock"/> when only one block has been added.</summary>
    public ulong LastBlock { get; private set; }

    /// <summary>Block context committed at sealing time.</summary>
    public BatchBlockContext? BlockContext { get; internal set; }

    /// <summary>Ordered transactions that the executor will replay deterministically.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Transactions => _transactions;

    /// <summary>L1 inbox messages that this batch consumes.</summary>
    public IReadOnlyList<CrossChainMessage> L1MessagesConsumed => _l1Messages;

    /// <summary>Withdrawals emitted in this batch (inserted into the withdrawal Merkle tree).</summary>
    public IReadOnlyList<WithdrawalRequest> Withdrawals => _withdrawals;

    /// <summary>L2 → L1 messages emitted in this batch.</summary>
    public IReadOnlyList<CrossChainMessage> L2ToL1Messages => _l2ToL1;

    /// <summary>L2 → L2 messages emitted in this batch.</summary>
    public IReadOnlyList<CrossChainMessage> L2ToL2Messages => _l2ToL2;

    private readonly List<ReadOnlyMemory<byte>> _transactions = new();
    private readonly List<CrossChainMessage> _l1Messages = new();
    private readonly List<WithdrawalRequest> _withdrawals = new();
    private readonly List<CrossChainMessage> _l2ToL1 = new();
    private readonly List<CrossChainMessage> _l2ToL2 = new();
    private bool _sealed;

    /// <summary>
    /// Construct a new in-progress batch at <paramref name="firstBlock"/> with starting
    /// <paramref name="preStateRoot"/>.
    /// </summary>
    public L2Batch(uint chainId, ulong batchNumber, ulong firstBlock, UInt256 preStateRoot)
    {
        ArgumentNullException.ThrowIfNull(preStateRoot);
        ChainId = chainId;
        BatchNumber = batchNumber;
        FirstBlock = firstBlock;
        LastBlock = firstBlock;
        PreStateRoot = preStateRoot;
    }

    /// <summary>True once <see cref="Seal"/> has been invoked. After sealing, no more content can be added.</summary>
    public bool IsSealed => _sealed;

    /// <summary>Number of transactions accumulated so far.</summary>
    public int TransactionCount => _transactions.Count;

    internal void AddBlock(ulong blockIndex)
    {
        EnsureNotSealed();
        if (blockIndex < FirstBlock)
            throw new ArgumentOutOfRangeException(nameof(blockIndex), $"blockIndex {blockIndex} precedes FirstBlock {FirstBlock}");
        if (blockIndex < LastBlock)
            throw new ArgumentOutOfRangeException(nameof(blockIndex), $"blockIndex {blockIndex} is older than LastBlock {LastBlock}; blocks must be appended in order");
        if (blockIndex != LastBlock && blockIndex != LastBlock + 1)
            throw new ArgumentOutOfRangeException(nameof(blockIndex),
                $"blockIndex {blockIndex} is non-contiguous after {LastBlock}; blocks must be sequential with no gaps");
        LastBlock = blockIndex;
    }

    internal void AddTransaction(ReadOnlyMemory<byte> serializedTx)
    {
        EnsureNotSealed();
        _transactions.Add(serializedTx);
    }

    internal void AddL1Message(CrossChainMessage message)
    {
        EnsureNotSealed();
        _l1Messages.Add(message);
    }

    internal void AddWithdrawal(WithdrawalRequest withdrawal)
    {
        EnsureNotSealed();
        _withdrawals.Add(withdrawal);
    }

    internal void AddL2ToL1Message(CrossChainMessage message)
    {
        EnsureNotSealed();
        _l2ToL1.Add(message);
    }

    internal void AddL2ToL2Message(CrossChainMessage message)
    {
        EnsureNotSealed();
        _l2ToL2.Add(message);
    }

    internal void Seal() => _sealed = true;

    private void EnsureNotSealed()
    {
        if (_sealed)
            throw new InvalidOperationException("L2Batch is sealed; no more content can be added");
    }
}
