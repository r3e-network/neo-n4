# AGENTS.md — Guide for AI Agents

This file orients AI agents working on `neo4`. Read it before making changes.

## Authority order

1. **`doc.md`** — master architecture spec (Chinese). Treat as the implementation contract.
   Section numbers in this file refer to `doc.md` sections.
2. **`ARCHITECTURE.md`** — English distillation; reference only, not authoritative.
3. **`docs/architecture-walkthrough.md`** — narrative tour mapping every `doc.md` section
   to a concrete file. The fastest way to find where a feature lives.
   `docs/telemetry.md` covers the metrics surface separately.
4. **`IMPLEMENTATION_STATUS.md`** — per-phase coverage matrix + per-component table + the
   explicit "what's not yet wired" deferral list. Always check this before assuming a
   component doesn't exist.
5. **`CHANGELOG.md`** — chronological view of what each iteration shipped.

If `doc.md` and code disagree, the spec wins. If `doc.md` and reality disagree (e.g. a
`neo-project/neo` API has changed), surface the conflict and propose an update before
silently diverging.

## Working scope

`neo4` is a **consolidation layer**, not a fork. It adds L2 / NeoHub / Gateway / Stack
components on top of pre-existing Neo ecosystem repos. The three critical build-dep
upstreams are vendored as git submodules under `external/` (see
`project_submodule_layout.md` in memory); other repos in `/home/neo/git/` are
reference implementations the agent can read but does NOT need to extend in place.

- `neo` — official Neo 4 core (net10.0). Vendored as a git submodule at `external/neo`
  (never released on NuGet; project references resolve directly to the source tree via
  `Directory.Build.props` `NeoCorePath`).
- `neo-zkvm`, `neo-axiom` — ZK proof systems. `bridge/neo-zkvm-bridge` (Rust cdylib in this
  repo) wraps `neo-zkvm-prover` for C# P/Invoke. neo-zkvm is vendored as a git submodule
  at `external/neo-zkvm` (Cargo path dep, behind the optional `real-prover` feature so
  the bridge cargo-checks fine even without it).
- `neo-devpack-dotnet` — compile C# to NeoVM (used for `contracts/`). Also vendored as a
  git submodule at `external/neo-devpack-dotnet` (never released on NuGet for the version
  we track). `contracts/Directory.Build.props` defaults `NeoDevpackPath` to the submodule.
- `neo-execution-specs` — deterministic state-transition spec (proving target).
- `neo-riscv-{vm,core,node}`, `neo-llvm` — RISC-V VM stack (NeoVM2 candidate).
- `neo-sovereign-rollup`, `neo-matrix`, `neo-nexus` — earlier attempts at related tooling;
  generally NOT what to extend.

**Before writing a new component, search this repo's existing libs first** (the per-component
table in `IMPLEMENTATION_STATUS.md` has 16 off-chain libs + 8 plugins + 19 contracts; many
features that look missing are already there).

## Mapping `doc.md` to code (current state)

