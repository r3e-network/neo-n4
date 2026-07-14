# Neo N4 — Task Split

Open work between **Neo core forks** (`r3e-network/neo`, L1 branch `r3e/neo-n3-core`, L2 branch `r3e/neo-n4-core`) and **this Elastic Network repo** (`neo4`). Counts verified against the tracked revisions on 2026-07-14.

---

## Neo Core — `r3e-network/neo`

Items that require unavoidable Neo core changes. Implement them in the
correct branch of `r3e-network/neo`: `r3e/neo-n3-core` only for Neo N3 L1 node
behavior that cannot be a deployed contract or plugin, and `r3e/neo-n4-core`
for Neo 4 L2 core/native-contract behavior. Update the
`external/neo` submodule pointer in this repo only after L2-core integration
tests pass.

### Critical — required for any L2 to function

- [ ] §6 `ChainMode` enum + activation hooks (`L1Mode` / `SidechainMode` / `L2RollupMode` / `L2ValidiumMode`)
- [ ] §13.2 GAS supply gating — only bridge-attested mint/burn when `ChainMode != L1Mode`
- [ ] §13.2 NEO governance restriction — disable vote / registerCandidate when `ChainMode != L1Mode`
- [ ] §13.2 Policy contract L2-mode hooks — `feeFactor` / `storagePrice` readable; mutation restricted

### High — required for production deployment

- [x] ~~§14.1 `RpcServer` plugin source availability~~ — the tracked core contains the
  official `RpcServerPlugin.RegisterMethods` source, and `Neo.Plugins.L2Rpc` registers all
  10 methods through it; real Kestrel HTTP tests cover the adapter.
- [ ] `OnPersist` / `PostPersist` ChainMode awareness — make genesis bootstrap a first-class L2-mode API
- [ ] §13.2 Optional Oracle gating — let an L2 skip the Oracle native contract

### Medium — full Elastic Network

- [x] ~~dBFT consensus selector hook~~ — the tracked core's native governance selector reads
  the finalized validator set from `L2SystemConfigContract`; `neo-stack` synchronizes an
  operator-selected `ISequencerCommitteeProvider` result through a governed native transaction.
- [ ] `ApplicationEngine` restricted-state mode — for v4 fraud verifier's on-L1 re-execution
- [ ] NeoVM2 / RISC-V execution mode opt-in (`ChainMode.L2RiscV`)

**Subtotal: 10 (2 closed, 8 remain)**

---

## This repo — `neo4`

Items inside the Elastic Network consolidation. Owned here, not blocked on upstream.

### Substantive findings

- [x] ~~**State-tree Merkle convention unification**~~ — `KeyedStateMerkleTree.ComputeRoot` / `Prove` / `Verify` now delegate to `MerkleTree` (Neo classic). New regression test `UT_KeyedStateMerkleTree_NeoClassicParity` pins agreement with the on-chain `SettlementManager.VerifyStateLeafWithProof` convention across 10 cardinalities incl. odd cases. CHANGELOG entry + `docs/spec-gap-plan.md` §state-tree-convention added.

### Minor reviewer-nits

- [x] ~~**CEI ordering in `OptimisticChallenge.Challenge`**~~ — `Storage.Put(AcceptedFraudKey...)` moved before the external slash calls.
- [x] ~~**`MessageRouter.PrefixGlobalRoot = 0x05`**~~ — fully wired as `PublishGlobalRoot` / `GetGlobalRoot` with settlement-manager-witness gate, publish-once-per-epoch replay protection, non-zero-root enforcement, and `OnGlobalRootPublished` event. ZKsync `MessageRoot.sol` equivalent.
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
- [x] ~~dBFT plugin wiring example using `SequencerRegistry` as the validator-set source~~ —
  `docs/launching-an-l2.md` documents the provider → governed native transaction → finalized
  `L2SystemConfigContract` → unmodified DBFTPlugin path, and `neo-stack start-sequencer
  --sync-only --broadcast` implements it.

### Future features

- [ ] `SP1CompressRoundProver` once SP1 toolchain integration matures (`IRoundProver` seam is ready)
- [ ] L1 restricted-state re-executor (blocked on the core `ApplicationEngine` item)

### ZKsync Elastic Chain parity (from `docs/zksync-comparison.md`)

Gaps surfaced by the side-by-side ZKsync v29 comparison. See `docs/zksync-comparison.md`
for full mapping + rationale. Each item names a concrete neo4 location.

