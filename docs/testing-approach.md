# Testing approach — Neo Elastic Network

Test methodology adopted across `neo4`. Mirrors ZKsync's Foundry + Hardhat
+ integration test stack, translated to .NET / Rust / TypeScript / Solidity
where each piece of the system lives.

---

## Test surface (37 .NET test projects + cross-language gates + SP1 host E2E)

| Tier | Framework | Where | What |
|------|-----------|-------|------|
| Unit | MSTest (xUnit-style) | `tests/` (37 solution test projects including integration suites) | Per-class invariants, edge cases, null-arg + null-field guards, metric-emission pins |
| Integration | MSTest | `tests/Neo.L2.IntegrationTests/` | E2E phase stitches (Phase 0 → 5), audit pipeline, persistence rehydration, NeoVM2/RISC-V seam, legacy NeoVM compatibility, custom executor full-stack |
| Property-based / invariant | MSTest + seeded `System.Random` | `UT_BridgeInvariants_PropertyBased.cs` (17 tests) | Random sequences of 200 ops × 4-8 seeds — 1600-3200 transitions per invariant. Asserts bridge accounting + nonce uniqueness + bidirectional registry holds at every intermediate state |
| Fuzz | MSTest + seeded `System.Random` | `UT_WireFormat_Fuzz.cs` (19 tests) | Random byte sequences to every decoder — must round-trip or reject with typed exception, never crash |
| Cross-language parity | byte-vector pins + canonical-bytes-match-csharp | Rust watcher tests + Foundry tests | Wire format byte-identical across C# encoder + Rust + Solidity verifier |
| On-chain ↔ off-chain parity | C# replicas of on-chain decision trees | `UT_OnChainMerkleVerifyParity`, `UT_RestrictedExecutionFraudVerifierParity`, `UT_GovernanceFraudVerifierParity`, `UT_MpcFraudProof_RealCrypto` | Off-chain algorithm replicates the on-chain verifier and produces identical roots/decisions; drift surfaces in unit tests rather than at L1 settlement time |
| Foundry | Solidity invariant + multi-chain | `external/foreign-contracts/eth/test/` (39 tests) | EVM-family Solidity router — 32 single-chain + 7 multi-chain pinning per-instance state isolation across 17 mainnet slots |
| Real-CPU SP1 prover | Rust `#[ignore]`-gated | `bridge/neo-zkvm-host/tests/end_to_end.rs` (2 tests) | Real ZK proof generation (~40s prove, ~20s verify, 2.78 MB proof) + tampered-hash-rejection negative test |
| Live-RPC | Rust `--features live-rpc` | `watchers/neo-bridge-watcher-eth/tests/` (55 tests) | `FakeRpcServer` in-process — exercises `EthRpcEventSource`+`NeoRpcSubmitter` through real `reqwest::blocking` HTTP cycles |
| TS SDK | vitest | `sdk/typescript/` (16 tests) | RPC client surface; error-taxonomy parity across .NET / Rust / TS |
| Rust SDK | cargo test + mockito | `sdk/rust/` (10 tests) | RPC client; same surface as TS + .NET |
| execution-core | cargo test | `bridge/neo-execution-core/` (5 tests) | Backend-neutral batch parsing, receipt/state folding, Merkle determinism, backend-dependency guard |
| zkvm-guest | cargo test | `bridge/neo-zkvm-guest/` (7 tests) | Host-mode execution of the Neo N3 VM through the shared batch core |

---

## Testing principles (cribbed from ZKsync Era + adapted)

### 1. Every decoder rejects garbage with a typed exception

Wire-format decoders MUST NOT crash on random byte input. The fuzz suite
(`UT_WireFormat_Fuzz`) drives `MerkleProofSerializer.Decode` and
`DepositPayload.Decode` with 500 random byte sequences per seed; the only
exceptions allowed are `ArgumentException` / `InvalidDataException`. Any
`NullReferenceException`, `IndexOutOfRangeException`, or `OverflowException`
indicates a missing bounds check.