| `doc.md` § | Topic                       | Code location |
| ---------- | --------------------------- | ------------- |
| §3.2 NeoHub                | L1 contract suite          | `contracts/NeoHub.*` (15 contracts incl. `GovernanceFraudVerifier` (structural v1/v2) + `RestrictedExecutionFraudVerifier` (trustless v3)) |
| §4 Neo Gateway             | Phase-5 aggregation        | `src/Neo.Plugins.L2Gateway` (`BinaryTreeAggregator` + `IRoundProver`) |
| §5 L2 node internals       | Per-L2 plugin layout       | `src/Neo.Plugins.L2{Batch,Settlement,Bridge,DA,Prover,Rpc,Gateway,Metrics}` |
| §7.1 Sequencer / dBFT      | Committee selection        | `contracts/NeoHub.SequencerRegistry` + `src/Neo.L2.Sequencer` |
| §7.2 Batcher               | Block ↦ batch              | `src/Neo.L2.Batch` + `src/Neo.Plugins.L2Batch` |
| §7.3 StateRootGenerator    | Per-batch roots            | `src/Neo.L2.State` + `src/Neo.L2.Executor.State.KeyedStateStore` |
| §7.4 DAWriter              | DA layer abstraction       | `src/Neo.L2.Abstractions.IDAWriter` + `src/Neo.Plugins.L2DA` |
| §7.5 ProverAdapter         | 3-stage proving            | `src/Neo.L2.Proving.Attestation/Optimistic/RiscVZk` + `src/Neo.L2.Proving.Sp1` |
| §8 Proof system            | Proving spec               | `src/Neo.L2.Executor/SPEC.md` |
| §9 Token / GAS model       | Bridged accounting         | `src/Neo.L2.Bridge.AssetRegistry` + `contracts/L2Native.L2BridgeContract` |
| §10 Neo Connect            | Cross-chain messaging      | `src/Neo.L2.Messaging` + `contracts/L2Native.L2MessageContract` |
| §11 SharedBridge           | Asset escrow               | `contracts/NeoHub.SharedBridge` + `src/Neo.L2.Bridge` |
| §12 Data Availability      | DA tiers                   | `src/Neo.L2.Abstractions.DAMode` + `src/Neo.Plugins.L2DA` (incl. `NeoFsLikeDAWriter`) |
| §13 L2 native contracts    | On-L2 system contracts     | `contracts/L2Native.*` (6 contracts) |
| §3 / §7.1 Custom chain logic | Operator-provided executor seam | `src/Neo.L2.Executor/ITransactionExecutor.cs` (interface), `samples/executors/Sample.CounterChainExecutor/` (runnable reference: 3-opcode custom tx format + state seam + withdrawal/message emission + 13 tests) |
| §14.1 L2 RPC               | RPC method surface         | `src/Neo.Plugins.L2Rpc.L2RpcMethods` |
| §14.2 neo-stack CLI        | Launch framework           | `tools/Neo.Stack.Cli` |
| §15.4 Forced inclusion     | Anti-censorship            | `contracts/NeoHub.ForcedInclusion` + `src/Neo.L2.ForcedInclusion` + `src/Neo.L2.Censorship` |
| §16 Governance             | Council + timelock         | `contracts/NeoHub.GovernanceController` |
| §17 Threat model           | Mitigations                | Distributed: `SequencerBond`, `OptimisticChallenge`, `BisectionGame`, `EmergencyManager` |
| §18 Phased rollout         | Phase 0–6 plan             | `IMPLEMENTATION_STATUS.md` |
| §19 Module layout          | Recommended structure      | `src/`, `contracts/`, `tools/` (this layout matches §19) |
| §20 MVP                    | Phase-0 success criteria   | `tests/Neo.L2.IntegrationTests/UT_Mvp_Phase0_Sidechain` |
| Cross-cutting              | Telemetry / observability  | `src/Neo.L2.Telemetry` + `src/Neo.Plugins.L2Metrics` (composition root); operator catalog in [`docs/telemetry.md`](./docs/telemetry.md) |

For more detail, see [`docs/architecture-walkthrough.md`](./docs/architecture-walkthrough.md).

## Conventions

### Code style

- `net10.0`, `nullable enable`, `ImplicitUsings enable` for runtime libs (contracts and tests
  set `ImplicitUsings disable`).
- `TreatWarningsAsErrors=true`. Exceptions: `CS1591` (missing XML doc) + `NU190x` (NuGet audit).
- **Records over classes** for data carriers. When a record contains `ReadOnlyMemory<byte>`,
  override `Equals` + `GetHashCode` so byte-content participates (see `L2BatchCommitment`).
- **Every public type's XML doc points at a `doc.md` section** in `<remarks>`. IDE tooltips
  become navigation aids.

### Canonical encodings (DO NOT REINVENT)

- Multi-byte integers: **little-endian** everywhere.
- Hashes: **`Hash256`** (double-SHA256), matching Neo's existing `MerkleTree` convention.
- `BigInteger` payloads: unsigned little-endian.
- Pinned canonical encoders:
  - `Neo.L2.Batch.BatchSerializer` — `L2BatchCommitment` + `PublicInputs`.
  - `Neo.L2.State.MessageHasher` — cross-chain messages + withdrawal records.
  - `Neo.L2.Bridge.DepositPayload` — deposit message body.
  - `Neo.L2.State.MerkleProofSerializer` — canonical inclusion-proof bytes (consumed by L1 SharedBridge for withdrawal verification).
  - `Neo.L2.L2ChainConfigSerializer` — 91-byte L2ChainConfig wire format (consumed by `NeoHub.ChainRegistry.RegisterChain`).
  - `Neo.L2.Proving.Attestation.MultisigProofPayload` / `Optimistic.OptimisticProofPayload`
    / `RiscVZk.RiscVProofPayload` — per-stage proof bytes.
  - `Neo.L2.Challenge.FraudProofPayload` — Phase-3 fraud-proof bytes.

