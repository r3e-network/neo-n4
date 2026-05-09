# neo-bridge-watcher-eth — deployment manifests

Reference manifests for running the watcher daemon in production. Both
files are self-contained examples; customize the placeholders before
applying.

## Files

| File | Purpose |
|------|---------|
| [`k8s.yaml`](./k8s.yaml) | Kubernetes Deployment + Service + ConfigMap + Secret + PVC. Wires `/healthz` to readiness/liveness probes; uses `Recreate` strategy + `terminationGracePeriodSeconds=30` for clean shutdown. |
| [`neo-bridge-watcher.service`](./neo-bridge-watcher.service) | systemd unit. SIGTERM-driven clean shutdown via `KillSignal=SIGTERM` + `TimeoutStopSec=30`. Hardened with `ProtectSystem=strict` / `NoNewPrivileges=true`. |

## Operational invariants the manifests assume

1. **Single-instance per `journal_dir`.** The journal acquires a
   `flock(LOCK_EX | LOCK_NB)` on `.lock` at startup; a second instance
   fails fast. The k8s manifest uses `Recreate` strategy (not
   `RollingUpdate`) so the old pod releases the lock before the new
   pod acquires it. For multi-instance setups, run separate watchers
   pointed at separate journal directories.

2. **SIGTERM → clean exit.** The daemon's signal handler flips an
   `AtomicBool`; the run loop polls it at top + during sleeps. Exit
   typically lands within ~100ms. The k8s manifest sets
   `terminationGracePeriodSeconds=30` (lots of margin); systemd uses
   `TimeoutStopSec=30`.

3. **Health probe shape.** `GET /healthz` returns 200 if a tick has
   succeeded within `threshold_secs` seconds, 503 otherwise. The body
   is JSON in both cases. Both manifests probe `/healthz` and consume
   the status code.

4. **Watcher key custody.** The 32-byte secp256k1 private key is
   sensitive — anyone with it can impersonate the watcher's
   committee membership. The k8s manifest has a placeholder Secret;
   in production, plug an external secret manager (Sealed Secrets,
   External Secrets Operator, HashiCorp Vault, AWS Secrets Manager).
   The systemd unit reads from `/etc/watcher/watcher.priv` — restrict
   to mode 0400 owned by the service user.

5. **Journal durability.** `consumed.log` + `cursor.bin` MUST survive
   pod restarts; otherwise the watcher re-submits everything from
   block 0 on the next start (Neo's verifier rejects the duplicates,
   but the watcher wastes RPC cycles). The k8s manifest mounts a
   PersistentVolumeClaim; the systemd unit places the journal under
   `/var/lib/watcher`.

## Prometheus metrics

The same `[health]` server also exposes `GET /metrics` in Prometheus
exposition format. Metrics:

| Metric | Type | Use |
|--------|------|-----|
| `watcher_started_at_unix_timestamp` | gauge | restart detection |
| `watcher_last_tick_unix_timestamp` | gauge | liveness via `time() - val` |
| `watcher_last_tick_success_unix_timestamp` | gauge | freshness alert: `time() - val > 300` |
| `watcher_ticks_total` | counter | activity rate |
| `watcher_events_processed_total` | counter | bridge throughput |
| `watcher_submissions_total` | counter | successful Neo submissions |
| `watcher_journal_cursor` | gauge | block lag: `eth_head - val` |
| `watcher_last_error_unix_timestamp` | gauge | error recency |
| `watcher_healthy` | gauge | 1/0 — same logic as `/healthz` 200/503 |

Scrape config (Prometheus side):
```yaml
scrape_configs:
  - job_name: neo-bridge-watcher
    kubernetes_sd_configs:
      - role: pod
    relabel_configs:
      - source_labels: [__meta_kubernetes_pod_label_app]
        action: keep
        regex: neo-bridge-watcher-eth
      - source_labels: [__meta_kubernetes_pod_label_chain]
        target_label: chain
      - target_label: __metrics_path__
        replacement: /metrics
      - source_labels: [__address__]
        action: replace
        regex: '([^:]+)(?::\d+)?'
        replacement: '$1:9090'
        target_label: __address__
```

The `chain` label comes from the pod's `chain=bsc` label (set in the
k8s manifest), so multi-chain operator setups get
`watcher_submissions_total{chain="bsc"}` etc. for free.

Recommended alert rules:
- `time() - watcher_last_tick_success_unix_timestamp > 300` —
  watcher hasn't made progress in 5 minutes.
- `watcher_healthy == 0` — same alert, threshold-aware.
- `rate(watcher_submissions_total[15m]) == 0 AND
  rate(watcher_events_processed_total[15m]) > 0` — events are
  arriving but submissions are stuck (verifier rejection or RPC
  outage).
- `(time() - watcher_last_error_unix_timestamp) < 60 AND
  rate(watcher_ticks_total[5m]) > 0` — recent error, daemon still
  running (will retry, but operator should look).

## What the manifests don't cover

- **Multi-chain operator setups.** If you're watching multiple EVM
  chains, deploy one Deployment per chain — separate journal volume,
  separate config, separate health-probe port. Don't share state.
  The `chain=` pod label keeps Prometheus metrics tagged correctly.
- **Log aggregation.** stdout/stderr go to the container/journal
  by default; pipe to your logging stack however you normally would.
- **Auto-bond + auto-rebond.** The watcher's bond lifecycle (on Neo
  via `NeoHub.ExternalBridgeBond`) is operator-managed today;
  a future iteration will add automatic top-up signals.
- **HSM-backed signer.** The v0 daemon uses `StubSignAndSend` (emits
  the script bytes + a synthetic tx hash for operator-side signing).
  Production needs a real `SignAndSend` impl wired to your KMS/HSM.
  See `src/live/neo_rpc.rs` for the trait.
