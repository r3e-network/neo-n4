using Neo.L2;
using Neo.L2.Proving.Attestation;
using Neo.L2.Settlement.Rpc;
using Neo.L2.Telemetry;
using Neo.Plugins.L2;
using Neo.Plugins.L2Gateway;

namespace Neo.L2.Gateway.Rpc;

/// <summary>
/// Gateway host composition root: durable aggregator/outbox + proof-bound publisher +
/// production publication profile from a chain working directory.
/// </summary>
/// <remarks>
/// See doc.md §4 / §14.2. Lives in Gateway.Rpc so <see cref="ProofBoundRpcGlobalRootPublisher"/>
/// can bind without circular plugin dependencies. Terminal proving circuit and replay domain /
/// verification key remain host-supplied; funded L1 confirmation is operator-owned.
/// Optional <see cref="IL2Metrics"/> wires outbox/aggregator telemetry (pair with
/// Multisig/Optimistic/Zk LocalHost metrics). Dispose the composition (gateway then publisher)
/// before reopening durable outbox paths.
/// </remarks>
public sealed class GatewayHostComposition : IDisposable
{
    private bool _disposed;

    private GatewayHostComposition(
        string chainDirectory,
        L2GatewayPlugin gateway,
        ProofBoundRpcGlobalRootPublisher publisher,
        IGatewayProofProver proofProver,
        bool ownsProofProver,
        IL2Metrics? metrics,
        L2MetricsPlugin? metricsPlugin,
        uint? expectedNetwork,
        bool hasL1RpcEndpoint,
        UInt256 replayDomain,
        UInt256 verificationKeyId)
    {
        ChainDirectory = chainDirectory;
        Gateway = gateway;
        Publisher = publisher;
        ProofProver = proofProver;
        OwnsProofProver = ownsProofProver;
        Metrics = metrics;
        MetricsPlugin = metricsPlugin;
        ExpectedNetwork = expectedNetwork;
        HasL1RpcEndpoint = hasL1RpcEndpoint;
        ReplayDomain = replayDomain;
        VerificationKeyId = verificationKeyId;
    }

    /// <summary>Absolute chain working directory.</summary>
    public string ChainDirectory { get; }

    /// <summary>Gateway plugin with durable outbox and production publication profile.</summary>
    public L2GatewayPlugin Gateway { get; }

    /// <summary>Proof-bound SettlementManager publisher (owns L1 RPC when opened from chain dir).</summary>
    public ProofBoundRpcGlobalRootPublisher Publisher { get; }

    /// <summary>Terminal Gateway proof prover installed on the publication profile.</summary>
    public IGatewayProofProver ProofProver { get; }

    /// <summary>True when this composition created and owns <see cref="ProofProver"/> disposal.</summary>
    public bool OwnsProofProver { get; }

    /// <summary>
    /// Optional metrics sink passed at open (e.g. Multisig LocalHost <c>Metrics.Metrics</c>
    /// or <see cref="MetricsPlugin"/>.Metrics). Null when no sink was supplied; export helpers
    /// then return empty snapshots.
    /// </summary>
    public IL2Metrics? Metrics { get; }

    /// <summary>
    /// Optional metrics plugin for <see cref="StartMetricsHttp"/> control. Distinct from a bare
    /// <see cref="IL2Metrics"/> sink: only a plugin can host <c>/metrics</c> + probes.
    /// Composition does not dispose the plugin (caller owns lifecycle).
    /// </summary>
    public L2MetricsPlugin? MetricsPlugin { get; }

    /// <summary>True when <see cref="MetricsPlugin"/> was supplied at open.</summary>
    public bool HasMetricsPlugin => MetricsPlugin is not null;

    /// <summary>Metrics plugin is enabled in settings (false when no plugin).</summary>
    public bool IsMetricsEnabled => MetricsPlugin?.IsEnabled == true;

    /// <summary>Metrics HTTP bound port (0 when not listening / no plugin).</summary>
    public int MetricsBoundPort => MetricsPlugin?.BoundPort ?? 0;

    /// <summary>True when metrics HTTP is listening (BoundPort &gt; 0).</summary>
    public bool IsMetricsHttpListening => MetricsBoundPort > 0;

    /// <summary>True when a <c>/readyz</c> readiness predicate is installed on the metrics plugin.</summary>
    public bool HasMetricsReadinessCheck => MetricsPlugin?.HasReadinessCheck == true;

