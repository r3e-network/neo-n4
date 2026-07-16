using Microsoft.Extensions.Configuration;
using Neo.L2;
using Neo.L2.Proving.Attestation;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2Gateway;

/// <summary>
/// Phase-5 Gateway plugin that durably aggregates finalized batches, creates a terminal proof over
/// the complete publication statement, and publishes the global root through a reconciled L1 adapter.
/// </summary>
/// <remarks>
/// See doc.md §4 (Neo Gateway). Production publication is fail-closed until an explicit
/// <see cref="PersistentGatewayOutbox"/>, production aggregator, terminal prover, and proof-bound
/// publisher are configured. A failed proof or publish remains the sole durable publication and is
/// retried with the same epoch, statement, and proof bytes. Automatic retries are bounded; a
/// poisoned publication requires an explicit operator recovery call.
/// </remarks>
public sealed class L2GatewayPlugin : Plugin
{
    private const int DefaultMaxAutomaticRetries = 3;
    private const int MaxTerminalProofBytes = 1 * 1024 * 1024;
    private const string QueueDepthMetric = "l2.gateway.outbox.queue_depth";
    private const string RetryMetric = "l2.gateway.outbox.retries";
    private const string PoisonMetric = "l2.gateway.outbox.poisoned";
    private const string ConfirmationLagMetric = "l2.gateway.outbox.confirmation_lag_ms";
    private readonly Lock _stateGate = new();
    private readonly SemaphoreSlim _publicationGate = new(1, 1);
    private volatile IGatewayAggregator _aggregator = new PassThroughAggregator();
    private volatile IGlobalRootPublisher _legacyGlobalRootPublisher = new NoOpGlobalRootPublisher();
    private ReadOnlyMemory<byte> _legacyVerificationKeyId = ReadOnlyMemory<byte>.Empty;
    private PersistentGatewayOutbox? _outbox;
    private bool _ownsOutbox;
    private GatewayPublicationCheckpoint? _recoveredPublication;
    private PublicationProfile? _publicationProfile;
    private PublicationAttempt? _pendingPublication;
    private IL2Metrics _metrics = NoOpMetrics.Instance;
    private int _maxAutomaticRetries = DefaultMaxAutomaticRetries;
    private bool _publicationInProgress;
    private bool _enabled = true;

    private sealed record PublicationProfile(
        IGatewayProofProver ProofProver,
        IProofBoundGlobalRootPublisher Publisher,
        UInt160 MessageRouter,
        UInt256 ReplayDomain,
        UInt256 VerificationKeyId);

    private sealed class PublicationAttempt(
        ulong batchEpoch,
        AggregatedCommitment commitment,
        PublicationProfile profile,
        long startedAtUnixMilliseconds)
    {
        public ulong BatchEpoch { get; } = batchEpoch;
        public AggregatedCommitment Commitment { get; } = commitment;
        public PublicationProfile Profile { get; } = profile;
        public long StartedAtUnixMilliseconds { get; } = startedAtUnixMilliseconds;
        public GatewayProofBinding? Binding { get; set; }
        public ReadOnlyMemory<byte> Proof { get; set; }
        public GatewayOutboxState State { get; set; } = GatewayOutboxState.Proving;
        public int RetryCount { get; set; }
        public string? LastError { get; set; }

        public GatewayPublicationCheckpoint ToCheckpoint() => new()
        {
            BatchEpoch = BatchEpoch,
            Commitment = Commitment,
            Binding = Binding ?? throw new InvalidOperationException("Gateway binding is not initialized"),
            Proof = Proof.ToArray(),
            State = State,
            RetryCount = RetryCount,
            StartedAtUnixMilliseconds = StartedAtUnixMilliseconds,
            LastError = LastError,
        };

        public static PublicationAttempt FromCheckpoint(
            GatewayPublicationCheckpoint checkpoint,
            PublicationProfile profile) => new(
                checkpoint.BatchEpoch,
                checkpoint.Commitment,
                profile,
                checkpoint.StartedAtUnixMilliseconds)
            {
                Binding = checkpoint.Binding,
                Proof = checkpoint.Proof.ToArray(),
                State = checkpoint.State,
                RetryCount = checkpoint.RetryCount,
                LastError = checkpoint.LastError,
            };
    }

