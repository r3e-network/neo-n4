# Telemetry

The `Neo.L2.Telemetry` library exposes metrics from every L2 plugin in canonical
form, snapshots them on demand, renders them as Prometheus exposition text, and
serves them over HTTP. Drop-in replacement for `prometheus-net` without the
package dependency.

## Stack

```
plugins emit ──> IL2Metrics ──> InMemoryMetrics ──> Snapshot()
                                                      │
                                                      ▼
                                              PrometheusExporter
                                                      │
                                                      ▼
                                            MetricsRequestHandler
                                                      │
                                                      ▼
                                              MetricsHttpServer
                                                      │
                                                      ▼
                                          GET http://node/metrics
```

Every layer is independently swappable: `IL2Metrics` defaults to `NoOpMetrics`,
the snapshot is a frozen `MetricsSnapshot`, the request handler is framework
agnostic (drop into ASP.NET / Kestrel / RpcServer), and the HTTP server uses raw
sockets — no third-party deps. Concurrent scrapes are hard-capped (default 32;
`L2Metrics.MaxConcurrentConnections`); excess clients receive HTTP 503 instead
of unbounded handler tasks.

## Try it locally

```bash
# Run a 3-batch devnet and self-scrape /metrics + /healthz + /readyz over real HTTP.
dotnet run --project tools/Neo.L2.Devnet -- 3 --metrics-port 9090
```

The trailing `───── live HTTP /metrics ─────` section shows the round-trip:
status code, Content-Type, body length, and the first few lines of Prometheus
exposition. Port 0 lets the OS pick any free port.

## Endpoints

The HTTP handler answers three paths (everything else is 404):

| Path | Status | Description |
|---|---|---|
| `GET /metrics` | 200 | Prometheus exposition text |
| `GET /healthz` | 200 | Process liveness — always 200 if the server is responding |
| `GET /readyz` | 200 / 503 | Wired predicate; 200 when no predicate is supplied |

`/healthz` and `/readyz` follow the Kubernetes naming convention. Use
`/healthz` for liveness (restart on failure) and `/readyz` for readiness
(remove from load balancer on failure). For example:

```csharp
var handler = new MetricsRequestHandler(metrics, readinessCheck: () =>
    settlementClient.LastCanonicalBatchAge < TimeSpan.FromMinutes(2));
```

## Wiring a node

The `Neo.Plugins.L2Metrics` plugin is the composition root — it owns the
shared sink and the HTTP server.

```csharp
using Neo.Plugins.L2;

// 1. The composition root.
var metrics = new L2MetricsPlugin();

// 2. Other plugins pull metrics.Metrics for their WithMetrics() call.
var batchPlugin = new L2BatchPlugin();
batchPlugin.WithMetrics(metrics.Metrics);

var settlementPlugin = new L2SettlementPlugin();
settlementPlugin.Wire(batchPlugin, prover, settlementClient);
settlementPlugin.WithMetrics(metrics.Metrics);

var daPlugin = new L2DAPlugin();
daPlugin.WithMetrics(metrics.Metrics);

var bridgePlugin = new L2BridgePlugin();
bridgePlugin.WithMetrics(metrics.Metrics);

// 3. Optional: gate /readyz on a real predicate.
metrics.WithReadinessCheck(() => settlementClient.LastFinalizedBatchAge < TimeSpan.FromMinutes(2));

// 4. Start the HTTP server (after Configure() has loaded settings).
metrics.Start();

// curl http://127.0.0.1:9090/metrics
```

Plugin config (in `Neo.Plugins.L2Metrics/config.json`):

```json
{
  "PluginConfiguration": {
    "Enabled": true,
    "BindAddress": "127.0.0.1",
    "Port": 9090
  }
}
```

## Metric catalog

Every metric below has an entry in `MetricCatalog.Descriptions`. Adding a new
metric requires adding both the `MetricNames` constant and the catalog entry —
the test suite enforces this via reflection.

### Batch (`Neo.Plugins.L2Batch`)

