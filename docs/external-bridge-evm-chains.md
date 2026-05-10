# EVM-chain onboarding for the external bridge

The Neo Elastic Network's external-bridge framework treats Ethereum-,
BSC-, Polygon-, Arbitrum-, Optimism-, Base-, Avalanche-, Linea-,
zkSync-, Scroll-, Mantle-, Fantom-, Celo-, etc. as variations of a
single underlying EVM chain template. Adding a new EVM chain takes
five steps and writes **zero new code**: only configuration + a
contract deployment + on-chain registration.

The two architectural choices that make this possible:

1. **The Eth-side router contract parameterizes `externalChainId` via
   constructor.** Source:
   `external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol`.
   The same Solidity bytecode deploys unchanged on any EVM chain —
   Ethereum mainnet, BSC, Polygon, Avalanche, an L2, even Tron (TVM is
   EVM-flavored enough for these primitives). The constructor-passed
   `externalChainId` becomes part of every emitted `Locked` event and
   binds the router to its slot in the Neo-side namespace.

2. **The watcher daemon (`neo-bridge-watcher-eth`) is fully
   chain-id-driven.** `EthRpcEventSource` polls `eth_getLogs` on any
   JSON-RPC endpoint that speaks the standard EVM API; the secp256k1
   `FileSigner` produces the same signatures regardless of which EVM
   chain the events came from; the canonical
   `ExternalCrossChainMessage` encoder writes the chain id into a
   fixed-position field. **Operators run the same daemon binary,
   pointed at a different RPC + a different config file's
   `external_chain_id`.**

## The 5-step runbook

The example throughout uses **BNB Smart Chain mainnet** (foreign chain
id `0xE000_0030`); substitute the constants for your target chain.

### Step 1 — Pick the foreign chain id

Look up your chain in
[`watchers/neo-bridge-watcher-eth/src/chains.rs`](../watchers/neo-bridge-watcher-eth/src/chains.rs).
The full curated table is reproduced in `Slot allocation` below. If
your chain isn't yet listed, add a constant in the appropriate
16-slot bank and submit a PR — the test
`family_banks_align_to_16_slots` will pin its placement.

For BSC mainnet:

```rust
use neo_bridge_watcher_eth::chains::BSC_MAINNET;
// = 0xE000_0030
```

### Step 2 — Deploy `NeoExternalBridgeRouter.sol` on the EVM chain

```bash
cd external/foreign-contracts/eth
forge build

# Deploy with the foreign chain id from Step 1 + an owner address
# (typically the operator's deployer key; later transferred to a
# multisig). Committee membership is set in a follow-up call (below).
forge create src/NeoExternalBridgeRouter.sol:NeoExternalBridgeRouter \
    --rpc-url $BSC_RPC_URL \
    --private-key $DEPLOYER_KEY \
    --constructor-args 0xE0000030 $OWNER_ADDRESS
```

Constructor args (defined in
[`NeoExternalBridgeRouter.sol`](../external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol)):

| Arg                | Type    | Meaning                                                                                       |
|--------------------|---------|-----------------------------------------------------------------------------------------------|
| `_externalChainId` | uint32  | Foreign chain id from Step 1 (`0xE0000030` for BSC). Required to carry the `0xE0_xx_xx_xx` namespace prefix; constructor reverts otherwise. |
| `_owner`           | address | Initial contract owner. Authorized to call `setCommittee` + `transferOwnership`. Must be non-zero. |

Capture the deployed router address — it's needed for Step 3 + Step 4.

After deployment, register the committee via `setCommittee` (owner-only):

```bash
# committee = list of Eth addresses derived from the watchers' secp256k1
# pubkeys via keccak256(pubkey)[12:]. The Neo side stores 33-byte
# compressed pubkeys for the same identities — both sides reference the
# same set of signers, just in different encodings.
cast send $ROUTER_ADDRESS \
    "setCommittee(address[],uint8)" \
    "[$ADDR_0,$ADDR_1,$ADDR_2,$ADDR_3,$ADDR_4,$ADDR_5,$ADDR_6]" \
    4 \
    --rpc-url $BSC_RPC_URL \
    --private-key $DEPLOYER_KEY
```

