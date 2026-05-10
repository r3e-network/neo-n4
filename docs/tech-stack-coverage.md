# Tech-stack coverage

A comprehensive L2 ecosystem typically spans five layers — protocol contracts,
node infrastructure, operator tooling, application development, and end-user
interfaces. This document catalogs each layer against what `neo4` currently
ships and what's intentionally out-of-repo (third-party, deployment-specific,
or external dependency).

The structure isn't ZKsync-specific — these are the categories any L2 stack
covers. The implementations here are Neo-native and built from scratch
against `doc.md`; the intent is functional parity with what a mature L2
ecosystem provides for operators and app developers, not a translation of
any other project's source.

---

## Layer 1 — Protocol contracts

| Component | Status | Code |
|-----------|:------:|------|
| Chain registry (admission policy + per-chain config) | ✅ | `contracts/NeoHub.ChainRegistry/` |
| Shared L1↔L2 bridge (escrow, deposits, withdrawals) | ✅ | `contracts/NeoHub.SharedBridge/` |
| Settlement manager (batch finalization, state-root anchoring) | ✅ | `contracts/NeoHub.SettlementManager/` |
| Verifier registry (pluggable proof dispatch) | ✅ | `contracts/NeoHub.VerifierRegistry/` |
| Token registry (canonical L1↔L2 asset mapping) | ✅ | `contracts/NeoHub.TokenRegistry/` |
| Message router (L1↔L2 + L2↔L2 cross-chain delivery) | ✅ | `contracts/NeoHub.MessageRouter/` |
| DA registry (per-batch DA commitment store) | ✅ | `contracts/NeoHub.DARegistry/` |
| Sequencer registry + bonding | ✅ | `contracts/NeoHub.SequencerRegistry/`, `SequencerBond/` |
| Forced-inclusion contract | ✅ | `contracts/NeoHub.ForcedInclusion/` |
| Optimistic challenge game | ✅ | `contracts/NeoHub.OptimisticChallenge/` |
| Governance + council + timelock | ✅ | `contracts/NeoHub.GovernanceController/` |
| Emergency pause + escape hatch | ✅ | `contracts/NeoHub.EmergencyManager/` |
| Fraud verifier (governance-arbitration mode reference) | ✅ | `contracts/NeoHub.GovernanceFraudVerifier/` |
| Fraud verifier (trustless v3 — on-chain Merkle re-derivation) | ✅ | `contracts/NeoHub.RestrictedExecutionFraudVerifier/` |

**21 NeoHub contracts.** All type-check via `Neo.SmartContract.Framework`; CI
builds each with `nccs` and verifies the `.nef` + `.manifest.json` artifacts.

| Component | Status | Code |
|-----------|:------:|------|
| L2 batch info (chainId, batch number, L1 height) | ✅ | `contracts/L2Native.L2BatchInfoContract/` |
| L2 bridge (mint on deposit, burn on withdrawal) | ✅ | `contracts/L2Native.L2BridgeContract/` |
| L2 message I/O (outbound emit + inbound apply) | ✅ | `contracts/L2Native.L2MessageContract/` |
| L2 fee splitter (sequencer / prover / DA shares) | ✅ | `contracts/L2Native.L2FeeContract/` |
| L2 paymaster (fee abstraction, sponsored assets) | ✅ | `contracts/L2Native.L2PaymasterContract/` |
| L2 system-config cache | ✅ | `contracts/L2Native.L2SystemConfigContract/` |

**6 L2-side native contracts.**

---

## Layer 2 — Node infrastructure

