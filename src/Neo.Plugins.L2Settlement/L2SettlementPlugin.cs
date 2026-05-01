using System;
using System.Collections.Generic;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.State;
using Neo.L2.Telemetry;

namespace Neo.Plugins.L2;

/// <summary>
/// Picks up sealed batches from <see cref="L2BatchPlugin"/>, generates the configured proof
/// (Stage 0 multisig, Stage 1 optimistic, Stage 2 ZK), and submits the resulting
/// <see cref="L2BatchCommitment"/> to NeoHub. See doc.md §15.1 (transaction flow) and §3.2
/// (SettlementManager).
/// </summary>
/// <remarks>
/// MVP scope: this plugin queues sealed batches and signs them with an in-process
/// <see cref="AttestationProver"/>. The actual L1 RPC submission step is delegated to
/// <see cref="ISettlementClient"/>, which production deployments wire to an
/// <c>RpcClient</c>-backed implementation.
/// </remarks>
public sealed class L2SettlementPlugin : Plugin
{
    private L2SettlementSettings _settings = new();
    private readonly Queue<L2BatchCommitment> _pending = new();
    private IL2Prover? _prover;
    private ISettlementClient? _client;
    private L2BatchPlugin? _batchPlugin;
    private IL2Metrics _metrics = NoOpMetrics.Instance;

    /// <inheritdoc />
    public override string Name => "L2SettlementPlugin";

    /// <inheritdoc />
    public override string Description => "Signs and submits sealed L2 batches to NeoHub.";

    /// <inheritdoc />
    protected override void Configure()
    {
        _settings = L2SettlementSettings.From(GetConfiguration());
    }

    /// <summary>
    /// Wire the plugin to its prover, settlement client, and the batcher whose sealed batches
    /// it consumes. Called by the host after all plugins have been instantiated.
    /// </summary>
    public void Wire(L2BatchPlugin batchPlugin, IL2Prover prover, ISettlementClient client)
    {
        ArgumentNullException.ThrowIfNull(batchPlugin);
        ArgumentNullException.ThrowIfNull(prover);
        ArgumentNullException.ThrowIfNull(client);
        _batchPlugin = batchPlugin;
        _prover = prover;
        _client = client;
        _batchPlugin.OnBatchSealed += OnBatchSealed;
    }

    /// <summary>
    /// Wire a metrics sink. The plugin emits <c>l2.settlement.submitted</c>,
    /// <c>l2.settlement.submit_failures</c>, <c>l2.settlement.submit_latency_ms</c>,
    /// <c>l2.proving.generated</c> (tagged by kind), and <c>l2.proving.latency_ms</c> against
    /// this sink. Call before the first batch arrives; defaults to <see cref="NoOpMetrics"/>.
    /// </summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_batchPlugin is not null)
            _batchPlugin.OnBatchSealed -= OnBatchSealed;
    }

    /// <summary>How many batches are currently queued for submission.</summary>
    public int PendingCount
    {
        get { lock (_pending) return _pending.Count; }
    }

    /// <summary>
    /// Enqueue a sealed batch for submission. The hot path goes through this on every
    /// <see cref="L2BatchPlugin.OnBatchSealed"/> event, but operators can also call it
    /// directly to backfill a missed batch (e.g. from a node restart).
    /// </summary>
    public void Enqueue(L2BatchCommitment commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        if (!_settings.Enabled) return;
        lock (_pending) _pending.Enqueue(commitment);
    }

    private void OnBatchSealed(object? sender, L2BatchCommitment commitment)
    {
        Enqueue(commitment);
        _ = SubmitNextAsync();
    }

    /// <summary>Drain one pending batch (best-effort — exceptions are logged, not surfaced).</summary>
    public async System.Threading.Tasks.Task SubmitNextAsync()
    {
        // Check wiring BEFORE dequeue — otherwise an un-wired plugin silently drains items
        // into the void. The metric path also wouldn't tag this as a failure (we'd just
        // exit). Items stay queued until Wire() is called.
        if (_prover is null || _client is null) return;

        L2BatchCommitment? next;
        lock (_pending)
        {
            if (_pending.Count == 0) return;
            next = _pending.Dequeue();
        }

        try
        {
            var publicInputs = BuildPublicInputs(next);
            var hash = StateRootCalculator.HashPublicInputs(publicInputs);

            var proveSw = System.Diagnostics.Stopwatch.StartNew();
            var proofResult = await _prover.ProveAsync(new ProofRequest
            {
                PublicInputs = publicInputs,
                Witness = ReadOnlyMemory<byte>.Empty,
                Kind = (ProofType)_settings.ProofType,
            });
            proveSw.Stop();
            var kindTag = ("kind", proofResult.Kind.ToString());
            _metrics.IncrementCounter(MetricNames.ProofsGenerated, 1, kindTag);
            _metrics.RecordHistogram(MetricNames.ProveLatencyMs, proveSw.Elapsed.TotalMilliseconds, kindTag);

            var finalCommitment = next with
            {
                ProofType = proofResult.Kind,
                Proof = proofResult.Proof,
                PublicInputHash = hash,
            };

            var submitSw = System.Diagnostics.Stopwatch.StartNew();
            await _client.SubmitBatchAsync(finalCommitment, publicInputs);
            submitSw.Stop();
            _metrics.IncrementCounter(MetricNames.BatchesSubmitted);
            _metrics.RecordHistogram(MetricNames.SubmitLatencyMs, submitSw.Elapsed.TotalMilliseconds);
        }
        catch (Exception)
        {
            _metrics.IncrementCounter(MetricNames.SubmitFailures);
            // Re-queue at the head so we retry. Production handler logs and bumps a metric.
            lock (_pending)
            {
                var rest = _pending.ToArray();
                _pending.Clear();
                _pending.Enqueue(next);
                foreach (var b in rest) _pending.Enqueue(b);
            }
        }
    }

    private static PublicInputs BuildPublicInputs(L2BatchCommitment c)
    {
        return new PublicInputs
        {
            ChainId = c.ChainId,
            BatchNumber = c.BatchNumber,
            PreStateRoot = c.PreStateRoot,
            PostStateRoot = c.PostStateRoot,
            TxRoot = c.TxRoot,
            ReceiptRoot = c.ReceiptRoot,
            WithdrawalRoot = c.WithdrawalRoot,
            L2ToL1MessageRoot = c.L2ToL1MessageRoot,
            L2ToL2MessageRoot = c.L2ToL2MessageRoot,
            L1MessageHash = UInt256.Zero,
            DACommitment = c.DACommitment,
            BlockContextHash = UInt256.Zero,
        };
    }
}
