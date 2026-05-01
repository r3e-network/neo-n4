# Changelog

All notable changes to **Neo Elastic Network** (`neo4`).
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Docs — Refresh README counts to match current state

- README's "What ships" section was stale at 194 tests / 19 test projects / 11 off-chain libs / 7 plugins. Now: 320 tests / 21 test projects / 15 off-chain libs / 8 plugins, with the new bullets calling out the production-grade telemetry stack (Prometheus + `/metrics` + `/healthz` + `/readyz`) and that every canonical wire format has a byte-layout test.
- Quick-start gets a `--metrics-port 9090` example for the live HTTP scrape.

### Docs — Walk #4 covers telemetry in `docs/architecture-walkthrough.md`

- Added a "Walk #4: telemetry — emit, snapshot, scrape" section with an ASCII diagram of the metrics pipeline + the catalog of every component → metric family mapping. Cross-references `docs/telemetry.md` for the operator detail.
- Added a "Cross-cutting / Telemetry" row to the doc.md→code mapping table so a contributor scanning the table for the observability path finds it.
- No code changes; 320 tests / 27 projects.

### Added — `BatchSerializer` byte layouts in XML docs + tests

- `BatchSerializer`'s XML doc now includes full offset tables for both `L2BatchCommitment` (321 + proofLen bytes) and `PublicInputs` (332 bytes). This is THE format `NeoHub.SettlementManager` reads on-chain — having the layout in the doc means a contract author parsing the bytes doesn't need to read the encoder source.
- **2 new tests** in `Neo.L2.Batch.UnitTests` pin every documented offset.

Cumulative: 320 tests / 27 projects.

### Added — Documented byte layouts for `OptimisticProofPayload` + `RiscVProofPayload`

- Both payload types previously had `<remarks>See doc.md §X</remarks>` but no actual byte layout written down. A contract author parsing them off the wire had to read the source to know offsets. Layouts now spelled out as offset/size tables matching the format used in other canonical encoders.
- **2 new tests** in `Neo.L2.Proving.UnitTests` pin every byte range so future encoder reorders fail the build.

Cumulative: 318 tests / 27 projects.

### Fixed — `FraudProofPayload` doc-comment layout matches the encoder

- The XML doc-comment listed fields in a different order than `Encode` actually produced. Real layout (101 bytes, all little-endian): version (1B) + preStateRoot (32B) + claimedPostStateRoot (32B) + replayedPostStateRoot (32B) + disputedTxIndex (uint32, 4B). Doc updated and a new byte-layout test pins the offsets so future reorders fail the build.
- **1 new test** in `Neo.L2.Challenge.UnitTests` asserts each byte range matches the documented offsets.

Cumulative: 316 tests / 27 projects.

### Added — `ChallengeOrchestrator.InspectWithBisectionAsync`

- New overload that takes per-tx checkpoint sequences from both parties, runs `BisectionGame` internally, and emits a `FraudProofPayload` with `DisputedTxIndex` set to the single narrowed tx index. Pulls the bisection step inside the orchestrator so the caller doesn't have to wire it manually.
- Returns `null` when checkpoints agree at the final index (no fraud). Otherwise emits `l2.challenge.fraud_proofs` (counter) and `l2.challenge.bisection_rounds` (histogram via `BisectionGame`).
- **3 new tests**: agreement returns null, log-N narrowing produces the right disputed index region, arg validation matches `InspectAsync`.

Cumulative: 315 tests / 27 projects.

### Added — `ChainAuditor` self-emits audit metrics

- **`ChainAuditor`** accepts an optional `IL2Metrics` constructor parameter and emits `l2.audit.runs` (counter, +1 per `AuditAsync` call) and `l2.audit.failures` (counter, delta = number of failed findings — not 1 per failed audit) automatically. Devnet's manual emission of these metrics is removed; the auditor handles it now.
- **`NoZeroProofCheck`** registered in the devnet's auditor pipeline alongside `Continuity` + `ProofValidity`.
- **4 new tests** in `Neo.L2.Audit.UnitTests`: passing-audit increments runs only, failing-audit increments runs + failures by failed-finding count, repeated audits accumulate, NoOp default safety.

Cumulative: 312 tests / 27 projects.

### Added — `L2Outbox` messaging telemetry