### 2. Round-trip is identity for well-formed input

Every encoder/decoder pair satisfies `decode(encode(x)) == x`. `UT_WireFormat_Fuzz`
asserts this across fuzzed (tree-shape, leaf-count, amount, address) tuples
for both Merkle proofs and `DepositPayload`. Drift between encoder and
decoder surfaces in unit tests, not at L1 settlement time.

### 3. Invariants hold across long random operation sequences

ZKsync's Foundry `invariant_*` functions assert properties at every
intermediate state of a random transaction sequence. neo4's
`UT_BridgeInvariants_PropertyBased` mirrors this with seeded random-walk
tests:

- **AssetRegistry bidirectional consistency** — for every active mapping,
  `TryGetByL2(l2)` and `TryGetByL1(l1, chainId)` must resolve to the same
  record. Asserted after every register / re-register / SetActive op.
- **Withdrawal nonce uniqueness** — every successful Stage adds to the
  intra-batch consumed set; every SealBatch promotes it to cross-batch
  consumed; re-staging any (sender, nonce) pair must throw.
- **Deposit accepted-sum accounting** — the `DepositsProcessed` counter
  emitted by `IL2Metrics` equals the count of `Process()` calls that did
  not throw.

Each invariant test runs 200 operations across 4-8 distinct seeds — 1600
to 3200 state transitions per invariant. Seeds are fixed so a regression
is reproducible byte-for-byte.

### 4. On-chain ↔ off-chain parity is pinned by C# replicas

ZKsync's Foundry tests exercise both the contract and a reference Rust
implementation, then diff. neo4 takes the same approach in C#: each on-chain
verifier has a parity test that replicates its decision tree (`UT_OnChainMerkleVerifyParity`
for SettlementManager, `UT_RestrictedExecutionFraudVerifierParity` for the
governance-only structural v3 path, `UT_RestrictedFraudProofV4` plus NeoVM tests
for the SettlementManager-bound executable restricted v4 path,
`UT_GovernanceFraudVerifierParity` for the v1/v2 governance arbitration verifier,
and `UT_MpcFraudProof_RealCrypto` for the
Phase-C MPC committee fraud verifier). Off-chain drift surfaces in unit tests
rather than at runtime.

The state-tree Merkle convention drift (the substantive finding from the
40-iteration validation sweep) was caught by exactly this pattern:
`UT_KeyedStateMerkleTree_NeoClassicParity` now pins
`KeyedStateMerkleTree.ComputeRoot(pairs) == MerkleTree.ComputeRoot(HashEntry leaves)`
across 10 cardinalities including the previously-divergent odd cases.

### 5. End-to-end phase stitches verify full pipeline

ZKsync's `era-test-node` boots a real local chain and runs scripted
scenarios. neo4 has equivalent integration tests in `tests/Neo.L2.IntegrationTests/`:

- `UT_Mvp_Phase0_Sidechain` — Phase-0 MVP (deposit → batch → withdraw)
- `UT_Mvp_Phase1_Cross_Component` — Phase-1 NeoHub v0 + SharedBridge
- `UT_Mvp_Phase2_FullStack` — Phase-2 batch settlement + Gateway aggregation
- `UT_Mvp_Phase3_OptimisticChallenge` — Phase-3 challenge window + fraud proofs
- `UT_Mvp_AllPhases_FullStack` — every phase stitched together
- `UT_E2E_RealVM_FullStack` — legacy NeoVM compatibility path with state-root continuity
- `UT_E2E_CustomExecutor_FullStack` — `Sample.CounterChainExecutor` end-to-end
- `UT_E2E_AuditPipeline` — `ChainAuditor` against healthy + broken scenarios
- `UT_E2E_Persistence_FullStack` — 4 RocksDB stores rehydrate from one root dir
- `UT_E2E_L1RpcPollers_FullStack` — RPC poller composition
- `UT_E2E_L2MetricsPlugin_CompositionRoot` — every instrumented component → one sink

### 6. Real cryptographic proofs are verified end-to-end