### Pluggability

Every cross-cutting capability has an interface so phases can swap implementations:

- Verifiers: `IL2ProofVerifier` (multisig / optimistic / mock-zk / SP1).
- Provers: `IL2Prover` (same dispatch).
- DA writers: `IDAWriter` (in-mem / NeoFsLike / L1 / DAC stubs).
- Round provers: `IRoundProver` (pass-through / future SP1Compress / Halo2).
- Audit checks: `IAuditCheck` (continuity / proof-validity / your custom check).

### Tests

- One `tests/<Project>.UnitTests/` per `src/<Project>/` (and `tools/<Tool>/`).
- MSTest with `Assert.ThrowsExactly<T>` (the analyzer flags `Assert.ThrowsException<T>`).
- Test methods named `<Subject>_<Behavior>` (e.g. `BatchBuilder_RejectsOutOfOrderBlocks`).
- Use `FakeClock` (in `Neo.L2.Censorship`) for deterministic time-dependent tests.
- Cross-component scenarios live in `tests/Neo.L2.IntegrationTests/`.

## When you're about to write a new component

1. **Check `IMPLEMENTATION_STATUS.md`** — the per-component tables are exhaustive. If you're
   thinking "I should write a `XxxRegistry`," confirm it doesn't already exist.
2. **Check the canonical encoders** above. If your component needs to serialize a model, the
   encoder probably exists already.
3. **Pick a number ABI/spec from `doc.md`**. Don't add config fields not in the spec without
   proposing a spec update.
4. Follow the recipe in `CONTRIBUTING.md` — csproj + XML docs + tests + `Neo.L2.sln` entry +
   per-component table update + `CHANGELOG.md` entry.

## Phased work

Phases 0/1/2/3/6 are ✅. Phases 4 and 5 are 🟡 — substantial scaffolding + tests,
but blocked on:

- **Phase 4**: real SP1 prover requires `cargo build --release --features real-prover` in
  `bridge/neo-zkvm-bridge`. neo-zkvm comes in via the `external/neo-zkvm` git submodule.
- **Phase 5**: real recursive-ZK round prover (SP1 Compress / Halo2 / Risc0 fold). The
  `IRoundProver` interface is the plug-in point.

Phase 6 (12 CLI subcommands: create-chain / init-l2 / register-chain / deploy-bridge-adapter
/ start-{sequencer,batcher,prover} / submit-batch / validate / scaffold-executor / new-l2 /
list-templates) is ✅ — every subcommand is functional. The 3 commands that need L1/L2
wallet integration (register-chain, deploy-bridge-adapter, submit-batch) print structured
operator plans rather than performing the wallet-side submission.

## Don'ts

- **Don't fork** `neo-project/neo`. Extend via plugins and references.
- **Don't issue canonical GAS on L2** outside the bridge mint path.
- **Don't bypass `ChainRegistry`** — every L2 must register before submitting batches.
- **Don't write `// added for X` or `// TODO once Y` style comments**. Track followups in
  the task list or git history.
- **Don't add config fields not in `doc.md`** without proposing a spec update first.
- **Don't reimplement canonical encoders.** Reuse `BatchSerializer`, `MessageHasher`, etc.
- **Don't break the byte format** of any encoding pinned in this repo without coordinating
  the matching contract change. The off-chain ↔ on-chain encodings are paired.

## Quick commands

```bash
# Type-check + run all 1051 tests
dotnet test Neo.L2.sln /p:NuGetAudit=false

# Devnet demonstration with audit pass
dotnet run --project tools/Neo.L2.Devnet -- 5

# Build a specific contract
dotnet build contracts/NeoHub.ChainRegistry /p:NuGetAudit=false /p:DisableNccs=true
```