    /// <summary>True when a <c>/healthprobe</c> JSON body provider is installed.</summary>
    public bool HasMetricsHealthProbe => MetricsPlugin?.HasHealthProbe == true;

    /// <summary>True when a <c>/operatorstatus</c> JSON body provider is installed.</summary>
    public bool HasMetricsOperatorStatus => MetricsPlugin?.HasOperatorStatus == true;

    /// <summary>
    /// Metrics HTTP health failure names. Empty when no plugin / metrics disabled, or when
    /// listening with readiness + healthprobe + operatorstatus providers wired.
    /// </summary>
    public IReadOnlyList<string> MetricsHttpHealthFailures =>
        GatewayHostOperatorStatus.BuildMetricsHttpHealthFailures(
            IsMetricsEnabled,
            HasMetricsPlugin,
            IsMetricsHttpListening,
            HasMetricsReadinessCheck,
            HasMetricsHealthProbe,
            HasMetricsOperatorStatus);

    /// <summary>True when <see cref="MetricsHttpHealthFailures"/> is empty.</summary>
    public bool IsMetricsHttpHealthy => MetricsHttpHealthFailures.Count == 0;

    /// <summary>
    /// Active aggregator installed on the gateway plugin
    /// (<see cref="L2GatewayPlugin.Aggregator"/>).
    /// </summary>
    public IGatewayAggregator Aggregator => Gateway.Aggregator;

    /// <summary>
    /// Commitments currently pending aggregation in the active aggregator
    /// (<see cref="IGatewayAggregator.PendingCount"/>). Offline ops surface without
    /// digging into <see cref="Gateway"/>; L1 publish remains a funded gate.
    /// </summary>
    public int AggregatorPendingCount => Aggregator.PendingCount;

    /// <summary>
    /// True when a durable Gateway outbox is attached
    /// (<see cref="L2GatewayPlugin.HasDurableOutbox"/>). OpenMerkle/Multisig/Sp1 hosts
    /// always attach one.
    /// </summary>
    public bool HasDurableOutbox => Gateway.HasDurableOutbox;

    /// <summary>
    /// True when production global-root publication is configured
    /// (<see cref="L2GatewayPlugin.IsPublicationConfigured"/>). Distinct from funded
    /// L1 confirmation of a specific epoch.
    /// </summary>
    public bool IsPublicationConfigured => Gateway.IsPublicationConfigured;

    /// <summary>
    /// Whether the Gateway plugin is enabled in settings
    /// (<see cref="L2GatewayPlugin.IsEnabled"/>).
    /// </summary>
    public bool IsEnabled => Gateway.IsEnabled;

    /// <summary>
    /// Configured max automatic publication retries
    /// (<see cref="L2GatewayPlugin.MaxAutomaticRetries"/>).
    /// </summary>
    public int MaxAutomaticRetries => Gateway.MaxAutomaticRetries;

    /// <summary>
    /// True when an unconfirmed publication remains retryable or poisoned
    /// (<see cref="L2GatewayPlugin.HasPendingPublication"/>).
    /// </summary>
    public bool HasPendingPublication => Gateway.HasPendingPublication;

    /// <summary>
    /// Pending publication epoch, or null when none awaits confirmation
    /// (<see cref="L2GatewayPlugin.PendingPublicationEpoch"/>).
    /// </summary>
    public ulong? PendingPublicationEpoch => Gateway.PendingPublicationEpoch;

    /// <summary>
    /// Expected L1 network magic from settlement config / <c>l1.deployed.json</c>
    /// (null when unset). Connectivity is not probed.
    /// </summary>
    public uint? ExpectedNetwork { get; }

    /// <summary>
    /// True when Open resolved an L1 RPC endpoint from the chain directory layout
    /// (does not probe connectivity; L1 publish remains a funded gate).
    /// </summary>
    public bool HasL1RpcEndpoint { get; }

    /// <summary>
    /// Offline publication-profile readiness: enabled, publication configured, durable outbox,
    /// L1 endpoint present, and non-zero replay domain / verification key / publisher hashes.
    /// Does not probe L1 or claim confirmation (funded gate).
    /// </summary>
    public bool IsPublicationProfileReady =>
        IsEnabled
        && IsPublicationConfigured
        && HasDurableOutbox
        && HasL1RpcEndpoint
        && !ReplayDomain.Equals(UInt256.Zero)
        && !VerificationKeyId.Equals(UInt256.Zero)
        && !SettlementManagerHash.Equals(UInt160.Zero)
        && !MessageRouterHash.Equals(UInt160.Zero);