    /// <inheritdoc />
    public override string Name => "L2GatewayPlugin";

    /// <inheritdoc />
    public override string Description =>
        "Durably aggregates finalized L2 batches and publishes proof-bound Gateway global roots.";

    /// <summary>Construct with default settings (enabled, 3 automatic retries).</summary>
    public L2GatewayPlugin()
    {
    }

    internal L2GatewayPlugin(L2GatewaySettings settings) : this()
    {
        ArgumentNullException.ThrowIfNull(settings);
        _enabled = settings.Enabled;
        _maxAutomaticRetries = settings.MaxAutomaticRetries;
    }

    /// <summary>
    /// Host composition factory: load Enabled/MaxAutomaticRetries from a chain directory
    /// without attaching the durable outbox yet.
    /// </summary>
    /// <remarks>
    /// Production order is fail-closed and intentional:
    /// <list type="number">
    ///   <item><description><see cref="CreateFromChainDirectory"/></description></item>
    ///   <item><description><see cref="UseAggregator"/> (must precede outbox)</description></item>
    ///   <item><description><see cref="AttachOutboxFromChainDirectory"/> or
    ///   <see cref="UsePersistentOutbox"/></description></item>
    ///   <item><description><see cref="ConfigureGlobalRootPublication"/></description></item>
    /// </list>
    /// Attaching the outbox first would freeze the default pass-through aggregator and block
    /// production <see cref="UseAggregator"/> (outbox rehydrate submits into the active aggregator).
    /// Prefer <see cref="CreateDurableFromChainDirectory"/> when the aggregator is known.
    /// </remarks>
    public static L2GatewayPlugin CreateFromChainDirectory(string chainDirectory)
    {
        var settings = L2GatewaySettings.FromChainDirectory(chainDirectory);
        return new L2GatewayPlugin(settings);
    }

    /// <summary>
    /// Host composition: settings + production aggregator + durable outbox under
    /// <c>data/gateway/outbox</c>. Call <see cref="ConfigureGlobalRootPublication"/> next.
    /// </summary>
    public static L2GatewayPlugin CreateDurableFromChainDirectory(
        string chainDirectory,
        IGatewayAggregator aggregator)
    {
        ArgumentNullException.ThrowIfNull(aggregator);
        var plugin = CreateFromChainDirectory(chainDirectory);
        plugin.UseAggregator(aggregator);
        plugin.AttachOutboxFromChainDirectory(chainDirectory);
        return plugin;
    }

    /// <summary>
    /// Host composition: durable Gateway with
    /// <see cref="BinaryTreeAggregator"/> + <see cref="MerklePathRoundProver"/>
    /// (no HSM/toolchain). Call <see cref="ConfigureGlobalRootPublication"/> next.
    /// </summary>
    /// <remarks>
    /// Local Multisig/dev hosts use this when every batch's inclusion in the aggregate
    /// root must be independently provable without a recursive-ZK prover. Terminal L1
    /// publication still requires an <see cref="IGatewayProofProver"/> and
    /// <see cref="IProofBoundGlobalRootPublisher"/> (see
    /// <c>ProofBoundRpcGlobalRootPublisher.OpenFromChainDirectory</c>).
    /// </remarks>
    public static L2GatewayPlugin CreateMerkleDurableFromChainDirectory(string chainDirectory)
        => CreateDurableFromChainDirectory(
            chainDirectory,
            new BinaryTreeAggregator(new MerklePathRoundProver()));

    /// <summary>
    /// Host composition: durable Gateway with
    /// <see cref="BinaryTreeAggregator"/> + <see cref="MultisigRoundProver"/> over the
    /// operator-supplied <see cref="ISignerSet"/>. Call
    /// <see cref="ConfigureGlobalRootPublication"/> next.
    /// </summary>
    /// <remarks>
    /// Production HSM/KMS adapters implement <see cref="ISignerSet"/>;
    /// <see cref="InMemorySignerSet"/> is for tests/devnet only.
    /// </remarks>
    public static L2GatewayPlugin CreateMultisigDurableFromChainDirectory(
        string chainDirectory,
        ISignerSet signers,
        int threshold)
    {
        ArgumentNullException.ThrowIfNull(signers);
        return CreateDurableFromChainDirectory(
            chainDirectory,
            new BinaryTreeAggregator(new MultisigRoundProver(signers, threshold)));
    }

