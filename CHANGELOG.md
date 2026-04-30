# Changelog

All notable changes to **Neo Elastic Network** (`neo4`).
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added — Plugin telemetry wiring

- **`L2BatchPlugin.WithMetrics(IL2Metrics)`** — emits `l2.batch.sealed` (counter), `l2.batch.seal_latency_ms` (histogram), and `l2.batch.tx_count` (gauge) on every seal. Default sink is `NoOpMetrics.Instance` so the metric path is opt-in.
- **`L2SettlementPlugin.WithMetrics(IL2Metrics)`** — emits `l2.proving.generated{kind=…}` (counter), `l2.proving.latency_ms{kind=…}` (histogram), `l2.settlement.submitted` (counter), `l2.settlement.submit_latency_ms` (histogram), and `l2.settlement.submit_failures` (counter on exception). Failed submits re-queue at the head, exactly matching the prior retry semantics.
- **`L2SettlementPlugin.Enqueue(L2BatchCommitment)`** — public hot-path entry that's also useful for backfilling missed batches after a node restart. Replaces the previously private event handler.
- **`Neo.Plugins.L2Settlement.UnitTests`** — 4 new unit tests exercising submit-success / submit-failure / default-NoOp paths with mocked `IL2Prover` + `ISettlementClient` + `InMemoryMetrics`. First plugin-level test project that drives an actual Plugin subclass.

Cumulative: 206 tests / 25 projects.

### Added — Auditor + devnet v0.3

