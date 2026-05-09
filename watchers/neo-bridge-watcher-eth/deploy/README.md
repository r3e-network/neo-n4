# neo-bridge-watcher-eth — deployment manifests

Reference manifests for running the watcher daemon in production. All
files are self-contained examples; customize the placeholders before
applying.

## Files

| File | Purpose |
|------|---------|
| [`../Dockerfile`](../Dockerfile) | Multi-stage Docker build: `rust:1.83-slim-bookworm` builder → `gcr.io/distroless/cc-debian12:nonroot` runtime. Strips the binary, runs as uid:gid 65532:65532, exposes `:9090`. ~50 MB final image. |
| [`k8s.yaml`](./k8s.yaml) | Kubernetes Deployment + Service + ConfigMap + Secret + PVC. Wires `/healthz` to readiness/liveness probes; uses `Recreate` strategy + `terminationGracePeriodSeconds=30` for clean shutdown. |
| [`neo-bridge-watcher.service`](./neo-bridge-watcher.service) | systemd unit. SIGTERM-driven clean shutdown via `KillSignal=SIGTERM` + `TimeoutStopSec=30`. Hardened with `ProtectSystem=strict` / `NoNewPrivileges=true`. |

## Build the image

### Pre-built image (CI-published)

The CI pipeline at
`.github/workflows/build-watcher-image.yml` builds + publishes the
image on every master push (and on manual dispatch) to:

```
ghcr.io/r3e-network/neo-bridge-watcher-eth:latest      # master HEAD
ghcr.io/r3e-network/neo-bridge-watcher-eth:master      # same as above
ghcr.io/r3e-network/neo-bridge-watcher-eth:sha-abc1234 # 7-char short SHA
```

Operators with read access to the public registry just `docker pull`:

```bash
docker pull ghcr.io/r3e-network/neo-bridge-watcher-eth:latest
```

Update the k8s manifest's `image:` field to match.

### Local build

```bash
# From the workspace root (NOT from this directory — cargo workspace
# resolution needs the root Cargo.toml + sibling crates).
docker build \
    -f watchers/neo-bridge-watcher-eth/Dockerfile \
    -t neo-bridge-watcher-eth:latest .

# Tag for your own registry:
docker tag neo-bridge-watcher-eth:latest \
    your-registry.example.com/neo-bridge-watcher-eth:latest
docker push your-registry.example.com/neo-bridge-watcher-eth:latest
```

The `.dockerignore` at the workspace root keeps the build context
lean (excludes `target/`, most of `external/`, `.git/`, .NET artifacts,
etc.; preserves `external/neo-zkvm` which a sibling workspace member
has a path dep on). First build pulls Rust deps fresh — usually
8–15 minutes; subsequent builds use Docker layer caching.

## Run with `docker`

```bash
docker run --rm \
    -v $(pwd)/watcher.toml:/etc/watcher/watcher.toml:ro \
    -v $(pwd)/watcher.priv:/var/lib/watcher/keys/watcher.priv:ro \
    -v watcher-journal:/var/lib/watcher/journal \
    -p 9090:9090 \
    neo-bridge-watcher-eth:latest

# Custom config path:
docker run ... neo-bridge-watcher-eth:latest --config /alt/path.toml
```

The named volume `watcher-journal` survives container restarts; it
backs `journal_dir` in the TOML config. Don't bind-mount this from
the host without a single-instance discipline — the journal's
`flock` defends against concurrent writes, but two containers
sharing the same host directory is asking for trouble.

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

**Every metric carries a `chain_id="0x..."` label automatically** —
the daemon binary calls `HealthState::with_chain_id(config.external_chain_id)`
at startup. So a multi-chain operator running, say, BSC + Polygon
watchers gets time series like:

```
watcher_submissions_total{chain_id="0xE0000030"} 142   # BSC
watcher_submissions_total{chain_id="0xE0000040"} 87    # Polygon
```

…without any Prometheus relabel rules needed.

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
      - target_label: __metrics_path__
        replacement: /metrics
      - source_labels: [__address__]
        action: replace
        regex: '([^:]+)(?::\d+)?'
        replacement: '$1:9090'
        target_label: __address__
```

The `chain_id` label is part of the metric itself — no relabel rule
is needed to derive it from pod metadata.

Recommended alert rules (fire per `chain_id` label):
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
