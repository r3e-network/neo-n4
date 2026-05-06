using System.Collections.Concurrent;
using Neo.L2;
using Neo.L2.Persistence;

namespace Neo.Plugins.L2Rpc;

/// <summary>
/// <see cref="IL2RpcStore"/> with in-memory dictionaries for batch / asset / deposit
/// lookups and a pluggable <see cref="IL2KeyValueStore"/> for the durability-critical
/// withdrawal-proof and message-proof maps. Production wires
/// <see cref="RocksDbKeyValueStore"/> so finalized inclusion proofs survive node
/// restarts and remain queryable indefinitely.
/// </summary>
public sealed class InMemoryL2RpcStore : IL2RpcStore, IDisposable
{
    private readonly ConcurrentDictionary<ulong, L2BatchCommitment> _batches = new();
    private readonly ConcurrentDictionary<ulong, BatchStatus> _statuses = new();
    private readonly ConcurrentDictionary<ulong, UInt256> _stateRoots = new();
    // Withdrawal + message proofs — durability-critical. Stored under prefixed keys in
    // the same KV backing so production deployments can persist both with one RocksDB
    // database. Prefix bytes: 0x01 = withdrawal, 0x02 = message.
    private readonly IL2KeyValueStore _proofs;
    private readonly bool _ownsProofs;
    private readonly ConcurrentDictionary<(uint, ulong), DepositStatus> _deposits = new();
    private readonly ConcurrentDictionary<UInt160, UInt160> _l1ByL2 = new();
    private readonly ConcurrentDictionary<UInt160, UInt160> _l2ByL1 = new();
    private readonly Lock _latestGate = new();
    private UInt256 _latestStateRoot = UInt256.Zero;
    private long _latestFinalizedBatch = -1;
    private bool _disposed;

    private const byte WithdrawalProofPrefix = 0x01;
    private const byte MessageProofPrefix = 0x02;

    /// <inheritdoc />
    public uint ChainId { get; }

    /// <inheritdoc />
    public SecurityLevel SecurityLevel { get; }

    /// <inheritdoc />
    public DAMode DAMode { get; init; } = DAMode.External;

    /// <inheritdoc />
    public bool GatewayEnabled { get; init; }

    /// <inheritdoc />
    public SequencerModel Sequencer { get; init; } = SequencerModel.DbftCommittee;

    /// <inheritdoc />
    public ExitModel Exit { get; init; } = ExitModel.Permissionless;

    /// <summary>Construct with the chain id, security label, and a default in-memory proof store.</summary>
    public InMemoryL2RpcStore(uint chainId, SecurityLevel level)
        : this(chainId, level, new InMemoryKeyValueStore(), ownsProofs: true) { }

    /// <summary>
    /// Construct with the chain id, security label, and a caller-supplied proof store —
    /// production wires <see cref="RocksDbKeyValueStore"/> so finalized inclusion proofs
    /// survive node restarts.
    /// </summary>
    public InMemoryL2RpcStore(uint chainId, SecurityLevel level, IL2KeyValueStore proofs, bool ownsProofs = false)
    {
        ArgumentNullException.ThrowIfNull(proofs);
        // Reject the L1-sentinel chain id (0) — every RPC call would later fail
        // AssertOurChain with a misleading "differs from local 0" comparison.
        ChainId = Neo.L2.ChainIdValidator.ValidateL2(chainId);
        // Range-check the SecurityLevel byte enum so a `(SecurityLevel)99` cast
        // doesn't silently propagate as `levelName = "99"` in RPC responses.
        if (level is not (SecurityLevel.Sidechain or SecurityLevel.Settled or SecurityLevel.Optimistic or SecurityLevel.Validity))
            throw new ArgumentOutOfRangeException(nameof(level),
                $"SecurityLevel {(byte)level} not in [0..3]");
        SecurityLevel = level;
        _proofs = proofs;
        _ownsProofs = ownsProofs;
    }

    private static byte[] BuildKey(byte prefix, UInt256 hash)
    {
        var key = new byte[33];
        key[0] = prefix;
        hash.GetSpan().CopyTo(key.AsSpan(1));
        return key;
    }

    /// <summary>Record a sealed batch + its initial status (typically Pending).</summary>
    public void AddBatch(L2BatchCommitment commitment, BatchStatus status)
    {
        ArgumentNullException.ThrowIfNull(commitment);
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
        ArgumentNullException.ThrowIfNull(l1Asset);
        ArgumentNullException.ThrowIfNull(l2Asset);
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
        // Same iter-148/183 pattern: surface null UInt256 / UInt160 keys at the API
        // boundary instead of as a generic Dictionary "key" message.
        ArgumentNullException.ThrowIfNull(leafHash);
        ArgumentNullException.ThrowIfNull(proofBytes);
        // IL2KeyValueStore.Put copies internally — the iter-167 defensive-copy
        // contract is now enforced uniformly across InMemory + RocksDB backends.
        _proofs.Put(BuildKey(WithdrawalProofPrefix, leafHash), proofBytes);
    }

    /// <summary>Record an inclusion proof for a message hash.</summary>
    /// <remarks>
    /// See <see cref="RecordWithdrawalProof"/> for the defensive-copy rationale.
    /// </remarks>
    public void RecordMessageProof(UInt256 messageHash, byte[] proofBytes)
    {
        ArgumentNullException.ThrowIfNull(messageHash);
        ArgumentNullException.ThrowIfNull(proofBytes);
        _proofs.Put(BuildKey(MessageProofPrefix, messageHash), proofBytes);
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
    public ReadOnlyMemory<byte>? GetWithdrawalProof(UInt256 leafHash)
    {
        ArgumentNullException.ThrowIfNull(leafHash);
        var bytes = _proofs.Get(BuildKey(WithdrawalProofPrefix, leafHash));
        // Explicit null-check: byte[] → ReadOnlyMemory<byte>? coerces null into a
        // non-null empty memory through the implicit conversion. We need
        // Nullable<ReadOnlyMemory<byte>> with HasValue=false on miss.
        return bytes is null ? null : (ReadOnlyMemory<byte>?)new ReadOnlyMemory<byte>(bytes);
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? GetMessageProof(UInt256 messageHash)
    {
        ArgumentNullException.ThrowIfNull(messageHash);
        var bytes = _proofs.Get(BuildKey(MessageProofPrefix, messageHash));
        return bytes is null ? null : (ReadOnlyMemory<byte>?)new ReadOnlyMemory<byte>(bytes);
    }

    /// <inheritdoc />
    public DepositStatus? GetL1DepositStatus(uint sourceChainId, ulong nonce) =>
        _deposits.TryGetValue((sourceChainId, nonce), out var s) ? s : null;

    /// <inheritdoc />
    public UInt160? GetCanonicalAsset(UInt160 l2Asset)
    {
        ArgumentNullException.ThrowIfNull(l2Asset);
        return _l1ByL2.TryGetValue(l2Asset, out var l1) ? l1 : null;
    }

    /// <inheritdoc />
    public UInt160? GetBridgedAsset(UInt160 l1Asset)
    {
        ArgumentNullException.ThrowIfNull(l1Asset);
        return _l2ByL1.TryGetValue(l1Asset, out var l2) ? l2 : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsProofs) _proofs.Dispose();
    }
}