- **`L2Outbox`** emits `l2.messaging.emitted` (counter) on every `Add`. Optional `IL2Metrics` constructor param. The metric was declared in iter 33's `MetricNames` but never emitted by any component.
- **2 new tests** in `Neo.L2.Messaging.UnitTests`: counter increments across L1 + L2 destinations, NoOp default safety.

Cumulative: 308 tests / 27 projects.

### Added — Sequencer registry telemetry

- **`InMemorySequencerCommitteeProvider`** emits `l2.sequencer.registered` (counter) on Register, `l2.sequencer.exits_started` (counter) on BeginExit, `l2.sequencer.exits_finalized` (counter) on Finalize, and `l2.sequencer.committee_size` (gauge) on every Register / Finalize. Optional `IL2Metrics` constructor param. Lets operators alert on unexpected committee shrinkage or rapid churn.
- **4 new `MetricNames`** constants + matching catalog entries.
- **4 new tests**: counter+gauge on Register, exits_started on BeginExit (size unchanged), exits_finalized + size decremented on Finalize, NoOp default safety.

Cumulative: 306 tests / 27 projects.

### Added — Forced-inclusion / censorship / challenge telemetry

The four `MetricNames` constants for these subsystems were declared in iter 33 but not actually emitted. Closing that gap:

- **`InMemoryForcedInclusionSource`** emits `l2.forced_inclusion.observed` on every `Enqueue`. Optional `IL2Metrics` constructor param.
- **`CensorshipDetector`** emits `l2.censorship.reports` (incremented by report count) when `DetectOverdueAsync` returns a non-empty list. Optional `IL2Metrics` constructor param.
- **`ChallengeOrchestrator`** emits `l2.challenge.fraud_proofs` when `InspectAsync` returns a non-null payload. Optional `IL2Metrics` constructor param.
- **`BisectionGame`** records `l2.challenge.bisection_rounds` (histogram) when the game settles, value = number of rounds taken. Optional `IL2Metrics` constructor param.
- **6 new tests** across the three lib test projects.

Cumulative: 302 tests / 27 projects. Every metric in `MetricCatalog` now has at least one emitter in source.

### Added — Misc polish

- **`Neo.Plugins.L2Metrics/config.json`** — config template so operators can drop the plugin into a Neo node and have it work. Mirrors the file shape every other L2 plugin uses (`PluginConfiguration` block).
- **`MerkleProof.Verify(root)`** — instance-method convenience; delegates to the existing `MerkleTree.Verify(proof, root)`. Lets call sites read `proof.Verify(root)` without the static dispatch boilerplate.

Cumulative: 296 tests / 27 projects.

### Added — Composition-root integration test

- **`UT_E2E_L2MetricsPlugin_CompositionRoot`** — wires every instrumented component (`BatchSealer`, `MetricsEmittingDAWriter`, `DepositProcessor`, `WithdrawalProcessor`, `BinaryTreeAggregator`, `L2RpcMethods`) to one shared sink hosted by `L2MetricsPlugin`, drives activity, scrapes `/metrics` through the plugin's HTTP server, and asserts every component's metric family is present in the response. Locks in that the composition root in `docs/telemetry.md` actually works end-to-end as advertised.

Cumulative: 295 tests / 27 projects.

### Added — `Neo.Plugins.L2Metrics` composition root

- New plugin **`L2MetricsPlugin`** owns the shared `InMemoryMetrics` sink the rest of the L2 plugin set wires its `WithMetrics()` calls to, and stands up the `MetricsHttpServer` based on settings (BindAddress, Port, Enabled). Pulls everything together — operators register this plugin first, then call `plugin.Metrics` from each other plugin's `WithMetrics()`.
- **Optional readiness predicate** via `WithReadinessCheck(Func<bool>)` — gates `/readyz` 200 vs 503.
- **Idempotent `Start()`** — extra calls are no-ops, simplifying host startup.
- **`L2MetricsSettings`** — `Enabled` (kill switch), `BindAddress` (default `127.0.0.1`), `Port` (default 9090, use 0 for any free port). Loaded from the standard plugin `config.json` `PluginConfiguration` section.
- **7 new tests** in `Neo.Plugins.L2Metrics.UnitTests`: bound-port-zero-before-Start, default-settings, idempotent Start, real HTTP scrape with emitted counter, readiness predicate gating 200 ↔ 503, null-arg validation.