    /// <summary>True when chain layout / settlement config provided a non-null expected network.</summary>
    public bool HasExpectedNetwork => ExpectedNetwork is not null;

    /// <summary>
    /// Offline Gateway passport checks that failed (empty when complete). Diagnostic names only;
    /// does not claim L1 confirmation (funded gate).
    /// Built by <see cref="GatewayHostOperatorStatus.BuildOfflinePassportFailures"/>.
    /// </summary>
    public IReadOnlyList<string> OfflinePassportFailures =>
        GatewayHostOperatorStatus.BuildOfflinePassportFailures(
            IsEnabled,
            IsPublicationConfigured,
            HasDurableOutbox,
            HasL1RpcEndpoint,
            !ReplayDomain.Equals(UInt256.Zero),
            !VerificationKeyId.Equals(UInt256.Zero),
            !SettlementManagerHash.Equals(UInt160.Zero),
            !MessageRouterHash.Equals(UInt160.Zero),
            HasExpectedNetwork,
            MaxAutomaticRetries >= 1);

    /// <summary>
    /// Offline Gateway passport: true when <see cref="OfflinePassportFailures"/> is empty.
    /// Does not claim L1 confirmation (funded gate).
    /// </summary>
    public bool IsOfflinePassportComplete => OfflinePassportFailures.Count == 0;

    /// <summary>
    /// True when the durable outbox publication state is poisoned and needs operator recovery
    /// (<see cref="RecoverPoisonedPublication"/>). Runtime state, not a config passport check.
    /// </summary>
    public bool IsOutboxPoisoned =>
        OutboxStatus.PublicationState == GatewayOutboxState.Poisoned;

    /// <summary>
    /// True when no publication is pending, the durable queue is empty, no last error is stored,
    /// and the outbox is not poisoned. Runtime idle health (distinct from offline config passport).
    /// Built by <see cref="GatewayHostOperatorStatus.IsOutboxRuntimeIdle"/>.
    /// </summary>
    public bool IsOutboxIdle
    {
        get
        {
            var outbox = OutboxStatus;
            return GatewayHostOperatorStatus.IsOutboxRuntimeIdle(
                HasPendingPublication,
                AggregatorPendingCount,
                outbox.QueueDepth,
                outbox.LastError,
                IsOutboxPoisoned);
        }
    }

    /// <summary>
    /// Offline passport complete and outbox idle. Does not claim L1 confirmation of a published
    /// root (funded gate); only local queue/outbox health plus config wiring.
    /// True when <see cref="PublicationHealthFailures"/> is empty.
    /// </summary>
    public bool IsPublicationHealthy => PublicationHealthFailures.Count == 0;

    /// <summary>
    /// Publication health checks that failed (empty when <see cref="IsPublicationHealthy"/>).
    /// Diagnostic only; not an L1 confirmation claim.
    /// Built by <see cref="GatewayHostOperatorStatus.BuildPublicationHealthFailures"/>.
    /// </summary>
    public IReadOnlyList<string> PublicationHealthFailures
    {
        get
        {
            var outbox = OutboxStatus;
            return GatewayHostOperatorStatus.BuildPublicationHealthFailures(
                IsOfflinePassportComplete,
                IsOutboxPoisoned,
                HasPendingPublication,
                AggregatorPendingCount,
                outbox.QueueDepth,
                outbox.LastError);
        }
    }

    /// <summary>
    /// Gateway host combined local health: publication health + metrics HTTP health
    /// (when a metrics plugin is enabled). Parity with LocalHost <c>IsLocalHostHealthy</c>.
    /// Does not claim L1 confirmation.
    /// </summary>
    public bool IsGatewayHostHealthy => GatewayHostHealthFailures.Count == 0;

    /// <summary>
    /// Failed Gateway host health checks (publication + optional metrics HTTP).
    /// Empty when <see cref="IsGatewayHostHealthy"/>.
    /// </summary>
    public IReadOnlyList<string> GatewayHostHealthFailures =>
        GatewayHostOperatorStatus.BuildGatewayHostHealthFailures(
            PublicationHealthFailures,
            MetricsHttpHealthFailures);

