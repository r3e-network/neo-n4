# Neo Elastic Network — Whitepaper

**Multi-L2 architecture on Neo 4 core with shared bridge, proof aggregation, and native cross-chain messaging.**

> This document is the formal technical reference for the Neo Elastic Network. The design
> is fully specified in [`doc.md`](./doc.md) (Chinese, authoritative); this whitepaper is
> the English-language formal write-up for external review and integrator onboarding.
> Implementation is tracked in [`IMPLEMENTATION_STATUS.md`](./IMPLEMENTATION_STATUS.md).

---

## Abstract

Neo Elastic Network is a multi-L2 stack that uses Neo 4 core as the L2 execution kernel,
unifies L2 chains under a shared L1 contract suite (NeoHub), and supports optional
proof aggregation and inter-L2 messaging through Neo Gateway. The design borrows ZKsync
Elastic Chain's *shared bridge / chain registry / proof aggregation* pattern and rebuilds
it on Neo's primitives — dBFT 2.0 finality, NEP-17 assets, NeoVM 2 / RISC-V execution, and
NeoFS data availability. Settlement progresses through three pluggable proof regimes —
multisig attestation, optimistic challenge, ZK validity — so chains can launch on the
weakest acceptable bar and tighten to ZK without changing the L2 stack or the L1 contracts.

---

## Table of contents

