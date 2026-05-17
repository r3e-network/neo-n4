# Architecture: L1 vs L2 division of responsibilities

> **The principle:** put on L1 only what *must* be globally agreed +
> economically secured; everything else lives on L2 where it can scale.
>
> This document explains *why* each component lives where it does,
> calls out a few places where the boundary is currently fuzzy, and
> lists concrete tasks to sharpen the division.

## Table of contents

1. [The dividing principle](#1-the-dividing-principle)
2. [What L1 does (and why it has to)](#2-what-l1-does-and-why-it-has-to)
3. [What L2 does (and why it can)](#3-what-l2-does-and-why-it-can)
4. [The bridge between them](#4-the-bridge-between-them)
5. [Decision rules: where does a new feature go?](#5-decision-rules-where-does-a-new-feature-go)
6. [Current contract layout audited against the principle](#6-current-contract-layout-audited-against-the-principle)
7. [Task list — sharpening the boundary](#7-task-list--sharpening-the-boundary)

---

## 1. The dividing principle

<p align="center">
  <img src="figures/architecture/dividing-principle.svg" alt="L1 equals canonical agreement plus economic security (slow, expensive, globally consistent — all L2s share this floor of truth). L2 equals execution plus state plus throughput (fast, cheap, locally consistent — anchored to L1)" width="900">
</p>

A piece of state or logic belongs on **L1** if and only if at least
one of these is true:

1. **Multiple L2s must agree on it.** Cross-L2 invariants live where
   all L2s can read the same source of truth. (Example: which chain
   ids exist, which assets are recognized, which proofs are valid.)
2. **Its security must come from L1's economic security.** Slashable
   bonds, escrowed assets, and emergency overrides need the L1
   validator set behind them. (Example: sequencer bonds, asset
   escrow, governance.)
3. **It defines the trust boundary.** What gets accepted as a valid
   batch / proof / withdrawal is *the* trust decision; that decision
   has to be made on L1 or it's circular.

Everything else — execution, mempools, receipts, local fees, app-
specific logic — runs on **L2** because L1 cannot scale to it.

---

## 2. What L1 does (and why it has to)

The 22 production NeoHub contracts (plus 1 testing stub) cluster into
six concerns. Each entry below names *the property that forces it
onto L1*:

<p align="center">
  <img src="figures/architecture/l1-concerns.svg" alt="The 23 NeoHub L1 contracts grouped into 6 concerns plus 2 specialized verifier slots. Settlement (SettlementManager + VerifierRegistry) defines the trust boundary. Bridge (SharedBridge + TokenRegistry + ChainRegistry) escrows assets. Messaging (MessageRouter + DARegistry + DAValidator + L1TxFilter) is the cross-L2 routing and data-availability arbiter. Security (SequencerRegistry + SequencerBond + ForcedInclusion + OptimisticChallenge) gates slashable bonds and anti-censorship. Governance + Emergency (GovernanceController + EmergencyManager) handle staged upgrades + escape hatch. External bridge (6 contracts) is the cross-foreign-chain bridge. Plus 2 fraud-verifier reference slots: GovernanceFraudVerifier (v1/v2 governance-arbitrated) + RestrictedExecutionFraudVerifier (v3 trustless on-chain re-derivation)" width="900">
</p>

**Key observation about L1 contracts:** they hold *commitments* and
*permissions*, not bulk state. NeoHub's storage is intentionally
sparse — registry entries, balances, slashable-bond ledgers, and
proof-acceptance records. The cost of L1 storage forces this; the
trust model requires it.

---

## 3. What L2 does (and why it can)

The 10 native L2 contracts + 8 plugins per L2 chain cluster around
*execution* + *bulk state* + *throughput-bound work*:

<p align="center">
  <img src="figures/architecture/l2-concerns.svg" alt="What L2 does — 3 layers per chain. Top: 10 L2 native contracts (per-chain state — L2BridgeContract, L2NativeExternalBridge, BridgedNep17Contract, L2MessageContract, L2BatchInfoContract, L2FeeContract, L2PaymasterContract, L2AccountAbstraction, L2InteropVerifier, L2SystemConfigContract). Middle: 8 L2 plugins (per-chain runtime — L2Batch, L2Settlement, L2Bridge, L2DA, L2Prover, L2Rpc, L2Gateway, L2Metrics). Bottom: L2 execution kernel — Neo 4 core vendored as a git submodule, with dBFT 2.0 consensus, NeoVM, mempool, local state storage, and receipt generation" width="900">
</p>

**Key observation about L2:** L2 holds the **bulk state + the heavy
execution**. Bridges + messages are *summaries* of what happened on
L2, attested + posted to L1. The hot path of any user tx never
touches L1.

---

## 4. The bridge between them

The L1↔L2 boundary is itself a designed surface. Three flows cross
it:

<p align="center">
  <img src="figures/architecture/l1-l2-bridge.svg" alt="Three flows cross the L1↔L2 boundary: bridge (deposit + withdrawal), messaging (inbound + outbound), and settlement (batch commitment plus proof)" width="900">
</p>

**Each flow has a single canonical wire format** ([wire-formats
chapter](./architecture-wire-formats.md)). Both endpoints recompute
the hash from the bytes — no off-wire trust. This is what makes the
L1↔L2 connection *minimally trusting* despite L2 doing the heavy
lifting.

---

## 5. Decision rules: where does a new feature go?

When designing a new component, ask in order:

<p align="center">
  <img src="figures/architecture/l1-l2-decision-tree.svg" alt="Five-question decision tree for placing a new component on L1 or L2: Q1 cross-L2 read requirement, Q2 slashable / economic security, Q3 trust boundary, Q4 throughput exceeds L1 capacity, Q5 app-specific logic for one chain. Default falls through to L2 to keep L1 lean" width="900">
</p>

A useful sanity check: if you can answer *no* to all of (1), (2),
(3), and the throughput is bounded — putting it on L1 is probably
*premature centralization*.

---

## 6. Current contract layout audited against the principle

Going through each NeoHub contract + each L2Native contract against
the rules in §5:

- **`NeoHub.ChainRegistry`** L1 ✅ → L1 — Cross-L2 invariant (rule 1)
- **`NeoHub.SettlementManager`** L1 ✅ → L1 — Trust boundary (rule 3)
- **`NeoHub.VerifierRegistry`** L1 ✅ → L1 — Trust boundary (rule 3)
- **`NeoHub.SharedBridge`** L1 ✅ → L1 — Asset escrow (rule 2 — assets are on L1)
- **`NeoHub.TokenRegistry`** L1 ✅ → L1 — Cross-bridge invariant (rule 1)
- **`NeoHub.MessageRouter`** L1 ✅ → L1 — Cross-L2 invariant (rule 1)
- **`NeoHub.DARegistry`** L1 ✅ → L1 — Cross-L2 invariant (rule 1)
- **`NeoHub.DAValidator`** L1 ✅ → L1 — Data-availability trust boundary (rule 3)
- **`NeoHub.L1TxFilter`** L1 ✅ → L1 — L1→L2 admission policy before canonical enqueue (rule 3)
- **`NeoHub.SequencerRegistry`** L1 ✅ → L1 — Cross-L2 invariant (rule 1)
- **`NeoHub.SequencerBond`** L1 ✅ → L1 — Slashable economic security (rule 2)
- **`NeoHub.ForcedInclusion`** L1 ✅ → L1 — Anti-censorship gate (rule 3)
- **`NeoHub.OptimisticChallenge`** L1 ✅ → L1 — Trust boundary + slashing (rules 2+3)
- **`NeoHub.GovernanceController`** L1 ✅ → L1 — Slow upgrade path (rule 3)
- **`NeoHub.EmergencyManager`** L1 ✅ → L1 — Out-of-band pause (rule 3)
- **`NeoHub.GovernanceFraudVerifier`** L1 ✅ → L1 — Verifier slot — same as `VerifierRegistry`
- **`NeoHub.RestrictedExecutionFraudVerifier`** L1 ✅ → L1 — Verifier slot
- **`NeoHub.MpcCommitteeVerifier`** L1 ✅ → L1 — Trust boundary for foreign chains
- **`NeoHub.MpcCommitteeFraudVerifier`** L1 ✅ → L1 — Slashing — same trust + economic argument
- **`NeoHub.ExternalBridgeRegistry`** L1 ✅ → L1 — Cross-foreign-chain invariant
- **`NeoHub.ExternalBridgeEscrow`** L1 ✅ → L1 — Asset escrow
- **`NeoHub.ExternalBridgeBond`** L1 ✅ → L1 — Slashable economic security
- **`NeoHub.ExternalBridgeStubVerifier`** L1 🟡 → testing only — **not registrable through `ExternalBridgeRegistry` production bridge kinds**

- **`L2Native.L2BridgeContract`** L2 ✅ → L2 — Per-L2 NEP-17 wrapped state (rule 4)
- **`L2Native.L2MessageContract`** L2 ✅ → L2 — Per-L2 inbox/outbox
- **`L2Native.L2BatchInfoContract`** L2 ✅ → L2 — L2-local view of L1's batch state
- **`L2Native.L2FeeContract`** L2 ✅ → L2 — L2-local fee config (rule 5)
- **`L2Native.L2PaymasterContract`** L2 ✅ → L2 — L2-app-specific (rule 5)
- **`L2Native.L2SystemConfigContract`** L2 ✅ → L2 — L2-local mirror of L1 chainConfig
- **`L2Native.ExternalBridgeContract`** L2 ✅ → L2 — Per-L2 wrapped foreign-asset state (rule 4)
- **`L2Native.BridgedNep17Contract`** L2 ✅ → L2 — Per-L2 canonical token representation (rule 4)
- **`L2Native.L2AccountAbstraction`** L2 ✅ → L2 — Per-chain validator/paymaster entry point (rule 5)
- **`L2Native.L2InteropVerifier`** L2 ✅ → L2 — Local proof verification against mirrored global roots (rules 4+5)

**Findings:**

✅ **22 of 23 NeoHub contracts are correctly placed on L1** — each
satisfies at least one of rules 1, 2, or 3.

✅ **All 10 L2 native contracts are correctly placed on L2** — each is
per-chain state with no cross-L2 read requirement.

🟡 **`ExternalBridgeStubVerifier` is L1 but is testing-only.** A
production NeoHub deployment must not register this verifier in
`ExternalBridgeRegistry`; this is now code-enforced because the registry
accepts only bridge kinds `1` (MPC), `2` (Optimistic), and `3` (ZK), while
the stub reports bridge kind `0`.

---

## 7. Task list — sharpening the boundary

Concrete, actionable tasks that improve the L1/L2 division. Ordered
roughly by impact.

### High priority

- [x] **Gate `ExternalBridgeStubVerifier` against production deploys.**
  `NeoHub.ExternalBridgeRegistry.WriteVerifier` refuses bridge kind `0`,
  so the devnet stub cannot be registered through the production verifier
  registry path. Deploy tooling also documents registering the MPC verifier
  with `bridgeKindMpc=1` instead of the stub.

- [ ] **Document each NeoHub contract's storage budget.** Per-contract
  README naming the storage keys + their max sizes + amortized
  cost-per-L2-batch. L1 storage is the scarce resource; making the
  budget visible per contract would let operators see if a future
  feature is over-charging L1.

- [ ] **Add a `governance-rationale` field to `chainConfig`.** Per-chain
  configurable string referencing why this L2 chose its
  `securityLevel` / `daMode` / `exitModel` combination. Forces
  operators to articulate the design when registering, audited
  on-chain forever.

### Medium priority

- [ ] **Add an "L1 footprint" check to `neo-stack validate`.** The
  command currently checks JSON sanity of `chain.config.json`.
  Extend it to estimate per-batch L1 gas (BatchSerializer size +
  proof verification cost + withdrawal proof posts) so operators
  see the L1 cost before deploying.

- [ ] **Consolidate the three external-bridge stub variants** —
  `ExternalBridgeStubVerifier` + the test-only paths in
  `MpcCommitteeVerifier` + the watcher's `StubSignAndSend`. They're
  currently named differently across crates; a single
  `--testnet-only` feature flag would make their non-production
  status uniform.

- [ ] **Make `L2NativeExternalBridgeContract` storage layout queryable
  from L1.** Current architecture is asymmetric: L1's
  ExternalBridgeEscrow holds the canonical record of consumed
  inbounds, but operators auditing per-L2 minted-token state must
  query each L2's NEP-17 contract separately. A per-(L2, foreignAsset)
  cumulative-mint counter on L1 (read-only mirror) would help.

### Low priority (design polish)

- [ ] **Promote rule (4) — throughput exceeds L1 capacity — to a
  measurable threshold.** Today it's described qualitatively. Pick
  an explicit ceiling (e.g. "if expected steady-state ≥ 1 tx/sec
  → L2") so future PRs have a clear cutoff.

- [ ] **Audit the L2 plugin set against the principle.** `L2Gateway`
  is per-L2 today, but Phase 5 will share aggregation across L2s.
  Is there a future L1 contract for the gateway's aggregation
  state? Document the trade-off.

- [ ] **Cross-link this document from `architecture-l2-lifecycle.md`
  and `architecture-trust-boundaries.md`.** Currently the lifecycle
  + trust docs reference what lives where but don't justify the
  division. This doc fills that gap; the others should link to it.

### Future / Phase D

- [ ] **ZK light client of foreign chains on L1.** Replaces the
  external-bridge committee model. Massive R&D — moves the
  external-bridge group from "trust = M-of-N committee" to
  "trust = math". The current 6-contract surface is intentionally
  designed to stay stable across this transition (only the
  registered verifier changes — see
  [`external-bridge-roadmap.md`](./external-bridge-roadmap.md)).

- [ ] **L1-anchored, L2-resident application registry.** Lets app
  developers register "this contract on L2-A is the same dApp as
  this contract on L2-B" so cross-L2 messages can route by
  application identity, not just chain id. Breaks rule 4 for
  application-id storage; ok IF the entries are sparse + bonded
  against spam.

---

## See also

- [`architecture-l2-lifecycle.md`](./architecture-l2-lifecycle.md) — system flow + 4-tier topology.
- [`architecture-wire-formats.md`](./architecture-wire-formats.md) — the bytes that cross the L1↔L2 boundary.
- [`architecture-trust-boundaries.md`](./architecture-trust-boundaries.md) — who verifies each cross-tier flow.
- [`architecture-glossary.md`](./architecture-glossary.md) — every contract/plugin/CLI defined in one place.
- [`launching-an-l2.md`](./launching-an-l2.md) — operator perspective on configuring an L2.
- [`security-model.md`](./security-model.md) — threats + mitigations at the L1/L2 boundary.
- [`doc.md`](../doc.md) — master spec (authoritative).
