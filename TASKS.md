# Neo N4 — Task Split

Open work between **Neo N4 Core** (upstream `neo-project/neo`) and **this Elastic Network repo** (`neo4`). Counts verified against the codebase on 2026-05-16.

---

## Neo N4 Core — `neo-project/neo`

Items that require modifications to the canonical Neo 4 protocol. Cannot be solved in this repo.

### Critical — required for any L2 to function

- [ ] §6 `ChainMode` enum + activation hooks (`L1Mode` / `SidechainMode` / `L2RollupMode` / `L2ValidiumMode`)
- [ ] §13.2 GAS supply gating — only bridge-attested mint/burn when `ChainMode != L1Mode`
- [ ] §13.2 NEO governance restriction — disable vote / registerCandidate when `ChainMode != L1Mode`
- [ ] §13.2 Policy contract L2-mode hooks — `feeFactor` / `storagePrice` readable; mutation restricted

### High — required for production deployment

- [ ] §14.1 `RpcServer` plugin source availability (unblocks the `[RpcMethod]` wrapper for the 10 L2 RPC methods)
- [ ] `OnPersist` / `PostPersist` ChainMode awareness — make genesis bootstrap a first-class L2-mode API
- [ ] §13.2 Optional Oracle gating — let an L2 skip the Oracle native contract

### Medium — full Elastic Network

- [ ] dBFT consensus selector hook — accept pluggable validator-set providers
- [ ] `ApplicationEngine` restricted-state mode — for v4 fraud verifier's on-L1 re-execution
- [ ] NeoVM2 / RISC-V execution mode opt-in (`ChainMode.L2RiscV`)

**Subtotal: 10**

---

## This repo — `neo4`

Items inside the Elastic Network consolidation. Owned here, not blocked on upstream.

### Substantive findings

- [x] ~~**State-tree Merkle convention unification**~~ — `KeyedStateMerkleTree.ComputeRoot` / `Prove` / `Verify` now delegate to `MerkleTree` (Neo classic). New regression test `UT_KeyedStateMerkleTree_NeoClassicParity` pins agreement with the on-chain `SettlementManager.VerifyStateLeafWithProof` convention across 10 cardinalities incl. odd cases. CHANGELOG entry + `docs/spec-gap-plan.md` §state-tree-convention added.

### Minor reviewer-nits

- [x] ~~**CEI ordering in `OptimisticChallenge.Challenge`**~~ — `Storage.Put(AcceptedFraudKey...)` moved before the external slash calls.
- [x] ~~**`MessageRouter.PrefixGlobalRoot = 0x05`**~~ — unused `const` replaced with explicit reservation comment that documents the byte as held for Phase-5 Gateway global root.
- [x] ~~**RocksDB doc/code drift**~~ — XML remarks now accurately describe default async-WAL behavior + operator override path.

### Spec-gap items

- [ ] **§8-witness-canonical** (deferred) — re-evaluate `WitnessRecord` layout once a real ZK prover beyond SP1 targets it
- [ ] **§v4-fraud-verifier** (deferred) — restricted-state on-L1 re-execution; blocked on upstream `ApplicationEngine` restricted-snapshot mode (see Cross-repo below)

### Documentation

- [x] ~~State-tree convention note~~ — added as `docs/spec-gap-plan.md §state-tree-convention` (now superseded by the code unification; entry kept as the historical record)
- [x] ~~`ExternalBridgeStubVerifier` deployment-refusal note~~ — added to `docs/external-bridge-roadmap.md` Phase A
- [x] ~~V4 fraud verifier roadmap sketch~~ — added to `docs/spec-gap-plan.md` §v4-fraud-verifier

### Production-readiness examples (operator seams; framework already supplies the interface)

- [ ] `NeoFsClientDAWriter` example wired against actual NeoFS gRPC SDK
- [ ] `JsonRpcL1DAWriter` worked example against a real Neo N3 node
- [ ] `RpcSettlementClient.SignAndSendAsync` worked example with KMS / HSM
- [ ] dBFT plugin wiring example using `SequencerRegistry` as the validator-set source

### Future features

- [ ] `SP1CompressRoundProver` once SP1 toolchain integration matures (`IRoundProver` seam is ready)
- [ ] L1 restricted-state re-executor (blocked on the core `ApplicationEngine` item)

**Subtotal: 14 (7 closed in this iteration, 7 remain — 4 operator examples + 2 future features + 1 spec-gap-deferred)**

---

## Cross-repo coordination

Items that need work in both repos and PR coordination across them.

- [ ] **L2 mode bootstrap handoff** — deprecate `NeoVMGenesisBootstrap` in this repo when core ships `ChainMode.L2RollupMode` initialization
- [ ] **RpcServer source migration** — file the `[RpcMethod]` wrapper partial class against `L2RpcMethods` once core exposes `RpcServer` source
- [ ] **NeoVM2 / RISC-V mode** — migrate `Neo.L2.Executor.RiscV` from P/Invoke binding to first-class core integration once core ships `ChainMode.L2RiscV`

**Subtotal: 3**

---

## Total: 27 actionable tasks (7 closed)

| Bucket | Total | Closed | Remaining |
|--------|------:|-------:|----------:|
| Neo N4 Core | 10 | 0 | 10 |
| This repo | 14 | 7 | 7 |
| Cross-repo | 3 | 0 | 3 |
| **Total** | **27** | **7** | **20** |

The 7 closed items in this iteration are: 1 substantive (state-tree Merkle convention) + 3 minor nits (CEI ordering, PrefixGlobalRoot reservation, RocksDB doc) + 3 docs (state-tree note, stub-verifier note, v4 sketch). The 20 remaining are split between **0 in-repo behavior tasks**, 4 production-readiness *examples* (operator-supplied seams the framework already exposes), 2 future features (toolchain-blocked), 1 deferred spec-gap, 10 upstream core items, and 3 cross-repo coordination items.

---

## Ownership rule

Anything touching **NeoVM execution semantics, native contracts, dBFT consensus, or `RpcServer` plugin internals** belongs in **core**. Everything else — L1 contract suite (NeoHub), off-chain libraries, watchers, plugins, tools, SDKs, docs — belongs in this repo.

---

## Validation snapshot

Reference state after the closed-iteration fixes:

- Tests green: **1373 .NET + 156 cross-language base + 2 real-CPU SP1 = 1531** (the +11 delta from 1362 came from the new `UT_KeyedStateMerkleTree_NeoClassicParity` regression suite added with the state-tree Merkle convention fix; the 2 SP1 ignored tests are verified end-to-end via `cargo test --release --tests -- --ignored` in `bridge/neo-zkvm-host/`)
  - 1373 .NET across 33 projects (1362 baseline + 11 new `UT_KeyedStateMerkleTree_NeoClassicParity` rows)
  - 156 cross-language (15 TS + 10 Rust SDK + 8 SP1 guest + 103 watcher + 20 Foundry)
- Build: 79 projects, 0 errors, 0 warnings (with `nccs` on PATH)
- Smart contracts: 28 NeoHub/L2Native + 2 sample dApps + 1 sample executor → all compile fresh
- Devnet 5-batch E2E: green, state root unchanged (`KeyedStateRootOracle` path was already Neo classic)
- Findings ledger: **0 substantive + 0 minor nits open** — all 4 findings from the 40-iteration sweep now closed
