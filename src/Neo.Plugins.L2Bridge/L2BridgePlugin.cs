using Microsoft.Extensions.Configuration;
using Neo.L2;
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
    private ISharedBridgeDepositSource? _depositSource;
    private IL2Metrics _metrics = NoOpMetrics.Instance;

    /// <inheritdoc />
    public override string Name => "L2BridgePlugin";

    /// <inheritdoc />
    public override string Description => "L2-side bridge orchestration: deposit consumer + withdrawal staging.";

    /// <summary>The asset registry (canonical L1↔L2 mapping cache).</summary>
    public AssetRegistry Registry => _registry;

    /// <summary>The deposit processor (consumes inbound L1 deposit messages → MintInstruction).</summary>
    /// <remarks>Throws if accessed before <see cref="Configure"/> has run. The previous
    /// <c>!</c> null-forgiving operator deferred the failure to the caller's next field
    /// access — a NRE without a clue about the cause. Iter 178 surfaces it at the source.</remarks>
    public DepositProcessor DepositProcessor =>
        _depositProcessor ?? throw new InvalidOperationException(
            "DepositProcessor accessed before Configure() — wire the L2BridgePlugin into the host first");

    /// <summary>The withdrawal processor (stages withdrawals into the per-batch tree).</summary>
    /// <remarks>See <see cref="DepositProcessor"/> for the not-yet-configured rationale.</remarks>
    public WithdrawalProcessor WithdrawalProcessor =>
        _withdrawalProcessor ?? throw new InvalidOperationException(
            "WithdrawalProcessor accessed before Configure() — wire the L2BridgePlugin into the host first");

    /// <summary>L1→L2 message inbox feeding the deposit processor.</summary>
    public L1MessageInbox Inbox => _inbox;

    /// <summary>
    /// Optional SharedBridge deposit source held for composition. Production operators should
    /// pass the same instance to <c>L2BatchPlugin.WireL1MessageInbox</c> / <c>WithDepositSource</c>
    /// so reserve → durable-seal confirm is owned by the batcher.
    /// </summary>
    public ISharedBridgeDepositSource? DepositSource => _depositSource;

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

    /// <summary>
    /// Wire the SharedBridge deposit discovery source for this L2. Must match the plugin
    /// chain id once <see cref="Configure"/> has run (or chain id is still unset).
    /// </summary>
    public void WithDepositSource(ISharedBridgeDepositSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_chainId != 0 && source.ChainId != _chainId)
            throw new InvalidOperationException(
                $"deposit source chain {source.ChainId} differs from bridge plugin chain {_chainId}");
        if (_depositSource is not null && !ReferenceEquals(_depositSource, source))
            throw new InvalidOperationException("a SharedBridge deposit source is already wired");
        _depositSource = source;
    }

    /// <summary>
    /// Non-mutating view of ready SharedBridge deposits. Returns empty when no source is
    /// wired. Mutating drain / confirm is owned by <c>L2BatchPlugin</c>.
    /// </summary>
    public IReadOnlyList<CrossChainMessage> PeekSharedBridgeDeposits(int maxMessages)
    {
        if (_depositSource is null)
            return Array.Empty<CrossChainMessage>();
        return _depositSource.Peek(maxMessages);
    }

    /// <inheritdoc />
    protected override void Configure()
    {
        var section = GetConfiguration();
        var rawChainId = section.GetValue<uint?>("ChainId");
        _chainId = rawChainId is null ? 0u : Neo.L2.ChainIdValidator.ValidateL2(rawChainId.Value);
        // Lazy init the processors — Configure may be called more than once (config-watcher
        // re-fire, host re-init). Without this guard, recreating the processors would
        // discard their consumed-nonce state, allowing already-processed deposits / closed-
        // batch withdrawals to be replayed on the L2 (the duplicate would only fail
        // hours later at L1 settlement).
        _depositProcessor ??= new DepositProcessor(_chainId, _registry, _metrics);
        _withdrawalProcessor ??= new WithdrawalProcessor(_chainId, _registry, _metrics);
    }
}
