using Neo.L2;
using Neo.L2.Bridge;
using Neo.L2.ForcedInclusion;
using Neo.L2.Messaging;
using Neo.L2.Sequencer;
using Neo.L2.Settlement.Rpc;

namespace Neo.Plugins.L2;

/// <summary>
/// Host composition root for the L1 inbox: SharedBridge deposits, ForcedInclusion, and
/// MessageRouter wired from a chain working directory with one shared L1 RPC client.
/// </summary>
/// <remarks>
/// See doc.md §7.2 / §15. Prefer this over three independent
/// <c>Create*FromChainDirectory</c> calls so RPC connections are not triplicated.
/// Caller owns disposal (disposes sources + RPC). Wire into
/// <see cref="L2BatchPlugin"/> via <see cref="WireBatch"/> before the first block.
/// </remarks>
public sealed class L1InboxFromChainDirectory : IDisposable
{
    private readonly JsonRpcClient _rpc;
    private bool _disposed;

    private L1InboxFromChainDirectory(
        string chainDirectory,
        uint chainId,
        JsonRpcClient rpc,
        RpcForcedInclusionSource forcedInclusion,
        RpcSharedBridgeDepositSource? deposits,
        RpcMessageRouter? messageRouter,
        Func<uint> l1FinalizedHeight,
        Func<UInt256> sequencerCommitteeHash)
    {
        ChainDirectory = chainDirectory;
        ChainId = chainId;
        _rpc = rpc;
        ForcedInclusion = forcedInclusion;
        Deposits = deposits;
        MessageRouter = messageRouter;
        L1FinalizedHeight = l1FinalizedHeight;
        SequencerCommitteeHash = sequencerCommitteeHash;
    }

    /// <summary>Absolute chain working directory.</summary>
    public string ChainDirectory { get; }

    /// <summary>L2 chain id from settlement plugin config.</summary>
    public uint ChainId { get; }

    /// <summary>Always present (ForcedInclusion is required settlement config).</summary>
    public RpcForcedInclusionSource ForcedInclusion { get; }

    /// <summary>Present when settlement config includes <c>SharedBridgeHash</c>.</summary>
    public RpcSharedBridgeDepositSource? Deposits { get; }

    /// <summary>Present when settlement config includes <c>MessageRouterHash</c>.</summary>
    public RpcMessageRouter? MessageRouter { get; }

    /// <summary>Seal-time L1 finalized height (<c>getblockcount</c> − finality depth).</summary>
    public Func<uint> L1FinalizedHeight { get; }

    /// <summary>
    /// Seal-time sequencer committee hash from <c>chain.config.json</c> validators
    /// (static). Override with a live registry provider for production polling.
    /// </summary>
    public Func<UInt256> SequencerCommitteeHash { get; }

    /// <summary>
    /// Open durable L1 inbox adapters from settlement plugin config + chain layout stores.
    /// </summary>
    /// <param name="chainDirectory">Chain root from <c>init-l2</c> / deploy-report.</param>
    /// <param name="openFinalizedProofStore">
    /// When MessageRouter is configured, open durable finalized proofs under
    /// <c>data/rpc/proofs</c> (default true).
    /// </param>
    public static L1InboxFromChainDirectory Open(
        string chainDirectory,
        bool openFinalizedProofStore = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var root = Path.GetFullPath(chainDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(
                $"Chain directory not found: {root}. Run neo-stack init-l2 first.");

        var settings = L2SettlementSettings.FromChainDirectory(root);
        var production = settings.ValidateProduction();
        if (settings.ForcedInclusionDeploymentHeight == 0)
            throw new InvalidOperationException(
                "L1InboxFromChainDirectory requires ForcedInclusionDeploymentHeight "
                + "in settlement plugin config (deploy-report blockIndex)");

        var rpc = new JsonRpcClient(production.RpcEndpoint.AbsoluteUri);
        RpcForcedInclusionSource? forced = null;
        RpcSharedBridgeDepositSource? deposits = null;
        RpcMessageRouter? router = null;
        try
        {
            forced = RpcForcedInclusionSource.OpenFromChainDirectory(
                root,
                rpc,
                production.ForcedInclusionHash,
                production.ChainId,
                settings.ForcedInclusionDeploymentHeight,
                settings.L1FinalityDepth,
                ownsRpc: false);

            if (production.SharedBridgeHash is not null && production.L2BridgeHash is not null)
            {
                if (settings.SharedBridgeDeploymentHeight == 0)
                    throw new InvalidOperationException(
                        "SharedBridgeHash is set; SharedBridgeDeploymentHeight must be non-zero");
                deposits = RpcSharedBridgeDepositSource.OpenFromChainDirectory(
                    root,
                    rpc,
                    production.SharedBridgeHash,
                    production.ChainId,
                    production.L2BridgeHash,
                    settings.SharedBridgeDeploymentHeight,
                    settings.L1FinalityDepth,
                    ownsRpc: false);
            }

            if (production.MessageRouterHash is not null)
            {
                if (settings.MessageRouterDeploymentHeight == 0)
                    throw new InvalidOperationException(
                        "MessageRouterHash is set; MessageRouterDeploymentHeight must be non-zero");
                router = RpcMessageRouter.OpenFromChainDirectory(
                    root,
                    rpc,
                    production.MessageRouterHash,
                    production.ChainId,
                    settings.MessageRouterDeploymentHeight,
                    settings.L1FinalityDepth,
                    openFinalizedProofStore: openFinalizedProofStore,
                    ownsRpc: false);
            }

            var finalizedHeight = new RpcL1FinalizedHeightSource(rpc, settings.L1FinalityDepth)
                .CreateSyncProvider();
            var committeeHash = SequencerCommitteeConfig
                .CreateStaticHashProviderFromChainDirectory(root);

            return new L1InboxFromChainDirectory(
                root,
                production.ChainId,
                rpc,
                forced,
                deposits,
                router,
                finalizedHeight,
                committeeHash);
        }
        catch
        {
            router?.Dispose();
            deposits?.Dispose();
            forced?.Dispose();
            rpc.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Install forced-inclusion + optional deposits/MessageRouter on the batch plugin.
    /// Must run before the first sealed block.
    /// </summary>
    public void WireBatch(L2BatchPlugin batchPlugin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batchPlugin);

        batchPlugin.WithForcedInclusionSource(ForcedInclusion);
        if (Deposits is not null || MessageRouter is not null)
        {
            batchPlugin.WireL1MessageInbox(
                ChainId,
                L1FinalizedHeight,
                SequencerCommitteeHash,
                Deposits,
                MessageRouter);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        MessageRouter?.Dispose();
        Deposits?.Dispose();
        ForcedInclusion.Dispose();
        _rpc.Dispose();
    }
}