Cumulative: 294 tests / 27 projects.

### Added — Devnet `--metrics-port` flag (live HTTP demo)

- `neo-l2-devnet <N> --metrics-port <P>` (or `--metrics-port 0` for "any free port") now stands up a real `MetricsHttpServer` after the batch run, self-scrapes `/metrics`, `/healthz`, and `/readyz` over real HTTP, and prints the round-trip status + content-type + body summary. Promotes the previously static "Prometheus text format" devnet section to a live demonstration of the production scrape path.

Cumulative: 287 tests / 26 projects (no new tests; the e2e telemetry integration test already covers this code path).

### Added — `NoZeroProofCheck` audit

- New `IAuditCheck` implementation flags batches that were soft-sealed but never had a real proof attached: `ProofType.None`, or non-`None` discriminator paired with empty `Proof` bytes. Cheap and fast — does not re-verify the proof (that's `ProofValidityCheck`'s job), it just catches the "soft-sealed but never proved" failure mode that would otherwise need full verification cost to detect.
- 5 new tests in `Neo.L2.Audit.UnitTests`: all-proved happy path, `ProofType.None`, empty proof bytes, multiple failures all reported, empty batch list.

Cumulative: 287 tests / 26 projects.

### Added — Canonical `MerkleProof` wire format

- **`MerkleProofSerializer`** (`Neo.L2.State`) — fixed-layout encoding of `MerkleProof` consumed by L1 NeoHub.SharedBridge for withdrawal verification. Closes a real gap: prior to this, off-chain code could `MerkleTree.GetProof` + `Verify`, but there was no canonical byte format for sending a proof across the off-chain ↔ on-chain boundary.
- Layout (48 + 32 × siblingCount bytes, all little-endian):
  - 0 .. 32  — Leaf hash
  - 32 .. 36 — LeafIndex (uint32)
  - 36 .. 44 — PathBitmap (uint64)
  - 44 .. 48 — SiblingCount (uint32)
  - 48 ..    — Siblings, 32 bytes each, leaf-to-root order
- **`MaxDepth = 64`** matches `MerkleTree.Verify`'s existing depth limit.
- **10 new tests**: round-trip 4-leaf, depth-0 single-leaf (header-only), exact byte-layout assertion, truncated-header rejection, truncated-siblings rejection, extra-trailing-bytes rejection, oversized-depth-on-encode rejection, header-claims-too-many-siblings rejection, null-arg, all-positions in 7-leaf tree round-trip.
- Listed in `AGENTS.md` "Canonical encodings" so future contributors don't reinvent.

Cumulative: 282 tests / 26 projects.

### Added — `/healthz` + `/readyz` endpoints

- **`MetricsRequestHandler`** now answers `/healthz` (always 200) and `/readyz` (200 or 503 based on optional predicate) in addition to `/metrics`. Standard Kubernetes-style liveness / readiness probes for load-balancer integration without bringing in an additional HTTP framework.
- **`MetricsRequestHandler` constructor** gets an optional `Func<bool>? readinessCheck` parameter. When unwired, `/readyz` always returns 200; when wired, the predicate is evaluated on every scrape.
- **6 new tests** covering: `/healthz` always-200, `/readyz` no-predicate-200, predicate-true-200, predicate-false-503, predicate evaluated per-request, trailing-slash + query-string tolerance on `/healthz`.
- `docs/telemetry.md` gets an "Endpoints" table and a `/readyz` predicate example.

Cumulative: 272 tests / 26 projects.

### Added — Gateway aggregation telemetry

- **`BinaryTreeAggregator`** accepts an optional `IL2Metrics` constructor parameter (default `NoOpMetrics`). On every successful `Aggregate()` it emits `l2.gateway.aggregations` (counter), `l2.gateway.batches_aggregated` (counter, +N constituents), `l2.gateway.aggregation_rounds` (histogram = tree depth), and `l2.gateway.aggregation_latency_ms` (histogram). Empty-pending case emits nothing.
- **4 new `MetricNames`** + matching catalog entries.
- **5 new tests** in `Neo.Plugins.L2Gateway.UnitTests`: 4-batch case verifies rounds = log2(4) = 2; 1-batch case verifies rounds = 0; empty case verifies no emission; repeated aggregations verify accumulation; default NoOp safety. **Last plugin without telemetry is now wired.**

