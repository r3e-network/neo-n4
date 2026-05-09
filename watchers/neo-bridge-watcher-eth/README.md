# neo-bridge-watcher-eth

Off-chain watcher daemon for Neo Elastic Network's Eth ↔ Neo external
bridge.

Two build modes:

- **Default** (`cargo build`): the messaging + signing + orchestration
  core only — no networking deps. Tests + downstream watcher crates
  (Tron / Solana) consume this. Lean dep tree.
- **`--features live-rpc`** (`cargo build --release --features live-rpc`):
  builds the runnable daemon binary `neo-bridge-watcher-eth`. Pulls
  reqwest + serde + tiny-keccak + toml. Wires the four trait impls
  (FileSigner + EthRpcEventSource + NeoRpcSubmitter + FileJournal) into
  a continuous loop with exponential backoff on errors.

The split is intentional. The cryptographically-load-bearing pieces —
canonical wire-format encoding, ECDSA signing — sit alone in the lib,
with byte-for-byte parity tests against the C# implementations in
`src/Neo.L2.Bridge/External/`. The daemon binary is a thin layer on
top: load config, construct the four impls, run `WatcherCore::tick()`
in a loop. Splitting this way lets the signing path evolve
independently of the RPC plumbing (HSM integration becomes a trait
swap, not a rewrite).

## Run the daemon

```bash
# Build:
CPATH=~/.local/include cargo build --release -p neo-bridge-watcher-eth --features live-rpc

# Generate a watcher private key (one-time):
cargo run --release -p neo-external-bridge --no-build -- genkey --out watcher.priv

# Write a watcher.toml config (see src/bin/neo-bridge-watcher-eth.rs
# header for the full schema):
cat > watcher.toml <<TOML
external_chain_id   = 0xE0000002             # Sepolia
eth_rpc_url         = "https://rpc.sepolia.org"
eth_router_address  = "0x..."                # NeoExternalBridgeRouter on Eth
neo_rpc_url         = "https://rpc.testnet.neo.org"
neo_escrow_address  = "0x..."                # NeoHub.ExternalBridgeEscrow on Neo
neo_signer_address  = "0x..."                # operator's Neo account
signer_key_path     = "watcher.priv"
journal_dir         = "./journal"
TOML

# Run:
./target/release/neo-bridge-watcher-eth --config watcher.toml
```

The v0 daemon ships a `StubSignAndSend` that emits the `invokefunction`
script bytes + a synthetic tx hash but does NOT actually sign + submit
a Neo transaction. Production deployments replace it with an HSM/KMS-
backed `SignAndSend` impl that wraps the script in a signed Neo
`Transaction` + POSTs `sendrawtransaction`. The watcher's pre-check
against the Neo RPC catches verifier-rejection paths regardless.

## Position in the stack

```
[Eth user]
  │ tx → NeoExternalBridgeRouter.lockETHAndSend(...)
  │
[Eth router emits Locked event]
  │
  ▼
[neo-bridge-watcher-eth daemon]                    ← lives in this crate
  • subscribes to Locked events (deferred — Phase B follow-on)
  • for each event, builds canonical
    ExternalCrossChainMessage bytes (this crate)
  • signs with secp256k1 + sha256 (this crate's Signer trait)
  • encodes proof bytes (NeoProofBytes for Neo, EthProofBytes for the
    Eth router)                                    (this crate)
  • submits to NeoHub.ExternalBridgeEscrow.Receive (deferred — Phase
    B follow-on)
  ▼
[Neo verifier accepts → escrow finalizes inbound]
```

## What lives here today

