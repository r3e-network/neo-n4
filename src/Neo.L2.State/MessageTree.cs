namespace Neo.L2.State;

/// <summary>
/// Merkle tree of cross-chain messages collected during a single batch. The root goes into
/// either <see cref="L2BatchCommitment.L2ToL1MessageRoot"/> or
/// <see cref="L2BatchCommitment.L2ToL2MessageRoot"/> depending on the destination class.
/// </summary>
/// <remarks>
/// See doc.md §10 (Neo Connect) and §15 (key flows). Each L2 should keep two instances per
/// batch — one for L2 → L1 and one for L2 → L2.
/// </remarks>
public sealed class MessageTree
{
    private readonly List<CrossChainMessage> _messages = new();
    private readonly List<UInt256> _leaves = new();
    private readonly Dictionary<UInt256, int> _byHash = new();
    private MerkleTree? _tree;

    /// <summary>Number of messages in the tree.</summary>
    public int Count => _messages.Count;

    /// <summary>Append a message and return its index.</summary>
    public int Add(CrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var index = _messages.Count;
        _messages.Add(message);
        _leaves.Add(message.MessageHash);
        _byHash[message.MessageHash] = index;
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

    /// <summary>Look up message index by its <see cref="CrossChainMessage.MessageHash"/>.</summary>
    public bool TryGetIndex(UInt256 messageHash, out int index) =>
        _byHash.TryGetValue(messageHash, out index);

    /// <summary>Generate an inclusion proof for the message at <paramref name="index"/>.</summary>
    public MerkleProof GetProof(int index)
    {
        _tree ??= new MerkleTree(_leaves);
        return _tree.GetProof(index);
    }

    /// <summary>Look up a message by index.</summary>
    public CrossChainMessage GetMessage(int index) => _messages[index];
}