    /// <summary>
    /// Terminal proof-system discriminator from <see cref="ProofProver"/>
    /// (<see cref="IGatewayProofProver.ProofSystem"/>).
    /// </summary>
    public byte ProofSystem => ProofProver.ProofSystem;

    /// <summary>
    /// Terminal aggregation backend id from <see cref="ProofProver"/>
    /// (<see cref="IGatewayProofProver.AggregationBackendId"/>).
    /// </summary>
    public byte AggregationBackendId => ProofProver.AggregationBackendId;

    /// <summary>
    /// Application/network replay domain bound into the publication profile at open
    /// (must be non-zero for production). Offline ops surface; L1 still funded.
    /// </summary>
    public UInt256 ReplayDomain { get; }

    /// <summary>
    /// Verification key id bound into the publication profile at open
    /// (must be non-zero for production).
    /// </summary>
    public UInt256 VerificationKeyId { get; }

    /// <summary>
    /// SettlementManager hash from the proof-bound publisher
    /// (<see cref="ProofBoundRpcGlobalRootPublisher.SettlementManagerHash"/>).
    /// </summary>
    public UInt160 SettlementManagerHash => Publisher.SettlementManagerHash;

    /// <summary>
    /// MessageRouter hash from the proof-bound publisher
    /// (<see cref="ProofBoundRpcGlobalRootPublisher.MessageRouterHash"/>).
    /// </summary>
    public UInt160 MessageRouterHash => Publisher.MessageRouterHash;

    /// <summary>
    /// Durable outbox / confirmation-lag status
    /// (<see cref="L2GatewayPlugin.OutboxStatus"/>).
    /// </summary>
    public GatewayOutboxStatus OutboxStatus => Gateway.OutboxStatus;

    /// <summary>
    /// Forward a finalized per-chain batch into the durable aggregator/outbox
    /// (<see cref="L2GatewayPlugin.ReceiveBatch"/>).
    /// </summary>
    public void ReceiveBatch(L2BatchCommitment commitment) => Gateway.ReceiveBatch(commitment);

    /// <summary>
    /// Pull a ready aggregate without publishing to L1
    /// (<see cref="L2GatewayPlugin.PullAggregate"/>).
    /// </summary>
    /// <remarks>
    /// Fails closed when a durable publication outbox is attached (host OpenMerkle/Multisig/Sp1);
    /// use <see cref="PublishAggregateAsync"/> for the production path. Useful for in-memory
    /// aggregator inspection only.
    /// </remarks>
    public AggregatedCommitment? PullAggregate() => Gateway.PullAggregate();

    /// <summary>
    /// Aggregate, prove, publish, and confirm the next Gateway epoch
    /// (<see cref="L2GatewayPlugin.PublishAggregateAsync"/>). Funded L1 confirmation remains
    /// operator-owned.
    /// </summary>
    public ValueTask<UInt256> PublishAggregateAsync(
        ulong batchEpoch,
        CancellationToken cancellationToken = default)
        => Gateway.PublishAggregateAsync(batchEpoch, cancellationToken);

    /// <summary>
    /// Reset a poisoned publication for operator recovery
    /// (<see cref="L2GatewayPlugin.RecoverPoisonedPublication"/>).
    /// </summary>
    public void RecoverPoisonedPublication() => Gateway.RecoverPoisonedPublication();