    /// <summary>
    /// Attach <see cref="PersistentGatewayOutbox"/> at
    /// <c>data/gateway/outbox</c> after <see cref="UseAggregator"/>.
    /// </summary>
    public void AttachOutboxFromChainDirectory(string chainDirectory)
    {
        UsePersistentOutbox(
            PersistentGatewayOutbox.OpenFromChainDirectory(chainDirectory),
            ownsOutbox: true);
    }

    /// <summary>Loaded gateway settings (host composition / tests).</summary>
    internal L2GatewaySettings Settings => new()
    {
        Enabled = _enabled,
        MaxAutomaticRetries = _maxAutomaticRetries,
    };

    /// <summary>True when a durable outbox is attached.</summary>
    internal bool HasPersistentOutbox => _outbox is not null;

    /// <summary>
    /// Replace the active aggregator before a durable outbox or any Gateway work is attached.
    /// </summary>
    public void UseAggregator(IGatewayAggregator aggregator)
    {
        ArgumentNullException.ThrowIfNull(aggregator);
        lock (_stateGate)
        {
            if (_publicationInProgress
                || _pendingPublication is not null
                || _recoveredPublication is not null
                || _aggregator.PendingCount != 0
                || _outbox is not null)
            {
                throw new InvalidOperationException("cannot replace aggregator while Gateway work or outbox is attached");
            }
            _aggregator = aggregator;
        }
    }

    /// <summary>The currently active aggregator.</summary>
    public IGatewayAggregator Aggregator => _aggregator;

