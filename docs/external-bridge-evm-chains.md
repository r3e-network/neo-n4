# EVM-chain onboarding for the external bridge

The Neo Elastic Network's external-bridge framework treats Ethereum-,
BSC-, Polygon-, Arbitrum-, Optimism-, Base-, Avalanche-, Linea-,
zkSync-, Scroll-, Mantle-, Fantom-, Celo-, etc. as variations of a
single underlying EVM chain template. Adding a new EVM chain takes
five steps and writes **zero new code**: only configuration + a
contract deployment + on-chain registration.

The two architectural choices that make this possible:

1. **The Eth-side router contract
   (`external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol`)
   parameterizes `externalChainId` via constructor.** The same Solidity
   bytecode deploys unchanged on any EVM chain — Ethereum mainnet, BSC,
   Polygon, Avalanche, an L2, even Tron (TVM is EVM-flavored enough
   for these primitives). The constructor-passed `externalChainId`
   becomes part of every emitted `Locked` event and binds the router
   to its slot in the Neo-side namespace.

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

# Deploy with the foreign chain id from Step 1, plus an initial
# committee threshold + signers (committee blob from the operator
# CLI — see Step 4):
forge create src/NeoExternalBridgeRouter.sol:NeoExternalBridgeRouter \
    --rpc-url $BSC_RPC_URL \
    --private-key $DEPLOYER_KEY \
    --constructor-args 0xE0000030 1000000000000000000 1099 0xCOMMITTEE_HASH
```

Constructor args (defined in
[`NeoExternalBridgeRouter.sol`](../external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol)):

| Arg                  | Type      | Meaning                                                       |
|----------------------|-----------|---------------------------------------------------------------|
| `_externalChainId`   | uint32    | Foreign chain id from Step 1 (`0xE0000030` for BSC)           |
| `_minLockAmount`     | uint256   | Dust threshold (e.g., 1e18 wei = 1 native token)              |
| `_neoChainId`        | uint16    | Neo-side chain id (1099 = Neo mainnet)                        |
| `_committeeRootHash` | bytes32   | sha256 of the canonical committee blob (from `committee-blob`)|

Capture the deployed router address — it's needed for Step 3 + Step 4.

### Step 3 — Run the watcher daemon

```bash
# Generate a watcher private key (one-time, per-chain or shared):
cargo run --release -p neo-external-bridge -- genkey --out bsc-watcher.priv

cat > bsc-watcher.toml <<TOML
external_chain_id   = 0xE0000030                                # BSC mainnet
eth_rpc_url         = "https://bsc-dataseed.binance.org"        # any BSC RPC
eth_router_address  = "0xDEPLOYED_ROUTER_FROM_STEP_2"
neo_rpc_url         = "https://rpc.testnet.neo.org"
neo_escrow_address  = "0xNEO_ESCROW_DEPLOYED_BY_NEO_HUB_DEPLOY"
neo_signer_address  = "0xWATCHER_NEO_ACCOUNT"
signer_key_path     = "bsc-watcher.priv"
journal_dir         = "./journal-bsc"

[poll]
poll_interval_ms       = 6000          # BSC ~3s blocks; 2-block cadence
initial_backoff_ms     = 500
max_backoff_ms         = 60000
TOML

# Build with live-rpc, point at config:
CPATH=~/.local/include cargo build --release \
    -p neo-bridge-watcher-eth --features live-rpc
./target/release/neo-bridge-watcher-eth --config bsc-watcher.toml
```

The daemon's startup log echoes the human-readable chain name from
`name_for_chain_id(...)` — operators verify the right chain at a
glance before letting it run.

### Step 4 — Register the committee + verifier on Neo

```bash
# Generate a canonical committee blob from the M signing keys:
cargo run --release -p neo-external-bridge -- committee-blob \
    --threshold 4 \
    --signer @key0.pub --signer @key1.pub --signer @key2.pub \
    --signer @key3.pub --signer @key4.pub --signer @key5.pub \
    --signer @key6.pub \
    --out committee-bsc.bin

# Generate a deploy bundle that registers the committee on
# MpcCommitteeVerifier + binds the verifier to chain 0xE0000030
# on ExternalBridgeRegistry:
cargo run --release -p neo-external-bridge -- deploy-bundle \
    --external-chain-id 0xE0000030 \
    --committee-blob committee-bsc.bin \
    --mpc-verifier 0xMPC_VERIFIER_FROM_NEO_HUB_DEPLOY \
    --registry      0xREGISTRY_FROM_NEO_HUB_DEPLOY \
    --out bundle-bsc.json
```

Apply the bundle on Neo via the Neo CLI / your wallet:

```bash
neo-cli invoke --bundle bundle-bsc.json
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

| Slot range           | Family             | Constants                                                    |
|----------------------|--------------------|--------------------------------------------------------------|
| `0xE000_0001..000F`  | Ethereum           | `ETH_MAINNET`, `ETH_SEPOLIA`, `ETH_HOLESKY`                  |
| `0xE000_0010..001F`  | Tron               | `TRON_MAINNET`, `TRON_NILE_TESTNET`, `TRON_SHASTA_TESTNET`   |
| `0xE000_0020..002F`  | Solana             | `SOLANA_MAINNET`, `SOLANA_DEVNET`, `SOLANA_TESTNET`          |
| `0xE000_0030..003F`  | BSC                | `BSC_MAINNET`, `BSC_TESTNET`                                 |
| `0xE000_0040..004F`  | Polygon            | `POLYGON_MAINNET`, `POLYGON_AMOY_TESTNET`, `POLYGON_ZKEVM`, `POLYGON_ZKEVM_CARDONA` |
| `0xE000_0050..005F`  | Arbitrum           | `ARBITRUM_ONE`, `ARBITRUM_SEPOLIA`, `ARBITRUM_NOVA`          |
| `0xE000_0060..006F`  | Optimism           | `OPTIMISM_MAINNET`, `OPTIMISM_SEPOLIA`                       |
| `0xE000_0070..007F`  | Base               | `BASE_MAINNET`, `BASE_SEPOLIA`                               |
| `0xE000_0080..008F`  | Avalanche          | `AVALANCHE_C_MAINNET`, `AVALANCHE_FUJI`                      |
| `0xE000_0090..009F`  | Linea              | `LINEA_MAINNET`, `LINEA_SEPOLIA`                             |
| `0xE000_00A0..00AF`  | zkSync Era         | `ZKSYNC_ERA_MAINNET`, `ZKSYNC_SEPOLIA`                       |
| `0xE000_00B0..00BF`  | Scroll             | `SCROLL_MAINNET`, `SCROLL_SEPOLIA`                           |
| `0xE000_00C0..00CF`  | Mantle             | `MANTLE_MAINNET`, `MANTLE_SEPOLIA`                           |
| `0xE000_00D0..00DF`  | Fantom / Sonic     | `FANTOM_OPERA`, `SONIC_MAINNET`                              |
| `0xE000_00E0..00EF`  | Celo               | `CELO_MAINNET`, `CELO_ALFAJORES`                             |
| `0xE000_00F0..00FF`  | reserved           | unused (future allocations)                                  |

For chains beyond this curated set, allocate the next free `..F0..FF`
slot or the next free 16-slot bank above `0xE000_00FF`. Submit a PR
adding the constant + a `name_for_chain_id` arm.

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
