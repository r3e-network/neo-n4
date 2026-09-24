# Architecture: Glossary + component catalog

> Single-page reference for every term used across the architecture
> chapters and every component the framework ships. Intentionally
> shallow — one or two lines per entry. For depth, follow the
> link to the chapter that goes deeper.

## Table of contents

1. [Glossary of terms](#1-glossary-of-terms)
2. [NeoHub L1 contracts](#2-neohub-l1-contracts-4-pillars)
3. [L2 native contracts](#3-l2-native-contracts-10)
4. [L2 plugins](#4-l2-plugins-8)
5. [Off-chain operators](#5-off-chain-operators)
6. [CLI tools](#6-cli-tools-7)
7. [Wire formats](#7-wire-formats-quick-index)
8. [Where each term is defined first](#8-where-each-term-is-defined-first)

---

## 1. Glossary of terms

| Term                          | One-line definition                                                                                            |
|-------------------------------|----------------------------------------------------------------------------------------------------------------|
| **batch**                     | A sealed bundle of consecutive L2 blocks summarized by a `BatchCommitment` and submitted to L1.                |
| **batchNumber**               | Per-chain monotonic counter for sealed batches. Carried in `BatchCommitment` + `PublicInputs`.                 |
| **canonical bytes**           | The single byte-for-byte encoding of a logical value. Both endpoints recompute hashes from these.              |
| **chain id**                  | uint32. L2 chain ids start at 1024+; foreign chain ids in `0xE0_xx_xx_xx` namespace.                           |
| **chainConfig** / **configBytes** | The 91-byte canonical encoding of an L2 chain's `L2ChainConfig` stored in `ChainRegistry`.                 |
| **committee** (L2)            | dBFT 2.0 consensus members producing L2 blocks. Distinct from the external-bridge committee.                   |
| **committee** (external bridge) | M-of-N signers attesting to foreign-chain events. Bonded via `ExternalBridgeBond`.                           |
| **CommittedEvent**            | The `Block.Committed` event Neo's `Blockchain` raises after dBFT finality. Drives L2 batchers.                 |
| **daCommitment**              | Hash committing to the batch's data-availability payload. Stored in `BatchCommitment` + `PublicInputs`.        |
| **daMode**                    | Where the batch payload goes: 0=L1, 1=NeoFS, 2=External, 3=DAC.                                                |
| **dBFT 2.0**                  | Neo's BFT consensus algorithm; tolerates up to 1/3 byzantine sequencers.                                       |
| **direction**                 | External-bridge message direction: 1=NeoToForeign, 2=ForeignToNeo.                                             |
| **exitModel**                 | How withdrawals settle: 0=Optimistic, 1=Permissionless, 2=ZkValidity.                                          |
| **externalChainId**           | uint32 in `0xE0_xx_xx_xx`; identifies a foreign chain in the cross-foreign-chain bridge.                       |
| **family bank**               | 16 contiguous slots in the foreign-namespace allocated to one chain family (Eth / BSC / Polygon / etc.).       |
| **forced inclusion**          | L1-driven mechanism to bypass a censoring sequencer; user posts a tx on L1 → L2 must include it.               |
| **gatewayEnabled**            | bool — whether this L2 batches into the optional shared `BinaryTreeAggregator` (Phase 5).                      |
| **Neo Core native L2BridgeContract** | The L2-side counterpart to NeoHub's `SharedBridge`. Mints/burns wrapped assets per (chainId, asset).           |
| **MerkleProofSerializer**     | Canonical encoder for Merkle proofs (used by withdrawals + cross-L2 messages).                                 |
| **MessageHasher**             | Canonical encoder for `CrossChainMessage` (cross-L2). Both endpoints recompute the hash.                       |
| **min_confirmations**         | Watcher-config field: refuse to emit events from blocks shallower than N confirmations from foreign-chain head. |
| **NeoHub**                    | The 4-Pillar consolidated L1 suite: RollupHub, SharedBridge, ZkVerifier, GovernanceController. See §2 below. |
| **nonce** (deposit/message)   | Per-(srcChain, direction) monotonic counter. Replay-protected.                                                 |
| **operatorManager**           | UInt160. Multisig that manages a registered L2 (set-verifier, pause, etc.). In the chain config.               |
| **postStateRoot**             | UInt256. State root after a batch's last tx. Carried in `BatchCommitment`.                                     |
| **preStateRoot**              | UInt256. State root before a batch's first tx. Must equal the previous batch's `postStateRoot`.                |
| **proofType**                 | byte. 0=Multisig, 1=RiscVZk, 2=Optimistic, ... — picked per-chain via the chain config.                        |
| **publicInputHash**           | UInt256. SHA256 of `PublicInputs` (332 bytes). The verifier recomputes this from the on-chain commitment.      |
| **securityLevel**             | byte 0..3. 0 = sidechain, 3 = full ZK rollup. Operators pick per chain.                                        |
| **sequencerModel**            | byte. 0=Solo, 1=Committee, 2=Permissionless. How L2 blocks are produced.                                       |
| **RollupHub**                 | NeoHub L1 contract (Pillar 1). Verifies submitted batches, tracks DA, and manages chain registry.               |
| **§16.2 dimensions**          | The 5-dimension chain config: securityLevel, daMode, sequencerModel, exitModel, gatewayEnabled.                |
| **trust boundary**            | A point where bytes cross between trust domains. The system has 5 cross-tier boundaries.                       |
| **ZkVerifier**                | NeoHub L1 contract (Pillar 3). Validates ZK Groth16 proofs over BN254 interops.                                |
| **watcher**                   | Off-chain daemon that relays foreign-chain events (Eth/Tron/Solana → Neo).                                     |
| **wire format**               | The canonical byte layout for a logical value. See [`architecture-wire-formats.md`](./architecture-wire-formats.md). |
| **withdrawalRoot**            | UInt256. Merkle root of L2→L1 withdrawals in this batch. User claims via Merkle proof.                         |

---

## 2. NeoHub L1 contracts (4 Pillars)

Lives at `contracts/NeoHub.*`. The 4-Pillar lean architecture consolidates 26 historical micro-contracts into 4 production pillars with direct zero-hop storage access:

- **Pillar 1: `NeoHub.RollupHub`** — Core rollup hub consolidating chain registration, batch settlement (with atomic single-step `submitAndFinalizeBatch`), DA tracking, forced inclusion queue, and Merkle withdrawal proof verification (`verifyWithdrawalLeaf`).
- **Pillar 2: `NeoHub.SharedBridge`** — Unified asset vault and cross-chain message router. Escrows canonical assets (NEO, GAS, USDT, USDC, BTC, NEP-17), enforces invariant $\text{Escrow} \equiv \sum \text{Deposits} - \sum \text{Withdrawals}$, manages asset mappings (`RegisterMapping`), and routes cross-chain messages with replay protection.
- **Pillar 3: `NeoHub.ZkVerifier`** — Unified ZK validity verifier consolidating envelope dispatch, verification key registry, and cryptographic pairing checks (SP1 6.2.x BN254 Groth16) via Neo native interops.
- **Pillar 4: `NeoHub.GovernanceController`** — Unified governance and risk controller consolidating council multisig, timelock delays, 2-tier emergency freeze (`pauseChain` vs `freezeAll`), and sequencer committee staking/slashing (`registerSequencer`, `slashSequencer`).

---

## 3. L2 native contracts (10)

Implemented in `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
on the r3e `external/neo` fork. They are registered by Neo core as native
contracts and exist at genesis on every N4 L2 chain; they are not deployed
later from `contracts/`.

- **`L2BridgeContract`** — L2-side bridge (mint/burn wrapped assets). Counterpart to NeoHub.SharedBridge.
- **`L2MessageContract`** — L2-side message inbox/outbox. Counterpart to NeoHub.MessageRouter.
- **`L2BatchInfoContract`** — Records per-batch metadata on the L2 itself (cursor / latest commitment).
- **`L2FeeContract`** — L2 gas fee config (base / priority / op-cost map).
- **`L2PaymasterContract`** — Optional gas sponsorship — third party covers fees for whitelisted txs.
- **`L2SystemConfigContract`** — L2-side mirror of select chainConfig fields, queryable by L2 contracts.
- **`L2NativeExternalBridgeContract`** — L2-side counterpart to NeoHub.ExternalBridgeEscrow for foreign-chain assets.
- **`BridgedNep17Contract`** — Canonical bridged NEP-17 representation.
- **`L2AccountAbstraction`** — Validator/paymaster/nonce entry point.
- **`L2InteropVerifier`** — Mirrors global roots and verifies inclusion locally.

---

## 4. L2 plugins (8)

Lives at `src/Neo.Plugins.L2*`. Loaded by neo-cli; subscribes to `Block.Committed`.

- **`Neo.Plugins.L2Batch`** — Subscribes to `Blockchain.Committed`; seals txs into `BatchCommitment` via `BatchSealer`.
- **`Neo.Plugins.L2Settlement`** — Wires prover + settlement client; submits sealed batches to L1.
- **`Neo.Plugins.L2Bridge`** — Hosts `AssetRegistry` + `DepositProcessor` + `WithdrawalProcessor`.
- **`Neo.Plugins.L2DA`** — Picks DA writer by `DAMode`; supports L1 / NeoFS / External test-store / CommitteeAttested writers.
- **`Neo.Plugins.L2Prover`** — Hosts `IL2Prover` for the configured `ProofType`. SP1 prover daemon connection.
- **`Neo.Plugins.L2Rpc`** — 10 RPC handlers (per `doc.md` §14.1). `IL2RpcStore` backend (in-memory or RocksDB).
- **`Neo.Plugins.L2Gateway`** — Optional Phase-5 multi-L2 aggregation via `BinaryTreeAggregator`.
- **`Neo.Plugins.L2Metrics`** — Composition root for `IL2Metrics` + `MetricsHttpServer` (`/metrics` + `/healthz` + `/readyz`).

---

## 5. Off-chain operators

| Operator                     | What it does                                                  | Source                                 |
|------------------------------|---------------------------------------------------------------|----------------------------------------|
| Sequencer                    | dBFT 2.0 consensus member; produces L2 blocks                 | `Neo.L2.Sequencer/`                    |
| Batcher                      | Subscribes to `Block.Committed`; seals batches; submits to L1 | `Neo.L2.Batch/` + `Neo.Plugins.L2Batch`|
| Prover daemon                | SP1 zkVM proves `execute_batch(payload)`                      | `bridge/neo-zkvm-host/` (Rust binary)  |
| DA writer                    | Publishes batch payload to NeoFS / L1 / committee             | `Neo.L2.DA*` + `IDAWriter` impl        |
| External-chain watcher       | Relays foreign-chain Locked events → Neo escrow               | `watchers/neo-bridge-watcher-*/` (Rust)|

---

## 6. CLI tools (7)

Lives at `tools/*`.

- **`Neo.Stack.Cli`** (`neo-stack`) — 12 subcommands: create-chain,
  init-l2, register-chain, scaffold-executor, new-l2, ...
- **`Neo.Hub.Deploy`** (`neo-hub-deploy`) — Plan/scaffold/verify NeoHub
  deployment (24-step ordered production bundle).
- **`Neo.L2.Devnet`** (`neo-l2-devnet`) — In-process end-to-end demo
  runner. `--executor counter` wires a sample executor.
- **`Neo.L2.Explore`** (`neo-l2-explore`) — Terminal block explorer +
  state-root continuity audit.
- **`Neo.L2.Faucet.Cli`** (`neo-l2-faucet`) — Production drip with rate
  limiting + RocksDB-persisted journal.
- **`Neo.L2.Bridge.Cli`** (`neo-bridge`) — Production CLI for
  SharedBridge invocation hex.
- **`Neo.External.Bridge.Cli`** (`neo-external-bridge`) —
  External-bridge committee key gen + dual-side deploy planning.

Plus the watcher daemon binary at `target/release/neo-bridge-watcher-eth`
(Rust, behind `--features live-rpc`).

---

## 7. Wire formats quick index

For details, see [`architecture-wire-formats.md`](./architecture-wire-formats.md).

- **`L2BatchCommitment`** (321 + N bytes) — Batcher → SettlementManager.
- **`PublicInputs`** (332 bytes, fixed) — Prover → Verifier (committed in proof).
- **`L2ChainConfig`** (91 bytes, fixed) — `register-chain` → `ChainRegistry`.
- **`ExternalCrossChainMessage`** (102 + N bytes) — External chain → Watcher → ExternalBridgeEscrow.
- **`DepositPayload`** (44 + amountLen bytes) — NeoHub.SharedBridge → L2BridgeContract.
- **`CrossChainMessage`** (`MessageHasher`) — L2 sender → NeoHub.MessageRouter → L2 receiver.
- **`WithdrawalRecord`** — L2BridgeContract → SharedBridge (in batch withdrawalRoot).
- **`MerkleProofSerializer`** — User claim → SharedBridge.FinalizeWithdrawalWithProof.
- **`MultisigProofPayload`** — Stage-0 prover → VerifierRegistry.
- **`RiscVProofPayload`** — Phase-4 SP1 zkVM prover → VerifierRegistry.
- **`OptimisticProofPayload`** — Stage-1 sequencer account + signature + bond reference → OptimisticChallenge.
- **`FraudProofPayload`** — Challenge winner → fraud verifier.

---

## 8. Where each term is defined first

For deeper understanding of any term, follow these pointers:

| Term                   | First defined in                                                                           |
|------------------------|--------------------------------------------------------------------------------------------|
| 4-tier system          | [architecture-l2-lifecycle.md §1](./architecture-l2-lifecycle.md#1-system-at-a-glance)     |
| §16.2 dimensions       | [architecture-l2-lifecycle.md §3](./architecture-l2-lifecycle.md#3-anatomy-of-an-l2-chain) |
| 3-phase admission      | [architecture-l2-lifecycle.md §4](./architecture-l2-lifecycle.md#4-creation-from-zero-to-registered) |
| Canonical bytes        | [architecture-wire-formats.md §1](./architecture-wire-formats.md#1-why-canonical-wire-formats) |
| Cross-tier verification chain | [architecture-trust-boundaries.md §3](./architecture-trust-boundaries.md#3-cross-tier-verification-chain) |
| Defense-in-depth       | [architecture-trust-boundaries.md §4](./architecture-trust-boundaries.md#4-defense-in-depth-per-flow) |
| Trust-minimization gradient | [architecture-trust-boundaries.md §6](./architecture-trust-boundaries.md#6-the-trust-minimization-gradient) |
| Foreign-namespace prefix | [architecture-wire-formats.md §5](./architecture-wire-formats.md#5-externalcrosschainmessage--external-bridge-102--n-bytes) |
| Committee model (external) | [external-bridge-roadmap.md](./external-bridge-roadmap.md)                              |
| Per-chain confirmation buffer | [external-bridge-evm-chains.md](./external-bridge-evm-chains.md)                       |

---

## See also

- [`architecture-atlas.md`](./architecture-atlas.md) — index of the 4 architecture chapters with reading order by role.
- [`architecture-l2-lifecycle.md`](./architecture-l2-lifecycle.md) — system flow.
- [`architecture-wire-formats.md`](./architecture-wire-formats.md) — canonical byte layouts.
- [`architecture-trust-boundaries.md`](./architecture-trust-boundaries.md) — trust model.
- [`architecture-walkthrough.md`](./architecture-walkthrough.md) — per-tx narrative tour.
- [`tech-stack-coverage.md`](./tech-stack-coverage.md) — what's vendored vs implemented.