    /// <summary>
    /// Attach the crash-safe outbox and rehydrate every sealed or unconfirmed item. Call after
    /// selecting the aggregator and before accepting batches or configuring production publication.
    /// </summary>
    public void UsePersistentOutbox(PersistentGatewayOutbox outbox, bool ownsOutbox = false)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        lock (_stateGate)
        {
            if (_outbox is not null)
                throw new InvalidOperationException("Gateway persistent outbox is already configured");
            if (_publicationInProgress
                || _publicationProfile is not null
                || _pendingPublication is not null
                || _recoveredPublication is not null
                || _aggregator.PendingCount != 0)
            {
                throw new InvalidOperationException("attach Gateway outbox before accepting or publishing work");
            }

            var recovery = outbox.Recover();
            foreach (var commitment in recovery.Sealed) _aggregator.Submit(commitment);
            _outbox = outbox;
            _ownsOutbox = ownsOutbox;
            _recoveredPublication = recovery.Publication;
        }
        EmitOutboxMetrics();
    }

    /// <summary>Wire the metrics sink for durable queue and retry telemetry.</summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
        if (_aggregator is BinaryTreeAggregator binary) binary.WithMetrics(metrics);
        EmitOutboxMetrics();
    }

    /// <summary>
    /// Configure the fail-closed production publication profile. The active aggregator and proving
    /// circuit must use the same non-pass-through backend, and a persistent outbox is mandatory.
    /// </summary>
    public void ConfigureGlobalRootPublication(
        IGatewayProofProver proofProver,
        IProofBoundGlobalRootPublisher publisher,
        UInt160 messageRouter,
        UInt256 replayDomain,
        UInt256 verificationKeyId,
        int? maxAutomaticRetries = null)
    {
        ArgumentNullException.ThrowIfNull(proofProver);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(messageRouter);
        ArgumentNullException.ThrowIfNull(replayDomain);
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        if (proofProver.ProofSystem is < 1 or > 4)
            throw new ArgumentException("proof prover must expose proofSystem 1..4", nameof(proofProver));
        if (!GatewayProofBindingSerializer.IsProductionAggregationBackend(
            proofProver.AggregationBackendId))
        {
            throw new ArgumentException(
                "pass-through/reserved aggregation backend is not publishable",
                nameof(proofProver));
        }
        if (messageRouter.Equals(UInt160.Zero))
            throw new ArgumentException("messageRouter must be non-zero", nameof(messageRouter));
        if (replayDomain.Equals(UInt256.Zero))
            throw new ArgumentException("replayDomain must be non-zero", nameof(replayDomain));
        if (verificationKeyId.Equals(UInt256.Zero))
            throw new ArgumentException("verificationKeyId must be non-zero", nameof(verificationKeyId));
        if (maxAutomaticRetries is < 1 or > 100)
            throw new ArgumentOutOfRangeException(
                nameof(maxAutomaticRetries),
                "maxAutomaticRetries must be in [1, 100]");

        lock (_stateGate)
        {
            if (_publicationInProgress || _publicationProfile is not null)
                throw new InvalidOperationException("cannot reconfigure an active production Gateway");
            if (_outbox is null)
                throw new InvalidOperationException("production Gateway requires PersistentGatewayOutbox");
            if (!GatewayProofBindingSerializer.IsProductionAggregationBackend(_aggregator.BackendId))
                throw new InvalidOperationException("active aggregator uses a pass-through/reserved backend");
            if (_aggregator.BackendId != proofProver.AggregationBackendId)
            {
                throw new InvalidOperationException(
                    $"aggregator backend {_aggregator.BackendId} does not match proving circuit " +
                    $"backend {proofProver.AggregationBackendId}");
            }

            var profile = new PublicationProfile(
                proofProver,
                publisher,
                messageRouter,
                replayDomain,
                verificationKeyId);
            if (_recoveredPublication is not null)
            {
                ValidateRecoveredProfile(_recoveredPublication, profile);
                _pendingPublication = PublicationAttempt.FromCheckpoint(
                    _recoveredPublication,
                    profile);
                _recoveredPublication = null;
            }
            if (maxAutomaticRetries is not null)
                _maxAutomaticRetries = maxAutomaticRetries.Value;
            _publicationProfile = profile;
        }
        EmitOutboxMetrics();
    }

    /// <summary>True when an unconfirmed publication remains retryable or poisoned.</summary>
    public bool HasPendingPublication
    {
        get
        {
            lock (_stateGate)
                return _pendingPublication is not null || _recoveredPublication is not null;
        }
    }

    /// <summary>Pending epoch, or <c>null</c> when no publication awaits confirmation.</summary>
    public ulong? PendingPublicationEpoch
    {
        get
        {
            lock (_stateGate)
                return _pendingPublication?.BatchEpoch ?? _recoveredPublication?.BatchEpoch;
        }
    }

    /// <summary>Current durable queue, retry, poison, and confirmation-lag state.</summary>
    public GatewayOutboxStatus OutboxStatus
    {
        get
        {
            lock (_stateGate)
            {
                if (_outbox is not null) return _outbox.GetStatus();
                return new GatewayOutboxStatus
                {
                    QueueDepth = _aggregator.PendingCount,
                    PublicationState = null,
                    RetryCount = 0,
                    LastError = null,
                    ConfirmationLagMilliseconds = 0,
                };
            }
        }
    }

    /// <summary>Forward a finalized per-chain batch into the active aggregator.</summary>
    public void ReceiveBatch(L2BatchCommitment commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        lock (_stateGate)
        {
            if (!_enabled) return;
            if (_outbox is not null && !_outbox.Enqueue(commitment)) return;
            _aggregator.Submit(commitment);
        }
        EmitOutboxMetrics();
    }

    /// <summary>
    /// Produce an aggregate for dev/test inspection. Disabled after production publication is
    /// configured because direct draining would bypass the retryable publication outbox.
    /// </summary>
    public AggregatedCommitment? PullAggregate()
    {
        lock (_stateGate)
        {
            if (_publicationProfile is not null || _outbox is not null)
                throw new InvalidOperationException("direct aggregate pulls bypass the durable publication path");
        }
        return _aggregator.Aggregate();
    }

    /// <summary>
    /// Aggregate, prove, publish, and confirm the next Gateway epoch. Failures retain the exact
    /// durable publication; retry with the same epoch until it is reconciled on L1.
    /// </summary>
    public async ValueTask<UInt256> PublishAggregateAsync(
        ulong batchEpoch,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled) return UInt256.Zero;
        await _publicationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PublicationAttempt? attempt;
            PublicationProfile profile;
            PersistentGatewayOutbox outbox;
            lock (_stateGate)
            {
                profile = _publicationProfile
                    ?? throw new InvalidOperationException("global-root publication is not configured");
                outbox = _outbox
                    ?? throw new InvalidOperationException("Gateway persistent outbox is not configured");
                attempt = _pendingPublication;
                if (attempt is not null && attempt.BatchEpoch != batchEpoch)
                {
                    throw new InvalidOperationException(
                        $"epoch {attempt.BatchEpoch} is still pending; retry it before epoch {batchEpoch}");
                }
                if (attempt?.State == GatewayOutboxState.Poisoned)
                {
                    throw new InvalidOperationException(
                        $"epoch {attempt.BatchEpoch} is poisoned after {attempt.RetryCount} failures; " +
                        "inspect OutboxStatus and call RecoverPoisonedPublication after remediation");
                }
                _publicationInProgress = true;
            }

            if (attempt is null)
            {
                var commitment = _aggregator.Aggregate();
                if (commitment is null) return UInt256.Zero;
                attempt = new PublicationAttempt(
                    batchEpoch,
                    commitment,
                    profile,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                attempt.Binding = GatewayProofBindingSerializer.Create(
                    profile.MessageRouter,
                    profile.ReplayDomain,
                    batchEpoch,
                    commitment,
                    profile.ProofProver.ProofSystem,
                    profile.VerificationKeyId);
                lock (_stateGate) _pendingPublication = attempt;
                outbox.SavePublication(attempt.ToCheckpoint());
                EmitOutboxMetrics();
            }

            try
            {
                attempt.Binding ??= GatewayProofBindingSerializer.Create(
                    attempt.Profile.MessageRouter,
                    attempt.Profile.ReplayDomain,
                    attempt.BatchEpoch,
                    attempt.Commitment,
                    attempt.Profile.ProofProver.ProofSystem,
                    attempt.Profile.VerificationKeyId);

                if (attempt.Proof.IsEmpty)
                {
                    var proof = await attempt.Profile.ProofProver.ProveAsync(
                        attempt.Binding,
                        attempt.Commitment,
                        cancellationToken).ConfigureAwait(false);
                    if (proof.IsEmpty)
                        throw new InvalidOperationException("gateway proof prover returned an empty proof");
                    if (proof.Length > MaxTerminalProofBytes)
                        throw new InvalidOperationException("gateway proof prover returned more than 1 MiB");
                    attempt.Proof = proof.ToArray();
                    attempt.State = GatewayOutboxState.Proved;
                    attempt.LastError = null;
                    outbox.SavePublication(attempt.ToCheckpoint());
                    EmitOutboxMetrics();
                }

                attempt.State = GatewayOutboxState.Submitted;
                outbox.SavePublication(attempt.ToCheckpoint());
                EmitOutboxMetrics();
                var transactionHash = await attempt.Profile.Publisher.PublishGlobalRootAsync(
                    attempt.Binding,
                    attempt.Commitment,
                    attempt.Proof,
                    cancellationToken).ConfigureAwait(false);

                var confirmed = attempt.ToCheckpoint();
                outbox.MarkConfirmed(confirmed);
                _metrics.SafeRecordHistogram(
                    ConfirmationLagMetric,
                    Math.Max(
                        0,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        - attempt.StartedAtUnixMilliseconds));
                lock (_stateGate)
                {
                    if (ReferenceEquals(_pendingPublication, attempt)) _pendingPublication = null;
                }
                EmitOutboxMetrics();
                return transactionHash;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                RecordFailure(attempt, exception, outbox);
                throw;
            }
        }
        finally
        {
            lock (_stateGate) _publicationInProgress = false;
            _publicationGate.Release();
        }
    }

    /// <summary>
    /// Reset a poisoned publication after the operator has corrected the prover, publisher, or L1
    /// connectivity. The exact epoch, aggregate, binding, and any successful proof are retained.
    /// </summary>
    public void RecoverPoisonedPublication()
    {
        lock (_stateGate)
        {
            if (_publicationInProgress)
                throw new InvalidOperationException("cannot recover while publication is in progress");
            var attempt = _pendingPublication
                ?? throw new InvalidOperationException("no pending Gateway publication");
            if (attempt.State != GatewayOutboxState.Poisoned)
                throw new InvalidOperationException("pending Gateway publication is not poisoned");
            var outbox = _outbox
                ?? throw new InvalidOperationException("Gateway persistent outbox is not configured");
            attempt.State = attempt.Proof.IsEmpty
                ? GatewayOutboxState.Proving
                : GatewayOutboxState.Proved;
            attempt.RetryCount = 0;
            attempt.LastError = null;
            outbox.SavePublication(attempt.ToCheckpoint());
        }
        EmitOutboxMetrics();
    }

    private void RecordFailure(
        PublicationAttempt attempt,
        Exception exception,
        PersistentGatewayOutbox outbox)
    {
        attempt.RetryCount = checked(attempt.RetryCount + 1);
        attempt.LastError = exception.Message;
        if (attempt.RetryCount >= _maxAutomaticRetries)
            attempt.State = GatewayOutboxState.Poisoned;
        outbox.SavePublication(attempt.ToCheckpoint());
        _metrics.SafeIncrementCounter(RetryMetric);
        EmitOutboxMetrics();
    }

    private static void ValidateRecoveredProfile(
        GatewayPublicationCheckpoint checkpoint,
        PublicationProfile profile)
    {
        var binding = checkpoint.Binding;
        if (!binding.MessageRouter.Equals(profile.MessageRouter)
            || !binding.ReplayDomain.Equals(profile.ReplayDomain)
            || !binding.VerificationKeyId.Equals(profile.VerificationKeyId)
            || binding.ProofSystem != profile.ProofProver.ProofSystem
            || binding.AggregationBackendId != profile.ProofProver.AggregationBackendId
            || checkpoint.Commitment.BackendId != profile.ProofProver.AggregationBackendId)
        {
            throw new InvalidOperationException(
                "recovered Gateway publication does not match the configured router/domain/backend/proof/vkey profile");
        }
    }

    private void EmitOutboxMetrics()
    {
        var status = OutboxStatus;
        _metrics.SafeSetGauge(QueueDepthMetric, status.QueueDepth);
        _metrics.SafeSetGauge(
            PoisonMetric,
            status.PublicationState == GatewayOutboxState.Poisoned ? 1 : 0);
    }

    /// <summary>Legacy publisher seam retained for source compatibility; not used by production.</summary>
    public void UseGlobalRootPublisher(IGlobalRootPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        _legacyGlobalRootPublisher = publisher;
    }

    /// <summary>Legacy publisher seam retained for source compatibility.</summary>
    public IGlobalRootPublisher GlobalRootPublisher => _legacyGlobalRootPublisher;

    /// <summary>Legacy verification-key seam retained for source compatibility.</summary>
    public ReadOnlyMemory<byte> GlobalRootVerificationKeyId => _legacyVerificationKeyId;

    /// <summary>Set the legacy 32-byte key id without enabling production publication.</summary>
    public void SetGlobalRootVerificationKeyId(ReadOnlyMemory<byte> verificationKeyId)
    {
        if (verificationKeyId.Length != 32)
            throw new ArgumentException("verificationKeyId must be 32 bytes", nameof(verificationKeyId));
        _legacyVerificationKeyId = verificationKeyId.ToArray();
    }

    /// <inheritdoc />
    protected override void Configure()
    {
        var section = GetConfiguration();
        _enabled = section.GetValue("Enabled", true);
        _maxAutomaticRetries = section.GetValue(
            "MaxAutomaticRetries",
            DefaultMaxAutomaticRetries);
        if (_maxAutomaticRetries is < 1 or > 100)
            throw new InvalidOperationException("MaxAutomaticRetries must be in [1, 100]");
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _publicationGate.Dispose();
            if (_ownsOutbox) _outbox?.Dispose();
        }
        base.Dispose(disposing);
    }
}