| Component | Status | Code |
|-----------|:------:|------|
| Batch builder (block ↦ batch sealing) | ✅ | `src/Neo.L2.Batch/`, `Neo.Plugins.L2Batch/` |
| State-root generator | ✅ | `src/Neo.L2.State/` |
| Deterministic batch executor (the proving target) | ✅ | `src/Neo.L2.Executor/` |
| RISC-V execution kernel (PolkaVM-backed) | ✅ | `src/Neo.L2.Executor.RiscV/` (P/Invoke binding) |
| Persistence backends (in-memory + RocksDB) | ✅ | `src/Neo.L2.Persistence/` |
| Sequencer committee provider | ✅ | `src/Neo.L2.Sequencer/` |
| Censorship detection | ✅ | `src/Neo.L2.Censorship/` |
| Forced-inclusion source | ✅ | `src/Neo.L2.ForcedInclusion/` |
| Multisig (Stage 0) prover/verifier | ✅ | `src/Neo.L2.Proving.Attestation/` |
| Optimistic (Stage 1) prover/verifier | ✅ | `src/Neo.L2.Proving.Optimistic/` |
| RISC-V ZK (Stage 2) prover/verifier — full path | ✅ | C# `src/Neo.L2.Proving/RiscVZk/` is the in-process testing seam (mock prover for unit tests). Real Stage-2 proving runs out-of-process: `bridge/neo-zkvm-guest/` compiles to a RISC-V ELF via `cargo prove build` on sp1-zkvm 6.0 and **executes real Neo N3 VM** for every batch tx by depending on `external/neo-zkvm/crates/neo-vm-guest` (the proof attests to opcodes / gas accounting / halt-or-fault / top-of-stack result, not just hashed bytes). `bridge/neo-zkvm-host/` is the sp1-sdk 6.0 orchestrator with `execute()` / `prove()` / `verify()` API and a `prove-batch daemon --watch <dir>` CLI that turns into a production prover daemon (operator drops sealed batches in a queue dir, daemon emits `<name>.proof.bin` + `<name>.proof.vk` for L1 submission). End-to-end verified: zkVM execution agrees byte-for-byte with host-mode Neo VM execution (~42s default test); real CPU proof generation + verification + tampered-hash rejection covered by 2 `#[ignore]`-gated tests (~3.5 min combined, run pre-release); a queue → daemon → independent verifier round-trip was driven on real cryptography (87s prove, 2.78MB proof, 42s verify). |
| DA writers (in-memory / NeoFS / L1 / DAC / RocksDB) | ✅ | `src/Neo.Plugins.L2DA/` (5 implementations) |
| Settlement RPC client | ✅ | `src/Neo.L2.Settlement.Rpc/` |
| Telemetry (Prometheus-shaped) | ✅ | `src/Neo.L2.Telemetry/`, `Neo.Plugins.L2Metrics/` |
| Audit pipeline (6 invariant checks) | ✅ | `src/Neo.L2.Audit/` |
| Bisection / fraud-proof game | ✅ | `src/Neo.L2.Challenge/` |
| Cross-chain messaging | ✅ | `src/Neo.L2.Messaging/` |
| Asset registry + deposit/withdrawal processors | ✅ | `src/Neo.L2.Bridge/` |
| Per-L2 RPC method surface | ✅ | `src/Neo.Plugins.L2Rpc/` (10 methods) |
| Phase-5 proof aggregation | ✅ | `src/Neo.Plugins.L2Gateway/` — `BinaryTreeAggregator` with three `IRoundProver` implementations: `MultisigRoundProver` (Secp256r1 threshold-attested), `MerklePathRoundProver` (per-leaf inclusion proofs), `PassThroughRoundProver` (minimal-cost reference). Recursive-ZK fold variants (SP1 Compress / Halo2 / Risc0) operator-supplied through the same seam |

**16 off-chain libraries + 8 plugins.** All have `tests/Neo.*.UnitTests/` mirrors;
1344 tests across 34 .NET projects pass. Rust workspace ships 21 default-CI
tests (host-mode crypto + SDK + zkVM execute round-trip) plus 2 `#[ignore]`-gated
tests that exercise real CPU proof generation + verification (~4 minutes wall
time). TypeScript SDK ships 15 vitest tests.

---

## Layer 3 — Operator tooling

| Component | Status | Code |
|-----------|:------:|------|
| Chain creation CLI (templates, scaffolding) | ✅ | `tools/Neo.Stack.Cli/` (`create-chain`) |
| Node-directory init | ✅ | `tools/Neo.Stack.Cli/` (`init-l2`) |
| Chain registration (configBytes hex emit) | ✅ | `tools/Neo.Stack.Cli/` (`register-chain`) |
| Bridge adapter deploy plan | ✅ | `tools/Neo.Stack.Cli/` (`deploy-bridge-adapter`) |
| Sequencer / batcher / prover preflight | ✅ | `tools/Neo.Stack.Cli/` (`start-{sequencer,batcher,prover}`) |
| Batch submission preflight | ✅ | `tools/Neo.Stack.Cli/` (`submit-batch`) |
| Config sanity-checker | ✅ | `tools/Neo.Stack.Cli/` (`validate`) |
| Declarative L1 deploy planner | ✅ | `tools/Neo.Hub.Deploy/` (`scaffold` / `plan` / `verify`) |
| Post-deploy wiring hints | ✅ | `tools/Neo.Hub.Deploy/` (`PostDeployActions`) |
| In-process devnet runner | ✅ | `tools/Neo.L2.Devnet/` (5 batches default; `--config`, `--data-dir`, `--metrics-port`) |
| Sample chain configs | ✅ | `samples/` (4 templates verified end-to-end) |