- `l2.batch.sealed` — counter — Number of L2 batches sealed by the local sequencer
- `l2.batch.seal_latency_ms` — histogram — Wall-clock milliseconds spent sealing each batch
- `l2.batch.tx_count` — gauge — Transactions in the most recently sealed batch
- `l2.batch.subscriber_failures` — counter — Per-subscriber failures dispatching `OnBatchSealed` (one buggy listener can't destabilize block import)

### Settlement (`Neo.Plugins.L2Settlement`)

- `l2.settlement.submitted` — counter — Batches submitted to NeoHub successfully
- `l2.settlement.submit_failures` — counter — Settlement reconciliation runs that surfaced an exception
- `l2.settlement.submit_latency_ms` — histogram — Round-trip wall-clock milliseconds for SubmitBatch
- `l2.settlement.pending` — gauge — Canonical artifacts not fully reconciled on L1
- `l2.settlement.confirmation_lag_batches` — gauge — Canonical batches not yet observed through the L1 settlement lifecycle; derived from durable manifests so it survives restarts
- `l2.settlement.retries` — counter — Failed durable reconciliation attempts
- `l2.settlement.poisoned` — gauge — `1` when the ordered settlement head requires explicit operator recovery

### Proving (emitted by Settlement plugin)

- `l2.proving.generated` — counter — `kind` — Proofs generated for sealed batches
- `l2.proving.latency_ms` — histogram — `kind` — Wall-clock milliseconds spent generating each proof
- `l2.proving.rejected` — counter — — — Proofs the local verifier rejected before submission

### Bridge (`Neo.L2.Bridge.{Deposit,Withdrawal}Processor`)

- `l2.bridge.deposits` — counter — Deposits processed from the L1 bridge inbox
- `l2.bridge.deposits_rejected` — counter — Deposits rejected by validation (replay / unknown asset / inactive mapping)
- `l2.bridge.withdrawals` — counter — Withdrawals staged into the L2 outbox
- `l2.bridge.withdrawals_rejected` — counter — Withdrawals rejected by validation (unknown asset / duplicate nonce / non-positive amount)

### DA (`Neo.Plugins.L2DA`)

The `MetricsEmittingDAWriter` decorator wraps every `IDAWriter` so any DA
backend automatically participates.

- `l2.da.published` — counter — `mode` — DA payloads published successfully
- `l2.da.publish_latency_ms` — histogram — `mode` — Wall-clock milliseconds for each DA publish
- `l2.da.publish_failures` — counter — `mode` — DA publishes that threw

### RPC (`Neo.Plugins.L2Rpc.L2RpcMethods`)

- `l2.rpc.calls` — counter — `method` — L2 RPC method calls
- `l2.rpc.latency_ms` — histogram — `method` — Wall-clock milliseconds for each L2 RPC call
- `l2.rpc.failures` — counter — `method` — L2 RPC calls that threw

### Gateway (`Neo.Plugins.L2Gateway.BinaryTreeAggregator`)

- `l2.gateway.aggregations` — counter — Aggregations performed by the local gateway
- `l2.gateway.batches_aggregated` — counter — Constituent batches folded into aggregations (incremented by N)
- `l2.gateway.aggregation_rounds` — histogram — Rounds of pairwise reduction (= log₂ N)
- `l2.gateway.aggregation_latency_ms` — histogram — Wall-clock milliseconds spent in each aggregation

### Forced inclusion / censorship / challenge

- `l2.forced_inclusion.observed` — counter — Forced-inclusion entries observed by this node
- `l2.censorship.reports` — counter — Censorship reports the detector emitted
- `l2.challenge.fraud_proofs` — counter — Fraud proofs the orchestrator emitted
- `l2.challenge.bisection_rounds` — histogram — Bisection rounds taken to settle each fraud dispute

### Sequencer (`Neo.L2.Sequencer.InMemorySequencerCommitteeProvider`)

- `l2.sequencer.registered` — counter — Sequencers added to the committee
- `l2.sequencer.exits_started` — counter — Times a sequencer entered the exit window
- `l2.sequencer.exits_finalized` — counter — Times a sequencer was permanently removed after their window expired
- `l2.sequencer.committee_size` — gauge — Current number of committee members (post-mutation)

### Messaging (`Neo.L2.Messaging.L2Outbox`)

- `l2.messaging.emitted` — counter — Cross-chain messages emitted by the local L2 (target-chain-tagged)

### Audit

- `l2.audit.runs` — counter — Times the chain auditor ran
- `l2.audit.failures` — counter — Audit findings that failed the audit

## Prometheus rendering

Names are mapped 1:1 with `.` and `-` rewritten to `_`. Counters get the
`_total` suffix and `counter` type. Gauges stay as-is with `gauge` type.
Histograms render as `summary` with `_count`, `_sum`, and `_max` aggregates —
no quantile buckets in the in-process exporter. For quantile buckets, wire an
OpenTelemetry exporter against `IL2Metrics` instead.

```
# HELP l2_batch_sealed_total Number of L2 batches sealed by the local sequencer
# TYPE l2_batch_sealed_total counter
l2_batch_sealed_total 4

# HELP l2_proving_generated_total Proofs generated for sealed batches, tagged by proof kind
# TYPE l2_proving_generated_total counter
l2_proving_generated_total{kind="Multisig"} 4

# HELP l2_batch_seal_latency_ms Wall-clock milliseconds spent sealing each batch
# TYPE l2_batch_seal_latency_ms summary
l2_batch_seal_latency_ms_count 4
l2_batch_seal_latency_ms_sum 8.45
l2_batch_seal_latency_ms_max 3.21
```

## Adding a new metric

1. Add a constant to `MetricNames` (e.g. `public const string FooBar = "l2.foo.bar";`).
2. Add a description to `MetricCatalog.Descriptions`.
   The reflection-based completeness test in `Neo.L2.Telemetry.UnitTests`
   fails the build if you forget step 2.
3. Emit from the relevant component via `_metrics.IncrementCounter / RecordHistogram / SetGauge`.
4. Add a test using `InMemoryMetrics` that asserts the value flows through.

## Disabling telemetry

Default `IL2Metrics` is `NoOpMetrics.Instance` — emission is a no-op pointer
chase, no allocation, no lock contention. Plugins that have not been wired via
`WithMetrics()` use the no-op sink, so leaving the metrics stack out of a
deployment costs nothing.
