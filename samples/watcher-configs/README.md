# Sample watcher configs

Ready-to-customize TOML configs for the `neo-bridge-watcher-eth`
daemon, one per major EVM chain. Pick the right starting point + edit
the `REPLACE_WITH_*` placeholders to fit your deployment.

## Files

| Config                    | Chain id    | Block time | min_confirmations | Notes                                                   |
|---------------------------|-------------|------------|-------------------|---------------------------------------------------------|
| `eth-mainnet.toml`        | 0xE0000001  | ~12s       | 12 (or 32)        | Default: 12 = ~99.9% finality. Raise to 32 for Casper.  |
| `bsc-mainnet.toml`        | 0xE0000030  | ~3s        | 15                | Parlia consensus cross-validator confirmation.          |
| `polygon-mainnet.toml`    | 0xE0000040  | ~2s        | 256               | Heuristic finality. Pair with CheckpointManager poll.   |
| `arbitrum-one.toml`       | 0xE0000050  | ~250ms     | 0                 | L1-batch-finality-gated. Pair with L1 signal.           |

For chains not listed here, copy the closest match + adjust:
- Look up the canonical chain id in
  [`watchers/neo-bridge-watcher-eth/src/chains.rs`](../../watchers/neo-bridge-watcher-eth/src/chains.rs).
- Look up the recommended `min_confirmations` via
  `chains::recommended_confirmations` (also documented in
  [`docs/external-bridge-evm-chains.md`](../../docs/external-bridge-evm-chains.md)).

Or generate from scratch:

```bash
neo-bridge-watcher-eth --config-template > my-chain.toml
$EDITOR my-chain.toml
```

## Workflow

```bash
# 1. Pick a starting config, copy it.
cp samples/watcher-configs/bsc-mainnet.toml my-bsc.toml

# 2. Edit the REPLACE_WITH_* placeholders + paths.
$EDITOR my-bsc.toml

# 3. Validate before deploy.
./target/release/neo-bridge-watcher-eth --config my-bsc.toml --preflight

# 4. Run.
./target/release/neo-bridge-watcher-eth --config my-bsc.toml

# 5. Inspect (separate shell, while daemon runs).
./target/release/neo-bridge-watcher-eth --config my-bsc.toml --journal-info
curl http://0.0.0.0:9090/healthz
curl http://0.0.0.0:9090/metrics | grep watcher_
```

## What each config bakes in

Defaults differ per chain because the underlying chains differ:

- **`eth_chunk_size`** — most Eth-family providers cap `eth_getLogs`
  at 5–10k blocks per request; some chains (Polygon free-tier) cap
  at 1–3k. Configs match the typical free-tier ceiling.
- **`poll_interval_secs`** — roughly 2× the chain's block time, so
  the watcher polls ~once per 2 blocks (catches events without
  hammering the RPC).
- **`min_confirmations`** — per-chain reorg buffer. See the
  `docs/external-bridge-evm-chains.md` § "Per-chain confirmation
  buffers" table for the full gradient.
- **`threshold_secs`** (health) — how long without a successful tick
  before `/healthz` returns 503. Tuned per chain: faster chains
  use a tighter window.

## Multi-chain operator setup

Running watchers for multiple chains? Each instance gets:

- **Its own `journal_dir`** (the `flock` defends against accidental
  reuse, but the right answer is separate dirs).
- **Its own `signer_key_path`** (or share if your committee uses
  per-watcher identities; in that case, ensure the per-pubkey
  threshold is met on Neo's `MpcCommitteeVerifier`).
- **Its own `[health].bind` port** (if running on the same host).
- **Its own systemd unit / k8s Deployment** — see
  [`watchers/neo-bridge-watcher-eth/deploy/`](../../watchers/neo-bridge-watcher-eth/deploy/).

Prometheus picks up the `chain_id="0x..."` label automatically (the
daemon binary calls `HealthState::with_chain_id`), so multi-chain
metrics get cleanly disambiguated:

```promql
# All chain submissions in the last hour
sum by (chain_id) (rate(watcher_submissions_total[1h]))

# Stale watchers (no tick success in 5 min)
time() - watcher_last_tick_success_unix_timestamp > 300
```

## See also

- [`watchers/neo-bridge-watcher-eth/README.md`](../../watchers/neo-bridge-watcher-eth/README.md) — daemon CLI surface + config schema.
- [`watchers/neo-bridge-watcher-eth/deploy/`](../../watchers/neo-bridge-watcher-eth/deploy/) — k8s + systemd manifests + Dockerfile.
- [`docs/external-bridge-evm-chains.md`](../../docs/external-bridge-evm-chains.md) — 5-step EVM-chain onboarding runbook.
- [`docs/architecture-l2-lifecycle.md`](../../docs/architecture-l2-lifecycle.md) — system architecture + how external chains connect.
