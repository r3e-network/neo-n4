# Changelog

All notable changes to **Neo Elastic Network** (`neo4`).
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

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

### Out of MVP scope (deferred)

- Live L1 RPC client for `ISettlementClient` (`Neo.Network.RPC` integration).
- One-shot NeoHub deploy + register-chain script.
- `nccs` artifact generation (the build target is wired with `ContinueOnError=true`; users install nccs to generate `.nef` + `.manifest.json`).
- RpcServer plugin integration partial that registers `L2RpcMethods` as `[RpcMethod]`-attributed entry points.
- Phase 4 SP1 FFI bridge to `neo-zkvm`.
- Phase 5 recursive proof aggregation (current `PassThroughAggregator` is a non-ZK reference impl).
- Forced inclusion handler (doc.md §15.4).