- **`Neo.L2.Audit`** — chain auditor: pluggable `IAuditCheck`, built-in `ContinuityCheck` (sequential batch numbers + state-root linking + non-overlapping block ranges) and `ProofValidityCheck` (re-runs each batch's proof through `VerifierRegistry`). `ChainAuditor` aggregates findings into `AuditReport` with human-readable `Summarize()`. 9 unit tests.
- **Devnet v0.3** — after the per-batch loop, runs the full `ChainAuditor` pass (continuity + proof validity) and prints the `AuditReport.Summarize()` output. The devnet is now a complete end-to-end demonstration: state-root continuity, real multisig proofs, balance arithmetic, and an explicit auditor pass.
- **`UT_Mvp_AllPhases_FullStack`** integration test — single readable scenario that runs Phase-1 deploy planner → Phase-0/2 batch lifecycle with state continuity → Phase-3 BisectionGame → Phase-5 Gateway aggregation, all in one test.

Cumulative: 194 tests / 23 projects.

### Added — Phase 3 completion (optimistic challenge window + bisection)

- **`NeoHub.OptimisticChallenge`** L1 contract — accepts fraud proofs against pending `Challengeable` batches; on accepted challenge, reads sequencer's full bond, splits per `ChallengerRewardBps` (default 50%), pays challenger via `SequencerBond.Slash`, treasures the rest, and calls `SettlementManager.RevertBatch`. `FinalizeIfPastWindow` for unchallenged batches. Owner-gated `SetWindowSeconds` (60s..7d).
- **`Neo.L2.Challenge.FraudProofPayload`** — 101-byte canonical wire format (1B version + 32B preStateRoot + 32B claimedPostStateRoot + 32B replayedPostStateRoot + 4B disputedTxIndex).
- **`Neo.L2.Challenge.ChallengeOrchestrator`** — pluggable `IFraudProofGenerator`; `InspectAsync` takes claimedCommitment + reconstructed inputs and emits a `FraudProofPayload` only when challenger's deterministic replay disagrees with the sequencer's claim.
- **`Neo.L2.Challenge.BisectionGame`** — pure state machine that converges challenger and sequencer to a single disputed tx index in `O(log N)` rounds (standard optimistic-rollup design). Makes on-chain fraud verification O(1) instead of O(N).
- **Phase-3 end-to-end integration test** (`UT_Mvp_Phase3_OptimisticChallenge`) demonstrates: 8-tx batch with `KeyedStateStore`-backed honest checkpoints → sequencer lies from index 5 → `ChallengeOrchestrator` detects → `BisectionGame` narrows to disputed tx index 4 in ≤ 3 rounds.

Phase 3 → ✅. Cumulative: 184 tests / 21 projects.

### Added — Phase 2 / 3 / 5 wave (real state, slashing, recursive aggregation)

- **`KeyedStateStore` + `KeyedStateRootOracle`** (`Neo.L2.Executor.State`) — replace the XOR-mix stub with a sorted-leaf Merkle root computed over `Hash256(4B keyLen || key || 4B valueLen || value)` leaves; deterministic + insert-order-independent. The devnet now runs with real state-root continuity (`postRoot[N] == preRoot[N+1]`).
- **`NeoHub.SequencerBond` + `NeoHub.SequencerRegistry`** — slashable bond escrow (`Deposit` / `Slash` / `Withdraw`) and per-chain dBFT pubkey registry (`Register` / `Unregister` / `Finalize` with exit window). `Register` gates on `SequencerBond.HasMinBond` via inter-contract call.
- **`Neo.L2.Sequencer`** — L2-side `ISequencerCommitteeProvider` + `InMemorySequencerCommitteeProvider`. Models the L1 contract semantics so L2 nodes can test their committee-aware code paths.
- **`Neo.L2.Censorship`** — off-chain `CensorshipDetector` that polls `IForcedInclusionSource` for overdue entries, uses `ISequencerCommitteeProvider` to identify the responsible signer, and emits `CensorshipReport[]` ready for `NeoHub.ForcedInclusion.ReportCensorship` + `NeoHub.SequencerBond.Slash`.
- **`BinaryTreeAggregator` + `IRoundProver`** (`Neo.Plugins.L2Gateway`) — log(N)-round pairwise reduction with pluggable round prover. Default `PassThroughRoundProver` (Hash256 + length-prefixed proof concat). Production swaps for SP1 Compress / Halo2 accumulator / Risc0 fold.
- **`NeoFsLikeDAWriter`** — content-addressed in-process DA writer with NeoFS object semantics (object id = SHA256(payload), per-chain container, 36-byte pointer = 4B chainId LE + 32B objectId).
- **`Neo.L2.Devnet` v0.2** — upgraded to use `KeyedStateRootOracle` and 3-member `InMemorySequencerCommitteeProvider`; verified end-to-end across 5 batches with Alice's balance arithmetic check.
- **Phase-2 full-stack integration test** (`UT_Mvp_Phase2_FullStack`) stitches `KeyedStateStore` continuity + sequencer committee + forced-inclusion + censorship detection + multi-chain `BinaryTreeAggregator` together.

Cumulative: 162 tests / 19 projects, all green.

### Added — Phase 0 / 1 / 2 substantial scaffolding

- **Off-chain libraries** (`src/Neo.L2.*`): `Abstractions`, `Batch`, `State`, `Bridge`, `Messaging`, `Proving`, `Executor`. 7 interfaces, 14 model records, deterministic batch executor + spec, Merkle tree matching Neo's `Hash256` convention, Stage 0 multisig prover (production-usable) + verifier, Stage 1 optimistic verifier, Stage 2 RISC-V mock backend.
- **neo-node plugins** (`src/Neo.Plugins.L2*`): `L2Batch`, `L2Settlement`, `L2Bridge`, `L2DA`, `L2Prover`, `L2Rpc`, `L2Gateway`. All extend `Neo.Plugins.Plugin` and type-check against the locally-vendored `neo-project/neo` master branch.
- **Smart contracts** (`contracts/`): 9 NeoHub L1 contracts (`ChainRegistry`, `SharedBridge`, `SettlementManager`, `VerifierRegistry`, `MessageRouter`, `TokenRegistry`, `DARegistry`, `GovernanceController`, `EmergencyManager`) and 6 L2 native contracts (`L2BridgeContract`, `L2MessageContract`, `L2BatchInfoContract`, `L2FeeContract`, `L2PaymasterContract`, `L2SystemConfigContract`). Compile via `Neo.SmartContract.Framework`.
- **Tooling** (`tools/`): `neo-stack` CLI (8 subcommands) and `neo-l2-devnet` runnable Phase 0 demo.
- **Tests**: 88 unit tests across 10 projects (incl. 1 MVP integration test that walks deposit → batch → prove → verify → withdraw end-to-end in-process).
- **Documentation**: `README.md`, `ARCHITECTURE.md` (English distillation of `doc.md`), `AGENTS.md` (agent guide), `IMPLEMENTATION_STATUS.md` (per-phase coverage matrix), `CHANGELOG.md` (this file). Each L2 module's interfaces are XML-doc'd so IDE tooltips trace back to `doc.md` section numbers.

### Architecture decisions locked in

- **Plugin-based extension over fork**: neo4 references `neo-project/neo` master via `ProjectReference` (offline-friendly). Every L2 capability is a separate `Plugin` subclass loaded at runtime, preserving upstream compatibility.
- **Deterministic batch executor as the proving boundary**: `SPEC.md` enumerates excluded surfaces (P2P, RPC, mempool, plugins, logging, wallet, on-disk DB) so the prover only commits to pure state-transition behavior.
- **Pluggable verifier registry**: `VerifierRegistry` dispatches by `ProofType`, mirroring NeoHub's L1 contract — the same wire-format moves from off-chain to on-chain unchanged.
- **Canonical encodings**: `L2BatchCommitment`, `PublicInputs`, `MessageHasher`, `DepositPayload` all serialize little-endian with deterministic byte layouts; the same encoding is what NeoHub's contracts decode.

### Added — Phase 1 / 4 acceleration

- **`NeoHub.ForcedInclusion` + `Neo.L2.ForcedInclusion`** (doc.md §15.4): anti-censorship primitive — L1 enqueue, L2 drain with deadline tracking, replay protection. 8 unit tests.
- **`Neo.L2.Settlement.Rpc`**: JSON-RPC 2.0 client over `HttpClient` + `Neo.Json` (no third-party deps). `RpcSettlementClient` implements `ISettlementClient` for read-only methods; submit-batch delegates to a caller-supplied signer. 6 unit tests with in-memory `HttpMessageHandler` mocks.
- **`Neo.Hub.Deploy`** (`neo-hub-deploy` CLI): declarative deploy planner with topological sort, `$step:<name>` placeholder resolution, cycle/unknown-dep detection, canonical 10-step NeoHub scaffold. 8 unit tests.
- **`bridge/neo-zkvm-bridge`** (Rust cdylib) + **`Neo.L2.Proving.Sp1`** (C#): Phase-4 SP1 FFI scaffold. Stable 4-symbol C ABI; default features = NOT_IMPLEMENTED so the C# side falls back to `MockRiscVProver`; `--features real-prover` links the actual `neo-zkvm-prover` crate. 6 unit tests.
- **Phase-1 cross-component integration test** that walks: deploy-planner topological resolve → forced-inclusion enqueue/drain → SP1 fallback prover → multi-chain Gateway aggregation. 5 new tests.

### Out of MVP scope (still deferred)

- **Live L1 signer for `RpcSettlementClient.SubmitBatchAsync`** — interface in place; concrete wallet integration is operator-specific.
- **One-shot deploy runner** — `Neo.Hub.Deploy` emits the bundle JSON; the consumer (signer + chain bookkeeper) lives outside this repo.
- **`nccs` artifact generation** — `Directory.Build.props` calls `nccs` with `ContinueOnError=true`; users install nccs separately.
- **RpcServer plugin integration partial** that registers `L2RpcMethods` as `[RpcMethod]`-attributed entry points (needs neo's RpcServer plugin source).
- **Real SP1 prover linkage** — flip `--features real-prover` on the bridge crate to enable.
- **Phase 5 recursive proof aggregation** — `PassThroughAggregator` is a non-ZK reference impl.
- **Forced-inclusion bond/slashing** — contract emits the report event; actual sequencer slashing depends on `SettlementManager` integration.
- **NeoFS DA writer** — stub class throws; production wires NeoFS client.
- **dBFT sequencer-committee selection per Neo Elastic** (doc.md §7.1) — defaults to neo's existing `DBFTPlugin` consensus.