### Step 3 — Run the watcher daemon

```bash
# Generate a watcher private key (one-time, per-chain or shared):
dotnet run --project tools/Neo.External.Bridge.Cli -- \
    genkey --out bsc-watcher.priv

cat > bsc-watcher.toml <<TOML
external_chain_id   = 0xE0000030                              # BSC mainnet
eth_rpc_url         = "https://bsc-dataseed.binance.org"      # any BSC RPC
eth_router_address  = "0xDEPLOYED_ROUTER_FROM_STEP_2"
neo_rpc_url         = "https://rpc.testnet.neo.org"
neo_escrow_address  = "0xNEO_ESCROW_DEPLOYED_BY_NEO_HUB_DEPLOY"
neo_signer_address  = "0xWATCHER_NEO_ACCOUNT"
signer_key_path     = "bsc-watcher.priv"
journal_dir         = "./journal-bsc"

[poll]
poll_interval_secs    = 6              # BSC ~3s blocks; 2-block cadence
backoff_initial_secs  = 5
backoff_max_secs      = 300
eth_chunk_size        = 5000
request_timeout_secs  = 30
min_confirmations     = 15             # BSC reorg buffer (see chains.rs)
start_block           = 38_400_000     # OPTIONAL — bootstrap mid-stream
                                       # (omit/set 0 to scan from genesis)

[health]                               # OPTIONAL — for k8s probes / Prometheus
bind                  = "0.0.0.0:9090"
threshold_secs        = 120
TOML

# Build with live-rpc:
CPATH=~/.local/include cargo build --release \
    -p neo-bridge-watcher-eth --features live-rpc

# Validate config + signer + journal + RPC reachability before running:
./target/release/neo-bridge-watcher-eth --config bsc-watcher.toml --preflight
# Exit 0 = safe to start. Walks 6 checks; failure is specific to the
# bad component (e.g. eth_blockNumber on http://...: connection refused).

# Run:
./target/release/neo-bridge-watcher-eth --config bsc-watcher.toml
```

The daemon's startup log echoes the human-readable chain name from
`name_for_chain_id(...)` — operators verify the right chain at a
glance before letting it run. If `min_confirmations = 0` but the
chain has a non-zero recommendation in `chains::recommended_confirmations`,
the daemon emits a `WARNING` at startup pointing at the recommended value.

**`start_block` for mid-stream bootstrap** (the highlighted field
above): when the daemon's journal cursor is below `start_block`,
the cursor is advanced at startup. Useful when deploying a watcher
against a chain that's been running for months — without
`start_block`, the daemon scans from block 0, hammering the RPC
provider for a year of empty blocks. Set `start_block` to the
chain's current head minus a few thousand blocks (a safety margin
for any in-flight inbound events you don't want to miss).
Subsequent restarts read from the journal as normal; `start_block`
is monotonic — only the first run that finds journal cursor <
start_block advances.

### Step 4 — Register the committee + verifier on Neo

The operator CLI lives in `tools/Neo.External.Bridge.Cli/` (built as
`neo-external-bridge` for short — invoked via `dotnet run --project
tools/Neo.External.Bridge.Cli` or the published binary).

```bash
# 4a — Convert the watchers' 33B compressed pubkeys into both encodings:
#      a Neo-side `committeeBlob` (hex) + the matching Eth-side address[]
#      that Step 2's setCommittee already accepted. The CLI cross-derives
#      the addresses from the pubkeys, so they can't drift.
dotnet run --project tools/Neo.External.Bridge.Cli -- committee-blob \
    --pubs-file watchers.pubs   # one pub33 hex per line
# Stdout: committee size, Neo blob (0x...), Eth address list per index.

# 4b — Generate the on-chain deploy bundle. This emits a step-by-step
#      runbook (printed to stdout) the operator's wallet executes; it does
#      not invoke contracts directly.
dotnet run --project tools/Neo.External.Bridge.Cli -- deploy-bundle \
    --external-chain-id 0xE0000030 \
    --verifier 0xMPC_VERIFIER_FROM_NEO_HUB_DEPLOY \
    --registry 0xREGISTRY_FROM_NEO_HUB_DEPLOY \
    --escrow   0xESCROW_FROM_NEO_HUB_DEPLOY \
    --eth-router 0xROUTER_FROM_STEP_2 \
    --threshold 4 \
    --committee-blob 0xBLOB_HEX_FROM_4A \
    --eth-addresses 0xADDR0,0xADDR1,0xADDR2,0xADDR3,0xADDR4,0xADDR5,0xADDR6
```

