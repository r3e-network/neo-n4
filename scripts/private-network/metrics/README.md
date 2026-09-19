# Neo N4 monitoring / alerting

The metrics surface for the Neo N4 private network. The **node-side metric
sink and HTTP endpoint already live in the codebase** — `Neo.L2.Telemetry`
(`IL2Metrics` → `InMemoryMetrics` → `PrometheusExporter` →
`MetricsHttpServer`) and the `Neo.Plugins.L2Metrics` composition root serve
`GET /metrics` (plus `/healthz`, `/readyz`, `/healthprobe`, `/operatorstatus`).
This directory wires those emitted metrics into Prometheus, Grafana, and
Prometheus Alertmanager, and adds the operator-facing **dashboards and alert
rules** that the bare `/metrics` endpoint does not provide.

The metric names referenced in this directory are the canonical catalog in
`docs/telemetry.md`, exposed with `.` → `_`, counters suffixed `_total`, and
histograms rendered as summaries (`_count` / `_sum` / `_max` — the in-process
exporter emits **no `_bucket` quantile series**, so dashboard latency panels use
`_max` / `_sum`/`_count`, never `histogram_quantile`).

## Stack

| Service      | Image                          | Port  | Purpose                                  |
|--------------|--------------------------------|-------|------------------------------------------|
| prometheus   | `prom/prometheus:v2.55.0`      | 9090  | Scrapes every node; evaluates alert rules|
| grafana      | `grafana/grafana:11.3.0`       | 3000  | Dashboards over the Prometheus datasource|
| alertmanager | `prom/alertmanager:v0.27.0`    | 9093  | Routes fired alerts to notification channels |

`prometheus.yml` wires `record`/`alert` rules and the Alertmanager target;
`prometheus-alerts.yml` holds the alert rules. Grafana provisioning points the
`Neo N4` provider at `grafana/dashboards/` (auto-loads `neo-n4-overview.json`).

## Bring it up

```bash
docker compose -f scripts/private-network/docker-compose.yml up -d

# Liveness of each service
curl -s localhost:9090/-/healthy      # prometheus
curl -s localhost:3000/api/health     # grafana
curl -s localhost:9093/-/healthy      # alertmanager

# Grafana: http://localhost:3000  (admin/admin) → dashboard "Neo N4 Overview"
# Prometheus: http://localhost:9090  (Targets / Alerts pages)
# Alertmanager: http://localhost:9093
```

Dashboards are file-provisioned; editing the JSON and waiting ~10s
(`updateIntervalSeconds`) reloads them. Do not hand-edit dashboards in the UI
and expect them to persist — the file is the source of truth.

## Alerting

Rules live in `prometheus-alerts.yml`. Fired alerts appear on the Prometheus
**Alerts** page and are forwarded to Alertmanager once the `receivers` route is
wired. `alertmanager.yml` ships a **no-op default receiver** so the stack boots
without external services; add a real channel (Slack / PagerDuty / generic
webhook) and supply its URL through a controlled config file or the receiver's
supported secret mechanism — YAML in this repo does **not** expand environment
variables automatically. Do not commit credentials.

### Verifying a rule

Prometheus self-scraping only produces Prometheus's own series — node `l2_*`
metrics come from the scrape targets, so confirm the node target is UP on the
Targets page before asserting anything about `l2_*` data:

```bash
curl -s 'http://localhost:9090/api/v1/targets'
# Query the series a rule depends on
curl -s 'http://localhost:9090/api/v1/query?query=l2_settlement_poisoned'
# Read current rule state (does NOT force or evaluate a rule)
curl -s 'http://localhost:9090/api/v1/rules?type=alert'
```

To validate alert behavior without touching real settlement state, run the
rules against synthetic series in an isolated environment with
`promtool test rules` and fixture files. Do not poison a shared settlement head
as a "test". Notification delivery must be verified against a wired receiver —
the no-op default receiver proves nothing about delivery.
This change has not been validated through a live deployment, dashboard render,
or notification delivery test. Starting the services alone does not establish
those acceptance results.

## Adding a metric / alert

1. Metric: add the constant to `Neo.L2.Telemetry.MetricNames` **and** a
   description to `MetricCatalog.Descriptions` (the reflection completeness test
   fails the build otherwise). Emit via `_metrics.IncrementCounter /
   RecordHistogram / SetGauge`.
2. Dashboard: add a panel against the new Prometheus name (remember the
   `.`→`_` / `_total` / summary rules above).
3. Alert: add a rule to `prometheus-alerts.yml`. Distinguish idle traffic from
   stalled processing: a bare `increase(...) == 0` with a short `for` pages on
   a chain with no traffic demand (e.g. `L2BatchNotSealing`); isolated audit
   events can be missed entirely by short `increase` windows combined with `for`.
   Gate stalled-processing alerts on observed traffic demand with explicit label
   matching, and test idle, active, missing-series and isolated-event cases with
   `promtool test rules` fixtures.
