using System.Collections.Concurrent;
using Neo.L2;

namespace Neo.Plugins.L2Rpc;

/// <summary>
/// In-memory <see cref="IL2RpcStore"/> for tests + devnet RPC. Plug your real settlement
/// store / batch ledger / L1 cache in for production.
/// </summary>
public sealed class InMemoryL2RpcStore : IL2RpcStore
{
    private readonly ConcurrentDictionary<ulong, L2BatchCommitment> _batches = new();
    private readonly ConcurrentDictionary<ulong, BatchStatus> _statuses = new();
    private readonly ConcurrentDictionary<ulong, UInt256> _stateRoots = new();
    private readonly ConcurrentDictionary<UInt256, byte[]> _withdrawalProofs = new();
    private readonly ConcurrentDictionary<UInt256, byte[]> _messageProofs = new();
    private readonly ConcurrentDictionary<(uint, ulong), DepositStatus> _deposits = new();
    private readonly ConcurrentDictionary<UInt160, UInt160> _l1ByL2 = new();
    private readonly ConcurrentDictionary<UInt160, UInt160> _l2ByL1 = new();
    private readonly Lock _latestGate = new();
    private UInt256 _latestStateRoot = UInt256.Zero;
    private long _latestFinalizedBatch = -1;

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <inheritdoc />
    public SecurityLevel SecurityLevel { get; }

    /// <summary>Construct with the chain id and its published security label.</summary>
    public InMemoryL2RpcStore(uint chainId, SecurityLevel level)
    {
        ChainId = chainId;
        SecurityLevel = level;
    }

    /// <summary>Record a sealed batch + its initial status (typically Pending).</summary>
    public void AddBatch(L2BatchCommitment commitment, BatchStatus status)
    {
        _batches[commitment.BatchNumber] = commitment;
        _statuses[commitment.BatchNumber] = status;
        _stateRoots[commitment.BatchNumber] = commitment.PostStateRoot;
    }

    /// <summary>Mark a batch finalized; bump latest state root only if this is the newest one finalized.</summary>
    /// <remarks>
    /// Without the monotonicity check, finalizing batch 5 then batch 3 would revert
    /// <c>_latestStateRoot</c> to batch 3's post-state — RPC <c>getl2stateroot</c> (no batch arg)
    /// would then return a stale older root, which a downstream relayer treats as a regression.
    /// </remarks>
    public void Finalize(ulong batchNumber)
    {
        if (!_batches.TryGetValue(batchNumber, out var b)) return;
        _statuses[batchNumber] = BatchStatus.Finalized;
        lock (_latestGate)
        {
            if ((long)batchNumber > _latestFinalizedBatch)
            {
                _latestFinalizedBatch = (long)batchNumber;
                _latestStateRoot = b.PostStateRoot;
            }
        }
    }

    /// <summary>Register an asset mapping (bidirectional).</summary>
    /// <remarks>
    /// Same orphan-cleanup pattern as <c>AssetRegistry.Register</c> (iter 100): when
    /// re-pointing an L1 asset to a new L2 token (or vice versa), remove the stale entry
    /// from the opposite index. Without this, a re-registration leaves a silent
    /// inconsistency where one direction returns the new mapping and the other still
    /// returns the orphaned old one.
    /// </remarks>
    public void RegisterAsset(UInt160 l1Asset, UInt160 l2Asset)
    {
        if (_l2ByL1.TryGetValue(l1Asset, out var oldL2) && !oldL2.Equals(l2Asset))
            _l1ByL2.TryRemove(oldL2, out _);
        if (_l1ByL2.TryGetValue(l2Asset, out var oldL1) && !oldL1.Equals(l1Asset))
            _l2ByL1.TryRemove(oldL1, out _);
        _l2ByL1[l1Asset] = l2Asset;
        _l1ByL2[l2Asset] = l1Asset;
    }

    /// <summary>Record an L1 deposit's status.</summary>
    public void RecordDeposit(DepositStatus status)
    {
        _deposits[(status.SourceChainId, status.Nonce)] = status;
    }

    /// <summary>Record an inclusion proof for a withdrawal leaf.</summary>
    /// <remarks>
    /// Defensive copy: the store retains the bytes for later RPC reads. Without the clone,
    /// a caller who mutates their copy after passing it in (or who reuses a single
    /// scratch buffer across many calls) would silently corrupt the stored proof.
    /// </remarks>
    public void RecordWithdrawalProof(UInt256 leafHash, byte[] proofBytes)
    {
        ArgumentNullException.ThrowIfNull(proofBytes);
        _withdrawalProofs[leafHash] = (byte[])proofBytes.Clone();
    }

    /// <summary>Record an inclusion proof for a message hash.</summary>
    /// <remarks>
    /// See <see cref="RecordWithdrawalProof"/> for the defensive-copy rationale.
    /// </remarks>
    public void RecordMessageProof(UInt256 messageHash, byte[] proofBytes)
    {
        ArgumentNullException.ThrowIfNull(proofBytes);
        _messageProofs[messageHash] = (byte[])proofBytes.Clone();
    }

    /// <inheritdoc />
    public L2BatchCommitment? GetBatch(ulong batchNumber) =>
        _batches.TryGetValue(batchNumber, out var b) ? b : null;

    /// <inheritdoc />
    public BatchStatus GetBatchStatus(ulong batchNumber) =>
        _statuses.TryGetValue(batchNumber, out var s) ? s : BatchStatus.Unknown;

    /// <inheritdoc />
    public UInt256 GetLatestStateRoot()
    {
        // Held under the same gate as Finalize so a concurrent caller doesn't observe a torn
        // UInt256 (32 bytes — not naturally atomic on most ABIs).
        lock (_latestGate) return _latestStateRoot;
    }

    /// <inheritdoc />
    public UInt256 GetStateRootAtBatch(ulong batchNumber) =>
        _stateRoots.TryGetValue(batchNumber, out var r) ? r : UInt256.Zero;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? GetWithdrawalProof(UInt256 leafHash) =>
        _withdrawalProofs.TryGetValue(leafHash, out var p) ? p : null;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? GetMessageProof(UInt256 messageHash) =>
        _messageProofs.TryGetValue(messageHash, out var p) ? p : null;

    /// <inheritdoc />
    public DepositStatus? GetL1DepositStatus(uint sourceChainId, ulong nonce) =>
        _deposits.TryGetValue((sourceChainId, nonce), out var s) ? s : null;

    /// <inheritdoc />
    public UInt160? GetCanonicalAsset(UInt160 l2Asset) =>
        _l1ByL2.TryGetValue(l2Asset, out var l1) ? l1 : null;

    /// <inheritdoc />
    public UInt160? GetBridgedAsset(UInt160 l1Asset) =>
        _l2ByL1.TryGetValue(l1Asset, out var l2) ? l2 : null;
}