The `deploy-bundle` output names each contract method + args in
order. Apply via your Neo wallet — for instance, `neo-cli` invokes:

```bash
# Step 1 from the bundle:
neo-cli invoke <verifier> RegisterCommittee \
    0xE0000030 4 1 0xBLOB_HEX
# Step 2 from the bundle:
neo-cli invoke <registry> RegisterVerifier \
    0xE0000030 <verifier> 1
# ... (further steps printed by deploy-bundle)
```

### Step 5 — Smoke-test

End user calls `lockETHAndSend` (or `lockERC20AndSend`) on the
deployed BSC router; the watcher relays the event to Neo's
`ExternalBridgeEscrow.Receive`; the recipient's wrapped balance on
Neo bumps. Reverse direction: Neo `ExternalBridgeEscrow` emits a
`WithdrawalReady` event → committee co-signs → user calls
`finalizeWithdrawal` on the BSC router with the proof bytes.

## Slot allocation

The 24-bit foreign chain id space below `0xE0_00_FF_FF` is organized
into 16-slot family banks. Each bank reserves room for that family's
mainnet + 1–3 testnets + future variants:

- **`0xE000_0001..000F`** — Ethereum: `ETH_MAINNET`, `ETH_SEPOLIA`,
  `ETH_HOLESKY`.
- **`0xE000_0010..001F`** — Tron: `TRON_MAINNET`, `TRON_NILE_TESTNET`,
  `TRON_SHASTA_TESTNET`.
- **`0xE000_0020..002F`** — Solana: `SOLANA_MAINNET`, `SOLANA_DEVNET`,
  `SOLANA_TESTNET`.
- **`0xE000_0030..003F`** — BSC: `BSC_MAINNET`, `BSC_TESTNET`.
- **`0xE000_0040..004F`** — Polygon: `POLYGON_MAINNET`,
  `POLYGON_AMOY_TESTNET`, `POLYGON_ZKEVM`, `POLYGON_ZKEVM_CARDONA`.
- **`0xE000_0050..005F`** — Arbitrum: `ARBITRUM_ONE`,
  `ARBITRUM_SEPOLIA`, `ARBITRUM_NOVA`.
- **`0xE000_0060..006F`** — Optimism: `OPTIMISM_MAINNET`,
  `OPTIMISM_SEPOLIA`.
- **`0xE000_0070..007F`** — Base: `BASE_MAINNET`, `BASE_SEPOLIA`.
- **`0xE000_0080..008F`** — Avalanche: `AVALANCHE_C_MAINNET`,
  `AVALANCHE_FUJI`.
- **`0xE000_0090..009F`** — Linea: `LINEA_MAINNET`, `LINEA_SEPOLIA`.
- **`0xE000_00A0..00AF`** — zkSync Era: `ZKSYNC_ERA_MAINNET`,
  `ZKSYNC_SEPOLIA`.
- **`0xE000_00B0..00BF`** — Scroll: `SCROLL_MAINNET`, `SCROLL_SEPOLIA`.
- **`0xE000_00C0..00CF`** — Mantle: `MANTLE_MAINNET`, `MANTLE_SEPOLIA`.
- **`0xE000_00D0..00DF`** — Fantom / Sonic: `FANTOM_OPERA`,
  `SONIC_MAINNET`.
- **`0xE000_00E0..00EF`** — Celo: `CELO_MAINNET`, `CELO_ALFAJORES`.
- **`0xE000_00F0..00FF`** — reserved: unused (future allocations).

For chains beyond this curated set, allocate the next free `..F0..FF`
slot or the next free 16-slot bank above `0xE000_00FF`. Submit a PR
adding the constant + a `name_for_chain_id` arm.

## Per-chain confirmation buffers (`min_confirmations`)