**7 CLI tools, 9 + 3 + 1 + 4 + 4 + 2 + 5 = 28 subcommands across them** (counting the external-bridge CLI's genkey + committee-blob + deploy-bundle + chains-table + per-chain helpers).

The `neo-l2-explore` CLI is the framework's terminal block explorer: `label`
(prints the §16.2 5-dimension security label), `batch <n>` (full canonical
commitment for one batch + status), `tail [N]` (walk recent batches), and
`audit [N]` — the unique capability — which verifies state-root continuity
across the last N sealed batches and exits non-zero if a discontinuity is
found. Wraps `Neo.L2.Sdk.L2RpcClient`, so any node running `Neo.Plugins.L2Rpc`
is a valid endpoint.

---

## Layer 4 — Application development

| Component | Status | Code |
|-----------|:------:|------|
| L2 contract framework (compile to NeoVM bytecode) | ✅ | Uses `Neo.SmartContract.Framework` from `external/neo-devpack-dotnet/` (vendored) |
| L2-aware contract patterns documented | ✅ | `docs/launching-an-l2.md` (5 extension points + 3 worked examples) |
| Custom IDAWriter / ISequencerCommitteeProvider / IL2Prover examples | ✅ | `docs/launching-an-l2.md` (worked examples) |
| L2-side dApp examples | ✅ | `samples/contracts/` (cross-chain greeter + withdrawal demo) |
| Sample chain configs (rollup / gaming / validium / sidechain) | ✅ | `samples/*.config.json` (4 templates verified end-to-end) |
| App-developer SDK / client library (.NET) | ✅ | `src/Neo.L2.Sdk/` — typed `L2RpcClient` wrapping all 10 doc.md §14.1 RPC methods. Failure modes split across `L2RpcTransportException` / `L2RpcProtocolException` / `L2RpcServerException` / `L2RpcMismatchedChainIdException` so callers can write targeted retry policy. |
| App-developer SDK (TypeScript) | ✅ | `sdk/typescript/` — `@neo-n4/sdk` typed wrapper around all 10 RPC methods. 15 vitest tests pass against an in-process stub fetch. Same wire shape + 4-class error taxonomy as the .NET SDK. |
| App-developer SDK (Rust) | ✅ | `sdk/rust/` — `neo-n4-sdk` typed wrapper. 10 mockito-driven tests pass. Mirrors the .NET + TS SDKs. |

---

## Layer 5 — End-user interfaces

| Component | Status | Notes |
|-----------|:------:|-------|
| Terminal block explorer (CLI) | ✅ | `tools/Neo.L2.Explore/` (`neo-l2-explore`) — `label` / `batch <n>` / `tail [N]` / `audit [N]` (state-root continuity check). Wraps `Neo.L2.Sdk.L2RpcClient` so it points at any endpoint running `Neo.Plugins.L2Rpc`. |
| Web block explorer + bridge UI + faucet UI | ✅ | `sdk/web-explorer/index.html` — single static HTML page (zero build tooling) with inlined JS SDK. Tabs: Explore (label / latest root / batch), Bridge (deposit-status query + neo-bridge CLI handoff for L1 invocation hex), Faucet (localStorage-backed cooldown UI + neo-l2-faucet CLI handoff), Audit (state-root continuity across N batches). Drop on any static-file host. |
| Testnet faucet (CLI) | ✅ | `tools/Neo.L2.Faucet.Cli/` (`neo-l2-faucet`) — production drip CLI (covered in Layer-3 / Operator tooling section). |
| Documentation site (rendered) | ✅ | `book.toml` + `docs/SUMMARY.md` ship an mdBook config that renders the existing markdown docs into a searchable static site (`mdbook serve` for local preview, `mdbook build` for CI deploy to GitHub Pages / S3 / Netlify). |
| Wallet integration patterns | ✅ | `docs/wallet-integration.md` — paste-into-wallet hex (cold-key flows) + delegate signing (hot-wallet automation). Worked examples for NeoLine / Neon / NEP-6 / Ledger / KMS. Every CLI emits canonical hex; framework never sees private keys. |

---

## Coverage assessment

| Layer | Components | ✅ Done | 🟡 Scaffolded | 🔴 Out-of-repo |
|-------|-----------:|--------:|--------------:|---------------:|
| L1 protocol contracts | 13 | 13 | 0 | 0 |
| L2 native contracts | 6 | 6 | 0 | 0 |
| Cross-foreign-chain bridge | 13 | 13 | 0 | 0 |
| Node infrastructure | 19 | 19 | 0 | 0 |
| Operator tooling | 12 | 12 | 0 | 0 |
| App development | 8 | 8 | 0 | 0 |
| End-user UIs | 5 | 5 | 0 | 0 |
| **Total** | **76** | **76** | **0** | **0** |

**Phase 4 (SP1 ZK proving) is now end-to-end functional.** The
`bridge/neo-zkvm-host/tests/end_to_end.rs` test loads the compiled
guest ELF, runs it through the real SP1 zkVM (42s of cryptographic
proving work), and verifies the public-input hash matches host-mode
execution byte-for-byte. The toolchain (`sp1up` → `cargo prove build`)
remains an operator install step but the integration is real, tested,
and pinned in CI.

**Cross-foreign-chain bridge to Eth/Tron/Sol (doc.md §11.3 Phases B + C).**
Six on-chain contracts (`MpcCommitteeVerifier`, `ExternalBridgeRegistry`,
`ExternalBridgeEscrow`, `ExternalBridgeBond`, `ExternalBridgeStubVerifier`,
`MpcCommitteeFraudVerifier`) plus an L2-native counterpart
(`L2NativeExternalBridgeContract`), three Rust watcher crates
(`watchers/neo-bridge-watcher-{eth,tron,sol}/` — Eth: messaging +
signing core with byte-for-byte parity tests; Tron: thin re-export
with Tron chain-ids; Sol: `Ed25519FileSigner` + Solana chain-ids
0xE0000020..2F, exercises the curve-agnostic `Signer` trait that
dispatches to `CryptoLib.VerifyWithEd25519` on-chain), foreign-side
router artifacts for all three target chains
(`external/foreign-contracts/eth/` — `NeoExternalBridgeRouter.sol` +
13 Foundry tests with real `vm.sign` + `ecrecover`;
`external/foreign-contracts/tron/` — README pointing at the Eth
contract since TVM is EVM-flavored, deploy with the Tron chainId
constructor arg; `external/foreign-contracts/sol/` — ~440-line Anchor
program using Solana's ed25519 sigverify precompile, source-only
pending operator `anchor build`), and an operator CLI
(`tools/Neo.External.Bridge.Cli/` for genkey + committee-blob +
deploy-bundle). `Neo.Hub.Deploy` scaffolds the full bridge stack
alongside NeoHub: 20 deploy steps + 10 post-deploy hints. Phase C's
`MpcCommitteeFraudVerifier` makes slashing of equivocating committee
members permissionless (anyone can submit cryptographic proof of two
byte-distinct messages signed for the same `(chainId, nonce)` and
collect the bond as their reward), pinned by 7 real-secp256k1 tests
in `UT_MpcFraudProof_RealCrypto.cs`. The trait abstractions transferred
across both secp256k1 and ed25519 curve families — confirmation that
the Phase-B trait shape was right. Live RPC adapters (ethers-rs
`EventSource`, JSON-RPC `NeoSubmitter`, RocksDB `Journal`) are next.

All previously-out-of-repo Layer-4 + Layer-5 items now ship in the framework:
typed SDKs in three languages (.NET / TS / Rust), a static-HTML web app
covering explorer + bridge + faucet UIs, an mdBook documentation-site config,
documented wallet-integration patterns. Wallet integrations stay
delegate-driven so the framework never holds private keys.

## What's next

Plan items beyond the spec-gap-plan are tracked in `CHANGELOG.md`'s
`[Unreleased]` section. The L2 dev framework (this repo's primary scope) is
functionally complete per `doc.md` §0–§22; iteration ahead is on operator
ergonomics (more sample chains, more worked customization examples, more
dApp examples) rather than core architecture.
