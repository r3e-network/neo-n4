using Microsoft.Extensions.Configuration;
using Neo.L2.Bridge;
using Neo.L2.Messaging;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// Wires <see cref="AssetRegistry"/>, <see cref="DepositProcessor"/>, and
/// <see cref="WithdrawalProcessor"/> into the running L2 node. The plugin owns the per-batch
/// withdrawal tree's lifecycle and exposes the components for the batch executor to consume.
/// See doc.md §13.1 (L2BridgeContract) and §15.2 / §15.3.
/// </summary>
public sealed class L2BridgePlugin : Plugin
{
    private uint _chainId;
    private readonly AssetRegistry _registry = new();
    private DepositProcessor? _depositProcessor;
    private WithdrawalProcessor? _withdrawalProcessor;
    private readonly L1MessageInbox _inbox = new();
    private IL2Metrics _metrics = NoOpMetrics.Instance;

    /// <inheritdoc />
    public override string Name => "L2BridgePlugin";

    /// <inheritdoc />
    public override string Description => "L2-side bridge orchestration: deposit consumer + withdrawal staging.";

    /// <summary>The asset registry (canonical L1↔L2 mapping cache).</summary>
    public AssetRegistry Registry => _registry;

    /// <summary>The deposit processor (consumes inbound L1 deposit messages → MintInstruction).</summary>
    public DepositProcessor DepositProcessor => _depositProcessor!;

    /// <summary>The withdrawal processor (stages withdrawals into the per-batch tree).</summary>
    public WithdrawalProcessor WithdrawalProcessor => _withdrawalProcessor!;

    /// <summary>L1→L2 message inbox feeding the deposit processor.</summary>
    public L1MessageInbox Inbox => _inbox;

    /// <summary>
    /// Wire a metrics sink. The processors emit
    /// <c>l2.bridge.deposits/deposits_rejected/withdrawals/withdrawals_rejected</c> against
    /// this sink. Defaults to <see cref="NoOpMetrics"/>.
    /// Swaps the sink in-place on existing processors so consumed-nonce / withdrawal-tree
    /// state survives a re-wire.
    /// </summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
        _depositProcessor?.WithMetrics(metrics);
        _withdrawalProcessor?.WithMetrics(metrics);
    }

    /// <inheritdoc />
    protected override void Configure()
    {
        var section = GetConfiguration();
        var rawChainId = section.GetValue<uint?>("ChainId");
        _chainId = rawChainId is null ? 0u : Neo.L2.ChainIdValidator.ValidateL2(rawChainId.Value);
        _depositProcessor = new DepositProcessor(_chainId, _registry, _metrics);
        _withdrawalProcessor = new WithdrawalProcessor(_chainId, _registry, _metrics);
    }
}