ZKsync's prover tests `#[ignore]`-gate the full real-proof flow because it's
expensive. neo4 does the same: `bridge/neo-zkvm-host/tests/end_to_end.rs`
has two `#[ignore]` tests that exercise real SP1 proof generation
(~40s prove, ~20s verify) and tampered-hash rejection. Run locally with:

```bash
cd bridge/neo-zkvm-host
cargo test --release --tests -- --ignored
```

### 7. Cross-language wire-format parity

ZKsync runs the same byte vectors through their Solidity verifier, their
Rust prover, and their TS SDK. neo4 has equivalent pin tests:

- `canonical_bytes_match_csharp_vector` (Rust watcher) — byte-for-byte
  parity vs C# `Neo.L2.Messaging.ExternalMessageHasher`
- `message_hash_matches_csharp_vector` (Rust watcher) — same for the
  hash computation
- 39 Foundry tests in `external/foreign-contracts/eth/` — the same Solidity
  router deploys unchanged across 14 EVM chain families and 17 mainnet slots

---

## CI integration

`.github/workflows/build.yml` runs the full suite on every push + PR:

1. `test` — `dotnet test Neo.L2.sln` across the complete current solution inventory
2. `contracts` — installs `Neo.Compiler.CSharp`, type-checks all 25 NeoHub
   projects plus the 2 sample contracts, verifies every `.nef` + `.manifest.json`
   artifact dynamically, and runs the `external/neo` N4 native-contract tests
3. `bridge` — `cargo check` on Rust workspace
4. `neo-zkvm-host` — `cargo build` + non-ignored tests (the 2 real-CPU
   ignored tests run nightly, not per-PR)
5. `sdk-typescript` — `npx vitest run` (16 tests)
6. `foreign-evm` — `forge test` (39 Solidity tests)
7. `docs-site` — `mdbook build` + link-check

A PR cannot merge until every job is green. Dependabot keeps cargo /
NuGet / npm deps up to date.

---

## How to add a new test

| Kind of code being tested | Test project | Pattern |
|---------------------------|-------------|---------|
| Off-chain library | `tests/Neo.L2.<Lib>.UnitTests/` | MSTest `[TestMethod]` per behavior; one `[DataRow]` per edge case |
| On-chain contract verifier | `tests/Neo.L2.<Lib>.UnitTests/UT_<Contract>Parity.cs` | Replicate the on-chain decision tree in C#; assert byte-identical wire format |
| Wire-format encoder | `tests/Neo.L2.State.UnitTests/UT_WireFormat_Fuzz.cs` | Add a new `[TestMethod]` with `[DataRow]` seeds; fuzz 500 random byte sequences per seed |
| Bridge / messaging invariant | `tests/Neo.L2.Bridge.UnitTests/UT_BridgeInvariants_PropertyBased.cs` | Seeded random-walk over 200 ops × 4-8 seeds; assert invariant at every step |
| Full pipeline behavior | `tests/Neo.L2.IntegrationTests/UT_E2E_<scenario>.cs` | E2E using `InMemoryKeyValueStore` + `AttestationProver` + `KeyedStateRootOracle` |
| Cross-language wire format | Rust: `watchers/neo-bridge-watcher-*/src/messaging.rs` `#[cfg(test)]`; Foundry: `external/foreign-contracts/eth/test/` | Hard-code a `csharp_vector` byte literal; assert your impl produces identical bytes |
| Real-CPU ZK proof | `bridge/neo-zkvm-host/tests/end_to_end.rs` | `#[test]` + `#[ignore]` + `#[serial_test::serial]` |

---

## See also

- [`docs/zksync-comparison.md`](zksync-comparison.md) — component-by-component
  map of ZKsync Elastic Chain vs neo4 (the source of these testing patterns)
- [`CONTRIBUTING.md`](../CONTRIBUTING.md) — full developer workflow
- [`.github/PULL_REQUEST_TEMPLATE.md`](../.github/PULL_REQUEST_TEMPLATE.md) —
  pre-merge checklist (parity tests if contracts touched, etc.)
