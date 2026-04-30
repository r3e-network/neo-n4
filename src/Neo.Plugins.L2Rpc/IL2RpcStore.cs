using Neo.L2;

namespace Neo.Plugins.L2Rpc;

/// <summary>
/// Backing store the L2 RPC handlers consult. Production deploys wire this to the running
/// node's batch ledger, settlement client, and L1 cache; tests use an in-memory implementation.
/// </summary>
public interface IL2RpcStore
{
    /// <summary>The chain id this node serves.</summary>
    uint ChainId { get; }

    /// <summary>The chain's published security level (matches ChainRegistry's).</summary>
    SecurityLevel SecurityLevel { get; }

    /// <summary>Look up a sealed batch commitment by its number.</summary>
    L2BatchCommitment? GetBatch(ulong batchNumber);

    /// <summary>Look up the lifecycle status of a batch.</summary>
    BatchStatus GetBatchStatus(ulong batchNumber);

    /// <summary>The latest finalized state root for the chain.</summary>
    UInt256 GetLatestStateRoot();

    /// <summary>Per-batch state root (pre or post). Returns <see cref="UInt256.Zero"/> if not known.</summary>
    UInt256 GetStateRootAtBatch(ulong batchNumber);

    /// <summary>Inclusion-proof bytes for a withdrawal leaf. <c>null</c> if not yet finalized.</summary>
    ReadOnlyMemory<byte>? GetWithdrawalProof(UInt256 withdrawalLeafHash);

    /// <summary>Inclusion-proof bytes for a cross-chain message hash. <c>null</c> if not yet finalized.</summary>
    ReadOnlyMemory<byte>? GetMessageProof(UInt256 messageHash);

    /// <summary>L1 deposit lookup by (sourceChain, nonce). Returns null if not seen.</summary>
    DepositStatus? GetL1DepositStatus(uint sourceChainId, ulong nonce);

    /// <summary>Resolve canonical L1 asset for a known L2 asset hash.</summary>
    UInt160? GetCanonicalAsset(UInt160 l2Asset);

    /// <summary>Resolve L2 asset hash for a known L1 asset.</summary>
    UInt160? GetBridgedAsset(UInt160 l1Asset);
}

/// <summary>Per-deposit lifecycle state.</summary>
public readonly record struct DepositStatus(uint SourceChainId, ulong Nonce, bool ConsumedOnL2, ulong? IncludedInBatch);