1. [Motivation](#1-motivation)
2. [System overview](#2-system-overview)
3. [L1 contract suite — NeoHub](#3-l1-contract-suite--neohub)
4. [L2 chain internals](#4-l2-chain-internals)
5. [Proof system](#5-proof-system)
6. [Asset model](#6-asset-model)
7. [Cross-chain messaging — Neo Connect](#7-cross-chain-messaging--neo-connect)
8. [Data availability](#8-data-availability)
9. [Neo Gateway — proof aggregation](#9-neo-gateway--proof-aggregation)
10. [Censorship resistance & forced inclusion](#10-censorship-resistance--forced-inclusion)
11. [Governance](#11-governance)
12. [Threat model](#12-threat-model)
13. [Phased rollout](#13-phased-rollout)
14. [Comparison to other rollup stacks](#14-comparison-to-other-rollup-stacks)
15. [Glossary](#15-glossary)

---

## 1. Motivation

Neo 4 core (the C# Neo node implementation) is a battle-tested execution kernel: dBFT 2.0
finality, native contracts, NEP-17 token standard, and a forthcoming NeoVM 2 instruction
set compatible with RISC-V. It can run a chain end-to-end. What it cannot do, on its own,
is be an L2 — there is no L1 settlement layer, no shared bridge, no proof verifier, no DA
layer, no batcher, no message router, no escape hatch. The Neo Elastic Network supplies
exactly those missing pieces, organized so that:

- **Many L2 chains can launch with one shared L1 footprint.** Each L2 reuses NeoHub for
  asset escrow, settlement, and message routing — no per-chain bridge, no per-chain
  verifier registry. This is the principal design lesson from ZKsync Elastic Chain.
- **Each L2 picks its own DA tier and proof regime** within governance-approved limits.
  RWA chains pay for L1 DA + ZK proofs; game chains can run on NeoFS DA + multisig
  attestation; users see the choice via on-chain *security labels*.
- **Chains can interoperate natively.** Neo Connect routes L1↔L2 and L2↔L2 messages
  through a single canonical message root, so a single user-facing transaction can
  legitimately span multiple L2s (a "cross-chain bundle").
- **Sequencer security is decoupled from execution security.** Even a single-operator
  sequencer can be ceremonially censorship-resistant by means of forced inclusion,
  bisection-game challenges, and explicit escape hatches.

The L1 contracts and the L2 plugin set are *finalized at the architecture level*; the
proof regime moves up the trust ladder one phase at a time without rewriting either.

---

## 2. System overview

<p align="center">
  <img src="docs/figures/architecture.svg" alt="Neo Elastic Network — three-tier architecture: L1 NeoHub anchor (top-level view: 13 core contracts grouped into Settlement, Bridge, Messaging, Security, Governance — fraud verifiers and Phase B/C external-bridge stack live in NeoHub too but are detailed in the neohub-anatomy figure), optional Phase 5 Neo Gateway aggregator, and N elastic L2 chains" width="900">
</p>

Three layers, each with one job:

| Layer            | Owns                                                                          | Does NOT own                          |
| ---------------- | ----------------------------------------------------------------------------- | ------------------------------------- |
| **L1**           | Canonical assets, settlement, message routing, governance, verifier registry  | Per-chain execution                   |
| **Gateway**      | Proof aggregation, global message root                                        | Custody — assets stay locked in L1    |
| **L2**           | Execution, batching, sequencing, local DA, proving                            | Independent gas issuance              |

The architectural invariant: **L2 chains can be many; assets, state verification, message
routing, and governance must be unified.**

---

## 3. L1 contract suite — NeoHub

NeoHub is the L1 contract suite shared by every L2. Conceptually it combines ZKsync's
BridgeHub, SharedBridge, VerifierRegistry, and MessageRouter into one suite. The 21 contracts:

<p align="center">
  <img src="docs/figures/architecture/neohub-anatomy.svg" alt="NeoHub L1 anatomy: all 21 contracts grouped by concern — Settlement (SettlementManager + VerifierRegistry), Bridge (SharedBridge + TokenRegistry + ChainRegistry), Messaging (MessageRouter + DARegistry), Security (SequencerRegistry + SequencerBond + ForcedInclusion + OptimisticChallenge + EmergencyManager), Governance (GovernanceController + GovernanceFraudVerifier + RestrictedExecutionFraudVerifier), and External Bridge for Phase B/C (MpcCommitteeVerifier + ExternalBridgeRegistry + ExternalBridgeEscrow + ExternalBridgeBond + MpcCommitteeFraudVerifier + ExternalBridgeStubVerifier — test-only)" width="900">
</p>

- **`ChainRegistry`** — Register / configure / pause L2 chains. Each entry:
  `{chainId, operatorManager, verifier, bridgeAdapter, messageAdapter,
  securityLevel(0–3), daMode(0–3), gatewayEnabled, permissionlessExit,
  active}`.
- **`SharedBridge`** — Escrow canonical GAS / NEO / NEP-17. Lock-mint and
  burn-unlock rules. Withdrawal finalization off finalized
  `withdrawalRoot`.
- **`SettlementManager`** — Accept `L2BatchCommitment` (chainId,
  batchNumber, pre/postStateRoot, txRoot, receiptRoot, withdrawalRoot,
  l2ToL1MessageRoot, l2ToL2MessageRoot, daCommitment, publicInputHash,
  proofType, proof). Forward verification to `VerifierRegistry`.
- **`VerifierRegistry`** — Pluggable verifier dispatch by `ProofType`:
  `Multisig`, `Optimistic`, `ZkRiscV`, `Aggregated`.
- **`MessageRouter`** — L1↔L2 and L2↔L2 message queues with
  `(chainId, nonce)` replay protection.
- **`TokenRegistry`** — Canonical L1↔L2 asset mapping:
  `{l1Asset, l2ChainId, l2Asset, assetType, mintBurn|lockMint, active}`.
- **`DARegistry`** — Record DA commitments per chain.
- **`GovernanceController`** — L2 admission policy, verifier upgrade,
  bridge emergency control, DA security-level registry.
- **`EmergencyManager`** — Pause individual chains; expose escape hatch.
- **`ForcedInclusion`** — Anti-censorship queue (see §10).
- **`SequencerBond`** — Sequencer collateral; slashing target for missed
  forced-inclusion deadlines.
- **`SequencerRegistry`** — Active sequencer committee per chain;
  admission / exit lifecycle.
- **`OptimisticChallenge`** — Phase-3 challenge window; entry point for
  the bisection-game fraud-proof flow.
- **`GovernanceFraudVerifier`** — Reference fraud verifier for
  governance-arbitration optimistic chains. Decodes the canonical
  `FraudProofPayload` (v1 = 101 bytes fixed, v2 = 105+N bytes with
  disputed-tx witness), validates structural integrity, emits
  accept/reject events for council review.
- **`RestrictedExecutionFraudVerifier`** — Trustless v3 fraud verifier
  — re-derives pre/post Merkle state roots on-chain from each storage
  proof's leaf-hash + siblings + leafIndex and matches against the v1
  header's `PreStateRoot` / `ReplayedPostStateRoot`. Accepted v3
  payloads are credible without council arbitration.

All 21 contracts type-check against `Neo.SmartContract.Framework`. The
`Neo.Hub.Deploy` tool emits a topologically-sorted, dependency-resolved deploy bundle.

The principle behind NeoHub is **one suite of L1 trust roots for all L2s**. A new L2 does
not deploy a new bridge or a new verifier; it registers in `ChainRegistry` and inherits the
existing contracts.

---

## 4. L2 chain internals

Each L2 chain runs Neo 4 core plus a plugin suite and a small set of on-L2 native contracts.

<p align="center">
  <img src="docs/figures/architecture/l2-components.svg" alt="L2 chain components — Neo 4 core (bottom) + 8 L2 plugins (middle) + 7 L2 native contracts (top)" width="900">
</p>

A transaction's life on an L2 chain — from user submission, through dBFT
sequencing, batch execution, sealing, proving, L1 submission, and L1 finalization
— follows a 9-stage pipeline:

<p align="center">
  <img src="docs/figures/tx-lifecycle.svg" alt="Transaction lifecycle on a Neo L2 chain: 9 stages from user submission to L1 finality and eventual withdrawal claim" width="900">
</p>

### 4.1 Plugins (`Neo.Plugins.L2*`)

Eight node plugins extending `Neo.Plugins.Plugin`:

- **`Neo.Plugins.L2Batch`** — Hooks `Blockchain.Committed`; the sealing
  logic lives in a testable `BatchSealer`.
- **`Neo.Plugins.L2Settlement`** — Wires prover + settlement client;
  signs and submits sealed batches.
- **`Neo.Plugins.L2Bridge`** — Hosts `AssetRegistry` + deposit /
  withdrawal processors.
- **`Neo.Plugins.L2DA`** — Picks a DA writer by configured `DAMode`
  (in-memory, NeoFS-like, L1, External, DAC).
- **`Neo.Plugins.L2Prover`** — Hosts an `IL2Prover` for the configured
  `ProofType`.
- **`Neo.Plugins.L2Rpc`** — Implements 10 L2 RPC methods (see §6 of
  `doc.md`); incl. `getsecuritylabel` for the §16.2 5-dimension label.
- **`Neo.Plugins.L2Gateway`** — Phase-5 proof aggregation entry point.
- **`Neo.Plugins.L2Metrics`** — Telemetry composition root: shared
  `IL2Metrics` sink + Prometheus HTTP endpoints.

### 4.2 Native L2 contracts

Six on-L2 native contracts, deployed identically on every L2:

- `L2BridgeContract` — mint / burn bridged assets; receives `MintInstruction` from the bridge plugin.
- `L2MessageContract` — emit / consume cross-chain messages.
- `L2BatchInfoContract` — exposes `chainId`, `batchNumber`, L1 finalized height to apps.
- `L2FeeContract` — sequencer / prover / DA fee management.
- `L2PaymasterContract` — stablecoin / sponsored fees.
- `L2SystemConfigContract` — config synced from NeoHub.

Adjusted (not new) native contracts: `GAS` (bridge-controlled supply on L2), `NEO` (bridged
but governance stays on L1), `Oracle` (local or via L1 pull), `Policy` (local fee control;
bridge / security are NeoHub-controlled).

### 4.3 Chain modes

Each L2 declares one of four modes via `ChainRegistry.securityLevel`:

| Mode                 | DA           | Proof             | Trust assumption                              |
| -------------------- | ------------ | ----------------- | --------------------------------------------- |
| `SidechainMode`      | local        | none / multisig   | Trust the sequencer committee                 |
| `L2RollupMode`       | L1 / NeoFS   | optimistic / ZK   | Trust the verifier (and the challenge window) |
| `L2ValidiumMode`     | DAC          | optimistic / ZK   | Trust the DAC + the verifier                  |
| `L1Mode`             | self         | self              | Trust Neo N3 / Neo 4 L1 itself                |

This is on-chain so users can read the chain's actual security level via the
`getsecuritylevel` RPC (`§14.1` of `doc.md`).

### 4.4 Durable state — `IL2KeyValueStore`

Six off-chain components carry state that must survive a restart: the keyed state
store, RPC withdrawal/message proofs, finalized message proofs, consumed forced-
inclusion nonces, sequencer committee membership (with mid-flight exit windows),
and DA payloads. An in-memory dict is fine for tests but unacceptable in production
— a sequencer mid-exit losing its `ExitsAtUnixSeconds` deadline on restart could
re-admit a sequencer that should be in cooldown, or fail to finalize an exit that
already passed its window.

Solution: an explicit `IL2KeyValueStore` abstraction (`Put` / `Get` / `Delete` /
`Contains` / `EnumeratePrefix` / `Count` / `IDisposable`) with two implementations:

- `InMemoryKeyValueStore` — `SortedDictionary<byte[], byte[]>` with lexicographic
  ordering. Devnet / test default.
- `RocksDbKeyValueStore` — RocksDB 10.10.1 with snappy compression. Production default.

Each stateful component takes an optional `IL2KeyValueStore` ctor overload and an
ownership flag; the bare default ctor still works (in-memory) for backwards
compatibility. Devnet's `--data-dir <path>` flag wires four of these stores
(state / rpc-proofs / sequencer / da) under one root automatically. See
[`docs/persistence.md`](docs/persistence.md) for the operator wiring recipes.

### 4.5 Invariant audit — `ChainAuditor` + `IAuditCheck`

Settlement gives canonical batches; proofs cryptographically bind state transitions; DA
keeps payloads recoverable. None of those individually answer the operator's day-2
question: *"is the chain still well-formed?"* `Neo.L2.Audit.ChainAuditor` composes a
sequence of `IAuditCheck` invariants and runs them over a batch sequence on a
periodic schedule. Six built-in checks ship:

- **`ContinuityCheck`** — inter-batch state-root continuity, monotonic batch numbers, non-overlapping block ranges.
- **`NoZeroProofCheck`** — flags batches with `ProofType.None` or empty proof bytes (soft-sealed but never proved).
- **`ProofValidityCheck`** — re-runs the cryptographic verifier against each commitment's public inputs.
- **`PublicInputHashConsistencyCheck`** — pins that the stored `PublicInputHash` matches what the commitment fields hash to (catches tampered submissions).
- **`BatchRangeCheck`** — intra-batch invariants (`firstBlock <= lastBlock`, `batchNumber >= 1`).
- **`DAAvailabilityCheck`** — pings each batch's `DACommitment` against the configured DA layer's `IsAvailableAsync`.

Failures bump `l2.audit.failures` for ops dashboards; the auditor catches buggy
custom checks (`Exception` thrown from `RunAsync`) and converts them to a failure
finding so one bad check doesn't abort the whole pass. Mixed-chainId batch lists
are rejected with `ArgumentException` upstream of the per-check pipeline.

---

## 5. Proof system

The settlement hot path — from L2 batch execution through proof generation,
L1 submission, and on-chain verification — is the canonical dataflow that
binds a chain's L2 state to its L1 trust roots:

<p align="center">
  <img src="docs/figures/architecture/settlement-sequence.svg" alt="Settlement hot-path sequence — 5 actors (L2 Blockchain, L2BatchPlugin, BatchSealer, prover daemon, SettlementManager). Block.Committed → tx batch + post-state-root → BatchSealer constructs canonical BatchCommitment → BatchPayload to prover daemon → SP1 zkVM proves execute_batch → validity_proof + vk back → SubmitBatch to SettlementManager → VerifierRegistry.VerifyCommitment dispatch → BatchSubmitted event" width="900">
</p>

### 5.1 What gets proved

Proof targets are **not** the C# node binary. They are the deterministic L2 state-transition
function:

```
ApplyBatch(preStateRoot, orderedTxs, l1Messages, blockContext)
  → (postStateRoot, receiptRoot, withdrawalRoot, l2ToL1MessageRoot, l2ToL2MessageRoot)
```

Public inputs (committed in `L2BatchCommitment.publicInputHash`):

```
chainId, batchNumber, preStateRoot, postStateRoot, txRoot, receiptRoot,
withdrawalRoot, l2ToL1MessageRoot, l2ToL2MessageRoot, l1MessageHash,
daCommitment, blockContextHash
```

Witness: ordered txs, contract bytecode, storage read/write paths, native-contract state
witness, L1 messages consumed, DA data, execution trace.

### 5.2 Three-stage progression

```
Stage 0 — Multisig attestation  (production-usable from day 1)
Stage 1 — Optimistic + bisection-game (the fraud-proof flow)
Stage 2 — ZK validity proof    (NeoVM 2 / RISC-V via SP1)
```

The verifier registry on L1 dispatches by `ProofType`; the same `L2BatchCommitment` shape
carries any of the three. A chain progresses by changing its registered verifier — no L2
plugin code or L2 contract changes required.

- **Stage 0 — `AttestationVerifier`.** Producer: `AttestationProver` +
  `ISignerSet`. Status: production-ready; M-of-N secp256r1 over
  canonical public-input bytes.
- **Stage 1 — `OptimisticVerifier`.** Producer: `OptimisticProofPayload`
  + sequencer signature. Status: Stage-1 verifier; `BisectionGame` for
  log-N narrowing of disputed tx.
- **Stage 2 — `RiscVZkVerifier`.** Producer: `prove-batch daemon` (real,
  out-of-process) + `MockRiscVProver` (in-process test seam). Status:
  real Neo N3 VM proven via SP1 6.0; end-to-end queue → daemon → verify
  pipeline validated.

Aggregated proofs (Phase 5 Gateway) reuse the same registry — `ProofType.Aggregated` plus a
backend tag identifies the recursive scheme used.

---

## 6. Asset model

- **Canonical GAS lives only on Neo N3 / Neo 4 L1.** L2s cannot issue independent canonical
  GAS; what looks like GAS on an L2 is a bridge-locked representation.
- **L2 fees default to bridged GAS.** Paymasters in `L2PaymasterContract` allow stablecoin
  fees and sponsored transactions.
- **NEO can be bridged**, but governance power stays on L1 — voting is computed at L1.
- **NEP-17 tokens** are mapped 1:1 via `TokenRegistry`. Per-asset config: `lockMint` (lock
  on L1, mint on L2) or `mintBurn` (canonical asset minted on L2, burned to release on L1).

This is invariant across chains: there is no per-L2 fork of the asset model.

---

## 7. Cross-chain messaging — Neo Connect

Three flows, all routed through the same `MessageRouter` + `(chainId, nonce)` replay-protected envelope:

<p align="center">
  <img src="docs/figures/architecture/cross-l2-messaging-sequence.svg" alt="Cross-L2 messaging sequence — source L2 emits via L2MessageContract, batch is finalized on NeoHub or Gateway, globalMessageRoot updates, relayer submits inclusion proof to target L2, target L2 native contract executes the message" width="900">
</p>

### 7.1 L1 → L2

```
NeoHub.MessageRouter.enqueueL1ToL2Message(chainId, target, payload)
  → L2 watches the L1 queue
  → L2 includes the message in the next batch
  → L2BatchCommitment.l1MessageHash commits to the consumed-set
  → L2 native contract executes the message
```

### 7.2 L2 → L1

```
L2 contract emits message via L2MessageContract
  → message hash → L2BatchCommitment.l2ToL1MessageRoot
  → batch finalized on NeoHub.SettlementManager
  → user submits Merkle proof to NeoHub to consume the message on L1
```

### 7.3 L2 → L2

```
Source L2 emits via L2MessageContract
  → batch finalized on NeoHub or Gateway
  → Gateway's globalMessageRoot updated
  → relayer submits inclusion proof to target L2
  → target L2 native contract executes the message
```

### 7.4 Cross-chain bundle

A user-facing primitive: a single transaction whose effect spans multiple L2s. Internally
implemented as multiple coordinated messages plus a relayer; user sees one tx hash and one
sign-flow. Detailed in `doc.md` §10.4.

---

## 8. Data availability

Three tiers, on-chain labeled in `ChainRegistry.daMode`:

<p align="center">
  <img src="docs/figures/architecture/cross-tier-verification.svg" alt="Cross-tier verification chain — how data flows and is verified across L2 execution, sealing, DA layer, and L1 settlement, with the verification step at each tier boundary" width="900">
</p>

| DA mode      | Cost   | Security                                   | Recommended for                       |
| ------------ | ------ | ------------------------------------------ | ------------------------------------- |
| `L1`         | high   | inherits L1 (Neo N3 / Neo 4)               | RWA, stablecoin, high-value DeFi      |
| `NeoFS`      | low    | NeoFS replication + L1-recorded commitment | game, social, enterprise              |
| `External`   | low    | user-trusts external DA layer              | ecosystem-specific (e.g. Celestia)    |
| `DAC`        | lowest | committee attestation only                 | approved-list chains; visibly labeled |

`MetricsEmittingDAWriter` wraps each DA backend with `mode`-tagged Prometheus metrics
(`l2.da.published`, `l2.da.publish_latency_ms`, `l2.da.publish_failures`) so operators can
compare DA backends on the same dashboard.

The `IDAWriter` contract is unchanged across modes; the DA mode determines which concrete
implementation gets injected at plugin-configure time.

---

## 9. Neo Gateway — proof aggregation

Phase 5 introduces an optional aggregation layer mirroring ZKsync Gateway. The Gateway:

<p align="center">
  <img src="docs/figures/proof-aggregation.svg" alt="Multi-L2 proof aggregation — N L2 batch commitments reduced through log(N) pairwise rounds by the Neo Gateway's BinaryTreeAggregator. Each round invokes a pluggable IRoundProver.Combine. The single root commitment is then submitted to L1 SettlementManager in one call." width="900">
</p>

- Collects `L2BatchCommitment` plus proof from multiple L2 chains.
- Aggregates them via `BinaryTreeAggregator` over `IRoundProver`-implemented combine
  rounds (log-N rounds; default `PassThroughRoundProver` is a hash combiner; production
  swaps in SP1 Compress, Halo2 fold, or a Risc-Zero accumulator).
- Maintains the `globalMessageRoot` for L2-to-L2 messages.
- Submits one aggregated commitment to NeoHub.

**Critical invariant: the Gateway does NOT custody assets.** Assets stay locked in
NeoHub.SharedBridge throughout; the Gateway moves only proofs and message roots.

---

## 10. Censorship resistance & forced inclusion

Sequencer censorship is the canonical L2 attack. Three layered defenses:

<p align="center">
  <img src="docs/figures/forced-inclusion.svg" alt="Forced inclusion + censorship slashing sequence — user posts forced tx on L1, L2 batcher polls and drains pending entries, sequencer censors past deadline, CensorshipDetector observes overdue, operator submits ReportCensorship, SequencerBond slashes responsible sequencer's bond and pays the reporter" width="900">
</p>

1. **Forced inclusion queue** (`NeoHub.ForcedInclusion`, `Neo.L2.ForcedInclusion`,
   `Neo.L2.Censorship`). A user can post a tx directly to L1 and assert a deadline. The
   sequencer must include the tx in a batch before the deadline elapses. Missing the
   deadline produces a `CensorshipReport` (`Neo.L2.Censorship.CensorshipDetector`) usable
   to slash via `NeoHub.SequencerBond`.

2. **Sequencer bonds** (`NeoHub.SequencerBond`, `NeoHub.SequencerRegistry`). Every active
   sequencer committee member posts collateral; censorship reports slash the responsible
   member's bond.

3. **Escape hatch** (`NeoHub.EmergencyManager`). On confirmed sequencer-side liveness
   failure, governance can pause the L2 and allow direct-from-L1 withdrawal proofs over
   the last finalized state root.

These three together mean: **no individual sequencer can permanently exclude a user's
transaction from a chain that is alive at all.**

---

## 11. Governance

Three layers:

| Layer       | Authority                                              | What it controls                                                              |
| ----------- | ------------------------------------------------------ | ----------------------------------------------------------------------------- |
| L1          | Neo Governance / Council / NEO holder referendum       | NeoHub upgrade, verifier registry, bridge upgrade, emergency pause, L2 admission policy |
| L2 local    | The L2's own governance contract                       | Sequencer committee, local fee policy, app-chain params, DA mode (within approved range) |
| App         | Each dApp / RWA issuer / stablecoin policy             | Per-app rules, KYC list, enterprise permissioning                              |

Every L2 must publish security labels per `doc.md` §16.2: securityLevel
(`SecurityLevel` enum — Sidechain / Settled / Optimistic / Validity / Validium),
daMode (`DAMode` enum — L1 / NeoFS / External / DAC), gatewayEnabled (Phase-5
aggregation participation), sequencerModel (Centralized / DbftCommittee /
Decentralized), exitModel (Permissionless / Delayed / OperatorAssisted).
Users query the full set via `getsecuritylabel` (or each dimension singly via
`getsecuritylevel` / `getsequencerModel` / `getExitModel` / `getDAMode` /
`getGatewayEnabled` / `getPermissionlessExit` on the on-chain
`ChainRegistry`); UIs should surface them prominently.

---

## 12. Threat model

Ten threat classes, each with a named mitigation. Detailed in `doc.md` §17.

| #  | Threat                          | Primary mitigation                                                  |
| -- | ------------------------------- | ------------------------------------------------------------------- |
| 1  | Sequencer censorship            | Forced inclusion + bond slashing + escape hatch (§10)               |
| 2  | Invalid state root              | ZK validity proof (Phase 4) or optimistic challenge (Phase 3)       |
| 3  | Bridge exploit                  | Lock-mint vs burn-unlock invariants; rate limits; emergency pause   |
| 4  | Replay attack (cross-chain)     | `(chainId, nonce)` envelope on every message                        |
| 5  | DA unavailability               | Public DA security label in `ChainRegistry`; escape hatch on opacity |
| 6  | Malicious validator committee   | Sequencer bonds; rotate-out via `SequencerRegistry`                 |
| 7  | Prover bug                      | `VerifierRegistry` upgrade behind governance delay + security council veto |
| 8  | Verifier upgrade attack         | Same governance-delay + veto path as prover bugs                    |
| 9  | Message duplication             | `MessageRouter` per-pair `(chainId, nonce)` dedup                    |
| 10 | L2 contract bug                 | Local L2 emergency pause + `EmergencyManager` escape hatch           |

The codebase additionally enforces dozens of defensive invariants — see CHANGELOG iter
67 onward for the catalog. Examples: cross-batch withdrawal-nonce dedup, public-input
hash equality between prover and settler, signer-set deduplication before signature
verification, exception-typed metric tags so dashboards can separate contract violations
from network failures.

---

## 13. Phased rollout

Each phase shifts a chain's *security label* one rung up the trust ladder
— from sequencer-trusted sidechain through optimistic rollup to ZK
validity. The L1 contracts and L2 plugin set are stable across phases;
the *verifier* changes:

<p align="center">
  <img src="docs/figures/trust-spectrum.svg" alt="Per-chain security spectrum (ChainRegistry.securityLevel) — 4-position trust gradient from sidechain (full sequencer trust) through settled L2 (DA + state roots) and optimistic rollup (fraud-proof challenge window) to ZK validity (cryptographic finality)" width="900">
</p>

Per `doc.md` §18:

| Phase | Goal                                | Security label (visible to users) |
| ----- | ----------------------------------- | --------------------------------- |
| 0     | Neo 4 sidechain PoC                 | sidechain                         |
| 1     | NeoHub v0 + SharedBridge            | connected sidechain               |
| 2     | Batch settlement                    | settled L2                        |
| 3     | Optimistic challenge window         | optimistic rollup                 |
| 4     | NeoVM 2 / RISC-V validity proof     | zk validity rollup                |
| 5     | Neo Gateway aggregation + L2-L2     | Neo Elastic Network               |
| 6     | Neo Stack CLI + templates           | (permissionless launch)            |

Each phase shifts the security label one rung up the trust ladder. The L1 contracts and
the L2 plugin set are stable across phases; the *verifier* changes.

---

## 14. Comparison to other rollup stacks

| Aspect                  | Neo Elastic Network              | ZKsync Elastic Chain    | OP Stack                      | Arbitrum Orbit                  |
| ----------------------- | -------------------------------- | ----------------------- | ----------------------------- | ------------------------------- |
| Execution kernel        | Neo 4 (NeoVM / NeoVM 2)          | EraVM (zkEVM)           | EVM (op-geth)                 | EVM (Nitro)                     |
| L1 settlement contracts | NeoHub (21 contracts)            | BridgeHub + SharedBridge + V.R. | OptimismPortal etc.    | RollupCore + Inbox              |
| Sequencer               | dBFT 2.0 committee (M-of-N)      | Centralized (with FCFS) | Centralized (decentralizing)  | Centralized (decentralizing)    |
| Proof regimes           | Multisig → Optimistic → ZK       | ZK (production)         | Optimistic (Cannon)           | Optimistic (BOLD challenge game) |
| Native interop          | L1↔L2 + L2↔L2 + bundles          | Native L2-L2 via Gateway | Superchain interop (early)   | Cross-chain Inbox messaging     |
| DA tiers                | L1 / NeoFS / External / DAC      | Validium + GW DA        | EthDA / AnyTrust              | AnyTrust + ETH DA               |
| Gas token               | Bridged GAS canonical            | Custom per chain        | ETH (no custom-base support yet) | Configurable                |
| Governance              | Neo Council + NEO holder referendum | DAO + security council | Optimism Foundation + Council | Arbitrum DAO + Security Council |

The headline architectural choice is **borrowing Elastic Chain's shared-bridge pattern but
swapping in Neo's primitives** — dBFT 2.0 finality (single-block confirms with no MEV
auction needed at L2 level), NEP-17 (no need for per-chain ERC-20 deployments), NeoVM 2 /
RISC-V (smaller proving target than zkEVM), and NeoFS DA (cheaper than blob DA for non-L1
tiers).

---

## 15. Glossary

| Term                       | Meaning                                                                                       |
| -------------------------- | --------------------------------------------------------------------------------------------- |
| **L2 chain**               | A rollup / sidechain / validium running Neo 4 core + the L2 plugin set, registered in NeoHub. |
| **NeoHub**                 | The 13-contract L1 suite shared by every L2.                                                  |
| **Neo Gateway**            | Optional Phase-5 proof-aggregation + global-message-root layer.                               |
| **Neo Connect**            | The cross-chain messaging system (L1↔L2, L2↔L2, bundles).                                     |
| **L2BatchCommitment**      | The per-batch on-chain object: roots, public-input hash, proof type, proof bytes.             |
| **publicInputHash**        | `Hash256` over the canonical encoding of the batch's public inputs; tying proof to commitment. |
| **withdrawalRoot**         | Merkle root of withdrawal-leaf hashes; users prove inclusion to claim L1 assets.              |
| **l2ToL1MessageRoot / l2ToL2MessageRoot** | Per-class outbox Merkle roots committed in the batch.                          |
| **daCommitment**           | Hash committing to the batch's DA blob; bound by DA mode.                                     |
| **Forced inclusion**       | L1-side queue any user can post to; sequencer must include before deadline.                   |
| **Bisection game**         | Phase-3 fraud-proof flow: log-N narrowing to a single disputed transaction's pre/post state.  |
| **Security label**         | Public on-chain claim of a chain's DA / proof / sequencer model; `getsecuritylevel` RPC.     |
| **Escape hatch**           | Operator-of-last-resort path for users to withdraw if the sequencer fails. Owned by `EmergencyManager`. |

---

For implementation specifics, see `IMPLEMENTATION_STATUS.md`. For the master spec, see
`doc.md`. For the narrative tour through the codebase, see
`docs/architecture-walkthrough.md`.