    /// <summary>
    /// Aggregate local Gateway operator readiness without Neo.CLI or funded L1 traffic.
    /// </summary>
    public GatewayHostOperatorStatus GetOperatorStatus()
    {
        var outbox = OutboxStatus;
        var metricsFailures = MetricsHttpHealthFailures;
        var hostFailures = GatewayHostHealthFailures;
        return new GatewayHostOperatorStatus
        {
            ChainDirectory = ChainDirectory,
            HasPendingPublication = HasPendingPublication,
            PendingPublicationEpoch = PendingPublicationEpoch,
            AggregatorPendingCount = AggregatorPendingCount,
            HasDurableOutbox = HasDurableOutbox,
            IsPublicationConfigured = IsPublicationConfigured,
            IsEnabled = IsEnabled,
            MaxAutomaticRetries = MaxAutomaticRetries,
            OutboxQueueDepth = outbox.QueueDepth,
            PublicationState = outbox.PublicationState,
            OutboxRetryCount = outbox.RetryCount,
            OutboxLastError = outbox.LastError,
            ConfirmationLagMilliseconds = outbox.ConfirmationLagMilliseconds,
            AggregationBackendId = ProofProver.AggregationBackendId,
            ProofSystem = ProofSystem,
            ExpectedNetwork = ExpectedNetwork,
            HasL1RpcEndpoint = HasL1RpcEndpoint,
            IsPublicationProfileReady = IsPublicationProfileReady,
            HasExpectedNetwork = HasExpectedNetwork,
            IsOfflinePassportComplete = IsOfflinePassportComplete,
            OfflinePassportFailures = OfflinePassportFailures,
            IsOutboxPoisoned = IsOutboxPoisoned,
            IsOutboxIdle = IsOutboxIdle,
            IsPublicationHealthy = IsPublicationHealthy,
            PublicationHealthFailures = PublicationHealthFailures,
            IsGatewayHostHealthy = hostFailures.Count == 0,
            GatewayHostHealthFailures = hostFailures,
            ReplayDomain = ReplayDomain,
            VerificationKeyId = VerificationKeyId,
            SettlementManagerHash = SettlementManagerHash,
            MessageRouterHash = MessageRouterHash,
            OwnsProofProver = OwnsProofProver,
            HasMetrics = Metrics is not null,
            MetricsEntryCount = CaptureMetricsSnapshot().TotalEntries,
            HasMetricsPlugin = HasMetricsPlugin,
            IsMetricsEnabled = IsMetricsEnabled,
            IsMetricsHttpListening = IsMetricsHttpListening,
            MetricsBoundPort = MetricsBoundPort,
            HasMetricsReadinessCheck = HasMetricsReadinessCheck,
            HasMetricsHealthProbe = HasMetricsHealthProbe,
            HasMetricsOperatorStatus = HasMetricsOperatorStatus,
            MetricsHttpHealthFailures = metricsFailures,
            IsMetricsHttpHealthy = metricsFailures.Count == 0,
        };
    }

    /// <summary>
    /// Start (or re-enter) the metrics HTTP server when <see cref="MetricsPlugin"/> was
    /// supplied at open. Idempotent. Wires <c>/readyz</c> to
    /// <see cref="IsOfflinePassportComplete"/>, <c>/healthprobe</c> to
    /// <see cref="FormatHealthProbeJson"/>, and <c>/operatorstatus</c> to
    /// <see cref="FormatOperatorStatusJson"/> (LocalHost StartMetricsHttp parity).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="MetricsPlugin"/> was supplied at open.
    /// </exception>
    public void StartMetricsHttp(int? portOverride = null, Func<bool>? readinessCheck = null)
    {
        if (MetricsPlugin is null)
        {
            throw new InvalidOperationException(
                "GatewayHostComposition.StartMetricsHttp requires Open* with metricsPlugin: "
                + "(L2MetricsPlugin). A bare IL2Metrics sink cannot host HTTP probes.");
        }

        MetricsPlugin.WithReadinessCheck(readinessCheck ?? (() => IsOfflinePassportComplete));
        MetricsPlugin.WithHealthProbe(FormatHealthProbeJson);
        MetricsPlugin.WithOperatorStatus(FormatOperatorStatusJson);
        MetricsPlugin.Start(portOverride);
    }

    /// <summary>
    /// Stop the metrics HTTP server without disposing <see cref="MetricsPlugin"/>
    /// (<see cref="L2MetricsPlugin.Stop"/>). Idempotent no-op when no plugin is attached.
    /// </summary>
    public void StopMetricsHttp() => MetricsPlugin?.Stop();

    /// <summary>
    /// Serialize <see cref="GetOperatorStatus"/> as indented camelCase JSON for ops scripts
    /// without writing a file. L1 confirmation remains funded and is not claimed.
    /// </summary>
    public string FormatOperatorStatusJson()
        => GatewayHostOperatorStatusDocument.FormatJson(
            GatewayHostOperatorStatusDocument.From(GetOperatorStatus()));