Cumulative: 266 tests / 26 projects. Telemetry coverage matrix complete.

### Added — RPC telemetry

- **`L2RpcMethods`** wraps each of its 9 RPC methods through a private `Time` helper that emits `l2.rpc.calls` (counter) + `l2.rpc.latency_ms` (histogram) on success and `l2.rpc.failures` (counter) on exception, all tagged by `method` name (e.g. `getl2stateroot`, `getl2batch`). Optional `IL2Metrics` constructor parameter; default `NoOpMetrics`.
- **3 new `MetricNames`**: `RpcCalls`, `RpcLatencyMs`, `RpcFailures` + matching catalog entries.
- **4 new tests** in `Neo.Plugins.L2Rpc.UnitTests`: per-method tag isolation, repeated-call accumulation, foreign-chain rejection ↑ failure counter, no-metrics-default safety.

Cumulative: 261 tests / 26 projects.

### Added — Bridge processor telemetry

- **`DepositProcessor`** + **`WithdrawalProcessor`** now accept an optional `IL2Metrics` constructor parameter and emit:
  - `l2.bridge.deposits` (counter) on successful `Process`, `l2.bridge.deposits_rejected` on validation failure (replay, unknown asset, inactive mapping).
  - `l2.bridge.withdrawals` (counter) on successful `Stage`, `l2.bridge.withdrawals_rejected` on validation failure (unknown asset, duplicate nonce, non-positive amount).
- **`L2BridgePlugin.WithMetrics(IL2Metrics)`** — re-creates the processors with the new sink. Default is `NoOpMetrics`.
- **2 new `MetricNames`**: `DepositsRejected`, `WithdrawalsRejected` + matching catalog entries.
- **7 new tests** in `Neo.L2.Bridge.UnitTests`: success path, replay, unknown asset, withdrawal success, duplicate nonce, negative amount, default-NoOp safety. Closes the gap where bridge counters were only emitted manually in the devnet's inline path; production plugin path now emits them too.

Cumulative: 257 tests / 26 projects.

### Added — `MetricCatalog` (operator-facing HELP strings)

- **`MetricCatalog`** — single source of truth for the operator-facing description of every canonical metric. `GetHelp(name)` returns a sentence-form description; `IsKnown(name)` answers whether the catalog has an entry. `Descriptions` exposes the full map.
- **`PrometheusExporter`** now consults `MetricCatalog.GetHelp(baseName)` so HELP lines read e.g. `# HELP l2_batch_sealed_total Number of L2 batches sealed by the local sequencer` instead of the previous generic `L2 telemetry counter (l2.batch.sealed)`.
- **6 new tests** in `Neo.L2.Telemetry.UnitTests`: catalog-completeness check (reflects over `MetricNames` constants and asserts every one has an entry — guards against future drift), unknown-name fallback, expected-description spot-checks, null-arg validation, no-trailing-period convention check, end-to-end exporter integration.

Cumulative: 250 tests / 26 projects.

### Added — End-to-end telemetry integration test

- **`UT_E2E_Telemetry_Pipeline`** — drives the full telemetry pipeline (single shared `InMemoryMetrics` + `BatchSealer` + `MetricsEmittingDAWriter` + synthesized settlement/proving/bridge counters), stands up a `MetricsHttpServer` on a free port, scrapes `/metrics` over real HTTP, and asserts on the resulting Prometheus exposition. Covers both success path (4 batches → counters at 4) and failure path (DA write throws → `l2_da_publish_failures_total` incremented, success counter absent).
- Locks the metric contract end-to-end: every metric the production stack emits has a regression test.

Cumulative: 244 tests / 26 projects.

### Added — `/metrics` HTTP endpoint