| Module        | Purpose                                                  |
|---------------|----------------------------------------------------------|
| `messaging`   | `ExternalCrossChainMessage` record + `canonical_message_bytes` + `message_hash`. Byte-for-byte parity with `Neo.L2.Messaging.ExternalMessageHasher`. |
| `proof`       | `NeoProofBytes` + `EthProofBytes` encoders. Same signatures, two wire formats — Neo's verifier wants 33B pubkey + 64B sig per signer; Eth's wants 1B index + 65B (r,s,v). The watcher generates both from one signing round. |
| `signer`      | `Signer` trait + a `FileSigner` for development. Production deployments plug HSM/KMS-backed implementations behind the trait. |
| `event_source`| `EventSource` trait + `LockedEvent` (Solidity event mapping) + `MockEventSource` for tests. |
| `submitter`   | `NeoSubmitter` trait — posts (messageBytes, proofBytes) to `NeoHub.ExternalBridgeEscrow.Receive`. `MockSubmitter` for tests. |
| `journal`     | `Journal` trait — last-processed block cursor + dedup of `(chainId, nonce)` so a transient submit failure never advances cursor or marks unsubmitted nonces as done. `InMemoryJournal` for tests. |
| `core`        | `WatcherCore` — wires Signer + EventSource + NeoSubmitter + Journal into the canonical pipeline. `process_event` is the hot path; `tick` / `drain` drive it from the event source. End-to-end tests with mocks pin the failure semantics (cursor MUST NOT advance on submit failure; replay rejection; chain-id mismatch). |

## What's deferred

| Component                                  | Reason for deferral                       |
|--------------------------------------------|-------------------------------------------|
| `ethers-rs` `EventSource` impl             | Needs a live Eth RPC + WebSocket setup; cleanest to land alongside an `anvil`-driven integration test rather than as untested glue. The `EventSource` trait is the abstraction; the production impl is a thin adapter. |
| Neo JSON-RPC `NeoSubmitter` impl           | Same; needs a live Neo devnet/RPC. The `NeoSubmitter` trait is the abstraction. |
| Bonded watcher orchestration               | The on-chain `NeoHub.ExternalBridgeBond` is wired but the watcher's auto-bond / auto-rebond logic needs a deployment pattern that's still being decided. |
| RocksDB `Journal` impl                     | `InMemoryJournal` is what tests use; the production impl is a 50-line RocksDB wrapper around the same trait. Lands with the daemon main loop. |
| HSM `Signer` impl                          | The `Signer` trait is the abstraction; HSM bindings (AWS KMS / GCP KMS / hardware) are operator-specific. |
| Daemon main loop                           | A trivial wrapper around `WatcherCore::tick` once the live impls of the three traits land. |

## Build + test

```bash
# From the repo root (workspace member):
CPATH=~/.local/include cargo test --release -p neo-bridge-watcher-eth
# 17 tests across messaging + proof + signer modules + parity integration.
```

The `tests/parity.rs` integration test pins byte-for-byte equivalence
with the C# encoders. A change to either side that breaks the constants
breaks both Rust and C# test suites simultaneously, forcing both
implementations to update together. That's the design.

## Wire-format parity test vector

Anchor for cross-language testing — both the Rust and C# tests assert
against this:

```
externalChainId      = 0xE000_0001 (Eth mainnet)
neoChainId           = 1099
nonce                = 7
direction            = ForeignToNeo (2)
sender               = 0x1111...11 (20B)
recipient            = 0xaaaa...aa (20B)
deadlineUnixSeconds  = 1_900_000_000
sourceTxRef          = 0xeeee...ee (32B)
messageType          = AssetTransfer (0)
payload              = ExternalAssetTransferPayload {
                         foreignAsset: 0xeeee...ee (20B),
                         amount: 1_000_000 (3-byte LE = 40 42 0F)
                       }.encode()  →  27 bytes total

canonical_message_bytes (129 B):
  010000e0 4b040000 0700000000000000 02
  1111111111111111111111111111111111111111
  aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
  00b33f7100000000
  eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee
  00 1b000000
  eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee 03000000 40420f

raw Hash256 = ce681e5ecb3eaf452d1834fd94c397271a6556736a4ecfa1e66e4d67e9e1bfac
            (C# UInt256.ToString reverses for display: acbfe1e9...)
```
