namespace Neo.L2.State;

/// <summary>
/// Merkle tree of withdrawal requests collected during a single batch. The root goes into
/// <see cref="L2BatchCommitment.WithdrawalRoot"/>, and individual users prove inclusion to
/// claim their canonical L1 asset on NeoHub.SharedBridge.
/// </summary>
/// <remarks>
/// See doc.md §15.3 (withdrawal flow).
/// </remarks>
public sealed class WithdrawalTree
{
    private readonly List<WithdrawalRequest> _withdrawals = new();
    private readonly List<UInt256> _leaves = new();
    private MerkleTree? _tree;

    /// <summary>Number of withdrawals in the tree.</summary>
    public int Count => _withdrawals.Count;

    /// <summary>Append a withdrawal. Tree is rebuilt lazily on the next <see cref="Root"/> call.</summary>
    public int Add(WithdrawalRequest withdrawal)
    {
        ArgumentNullException.ThrowIfNull(withdrawal);
        var index = _withdrawals.Count;
        _withdrawals.Add(withdrawal);
        _leaves.Add(MessageHasher.HashWithdrawal(withdrawal));
        _tree = null;
        return index;
    }

    /// <summary>Compute (and cache) the Merkle root.</summary>
    public UInt256 Root
    {
        get
        {
            _tree ??= new MerkleTree(_leaves);
            return _tree.Root;
        }
    }

    /// <summary>Generate an inclusion proof for the withdrawal at <paramref name="index"/>.</summary>
    public MerkleProof GetProof(int index)
    {
        _tree ??= new MerkleTree(_leaves);
        return _tree.GetProof(index);
    }

    /// <summary>Look up a withdrawal by index.</summary>
    public WithdrawalRequest GetWithdrawal(int index) => _withdrawals[index];
}