    /// <summary>
    /// Write <see cref="GetOperatorStatus"/> as indented camelCase JSON for host health files
    /// without Neo.CLI (L1 confirmation remains funded and is not claimed).
    /// </summary>
    public async ValueTask WriteOperatorStatusAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(fullPath, FormatOperatorStatusJson(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Build a compact in-memory health probe (passport / outbox / publication) without the
    /// full operator status document. L1 confirmation remains a funded gate and is not claimed.
    /// </summary>
    public GatewayHostHealthProbeDocument GetHealthProbe()
    {
        var publicationFailures = PublicationHealthFailures;
        var metricsFailures = MetricsHttpHealthFailures;
        var hostFailures = GatewayHostHealthFailures;
        var outbox = OutboxStatus;
        return new GatewayHostHealthProbeDocument
        {
            IsOfflinePassportComplete = IsOfflinePassportComplete,
            OfflinePassportFailures = OfflinePassportFailures,
            IsEnabled = IsEnabled,
            IsPublicationConfigured = IsPublicationConfigured,
            HasDurableOutbox = HasDurableOutbox,
            HasL1RpcEndpoint = HasL1RpcEndpoint,
            HasExpectedNetwork = HasExpectedNetwork,
            ExpectedNetwork = ExpectedNetwork,
            IsPublicationProfileReady = IsPublicationProfileReady,
            MaxAutomaticRetries = MaxAutomaticRetries,
            ProofSystem = ProofSystem,
            AggregationBackendId = AggregationBackendId,
            ReplayDomain = ReplayDomain.ToString(),
            VerificationKeyId = VerificationKeyId.ToString(),
            SettlementManagerHash = SettlementManagerHash.ToString(),
            MessageRouterHash = MessageRouterHash.ToString(),
            OwnsProofProver = OwnsProofProver,
            MetricsEntryCount = CaptureMetricsSnapshot().TotalEntries,
            HasMetrics = Metrics is not null,
            HasMetricsPlugin = HasMetricsPlugin,
            IsMetricsEnabled = IsMetricsEnabled,
            IsMetricsHttpListening = IsMetricsHttpListening,
            MetricsBoundPort = MetricsBoundPort,
            HasMetricsReadinessCheck = HasMetricsReadinessCheck,
            HasMetricsHealthProbe = HasMetricsHealthProbe,
            HasMetricsOperatorStatus = HasMetricsOperatorStatus,
            IsMetricsHttpHealthy = metricsFailures.Count == 0,
            MetricsHttpHealthFailures = metricsFailures,
            HasPendingPublication = HasPendingPublication,
            PendingPublicationEpoch = PendingPublicationEpoch,
            AggregatorPendingCount = AggregatorPendingCount,
            OutboxQueueDepth = outbox.QueueDepth,
            OutboxRetryCount = outbox.RetryCount,
            OutboxLastError = outbox.LastError,
            ConfirmationLagMilliseconds = outbox.ConfirmationLagMilliseconds,
            PublicationState = outbox.PublicationState,
            IsOutboxPoisoned = IsOutboxPoisoned,
            IsOutboxIdle = IsOutboxIdle,
            IsPublicationHealthy = publicationFailures.Count == 0,
            PublicationHealthFailures = publicationFailures,
            IsGatewayHostHealthy = hostFailures.Count == 0,
            GatewayHostHealthFailures = hostFailures,
        };
    }

    /// <summary>
    /// Serialize <see cref="GetHealthProbe"/> as indented camelCase JSON for ops scripts
    /// without writing a file. L1 confirmation remains a funded gate and is not claimed.
    /// </summary>
    public string FormatHealthProbeJson()
        => GatewayHostHealthProbeDocument.FormatJson(GetHealthProbe());

    /// <summary>
    /// Write <see cref="GetHealthProbe"/> as indented camelCase JSON for ops scripts without
    /// the full operator status document. L1 confirmation remains a funded gate and is not claimed.
    /// </summary>
    public async ValueTask WriteHealthProbeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(fullPath, FormatHealthProbeJson(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Capture an in-process metrics snapshot when <see cref="Metrics"/> implements
    /// <see cref="IMetricsSource"/>; otherwise returns <see cref="MetricsSnapshot.Empty"/>.
    /// </summary>
    public MetricsSnapshot CaptureMetricsSnapshot()
    {
        if (Metrics is IMetricsSource source)
            return source.Snapshot();
        return MetricsSnapshot.Empty;
    }

    /// <summary>
    /// Render Prometheus exposition text from the wired metrics sink
    /// (<see cref="PrometheusExporter.Format"/>). Empty when no exportable sink is present.
    /// </summary>
    public string ExportPrometheusMetrics()
        => PrometheusExporter.Format(CaptureMetricsSnapshot());

    /// <summary>
    /// Write <see cref="ExportPrometheusMetrics"/> text to <paramref name="path"/> for offline scrape files.
    /// </summary>
    public async ValueTask WritePrometheusMetricsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var body = ExportPrometheusMetrics();
        await File.WriteAllTextAsync(fullPath, body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Open Merkle-path Gateway composition: durable Merkle aggregator/outbox + publisher
    /// + <see cref="L2GatewayPlugin.ConfigureGlobalRootPublicationFromChainDirectory"/>.
    /// </summary>
    /// <param name="chainDirectory">Chain root after init-l2 / deploy-report materialization.</param>
    /// <param name="proofProver">Terminal proving circuit matching Merkle backend 0xC1.</param>
    /// <param name="signer">L1 transaction signer for publishGatewayGlobalRoot.</param>
    /// <param name="replayDomain">Non-zero application/network replay domain.</param>
    /// <param name="verificationKeyId">Non-zero verification key id bound on L1.</param>
    /// <param name="options">Optional RPC sender options (network defaults from deployed layout).</param>
    /// <param name="metrics">Optional metrics sink (e.g. Multisig LocalHost <c>Metrics.Metrics</c>).</param>
    /// <param name="metricsPlugin">
    /// Optional metrics plugin for <see cref="StartMetricsHttp"/>. When set, its
    /// <see cref="L2MetricsPlugin.Metrics"/> sink is used (overrides <paramref name="metrics"/>).
    /// </param>
    public static GatewayHostComposition OpenMerkle(
        string chainDirectory,
        IGatewayProofProver proofProver,
        INeoTransactionSigner signer,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        RpcTransactionSenderOptions? options = null,
        IL2Metrics? metrics = null,
        L2MetricsPlugin? metricsPlugin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(proofProver);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(replayDomain);
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        if (proofProver.AggregationBackendId != MerklePathRoundProver.ConstBackendId)
        {
            throw new ArgumentException(
                $"OpenMerkle requires MerklePathRoundProver backend "
                + $"{MerklePathRoundProver.ConstBackendId}, got {proofProver.AggregationBackendId}",
                nameof(proofProver));
        }

        return Open(
            chainDirectory,
            L2GatewayPlugin.CreateMerkleDurableFromChainDirectory(chainDirectory),
            proofProver,
            ownsProofProver: false,
            signer,
            replayDomain,
            verificationKeyId,
            options,
            metrics,
            metricsPlugin);
    }

    /// <summary>
    /// Open Multisig-round Gateway composition: durable Multisig aggregator/outbox +
    /// publisher + publication profile. Terminal <paramref name="proofProver"/> must
    /// match Multisig backend 0xC0.
    /// </summary>
    /// <param name="chainDirectory">Chain root after init-l2 / deploy-report materialization.</param>
    /// <param name="signers">Committee <see cref="ISignerSet"/> for round attestations.</param>
    /// <param name="threshold">Minimum signatures per aggregation round.</param>
    /// <param name="proofProver">Terminal proving circuit matching Multisig backend 0xC0.</param>
    /// <param name="signer">L1 transaction signer for publishGatewayGlobalRoot.</param>
    /// <param name="replayDomain">Non-zero application/network replay domain.</param>
    /// <param name="verificationKeyId">Non-zero verification key id bound on L1.</param>
    /// <param name="options">Optional RPC sender options.</param>
    /// <param name="metrics">Optional metrics sink for outbox/aggregator telemetry.</param>
    /// <param name="metricsPlugin">Optional metrics plugin for <see cref="StartMetricsHttp"/>.</param>
    public static GatewayHostComposition OpenMultisig(
        string chainDirectory,
        ISignerSet signers,
        int threshold,
        IGatewayProofProver proofProver,
        INeoTransactionSigner signer,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        RpcTransactionSenderOptions? options = null,
        IL2Metrics? metrics = null,
        L2MetricsPlugin? metricsPlugin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(signers);
        ArgumentNullException.ThrowIfNull(proofProver);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(replayDomain);
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        if (proofProver.AggregationBackendId != MultisigRoundProver.ConstBackendId)
        {
            throw new ArgumentException(
                $"OpenMultisig requires MultisigRoundProver backend "
                + $"{MultisigRoundProver.ConstBackendId}, got {proofProver.AggregationBackendId}",
                nameof(proofProver));
        }

        return Open(
            chainDirectory,
            L2GatewayPlugin.CreateMultisigDurableFromChainDirectory(
                chainDirectory, signers, threshold),
            proofProver,
            ownsProofProver: false,
            signer,
            replayDomain,
            verificationKeyId,
            options,
            metrics,
            metricsPlugin);
    }

    /// <summary>
    /// Open SP1 recursive Gateway composition: durable SP1 aggregator/outbox +
    /// <see cref="Sp1GatewayProofProver.OpenFromChainDirectory"/> + publisher +
    /// publication profile.
    /// </summary>
    /// <param name="chainDirectory">Chain root after init-l2 / deploy-report materialization.</param>
    /// <param name="gatewayVerificationKey">Locked Gateway guest program verification key.</param>
    /// <param name="signer">L1 transaction signer for publishGatewayGlobalRoot.</param>
    /// <param name="replayDomain">Non-zero application/network replay domain.</param>
    /// <param name="verificationKeyId">
    /// Non-zero L1-bound verification key id (often the same raw key as
    /// <paramref name="gatewayVerificationKey"/>).
    /// </param>
    /// <param name="options">Optional RPC sender options.</param>
    /// <param name="resultTimeout">Optional SP1 result wait timeout.</param>
    /// <param name="pollInterval">Optional SP1 result poll interval.</param>
    /// <param name="metrics">Optional metrics sink for outbox/aggregator telemetry.</param>
    /// <param name="metricsPlugin">Optional metrics plugin for <see cref="StartMetricsHttp"/>.</param>
    public static GatewayHostComposition OpenSp1(
        string chainDirectory,
        UInt256 gatewayVerificationKey,
        INeoTransactionSigner signer,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        RpcTransactionSenderOptions? options = null,
        TimeSpan? resultTimeout = null,
        TimeSpan? pollInterval = null,
        IL2Metrics? metrics = null,
        L2MetricsPlugin? metricsPlugin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        ArgumentNullException.ThrowIfNull(gatewayVerificationKey);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(replayDomain);
        ArgumentNullException.ThrowIfNull(verificationKeyId);

        var proofProver = Sp1GatewayProofProver.OpenFromChainDirectory(
            chainDirectory,
            gatewayVerificationKey,
            resultTimeout,
            pollInterval);
        return Open(
            chainDirectory,
            L2GatewayPlugin.CreateSp1DurableFromChainDirectory(chainDirectory),
            proofProver,
            ownsProofProver: true,
            signer,
            replayDomain,
            verificationKeyId,
            options,
            metrics,
            metricsPlugin);
    }

    private static GatewayHostComposition Open(
        string chainDirectory,
        L2GatewayPlugin gateway,
        IGatewayProofProver proofProver,
        bool ownsProofProver,
        INeoTransactionSigner signer,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        RpcTransactionSenderOptions? options,
        IL2Metrics? metrics,
        L2MetricsPlugin? metricsPlugin)
    {
        ProofBoundRpcGlobalRootPublisher? publisher = null;
        try
        {
            var resolvedMetrics = metricsPlugin?.Metrics ?? metrics;
            if (resolvedMetrics is not null)
                gateway.WithMetrics(resolvedMetrics);

            var endpoints = L1DeployedEndpoints.FromChainDirectory(chainDirectory);
            publisher = ProofBoundRpcGlobalRootPublisher.OpenFromChainDirectory(
                chainDirectory,
                signer,
                options);
            gateway.ConfigureGlobalRootPublicationFromChainDirectory(
                chainDirectory,
                proofProver,
                publisher,
                replayDomain,
                verificationKeyId);
            return new GatewayHostComposition(
                Path.GetFullPath(chainDirectory),
                gateway,
                publisher,
                proofProver,
                ownsProofProver,
                resolvedMetrics,
                metricsPlugin,
                endpoints.ExpectedNetwork,
                hasL1RpcEndpoint: true,
                new UInt256(replayDomain.GetSpan()),
                new UInt256(verificationKeyId.GetSpan()));
        }
        catch
        {
            gateway.Dispose();
            publisher?.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Gateway.Dispose();
        Publisher.Dispose();
        if (OwnsProofProver && ProofProver is IDisposable disposableProver)
            disposableProver.Dispose();
        GC.SuppressFinalize(this);
    }
}