- **`MetricsRequestHandler`** — framework-agnostic pure handler. Takes a request path, returns `MetricsHttpResponse` (status / content-type / body). Routes <c>/metrics</c> (with tolerance for trailing slash and query string) to a fresh `PrometheusExporter.Format(snapshot)`; everything else returns 404. Drop into any HTTP host (ASP.NET, Kestrel, RpcServer plugin) by routing GET <c>/metrics</c> through `Handle()`.
- **`IMetricsSource`** — read-side companion to `IL2Metrics`. `InMemoryMetrics` implements both. Decouples the snapshot read from the exporter so future sources (e.g. an OpenTelemetry-backed cache) plug in cleanly.
- **`MetricsHttpServer`** — minimal in-process HTTP server. Uses `TcpListener` + raw HTTP/1.0 framing instead of `HttpListener` (which is unreliable on Linux). Binds to a configurable IP/port (use port 0 for "any free port"; the resolved port is exposed via `Endpoint`). No third-party deps.
- **13 new tests**: 8 handler tests (status routing, query/trailing-slash tolerance, fresh-snapshot-per-call, null-arg validation, `IMetricsSource` round-trip), 5 server tests (real HTTP scrape, 404 on bad path, sequential requests, null-arg, port-zero binding). HttpClient explicitly bypasses ambient proxy env vars.

Cumulative: 242 tests / 26 projects.

### Added — Prometheus exporter

- **`MetricsSnapshot`** — frozen point-in-time read of every counter / gauge / histogram. Decouples accumulation (`IL2Metrics`) from export so future exporters (OpenTelemetry, StatsD, …) reuse the same shape.
- **`InMemoryMetrics.Snapshot()`** — produces a `MetricsSnapshot` immune to subsequent emissions.
- **`PrometheusExporter.Format(snapshot)`** — emits standards-compliant Prometheus exposition text: counters get `_total` suffix and `counter` type, gauges stay as-is with `gauge` type, histograms produce `_count` + `_sum` + `_max` aggregates with `summary` type. Tag pairs become labels with proper quoting; `.` and `-` in metric names sanitize to `_` per Prometheus naming rules. HELP/TYPE preambles emitted once per metric family.
- **`PrometheusExporter.ContentType`** — the canonical `text/plain; version=0.0.4; charset=utf-8` HTTP header value.
- **Devnet** now prints a `───── /metrics (Prometheus text format) ─────` section after each run, demonstrating the same data viewed through both the human summary and the production exporter.
- **10 new tests** in `Neo.L2.Telemetry.UnitTests`: empty-snapshot, counter, tagged counter, gauge, histogram, mixed kinds, content-type constant, name sanitization with dots+dashes, tagged histogram, snapshot frozenness.

Cumulative: 229 tests / 26 projects.

### Added — DA telemetry decorator

- **`MetricsEmittingDAWriter`** (`Neo.Plugins.L2DA`) — composition-pattern decorator that wraps any `IDAWriter` and emits `l2.da.published` (counter), `l2.da.publish_latency_ms` (histogram), and `l2.da.publish_failures` (counter), all tagged by `mode`. New writers automatically participate in the metric contract by being wrapped at plugin configure time.
- **`L2DAPlugin.WithMetrics(IL2Metrics)`** — wraps the chosen raw writer in the decorator. Idempotent: re-wiring a different metrics sink unwraps and re-wraps. NoOp metrics (the default) skip wrapping entirely.
- **6 new tests** in `Neo.Plugins.L2DA.UnitTests`: success path, propagating-throw failure path, accumulation across multiple publishes, `IsAvailableAsync` pass-through, null-arg validation, mode mirroring.
- **2 new `MetricNames`** entries: `DAPublishLatencyMs`, `DAPublishFailures`.

Cumulative: 219 tests / 26 projects.

### Refactor — extract testable `BatchSealer`

- **`BatchSealer`** (`Neo.Plugins.L2Batch`) — pure batch-accumulation state machine extracted from `L2BatchPlugin`. Owns `BatchBuilder` lifecycle, the three seal triggers (block-count, tx-count, age), block-context construction, and metric emission. Takes an injectable `Func<long>` clock so age-based seal can be exercised without sleeping. The plugin shrinks to ~70 lines whose only job is forwarding `Blockchain.Committed` to the sealer.
- **`Neo.Plugins.L2Batch.UnitTests`** — 7 unit tests that drive `BatchSealer` directly: seal-on-block-count, seal-on-tx-count, seal-on-age (with fake clock), batch-number monotonicity across seals, builder reset post-seal, NoOp metrics default safety, gauge-replace semantics for `l2.batch.tx_count`. Locks down the sealer's contract so future plugin refactors can't silently break the seal triggers.

Cumulative: 213 tests / 26 projects.

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