Each EVM chain has different reorg characteristics. The watcher's
`[poll]` config exposes `min_confirmations` — the source will not
emit events from blocks shallower than `min_confirmations` deep
from the chain head. Setting it correctly is the operator's
defense against short-reorg-induced phantom mints.

| Chain                | `min_confirmations` | Rationale                                                          |
|----------------------|---------------------|--------------------------------------------------------------------|
| Ethereum mainnet     | **12** (or 32)      | 12 ≈ 99.9% finality; 32 ≈ Casper-finalized (recommended for gov)   |
| Ethereum testnets    | 5                   | Faster feedback for dev; testnet reorgs are common but cheap       |
| BSC mainnet          | 15                  | Parlia consensus; ~15 blocks for cross-validator confirmation      |
| Polygon PoS          | 256                 | Heuristic finality; CheckpointManager finalizes every ~30 min      |
| Polygon zkEVM        | 0                   | ZK validity proofs gate L2 finality on L1 batch posts              |
| Arbitrum One/Nova    | 0                   | Operator waits for L1 batch finality via separate signal           |
| Optimism / Base      | 0                   | Same — settles on L1                                               |
| Avalanche C-Chain    | 1                   | Snowman++ near-instant finality; 1 confirmation suffices           |
| Linea / Scroll       | 0                   | ZK rollup; finality follows L1 batch posts                         |
| zkSync Era           | 0                   | Same                                                               |
| Mantle / Mode        | 0                   | OP Stack derivative                                                |
| Fantom / Sonic       | 5                   | Lachesis aBFT; ~5 blocks safe                                      |
| Celo mainnet         | 1                   | IBFT; near-instant finality                                        |
| Tron mainnet         | 19                  | DPoS Super-Representative-confirmed (KSR/SR2 round)                |
| Tron Nile/Shasta     | 1                   | Testnet — fast feedback                                            |

For L2s where `min_confirmations` is 0, the operator must layer their
own signal — typically by polling the L1 settlement contract for
batch finality before processing the L2 events.

## What the framework guarantees across EVM chains

The same trait abstractions (`Signer`, `EventSource`, `NeoSubmitter`,
`Journal`) apply unchanged. The EVM-family classification helper:

```rust
use neo_bridge_watcher_eth::chains::is_evm_family;

assert!(is_evm_family(BSC_MAINNET));
assert!(is_evm_family(POLYGON_MAINNET));
assert!(is_evm_family(TRON_MAINNET));        // EVM-flavored
assert!(!is_evm_family(SOLANA_MAINNET));     // ed25519 — different stack
```

…tells operator tooling whether the Eth watcher binary applies. For
Solana, swap to `neo-bridge-watcher-sol` (ed25519 signer, same
orchestrator, same `WatcherCore::tick` loop).

## What the framework does *not* guarantee

- **Block-finality semantics.** Each EVM chain has different
  reorganization characteristics — Ethereum waits ~12 confirmations
  for ~99.9% finality, BSC ~15, an L2 settles when its rollup batch
  posts. The watcher's `[poll]` config exposes `poll_interval_ms`
  but does *not* implement a per-chain confirmation policy. Operators
  must understand their target chain's reorg risk before relying on
  fast settlement.
- **MEV / front-running protection.** The router's
  `lockETHAndSend(...)` is a public function; users who want
  protection from MEV bots layer their own (Flashbots / private
  mempools / commit-reveal) on top.
- **Per-chain gas / fee logic.** Some EVM chains have their own gas
  models (Arbitrum's L1+L2 fee split, Optimism's data fee). The
  router's gas accounting is the standard EVM model — operators
  budget extra on chains where the actual cost differs.

## See also

- [`watchers/neo-bridge-watcher-eth/src/chains.rs`](../watchers/neo-bridge-watcher-eth/src/chains.rs) — the canonical chain-id table.
- [`external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol`](../external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol) — the EVM router contract (deploys unchanged on any EVM chain).
- [`watchers/neo-bridge-watcher-eth/README.md`](../watchers/neo-bridge-watcher-eth/README.md) — the daemon's full config schema + run instructions.
- [`docs/external-bridge-roadmap.md`](external-bridge-roadmap.md) — Phase A → B → C delivery plan + future zk light-client R&D.