- [x] ~~On-chain global message root~~ — `MessageRouter.PublishGlobalRoot` (ZKsync `MessageRoot.sol` equivalent)
- [x] ~~Permanent restriction mechanism~~ — `GovernanceController.SetImmutableFlag` (ZKsync `PermanentRestriction` equivalent)
- [x] ~~`NeoHub.DAValidator`~~ — L1 DAC attestation validator implemented and wired into `SettlementManager` through `SetDARegistry` / `SetDAValidator`; `SubmitBatch` records DA tuples and `FinalizeBatch` enforces validation.
- [x] ~~`L2Native.BridgedNep17Contract`~~ — canonical bridge-controlled NEP-17 mint/burn template implemented for L1-asset mappings.
- [x] ~~`L2Native.L2AccountAbstraction`~~ — programmable AA entry point implemented with validator binding, nonce checks, magic-value `validateTransaction`, paymaster charge, and `executeTx`.
- [x] ~~Staged-upgrade timer in `GovernanceController`~~ — notice / execution / cool-down windows implemented with `GetProposalStage`, `IsInExecutionWindow`, and `MarkProposalExecuted`.
- [x] ~~Per-chain `IL1TxFilter` extension point~~ — `MessageRouter.SetL1TxFilter` and `NeoHub.L1TxFilter` implemented for sender/receiver/message-type pre-enqueue filtering.
- [x] ~~L2-side message verification helper~~ — `L2Native.L2InteropVerifier` implemented with mirrored global roots, L1 finalized-height check, Merkle proof verification, and local replay protection.
- [ ] Additional sample dApps — `Sample.Erc20PaymasterClient`, `Sample.MultisigAccount`, `Sample.GatedMint`, `Sample.CrossChainSwap`
- [ ] Go SDK — generate from the shared conformance surface; Python joins the other three typed SDK source implementations under the shared conformance gate

**Subtotal: 24 (16 closed, 8 remain — 3 operator examples + 2 future features + 1 spec-gap-deferred + 2 ecosystem items)**

---

## Cross-repo coordination

Items that need work in both repos and PR coordination across them.

- [ ] **L2 mode bootstrap handoff** — deprecate `NeoVMGenesisBootstrap` in this repo when core ships `ChainMode.L2RollupMode` initialization
- [x] ~~**RpcServer source migration**~~ — `L2RpcPlugin` now registers its official adapter
  directly through the tracked core's `RpcServerPlugin.RegisterMethods` API.
- [ ] **NeoVM2 / RISC-V mode** — migrate `Neo.L2.Executor.RiscV` from P/Invoke binding to first-class core integration once core ships `ChainMode.L2RiscV`

**Subtotal: 3 (1 closed, 2 remain)**

---

## Total: 37 actionable tasks (19 closed)

| Bucket | Total | Closed | Remaining |
|--------|------:|-------:|----------:|
| Neo N4 Core | 10 | 2 | 8 |
| This repo | 24 | 16 | 8 |
| Cross-repo | 3 | 1 | 2 |
| **Total** | **37** | **19** | **18** |

The ZKsync Elastic Chain comparison (`docs/zksync-comparison.md`) originally
added 8 in-repo parity tasks. The high-value contract gaps are now closed:
global message root, immutable flags, DA validator, canonical bridged token,
AA, staged governance, L1 transaction filter, and L2 interop verifier.

The 18 remaining items are split between 3 production-readiness examples
(operator-supplied seams the framework already exposes), 2 future features
(toolchain-blocked), 1 deferred spec-gap, 2 ecosystem items (samples + Go SDK),
8 upstream core items, and 2 cross-repo coordination items.

---

## Ownership rule

Anything touching **NeoVM execution semantics, native contracts, dBFT consensus, or `RpcServer` plugin internals** belongs in **core**. Everything else — L1 contract suite (NeoHub), off-chain libraries, watchers, plugins, tools, SDKs, docs — belongs in this repo.

---

## Validation snapshot

This backlog is not release evidence. Exact test counts and results belong to the
CI run for the reviewed commit, not to a timeless task list. The current source
inventory is 38 .NET test projects, 26 NeoHub projects (24 production, one
advisory, one test-only), 10 L2 native contracts, and 44 Foundry tests. Consult
[`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md) for maturity boundaries and
[`SECURITY.md`](SECURITY.md) for the release/deployment gates; no exact-revision
public deployment or production release is claimed here.
