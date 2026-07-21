# Implementation Status

> Snapshot of what's built in `neo4` against `doc.md`. Updated alongside meaningful PRs.

## Phase maturity matrix (doc.md §18)

The columns are deliberately independent. A phase can have complete design and
working code without having a reviewed live deployment or being production-ready.

| Phase | Goal | Design / spec | Code shape | Integrated path | Cryptographically enforced | Current-revision deployment evidence | Production-ready |
| ----- | ---- | :-----------: | :--------: | :-------------: | :------------------------: | :----------------------------------: | :--------------: |
| 0 | Sidechain PoC | ✅ | ✅ | ✅ local devnet | N/A | ❌ no exact-revision public deployment | ❌ |
| 1 | NeoHub v0 + Shared Bridge | ✅ | ✅ 26 projects / 24 production | ✅ planner + runtime composition | 🟡 security profile dependent | ❌ no exact-revision reviewed deployment | ❌ |
| 2 | Batch Settlement | ✅ | ✅ | ✅ local end-to-end path | 🟡 multisig / optimistic / ZK profile dependent | ❌ no exact-revision reviewed deployment | ❌ |
| 3 | Optimistic Challenge Window | ✅ | 🟡 restricted executable v4 | 🟡 one committed Counter transition | 🟡 exact registered v4 only; general NeoVM fails closed | ❌ no exact-revision reviewed deployment | ❌ |
| 4 | NeoVM2 / RISC-V ZK Validity Proof | ✅ | ✅ PolkaVM profile + exact-semantic SP1 profile | ✅ native C#→Rust + terminal proof in local/CI gates | ✅ `Sp1StatefulNeoVmV1` native/guest parity, pinned Groth16 verifier, binding, and tamper rejection; PolkaVM validity requires a matching prover | ❌ reviewed NEF/VK deployment evidence absent | ❌ |
| 5 | Neo Gateway proof aggregation | ✅ | ✅ aggregator + durable outbox | ✅ atomic settlement publication path | 🟡 SP1 validity profile is enforced; attested/Merkle modes have different trust | ❌ executed exact-revision deployment absent | ❌ |
| 6 | Neo Stack CLI / Templates | ✅ | ✅ 12 commands | 🟡 three wallet-gated commands emit operator plans | N/A | ❌ no exact-revision operator deployment record | ❌ |

Legend: ✅ proved for the current revision · 🟡 partial or profile/configuration
dependent · ❌ missing · N/A not applicable. No phase is production-ready until
its exact reviewed revision has independent audit evidence, reproducible release
artifacts, a recorded deployment, live positive/negative smoke tests, and the
operator requirements in [`SECURITY.md`](SECURITY.md) are satisfied.

## Production-readiness audit

The phase matrix above measures **architectural coverage** — does the
component exist with the right shape? It does NOT measure whether each
component is mainnet-ready. Below is an honest readiness audit.

### Production-shaped and locally verified

These components have production-shaped implementations and focused local coverage.
This label is not a mainnet-readiness claim; live deployment evidence, external audits,
and every open release gate still apply:

- **All 26 NeoHub L1 contract projects** type-check via
  `Neo.SmartContract.Framework`; CI compiles each with `nccs` and verifies
  the `.nef` + `.manifest.json` artifacts. The 24-contract production bundle
  excludes advisory `GovernanceFraudVerifier` and test-only `ExternalBridgeStubVerifier`, and includes
  `ContractZkVerifier` plus immutable `Sp1Groth16Verifier`: the router validates
  proof envelopes and dispatches SP1 proofs to the terminal BN254 verifier. NeoHub is intentionally shipped as
  deployed contracts plus plugin/service integration, not as L1 Neo core native
  contracts. Production deployment requires explicit M-of-N GovernanceController admission and
  irreversibly locks SettlementManager against hot-owner dependency rewiring or direct rollback;
  exceptional finalized-head rollback is proposal-bound, threshold-approved, timelocked, and
  one-time. **All 10 N4 L2 system contracts**
  are Neo core native contracts in `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
  and are verified by `external/neo/tests/Neo.UnitTests/SmartContract/Native/UT_L2NativeContracts.cs`.
- **Off-chain canonical encoders**, byte-layout-pinned + tested:
  `BatchSerializer`, `MessageHasher`, `MerkleProofSerializer`,
  `L2ChainConfigSerializer`, `DepositPayload`, `MultisigProofPayload`,
  `RiscVProofPayload`, `OptimisticProofPayload`, `FraudProofPayload`.
- **Persistence layer** — `IL2KeyValueStore` with `InMemoryKeyValueStore`
  (tests) + `RocksDbKeyValueStore` (production); per-component reopen tests
  pin the durability story across 7 components. `IAtomicL2KeyValueStore.CompareExchangeBatch`
  guards bounded artifact/recovery mutations, while `CompareExchangeAll` compares the complete
  witnessed pre-state and commits the complete SP1 post-state through one lock-protected in-memory
  swap or one durable RocksDB `WriteBatch`; concurrent writers cannot overwrite a winning transition
  and invalid replacement input cannot partially mutate state.
- **SP1 stateful execution handoff** — `Sp1SettlementExecutionStack` binds one persisted
  genesis root, complete `NEO4STW1` source, SHA-256-pinned native
  `neo-zkvm-executor`, atomic state store, durable prover queue, program VK, and ZK
  pipeline profile. Chain registration atomically stores the same non-zero root as an immutable
  L1 trust anchor; batch 1 submission/finalization and every off-chain restart cross-check it
  before side effects. The C# executor and SP1 guest call the same Rust execution core;
  cross-language tests execute a real bootstrapped Neo genesis transaction and reject
  forged request/output bindings without mutating state. Settlement atomically persists and
  byte-verifies the immutable proof artifact before replaying that exact transition into the
  atomic state store; idempotent retry and startup recovery close the artifact/state crash window.
  The content-addressed SP1 queue uses owner-only `0700` directories, `0600` artifacts, 16-GiB /
  64-task hard limits, and a durable settlement acknowledgement. The Rust daemon prunes request,
  proof, VK, public values, result, and archive bytes only after `SettlementFinalized`; TTL deletion
  is forbidden.
- **Stage 0 multisig prover** — real Secp256r1 signature aggregation
  (`AttestationProver`, `AttestationVerifier`).
- **Optimistic challenge bisection game** — real log-N narrowing algorithm.
- **Bridge accounting** — `AssetRegistry`, `DepositProcessor`,
  `WithdrawalProcessor` with replay protection + nonce dedup, plus per-batch
  withdrawal verification on L1 (`SettlementManager.VerifyWithdrawalLeafWithProof`).
  Production L1 deposit discovery is `RpcSharedBridgeDepositSource`: durable
  `DepositEnqueued` scanning, `GetDeposit` materialization into the canonical
  `CrossChainMessage` + `DepositPayload`, and batcher drain via `Peek` /
  `ConfirmConsumed` (SharedBridge does not enqueue MessageRouter).
- **Forced-inclusion spam control** — `NeoHub.ForcedInclusion` charges a configurable amount of
  Neo N3 native GAS per enqueue (`SetFee` / `SetFeeRecipient` / `SetGasToken`); every deploy,
  configuration, readiness, and enqueue path rejects substitute NEP-17 contracts. CEI ordering
  plus atomic fault rollback prevents nonce reuse and partial state; default 0 preserves the
  fee-free development path.
- **Audit pipeline** — 6 invariant checks (continuity / proof-validity /
  public-input hash / no-zero-proof / DA availability / batch range).
- **CLI tooling** — `neo-stack` plan-printers + `validate` subcommand;
  `neo-hub-deploy` declarative L1 deploy planner (now scaffolds the
  external-bridge stack alongside NeoHub: 24 steps + explicit verifier,
  payout-route, liquidity, governance-lock, and DA post-deploy wiring
  hints); `neo-external-bridge` operator CLI for bridge committee
  setup + dual-side deploy planning.
- **Cross-foreign-chain bridge (Phase B + C — doc.md §11.3)** —
  pluggable M-of-N committee verifier (`NeoHub.MpcCommitteeVerifier`
  with secp256k1 + ed25519 dispatch, replay-protected per nonce; now
  also stores per-signer bond-holder member binding via
  `RegisterCommitteeWithMembers`) + `NeoHub.ExternalBridgeRegistry`
  for verifier dispatch + `NeoHub.ExternalBridgeEscrow` (locks
  NEP-17 outbound + verifies inbound via registry + atomically releases
  funded NEP-17 for the explicit L1 domain or invokes a mandatory
  version/update-counter-pinned payout-v1 adapter for an L2 destination;
  immutable/reverse-unique asset mapping, replay protection,
  and irreversible proposal-only production governance) +
  `NeoHub.ExternalBridgeBond` (committee bonding mirroring
  `SequencerBond`) + `NeoHub.MpcCommitteeFraudVerifier` (Phase C —
  proves equivocation cryptographically + slashes the full bond +
  pays the reporter; replay-protected per `(chainId, signerIdx)`) +
  `L2NativeExternalBridgeContract` (Neo core native L2-side burn/mint counterpart).
  Eth-side `NeoExternalBridgeRouter.sol` (Solidity 0.8.24,
  **44 Foundry tests** = 37 single-chain coverage + 7 multi-chain
  pinning per-instance state isolation across 17 canonical mainnet
  slots) ships in `external/foreign-contracts/eth/`. The same Solidity
  bytecode deploys unchanged on **any EVM chain** — constructor
  parameterizes `externalChainId`, `EthRpcEventSource` polls
  `eth_getLogs` against any EVM RPC endpoint, and the secp256k1
  `Signer` is reusable. Canonical 16-slot family banks in
  `watchers/neo-bridge-watcher-eth/src/chains.rs` cover Ethereum,
  Tron, BSC, Polygon, Arbitrum, Optimism, Base, Avalanche, Linea,
  zkSync Era, Scroll, Mantle, Fantom/Sonic, Celo; adding a new EVM
  chain takes 5 steps and writes zero new code (operator runbook in
  `docs/external-bridge-evm-chains.md`). Off-chain signing core
  lives in `watchers/neo-bridge-watcher-eth/` (Rust crate,
  byte-for-byte parity tests against the C# encoder, end-to-end
  orchestration with mockable trait abstractions). 7 real-secp256k1 tests in
  `UT_MpcFraudProof_RealCrypto.cs` pin the equivocation proof shape
  end-to-end (happy path + identical-messages reject + different-nonces
  reject + wrong-pubkey reject + chainId-mismatch reject + nonce-zero
  edge + committee-blob layout invariant).

### Optimistic-challenge fraud-proof game — restricted v4 shipped

`GovernanceFraudVerifier` v1/v2 and `RestrictedExecutionFraudVerifier` v3 validate
structural evidence only. They are audit/advisory artifacts: `OptimisticChallenge`
rejects them before verifier dispatch, including when the owner/governance signs.
Legacy global permissionless registration is disabled.

The versioned v4 path is permissionless only for an exact chain-scoped profile.
It reads the canonical 321-byte `Challengeable` optimistic header from
`SettlementManager`, binds chain/batch/pre/post/tx roots, replay domain,
executor semantic id, canonical degenerate `[0,1]` transcript, transaction index,
claim id, and complete witness. It verifies the canonical single-leaf tx proof,
reconstructs the old and committed-new leaves against the committed pre/post
roots, executes one existing-key Counter Increment transition from the old
pre-state path, returns false for a correct committed transition, and returns
true only for an actually incorrect committed root.
`OptimisticChallenge` consumes successful v4 claim ids and writes CEI guards
before atomic revert/slash calls.

This is trustless only inside the declared restricted semantic profile; it is
not a general NeoVM claim:

| Item | What's still missing | Where |
|------|---------------------|-------|
| General NeoVM / multi-transaction fraud proofs | The current batch header has no committed transaction count or intermediate execution-trace root, so v4 deliberately accepts only `txIndex=0`, `txCount=1`, interval `[0,1]`, and semantic id `Hash256("neo4-executor:counter-increment-existing-key:v1")`. Arbitrary NeoVM opcodes, other custom executors, key insertion/deletion, and multi-tx bisection fail closed. A general protocol needs committed tx-count/trace anchors plus complete single-step NeoVM semantics. | Restricted v4 ✅; general NeoVM ❌ |
| Default deployment automation | The 24-step production planner excludes advisory `GovernanceFraudVerifier`, deploys the immutable domain-bound `L2PayoutAdapter`, deploys `RestrictedExecutionFraudVerifier` with `[SettlementManager, replayDomain]`, requires explicit non-zero relay/domain inputs, registers only the exact chain/semantic/domain v4 profile, and smoke-checks verifier configuration plus profile state. Legacy empty verifier deployments are not registered and remain fail closed. | ✅ Production planner + legacy fail-closed compatibility |

### Reference / scaffolding — operator must replace

These are the deliberate "framework provides seam, operator brings impl"
boundaries. They're functional for the in-process devnet and tests, but
production would inject a real implementation through the documented
interface:

| Reference / scaffolding default | Production needs | Plug-in point |
|---------------------------------|------------------|---------------|
| `ReferenceTransactionExecutor` (devnet/tests) | `RiscVTransactionExecutor` ships in `src/Neo.L2.Executor.RiscV/` and is the canonical Neo N4 L2 executor (`--executor riscv`, also accepted as `--executor neovm2-riscv`). `ApplicationEngineTransactionExecutor` remains available only for legacy NeoVM compatibility checks (`--executor neovm`) and N3-era state-continuity tests. | `ITransactionExecutor` |
| `ReferenceBatchExecutor` + `DerivedPostStateRootOracle` (XOR placeholder) | `MerkleStatePostStateRootOracle` ships in `src/Neo.L2.Executor/` — production state root via `KeyedStateMerkleTree` (binary Merkle over sorted (key, value) pairs, same primitive ZKsync / Polygon zkEVM / Optimism use). Per-key inclusion proofs via `Prove(byte[])`. Plugs into the existing `ReferenceBatchExecutor` (which is otherwise production-quality); replacing the oracle turns the whole batch executor into production code | `IL2BatchExecutor` / `IPostStateRootOracle` |
| `MockRiscVProver` / `MockRiscVVerifier` (in-process testing) | Real ZK prover lives out-of-process: `prove-batch daemon` (Rust, `bridge/neo-zkvm-host/`). The .NET `L2ProverPlugin` keeps the in-process Zk path mock-only by design — see `docs/launching-an-l2.md` § "Prover deployment" | `IL2Prover` / `IL2ProofVerifier` |
| `PassThroughRoundProver` is a deterministic reference combiner; `MultisigRoundProver`, `MerklePathRoundProver`, and the dedicated `neo-zkvm-gateway-{guest,host}` SP1 terminal path ship | Operators may still add Halo2/Risc0 alternatives; the bundled SP1 path requires canonical compressed batch sidecars and proving hardware | `IRoundProver` + Gateway file queue |
| `InMemorySequencerCommitteeProvider` (devnet/tests) | `RpcSequencerCommitteeProvider` ships in `src/Neo.L2.Sequencer/` — production L1-RPC poller with configurable cache TTL, parallel status fanout across known keys, operator-supplied known-keys bootstrap (genesis + RegisterKnownKey hook for event-driven additions). `IsRegisteredAsync` always hits L1 (source of truth) | `ISequencerCommitteeProvider` |
| `InMemoryForcedInclusionSource` (devnet/tests) | `RpcForcedInclusionSource` + `RpcForcedInclusionEventScanner` ship in `src/Neo.L2.ForcedInclusion/`. Production wiring scans finalized L1 blocks, parses contract-bound `ForcedTxEnqueued` application logs, durably persists each nonce before advancing a hash-verified restart cursor, then issues parallel `getEntry` + `isConsumed` reads and returns deadline order. Finalized-history mismatch and malformed logs fail closed; manual `RegisterNonce` is recovery-only. | `IForcedInclusionSource` |
| In-process deposit injection (devnet) | `RpcSharedBridgeDepositSource` + scanner + `InMemorySharedBridgeDepositSource`. Full lifecycle: **Scan at seal** (`L1MessageDrain.FromDeposits`) → **Drain (reserve)** → durable batch seal → **ConfirmConsumed** (or **ReleaseReservations** on persist failure). `L2BatchPlugin.WireL1MessageInbox` is the production composition root for deposits ± MessageRouter; `L2SettlementPlugin.Wire` / `WireProduction` attach the same inbox before the sealed-batch sink (and may own the RPC deposit source when `SharedBridgeHash` is set). Unit evidence covers scan-at-seal, seal-confirm, persist-fail release/retry, and settlement wiring fail-closed. | `ISharedBridgeDepositSource` |
| `InMemoryMessageRouter` (devnet/tests) | `RpcMessageRouter` + `RpcMessageRouterEventScanner` ship in `src/Neo.L2.Messaging/`. Production discovers finalized `L1ToL2Enqueued` events (durable cursor), then polls `getL1ToL2` + `isConsumed`; local outbox for outbound; pluggable finalized-proof store for `GetMessageProofAsync`. `WireProduction` owns the stack when `MessageRouterHash` is set and installs it on `L2BatchPlugin` via `WireL1MessageInbox` (exposed as `batchPlugin.MessageRouter`). Seal-path unit evidence covers router-only and deposit+router merged inboxes. `DecodeMessage` recomputes the canonical hash via `MessageHasher` — never trusts an off-wire hash | `IMessageRouter` |
| `InMemorySettlementClient` | `L2SettlementPlugin.WireProduction` constructs the real `RpcSettlementClient` + network-pinned `RpcTransactionSender` + durable forced-inclusion event scanner/source/finalizer; operator supplies the reviewed `INeoTransactionSigner` and opens RocksDB at the recommended `data/settlement/*` paths (heights default from plugin config when set by `--from-deploy-report`) | `ISettlementClient` / `INeoTransactionSigner` |
| `InMemoryDAWriter`, `NeoFsLikeDAWriter` (dev/sim only) | Production: `NeoFsRestDAWriter` + `NeoFsRestDAReader` via `WithProductionBackend`, or a reviewed NeoFS SDK adapter with independent retrieval | `IProductionDAWriter` / `IProductionDAReader` |
| `JsonRpcL1DAWriter` (signer = delegate), `CommitteeAttestedDAWriter` (committee = delegate) | Signed L1 transactions / real DAC committee credentials supplied through DI | `IDAWriter` |

### Operator execution boundaries

Transaction-producing commands support an explicit signed path, and process commands
supervise the real operator binaries:

- `neo-stack register-chain`, `deploy-bridge-adapter`, and `submit-batch` retain a
  deterministic plan mode; `--broadcast` selects a local WIF or fail-closed
  external signer command, then signs, preflights, broadcasts, and confirms.
- `neo-stack start-sequencer` and `start-batcher` launch a reviewed Neo.CLI deployment
  through `ProcessStartInfo.ArgumentList`, propagate its exit code, and terminate the child
  gracefully on SIGINT/SIGTERM before a bounded kill fallback.
- `neo-stack start-prover` launches the real `prove-batch daemon` with durable inbox/archive
  directories and the same supervision guarantees.
- `start-sequencer --sync-committee` submits the canonical native
  `setSequencerValidators` governance transaction before launch; `--sync-only` performs only
  the confirmed rotation.

Wallet integration patterns are documented in
[`docs/wallet-integration.md`](docs/wallet-integration.md) and
[`docs/operator-signer-command-protocol.md`](docs/operator-signer-command-protocol.md):
paste-into-wallet hex (cold-key flows), delegate signing, and the CLI's
provider-neutral HSM/KMS command boundary. The framework never holds private
keys.

### Out of repo by design

- A Neo.CLI/DBFTPlugin binary distribution is operator-supplied. The repo does not claim a
  non-existent `r3e-network/neo-node` release; `neo-stack` validates the supplied deployment
  and the r3e Neo core's native selector makes the unmodified DBFTPlugin consume the on-chain
  L2 validator set.
- **Block explorer / bridge UI / faucet UI**: web variants ship in
  `sdk/web-explorer/index.html` (single static-file app with inlined JS SDK).
- **Typed SDKs**: source implementations are present in four languages — `src/Neo.L2.Sdk/` (.NET),
  `sdk/typescript/` (TS), `sdk/rust/` (Rust), and `sdk/python/` (Python).
  All consume one canonical conformance vector set, expose the same wire shape
  and 4-class error taxonomy, and have opt-in live N3/N4 node tests.
- **Faucet CLI**: `tools/Neo.L2.Faucet.Cli/` (`neo-l2-faucet`) — production
  drip with rate limiting + RocksDB-persisted journal.

### Bottom line

The framework is **architecturally complete + sufficient for a devnet** with
real cryptographic primitives, real persistence, real test coverage. It is
**not** a turnkey mainnet deployment — operators targeting production must
(a) replace the reference / in-memory scaffolding through the documented
plug-in seams, (b) wire production fee config on `ForcedInclusion`
(`SetFee` / `SetFeeRecipient` / native-GAS-only `SetGasToken`) to enable spam control,
(c) use the `*WithProof` variants of `SettlementManager.VerifyWithdrawalLeaf*`
and `EmergencyManager.EscapeHatchExit` (the no-proof variants are
intentional single-leaf fast paths, only valid when the relevant tree has
exactly one entry), and (d) supply audited Neo.CLI/DBFTPlugin and HSM/KMS operator
deployments for the documented process/signing seams.

## Completed work — by code

### Off-chain libraries (`src/Neo.L2.*`)

| Project                   | Role                                                              |
| ------------------------- | ----------------------------------------------------------------- |
| `Neo.L2.Abstractions`     | 7 interfaces + 14 model records (doc.md §19)                      |
| `Neo.L2.Batch`            | `L2Batch`, `BatchBuilder`, deterministic `BatchSerializer`        |
| `Neo.L2.State`            | `MerkleTree` (matches Neo `Hash256`), **`MerkleProofSerializer`** (canonical 48 + 32×N byte wire format consumed by L1 SharedBridge), `MessageHasher`, `WithdrawalTree`, `MessageTree`, `StateRootCalculator` |
| `Neo.L2.Bridge`           | `AssetRegistry`, `DepositPayload`, `SharedBridgeDepositRecord`, `DepositProcessor`, `WithdrawalProcessor`, production `RpcSharedBridgeDepositScanner` + `RpcSharedBridgeDepositSource` (DepositEnqueued → GetDeposit → canonical L1→L2 message). Processors emit `l2.bridge.deposits/deposits_rejected/withdrawals/withdrawals_rejected` |
| `Neo.L2.Messaging`        | `MessageBuilder`, `L1MessageInbox`, `L2Outbox` (emits `l2.messaging.emitted`), `InMemoryMessageRouter`, canonical external-message encoder/decoder |
| `Neo.L2.ExternalBridge`   | Finalized L1 payout-queue scanner + RocksDB durable outbox + prepared-transaction recovery + authenticated L2 native credit and L1 acknowledgement clients. State advances `enqueued -> credit-prepared -> credited -> ack-prepared -> acknowledged`; exact message bytes/hash and L2 transaction receipt are replay-checked across restart. |
| `Neo.L2.Proving`          | Stage 0 multisig (`AttestationProver`/`Verifier`), Stage 1 optimistic (`OptimisticProver`/`Verifier` + bond payload), Stage 2 mock RISC-V (in-process testing seam); `VerifierRegistry`. Real Stage-2 ZK proving lives in `bridge/neo-zkvm-host/` (Rust) — a separate process operators run as the `prove-batch daemon`. |
| `Neo.L2.Executor`         | `SPEC.md` + fixed 105-byte `Receipt`, pluggable `ITransactionExecutor` / `IPostStateRootOracle` / `IL1MessageProcessor`, `ReferenceBatchExecutor`, **`ExecutionStateTransaction` + canonical execution effects V1 + `KeyedStateStore` + `KeyedStateRootOracle`**, plus complete `NEO4STW1` capture and SHA-256-pinned `Sp1StatefulBatchExecutor` with validated `NEO4EXR1`; its committed-artifact state sink replays and atomically commits the exact post-state only after durable artifact publication |
| `Neo.L2.ForcedInclusion`  | Anti-censorship `IForcedInclusionSource` + in-memory backend with optional `IL2KeyValueStore` (RocksDB) for consumed-nonce durability across restart (emits `l2.forced_inclusion.observed` on Enqueue) |
| `Neo.L2.Sequencer`        | `ISequencerCommitteeProvider` + in-memory/RPC backends, optional RocksDB durability, and `SequencerCommitteeTransactionBuilder` for canonical native validator rotations. The r3e Neo core's stock `Governance.GetNextBlockValidators` reads the configured `L2SystemConfigContract` set, so an unmodified DBFTPlugin follows consensus state. |
| `Neo.L2.Censorship`       | `CensorshipDetector` — turns overdue forced-tx entries into `CensorshipReport[]` (emits `l2.censorship.reports` per detection batch) |
| `Neo.L2.Challenge`        | `FraudProofPayload` v1/v2/v3 governance evidence + `RestrictedFraudProofV4` canonical codec/reference verifier + `ChallengeOrchestrator` (`InspectAsync` replay detection, `InspectWithBisectionAsync` log-N narrowing) + `BisectionGame`; v4 is SettlementManager/root/replay/semantic/claim/witness-bound and deliberately single-tx Counter-only |
| `Neo.L2.Settlement.Rpc`   | JSON-RPC client + confirmed `RpcTransactionSender` + `RpcSettlementClient` + finalized-root-bound `RpcForcedInclusionFinalizationClient` |
| `Neo.L2.Audit`            | End-to-end chain auditor: `ContinuityCheck` + `ProofValidityCheck` + `NoZeroProofCheck` + `PublicInputHashConsistencyCheck` + `DAAvailabilityCheck` + **`BatchRangeCheck`** + `ChainAuditor` (auto-emits `l2.audit.runs` + `l2.audit.failures`) |
| `Neo.L2.Telemetry`        | `IL2Metrics` (counter/histogram/gauge) + `NoOpMetrics` + `InMemoryMetrics` + `MetricsSnapshot` + `PrometheusExporter` + `MetricsRequestHandler` (`/metrics` + **`/healthz` + `/readyz`**) + `MetricsHttpServer` (TcpListener-based, no third-party deps) + canonical `MetricNames` + `MetricCatalog` (operator-facing HELP descriptions) |
| `Neo.L2.Persistence`      | **`IL2KeyValueStore` abstraction + atomic-complete-state `IAtomicL2KeyValueStore` + `InMemoryKeyValueStore` + `RocksDbKeyValueStore` (RocksDbSharp 10.10.1, snappy compression default).** Wired into `KeyedStateStore`, `InMemoryL2RpcStore`, `InMemoryMessageRouter`, `InMemoryForcedInclusionSource`, `InMemorySequencerCommitteeProvider`, `PersistentDAWriter`, and SP1 post-state commit so production data survives restart. Per-component reopen and replacement tests pin durability and no-partial-write behavior. |
| `Neo.L2.Executor.RiscV`   | **Phase 4 stateful RISC-V execution engine.** Binds `libneo_riscv_host`'s `neo_riscv_execute_script_with_host` callback ABI with rooted delegates, `GCHandle` transaction context, explicit native/callback buffer ownership, recursive stack marshalling, Neo runtime/storage/iterator callbacks, real native + host fee accounting, and HALT-only state/effect commit. Unknown and not-yet-safe consensus syscalls fail closed. Native lib operator-deployed via `LD_LIBRARY_PATH` or alongside the C# binaries. |

### Native FFI bridge (`bridge/`) + watchers (`watchers/`)

| Crate                       | Role                                             |
| --------------------------- | ------------------------------------------------ |
| `neo-zkvm-guest`            | One Rust crate with two binaries over the same execution core: the SP1 RISC-V guest verifies `NEO4PWIT` and commits its public-input hash; host-native `neo-zkvm-executor` consumes `NEO4EXEC` + complete `NEO4STW1` and emits content-addressed `NEO4EXR1` for the C# sequencer execution boundary. N4 genesis V1 supports the bounded native/syscall profile and immutable deployed-contract descriptor set; unsupported semantics fail closed. |
| `neo-zkvm-host`             | Rust binary (sp1-sdk 6.2.1): `prove-batch daemon --watch <dir> --gateway-sidecars <dir>` emits terminal batch proofs and tuple-bound compressed child sidecars. Its private, bounded queue prunes content-addressed artifacts only after a hash-bound settlement acknowledgement. Build scripts validate SHA-256/VK from one Docker ELF snapshot, then embed an isolated read-only Cargo `OUT_DIR` copy. Also exposes `execute()` / `prove()` / `prove_compressed()` / `verify()`. |
| `neo-zkvm-gateway-host`     | Rust binary (sp1-sdk 6.2.1): `prove-gateway daemon --queue <dir> --child-proofs <dir>` verifies canonical child sidecars and emits the terminal 356-byte recursive Gateway Groth16 proof. |
| `neo-bridge-watcher-eth`    | Rust crate: messaging + signing core for the **entire EVM family** ↔ Neo external bridge. Same daemon binary serves Ethereum, Tron, BSC, Polygon, Arbitrum, Optimism, Base, Avalanche, Linea, zkSync, Scroll, Mantle, Fantom, Celo — chain-id is a config field, not a code dimension. Canonical `ExternalCrossChainMessage` encoder (byte-for-byte parity with C# `Neo.L2.Messaging.ExternalMessageHasher`) + `NeoProofBytes` / `EthProofBytes` encoders (same signatures, two wire formats) + curve-agnostic `Signer` trait + `WatcherCore<S, ES, NS, J>` orchestration that pins the safety invariant *cursor MUST NOT advance on submit failure*. Default and `live-rpc` suites are discovered from `src/live/` and `tests/` rather than documented as a brittle static split. The daemon source includes graceful SIGTERM/SIGINT shutdown, `flock(LOCK_EX | LOCK_NB)`-based concurrent-instance detection, per-chain `min_confirmations`, and `/healthz`, `/info`, and `/metrics` endpoints. Reference k8s + systemd manifests live in `watchers/neo-bridge-watcher-eth/deploy/`; independent audit and current-revision live deployment evidence remain operator gates. |
| `neo-bridge-watcher-tron`   | Rust crate: thin re-export of `neo-bridge-watcher-eth` with Tron-specific chain-id constants (`TRON_MAINNET_CHAIN_ID = 0xE000_0010` + Nile/Shasta testnets). Tron uses the same secp256k1+SHA256 + Keccak256 address derivation as Ethereum, so no separate messaging or signing core is needed — confirmation that the Phase-B abstractions were chain-agnostic at the right level. Source tests pin chain-id namespacing + cross-chain hash distinctness. |
| `neo-bridge-watcher-sol`    | Rust crate: `Ed25519FileSigner` implementing `Signer` with `curve_tag = 2` (vs Eth/Tron's 1) — validates the curve-agnostic refactor. Solana chain-ids (`0xE000_0020..2F`). On-chain dispatch flows to `CryptoLib.VerifyWithEd25519` per the registered curveTag. Per `doc.md` §11.3.4, Solana stays MPC-committee-only (Tower BFT light client is too expensive on-chain); the committee model handles Solana via the same trait surface. Source tests include real `ed25519-dalek` sign+verify and trait-object polymorphism coverage. |

#### Foreign-side router artifacts (`external/foreign-contracts/`)

| Path                        | Role                                             |
| --------------------------- | ------------------------------------------------ |
| `eth/`                      | `NeoExternalBridgeRouter.sol` (solc 0.8.24, via_ir + optimizer). Locks ETH/ERC-20 bound for Neo, finalizes Neo → Eth withdrawals via committee-attested `ecrecover` proofs. **44 Foundry tests** with real `vm.sign` round-trips: 37 single-chain (constructor + committee + custody + withdrawal + access/payload/signature/reentrancy guards) + 7 multi-chain (17 canonical mainnet slots construct, out-of-namespace ids reject, per-router state isolation across nonces / committees / chain-id stamping, BSC router rejects Polygon-claiming messages). Deploys unchanged on Ethereum, BSC, Polygon, Arbitrum, Optimism, Base, Avalanche, Linea, zkSync Era, Scroll, Mantle, Fantom, Celo, Tron via `forge create` with the right `externalChainId` constructor arg. |
| `tron/`                     | README pointing at `eth/` since TVM is EVM-flavored — same Solidity, different `externalChainId` constructor arg (`0xE000_0010` mainnet / `0xE000_0011` Nile / `0xE000_0012` Shasta). Documents tronbox / tronweb deployment, Tron-specific energy/bandwidth budgeting, and TVM opcode caveats. The full slot allocation table for *every* supported EVM chain lives in `watchers/neo-bridge-watcher-eth/src/chains.rs` — Tron is one of 14 chain families. |
| `sol/`                      | Anchor program implementing the same semantics on Solana: PDA-based state (`BridgeState` + `Vault` + per-`(chainId, nonce)` `ConsumedNonce` for replay protection), ed25519 verification via Solana's sigverify precompile (the canonical Wormhole/Neon pattern — saves ~30k CU/sig vs in-program ed25519), four instructions (`initialize` / `set_committee` / `lock_sol_and_send` / `finalize_withdrawal`). Source-only in this iteration; operators run `anchor build` + `anchor test` against `solana-test-validator`. v0 is SOL-only (SPL deferred), `MSG_TYPE_CALL` reverts, recipient zero-pads upper 12 bytes. Reviewed-needed flag in the README before mainnet. |

### neo-node plugins (`src/Neo.Plugins.L2*`)

| Plugin                       | Role                                                  |
| ---------------------------- | ----------------------------------------------------- |
| `Neo.Plugins.L2Batch`        | Hooks `Blockchain.Committed`; seal logic lives on testable `BatchSealer`; emits `l2.batch.sealed/seal_latency_ms/tx_count` via `WithMetrics()` |
| `Neo.Plugins.L2Settlement`   | Durable execution/DA/witness/proving/settlement coordinator. `Sp1SettlementExecutionStack.Create` binds the real stateful executor, atomic state, native binary SHA-256, file-queue prover, VK, and exact ZK profile; the pipeline rejects missing predecessors, block gaps, and state-root gaps before execution or DA, then durably commits and byte-verifies each immutable proof artifact before invoking its idempotent state sink. Retry/startup replay repairs an interrupted artifact/state handoff. `WireProduction` validates explicit endpoint/network/non-zero NeoHub hashes, requires durable witness and forced-event stores, and owns the real RPC sender/client plus paired forced-inclusion source/finalizer; `Wire` preserves caller-owned test/custom DI. Strict contiguous settlement uses durable bounded retries, explicit poison status, and exact-artifact operator reset without bypassing later batches. **Emits `l2.settlement.{submitted,submit_failures,submit_latency_ms,pending,retries,poisoned}` + `l2.proving.generated/latency_ms` via `WithMetrics()`** |
| `Neo.Plugins.L2Bridge`       | Hosts `AssetRegistry` + `DepositProcessor` + `WithdrawalProcessor`; emits `l2.bridge.{deposits,withdrawals,*_rejected}` via `WithMetrics()` |
| `Neo.Plugins.L2DA`           | Picks DA writer by `DAMode` config — `InMemoryDAWriter` (External default), **`NeoFsLikeDAWriter`** (dev semantic simulator only), production NeoFS via **`NeoFsRestDAWriter` + `NeoFsRestDAReader`** through `WithProductionBackend`, `CommitteeAttestedDAWriter` (DAC mode, operator-injected), `PersistentDAWriter` over RocksDB when `DataDirectory` is set, L1 mode requires operator-supplied L1-RPC writer (`JsonRpcL1DAWriter`); production profile rejects local/simulated fallbacks; `WithMetrics()` wraps the chosen writer in `MetricsEmittingDAWriter` (mode-tagged `l2.da.published/publish_latency_ms/publish_failures`) |
| `Neo.Plugins.L2Prover`       | Hosts `IL2Prover` for the configured `ProofType`      |
| `Neo.Plugins.L2Rpc`          | 10 canonical RPC handlers (doc.md §14.1) + official network-scoped `RpcServerPlugin.RegisterMethods` integration + `IL2RpcStore` (`InMemoryL2RpcStore` with optional `IL2KeyValueStore` for withdrawal/message proofs); exact decimal-string u64 wire shape, chain/request binding, and per-method `l2.rpc.calls/latency_ms/failures` tagged by `method` |
| `Neo.Plugins.L2Gateway`      | `BinaryTreeAggregator` with pluggable `IRoundProver` (default `PassThroughRoundProver`); `PassThroughAggregator` for flat aggregation; emits `l2.gateway.aggregations/batches_aggregated/aggregation_rounds/aggregation_latency_ms` |
| `Neo.Plugins.L2Metrics`      | **Composition root**: hosts the shared `IL2Metrics` sink + `MetricsHttpServer`; other plugins call `metricsPlugin.Metrics` and pass to their `WithMetrics()` setters; configurable bind address + port + readiness predicate |

### Smart contracts - 26 NeoHub projects + 10 Neo core native L2 contracts

**NeoHub L1 suite (26 projects / 24 production + 1 advisory + 1 test-only stub):**
Phase 0-4: `ChainRegistry` · `SharedBridge` · `SettlementManager` · `VerifierRegistry` · **`ContractZkVerifier`** (ProofType.Zk router -> deployable proof-verifier contracts) · **`Sp1Groth16Verifier`** (immutable SP1-compatible BN254 terminal verifier) · `MessageRouter` · `TokenRegistry` · `DARegistry` · **`DAValidator`** · **`L1TxFilter`** · `GovernanceController` · `EmergencyManager` · `ForcedInclusion` · `SequencerBond` · `SequencerRegistry` · `OptimisticChallenge` (exact executable-v4 chain/semantic/replay profile + global claim replay protection + CEI) · **`RestrictedExecutionFraudVerifier`** (SettlementManager-bound executable v4 for one existing-key Counter Increment; not general NeoVM). `GovernanceFraudVerifier` remains an advisory structural v1/v2 artifact and is excluded from the production plan; restricted v3 is likewise non-state-changing.

External-bridge stack (doc.md §11.3 — cross-foreign-chain to Eth/Tron/Sol):
**`MpcCommitteeVerifier`** (Phase B M-of-N secp256k1/ed25519 verifier; Phase C-extended with per-signer bond-holder binding via `RegisterCommitteeWithMembers`) · **`ExternalBridgeRegistry`** (pluggable verifier dispatch; same upgrade-via-governance shape as `VerifierRegistry`) · **`ExternalBridgeEscrow`** (locks NEP-17 outbound; inbound performs atomic direct NEP-17 release only for an explicit L1 domain or mandatory pinned payout-v1 adapter credit for L2, with immutable/reverse-unique routes, exact domain/value binding, replay rollback, and proposal-only production governance) · **`L2PayoutAdapter`** (immutable escrow-authenticated payout-v1 queue for one Neo L2, with exact canonical field binding, duplicate idempotency, and relay-witness acknowledgement) · **`ExternalBridgeBond`** (committee bonding + slashing-on-equivocation; mirrors `SequencerBond` 1:1) · **`ExternalBridgeStubVerifier`** (Phase-A devnet acceptance verifier; bridgeKind=0 to refuse production deployments) · **`MpcCommitteeFraudVerifier`** (Phase C — proves equivocation cryptographically + slashes full bond + pays reporter; replay-protected per `(chainId, signerIdx)`)

**L2 native (10):**
`L2BridgeContract` · `L2MessageContract` · `L2BatchInfoContract` · `L2FeeContract` · `L2PaymasterContract` · `L2SystemConfigContract` · **`L2NativeExternalBridgeContract`** (relay-witness-gated external payout endpoint; recomputes the canonical message hash, validates every field and local asset mapping, persists message/L2-transaction receipt before one-time mint, and rejects replay) · **`BridgedNep17Contract`** (canonical mint/burn NEP-17 representation for bridged L1 assets) · **`L2AccountAbstraction`** (validator/paymaster/nonce entry point) · **`L2InteropVerifier`** (L2-side global message-root mirror and inclusion verifier)

### Tools (`tools/`)

| Tool                  | Role                                                  |
| --------------------- | ----------------------------------------------------- |
| `Neo.External.Bridge.Cli` | `neo-external-bridge` CLI: operator key-gen + dual-side committee setup + ordered deploy plan for the cross-foreign-chain bridge. `genkey` (real secp256k1 keypair → pub33 + ethAddr20 for the same identity; private key written to a file, 0600 on POSIX); `committee-blob` (validates each pubkey is a real secp256k1 point, rejects duplicates / oversize, emits Neo blob + matching Eth address list); `deploy-bundle` (cross-checks committee size + threshold, prints the ordered 4-step Neo+Eth wire-up checklist). Same plan-printer pattern as `neo-bridge` / `neo-l2-faucet` — no live RPC, no built-in signer. |
| `Neo.Stack.Cli`       | `neo-stack` CLI: 13 subcommands all functional (`bootstrap-genesis` added). Signed transaction commands provide plan and confirmed `--broadcast` modes. `start-sequencer` / `start-batcher` supervise operator-supplied Neo.CLI deployments without a shell, validate protocol/validator/plugin consistency, propagate exit codes, and handle SIGINT/SIGTERM; `start-prover` supervises the real SP1 daemon. `init-l2` prepares isolated sequencer, batcher, and prover state directories and can install a reviewed Neo.CLI config without overwriting it. `bootstrap-genesis` materializes durable SP1 genesis roots; `register-chain --from-deploy-report` + `--genesis-manifest` closes the local L1 registration encoding path. |
| `Neo.L2.Devnet`       | `neo-l2-devnet <N> [--metrics-port <P>] [--data-dir <path>] [--config <path>] [--executor <kind>]` — runs N batches end-to-end with real `KeyedStateStore` continuity + sequencer committee + DA publish per batch + post-run `ChainAuditor` pass; with `--metrics-port` stands up a live HTTP server + self-scrapes `/metrics`, `/healthz`, `/readyz`; with `--data-dir` wires `RocksDbKeyValueStore` instances under that path so committee + state + RPC proofs + DA payloads all survive restart; with `--config <path>` reads §16.2 dimensions (security/da/sequencer/exit/gateway) from a `chain.config.json`; with `--executor counter` wires `Sample.CounterChainExecutor` end-to-end (state mutation via `KeyedStateStoreAdapter`, real receipts/withdrawals/messages from CounterTxBuilder-built transactions) so an operator can preview a real custom executor through the same pipeline. |
| `Neo.Hub.Deploy`      | `neo-hub-deploy` — declarative L1 deploy planner: scaffold / plan / verify |

### Tests

**The solution currently contains 38 .NET test projects, plus cross-language
gates for the shared four-language SDK conformance suite, Rust core/watchers/zkVM, Node, Solidity,
Solana, vendored VM workspaces, and SP1 release proofs. The 2026-07-15 serial full-solution run
discovered 2,591 tests: 2,587 passed, 0 failed, and 4 production-environment tests were explicitly
not executed (one real native executor test and three exact live-SDK deployment tests). The
numeric column below is the discovered count from that run and must be refreshed from runner
output whenever the suite changes.** Phase-C
real-crypto fraud-proof tests pin the
equivocation slash path's bytes-on-the-wire contract end-to-end with
real secp256k1 signatures.

| Project                              | Discovered tests | Coverage                           |
| ------------------------------------ | ----- | ------------------------------------------- |
| `Neo.L2.Abstractions.UnitTests`      | 62    | enum discriminants (ChainMode / SecurityLevel / DAMode / ProofType / MessageType / BatchStatus / AssetType / **SequencerModel / ExitModel** — closing doc.md §16.2 spec coverage), models, interface shape, **`ProofTypeExtensions.Resolve` boundary tests, `ChainIdValidator.ValidateL2` (zero-rejection / non-zero-acceptance / setting-name), record byte-content equality (DAPublishRequest / DAReceipt / ProofRequest / ProofResult / BatchExecutionRequest — overrides per AGENTS.md convention, including list-of-bytes element-wise comparison), `L2ChainConfigSerializer` 91-byte wire-format pin (layout + roundtrip + enum-extreme / wrong-length / out-of-range-byte / null rejection + chainId LE parity with the on-chain `ChainRegistry` parser), `L2ChainConfigJsonReader` (full population from create-chain JSON + 4 UInt160 hashes, named-error-message paths for unknown enum / missing field / malformed UInt160 / null inputs, validium-template shape pin, roundtrip through serializer)** |
| `Neo.L2.Batch.UnitTests`             | 68    | builder lifecycle, serializer round-trip, **proof-length bounds, unknown-ProofType rejection, all-valid-ProofType round-trip, trailing-byte rejection** |
| `Neo.L2.State.UnitTests`             | 120   | Merkle tree, proof verify, hashers, **canonical proof wire format (round-trip, layout, truncation, oversized depth, 7-leaf all-positions), `MessageHasher.HashMessage` + `HashWithdrawal` canonical-buffer layout pinned, HashMessage field-order sensitivity, HashWithdrawal at-max 64-byte amount accepted, on-chain Merkle verifier parity (4-leaf / 5-leaf odd-card / 7-leaf all-positions / tampered-sibling rejection / state-tree pin), `KeyedStateMerkleTree.ComputeRoot` ↔ `MerkleTree.ComputeRoot(HashEntry leaves)` cross-pin across 10 cardinalities incl. odd cases + `HashLeaf` ↔ `KeyedStateStore.HashEntry` byte-identity (NeoClassicParity suite), **wire-format fuzz suite** (`UT_WireFormat_Fuzz`, 19 ZKsync-style tests): random byte sequences through `MerkleProofSerializer.Decode` + `DepositPayload.Decode` (must reject with typed exception, never crash); differential round-trip across fuzzed tree shapes (1..16 leaves) + fuzzed (l1Asset, l2Recipient, amount) tuples; suffix-truncation rejection** |
| `Neo.L2.Messaging.UnitTests`         | 48    | inbox FIFO, replay protection, outbox split, **L2Outbox metric emission across destinations, persistence reopen pins, MessageBuilder rejects self-routed messages (incl. zero-to-zero)** |
| `Neo.L2.ExternalBridge.UnitTests`    | 17    | exact canonical payout field preservation, duplicate/conflicting mapped-asset queue identity, prepared-transaction retry reload, and bounded poison transition |
| `Neo.L2.Bridge.UnitTests`            | 94    | registry, deposit replay, withdrawal staging, **metric emission on all paths, retryability after transient validation failure, registry orphan cleanup, `DepositPayload` byte-layout pinned at documented offsets, at-max 64-byte amount boundary, **property-based invariant suite** (`UT_BridgeInvariants_PropertyBased`, 17 ZKsync-style tests): seeded random walks (200 ops × 4-8 seeds) asserting AssetRegistry bidirectional consistency, WithdrawalProcessor nonce-uniqueness across SealBatch promotion, DepositProcessor accepted-sum ↔ DepositsProcessed counter equality** |
| `Neo.L2.Proving.UnitTests`           | 82    | Stage 0/1/2 prove+verify, registry dispatch, **`OptimisticProver` prove→`OptimisticVerifier` round-trip + zero-bond/wrong-kind fail-closed**, optimistic sequencer account/signature binding, proof-payload boundary tests, AttestationVerifier dedup-before-verify, MultisigProofPayload byte-layout pins, ProofSystem discriminants |
| `Neo.L2.Executor.UnitTests`          | 106   | empty/single/many, ordering, determinism, canonical output binding, **KeyedStateStore + oracle, persistence reopen pins, transaction-overlay rollback/conflict checks, canonical effects V1 ordering/full-field/event-state pins, 105-byte receipt encoding, complete SP1 witness/output binding, durable-artifact restart recovery, post-state idempotency, and an injected cross-instance complete-state CAS race** |
| `Neo.L2.ForcedInclusion.UnitTests`   | 35    | nonce ordering, replay, overdue detection, encoded transaction hash validation, L1-confirmed consumption, **persistence reopen pins** |
| `Neo.L2.Sequencer.UnitTests`         | 35    | register/exit/finalize lifecycle, **metric emission for all three lifecycle ops + committee-size gauge, `SetMaxCommitteeSize` shrink-below-count rejection, persistence reopen pins (incl. exit-window survives restart), and canonical native committee transaction construction with order-independence / duplicate / count / infinity rejection** |
| `Neo.L2.Censorship.UnitTests`        | 18    | overdue detection, explicit finalized-evidence attribution, fail-closed unknown identity, **metric emission per detection batch** |
| `Neo.L2.Challenge.UnitTests`         | 114   | fraud-proof payload, orchestrator, BisectionGame, **`InspectWithBisectionAsync` (no-fraud agreement, log-N narrowing, arg validation, empty/mismatched/single-element checkpoint shape rejection, **v2 witness auto-emission** + bounded fallback to v1 when disputed tx is oversized / index out of range)**, metric emission, **`GovernanceFraudVerifier` parity coverage (real-discrepancy → accept; same-root → reject NoDiscrepancy; bad-length → reject BadLength; bad-version → reject BadVersion; decision-tree order pins; layout-offsets parity vs FraudProofPayload encoder; DisputedTxIndex doesn't change structural verdict; v2 round-trip incl. truncated/oversized witness rejection; v2 acceptance through verifier; v2 OversizedWitness path), **v3 wire-format round-trip + caps + auto version dispatch (v1/v2/v3 IsX flags; storage-proof per-proof + per-payload caps; truncated key/value/sibling rejection; zero-proof v3 rejected — use v2 instead)**, **`V3StorageProofVerifier` (off-chain reference: NotV3 / NoDiscrepancy returns; happy-path 2-leaf-tree Verified; PreStateRootMismatch + ReplayedPostStateRootMismatch rejected; encode→decode round-trip preserves verifiability; `HashEntry` layout pinned in lockstep with `KeyedStateStore.HashEntry`)**, **`RestrictedExecutionFraudVerifier` parity (15 tests: happy-path 2-leaf-tree → ReasonAccepted; v1/v2 payloads → BadVersion; truncated below v2 header / past num-proofs prefix → BadLength; oversized witness → OversizedWitness; same-root → NoDiscrepancy short-circuits before per-proof verify; zero-proof / >MaxStorageProofsPerPayload → ProofCountInvalid; pre-derived root mismatch → PreStateRootMismatch; post-derived root mismatch → ReplayedPostStateRootMismatch; decision-tree order pins; layout-offset pins for PreStateRoot@1 + ReplayedPostStateRoot@65; encode→decode→encode survives on-chain accept)** **Restricted v4 adds canonical round-trip, correct/no-fraud vs incorrect/fraud execution, committed pre/post witness binding, committed-root substitution, chain/batch/tx/bisection/replay/semantic/claim/witness/path tamper, unsupported opcode/multi-tx, and legacy/trailing-byte fail-closed coverage.** |
| `Neo.L2.Audit.UnitTests`             | 60    | continuity + proof-validity + `NoZeroProofCheck` + DA/range checks; public-input hashes require a real payload resolver and reject zero-fill fallback or tampered L1/block-context inputs |
| `Neo.Plugins.L2Rpc.UnitTests`        | 44    | all 10 RPC methods plus the state-root overload through a real `NeoSystem` + official Kestrel `RpcServerPlugin`, exact JSON-RPC id/version echo, decimal-string u64s, bridged/proof chain binding, fail-closed unwired/disposed lifecycle, foreign-chain rejection, **per-method metric emission (calls/latency/failures), too-few-params clear-error, oversized-chainId rejection, monotonic `_latestStateRoot` on out-of-order Finalize, persistence reopen pins, `getsecuritylabel` defaults + override propagation pins, IL2RpcStore default-interface defaults (External / false / DbftCommittee / Permissionless) + e2e through getsecuritylabel for third-party minimal-impl stores** |
| `Neo.Plugins.L2DA.UnitTests`         | 105   | InMemory + NeoFsLike DA writers + **MetricsEmittingDAWriter (success / throw / accumulate / passthrough), `ResolveDAMode` accepts 0..3 / rejects unknown, all DAWriter null-arg paths, `L2DAPlugin` default-writer / `WithWriter` injection / Name+Description non-empty / `WithMetrics` propagates to active writer (mid-flight sink swap), `CommitteeAttestedDAWriter` round-trip + tampered-sig + null-arg + buggy-callback contracts, `BuildDefaultWriter` (External/NeoFS/L1/DAC × dataDir-set/null/empty/whitespace boundary), `PersistentDAWriter` (RocksDB-backed: round-trip + configured-mode-flow + cross-instance reopen pin + unknown-commitment / null-store / null-request / null-receipt / null-commitment guards + defensive-copy + dispose-owning-vs-borrowed semantics + default-mode = External), `JsonRpcL1DAWriter` (mode=L1, ctor null-guards / empty-rpc-method, PublishAsync delegates with contract+request, Commitment=Hash256(payload) cross-tier convention, pointer=32B tx hash, null-tx-hash defense, IsAvailableAsync zero-commitment short-circuit / HALT-true / HALT-false / FAULT-state-false, dispose semantics)** |
| `Neo.Plugins.L2Gateway.UnitTests`    | 103   | flat + binary-tree aggregator, edge cases, durable Merkle/Multisig/SP1 factories, ConfigureGlobalRootPublicationFromChainDirectory, Sp1GatewayProofProver.OpenFromChainDirectory, **metric emission with rounds=log2(N) + per-batch accumulation, `PassThroughRoundProver` round-prover-level pinning (BackendId=0xFE constant, right-null odd-leaf rule, Hash256 message-root composition, [4B leftLen][bytes][4B rightLen][bytes] proof byte layout, both-empty-proof envelope, asymmetry, null-left rejection)** |
| `Neo.Plugins.L2Metrics.UnitTests`    | 20    | composition root: bound port, idempotent Start, real HTTP scrape, readiness predicate gating, default settings, **`ResolveBindAddress` boundary tests, concurrent-Start race-safety, `ValidatePort` boundary tests, plugin Name + Description non-empty** |
| `Neo.Plugins.L2Batch.UnitTests`      | 48    | `BatchSealer` immutable execution payload, real L1/block context, pending/persist/ack hand-off, sink retry, durable checkpoint restore, crash continuity, duplicate/gap fail-closed behavior, immutable sink/input wiring, chain-domain validation, metrics re-wiring, forced nonce filtering without early consumption, null-source-result rejection, triggers and plugin lifecycle |
| `Neo.Plugins.L2Bridge.UnitTests`     | 10    | `L2BridgePlugin` lifecycle, asset registration, default behavior, **WithMetrics propagates to existing Deposit + Withdrawal processors (symmetric pins — without both, a refactor that drops one of the `?.WithMetrics()` calls would silently lose half the `l2.bridge.*` metric stream)** |
| `Neo.Plugins.L2Prover.UnitTests`     | 21    | `L2ProverPlugin` lifecycle, ProofType resolution, **CreateFromChainDirectory / MultisigWired / ZkWired / OptimisticWired**, Wire dispatch Multisig/Zk/Optimistic/None |
| `Neo.Plugins.L2Settlement.UnitTests` | 73    | canonical artifact bytes reach prover, ZK fail-closed policy, DA/result/public-input cross-checks, pre-execution predecessor/block/state continuity rejection, strict contiguous durable reconciliation, bounded retry/poison/restart/operator recovery, no-loss and no-bypass checks, validated continuous checkpoint recovery, process-crash recovery across proof/broadcast/observation/finality/consume windows, transaction-aware duplicate suppression, restored success/failure/concurrency/throwing-metrics coverage, explicit legacy control, and production durable-store/configuration/constructor/forced-pair/ownership/disposal coverage |
| `Neo.L2.Settlement.Rpc.UnitTests`    | 61    | JSON-RPC envelope, stack parsing, signer, batch lifecycle, duplicate-broadcast confirmation recovery, transaction-status reconciliation, and finalized-root-bound forced-inclusion consumption with idempotent L1 read-back |
| `Neo.L2.Telemetry.UnitTests`         | 103   | counter/histogram/gauge accumulation, tag canonicalization, Prometheus exporter (counter/gauge/summary, labels, name sanitization, frozen-snapshot), request handler routing, TCP server round-trip + multi-request, catalog completeness vs MetricNames + Prometheus integration, **`/healthz` + `/readyz` (with predicate)** |
| `Neo.L2.Persistence.UnitTests`       | 70    | **`InMemoryKeyValueStore` + `RocksDbKeyValueStore` parity plus atomic artifact/proof-manifest persistence, content-hash binding, ordered recovery enumeration, durable retry/poison/reset checkpoints, versioned submission state and replacement CAS, forced nonce tracking/finalization, reopen durability** |
| `Neo.L2.Executor.RiscV.UnitTests`    | 31    | stateful ABI constants/ownership/fee boundaries; storage read-through Put/Delete/Find and rollback; ordered full-state notifications; transaction/block context and `CheckWitness`; unsupported-syscall/OOG/collector fail-closed paths; ApplicationEngine receipt parity; real native PolkaVM RET, storage callback, and unsupported-syscall tests |
| `Neo.Hub.Deploy.UnitTests`           | 110   | topology/cycle/plan validation; full 24-step production scaffold with concrete immutable L2 payout adapter; exact SP1 bootstrap-and-lock order; immutable remote NEF/manifest matching; explicit L2, relay, and v4 replay domains; advisory v1/v2/v3 exclusion, malformed optimistic-plan rejection, and atomic exact executable-v4 registration; resumable completion checks and production smoke postconditions; signer/RPC/report failure paths; critical English/Chinese settlement-spec anchor parity. |
| `Neo.L2.IntegrationTests`            | 31    | Phase 0 MVP + Phase 1 cross-component + Phase 2 full-stack + Phase 3 optimistic-challenge + all-phases stitch + e2e telemetry pipeline + **L2MetricsPlugin composition root (every instrumented component → one sink → HTTP scrape) + e2e RocksDB persistence (KeyedStateStore + InMemoryL2RpcStore + InMemorySequencerCommitteeProvider all rehydrate from one shared data dir on reopen) + e2e audit pipeline (all 6 checks pass on healthy chain + DA-dropped scenario specifically catches via `DAAvailabilityCheck` + broken-batch-range failure-detection metric counts) + **e2e custom-executor full-stack (Sample.CounterChainExecutor + KeyedStateStoreAdapter + ReferenceBatchExecutor + KeyedStateRootOracle + AttestationProver/Verifier all wire cleanly: 3-batch run with mixed Increment/Withdraw/Message txs → all 4 batch roots non-zero, state-root advances per batch + uniqueness pin across 4 distinct roots, multisig verifier accepts custom-executor commitments, BatchSerializer encode/decode round-trip is identity, final state has 6 expected counter entries; failed-tx batch → effects don't pollute withdrawal/message roots, gas accounting still correct, state from successful txs intact)** **Restricted-v4 integration pins `BatchSerializer → canonical payload → executable verifier` for both honest and fraudulent committed transitions.** |
| `Neo.Stack.Cli.UnitTests`            | 175   | Full command surface plus real operator-process launch plans, Neo.CLI/prover bundle validation, child exit-code propagation, shell-metacharacter isolation, SIGTERM and transaction-confirmation cancellation, forwarded-argument boundary parsing, separate batcher data, immutable genesis-root registration, genesis/rotated committee verification, committee sync dry-run, NEP-6 password/decryption/derived-public-key validation with malformed, mismatched, or unsupported wallet rejection, wallet/DBFT auto-start, storage, network, config, external signer-command request binding, explicit WIF exclusion, empty/mismatched fee-witness rejection, and the complete quick path |
| `Neo.External.Bridge.Cli.UnitTests`  | 16    | external-bridge key generation, committee-blob validation, duplicate and malformed key rejection, deploy-bundle topology, and safe private-key file handling |
| `Neo.L2.Bridge.Cli.UnitTests`        | 15    | canonical invocation construction and Merkle-proof decoding, including malformed and boundary inputs |
| `Neo.L2.Devnet.UnitTests`            | 30    | devnet argument parsing, executor selection, persistent paths, metrics configuration, and explicit security-label override behavior |
| `Neo.L2.Explore.UnitTests`           | 14    | batch, audit, tail, and security-label command rendering against deterministic stubbed RPC responses |
| `Neo.L2.Faucet.Cli.UnitTests`        | 13    | faucet policy limits, replay/idempotency journal behavior, and durable reopen semantics |
| `Neo.L2.Gateway.Rpc.UnitTests`       | 18    | proof-bound and direct global-root publication, chain-directory publisher open, CreateSignAndSend, RPC validation, finality binding, and failure propagation |
| `Neo.L2.Sdk.UnitTests`               | 35    | offline four-language fixture conformance, .NET RPC happy/error/server-contract paths, plus three exact-deployment live tests gated on an operator-provided N3/N4 fixture |
| `NeoHub.Contracts.VmTests`           | 542   | the complete NeoHub VM contract suite, canonical wire formats, authorization/replay/domain invariants, immutable owner/public chain-registration genesis roots and batch-1 trust-anchor binding, native-GAS forced inclusion, executable restricted-v4 fraud proofs, bridge escrow/payout, governance, and generated-artifact parity |
| `NeoHub.Sp1Groth16Verifier.UnitTests` | 12   | positive SP1 Groth16 vector, malformed/tampered proof and public-input rejection, and fresh verifier/contract artifact equivalence |
| `Sample.CounterChainExecutor.UnitTests` | 24 | **operator-facing reference for "how to plug in custom chain logic"**: 3-opcode custom executor (IncrementCounter / EmitWithdrawal / EmitMessage) demonstrates the `ITransactionExecutor` seam. Per-sender-counter happy-path + accumulation + ulong wraparound semantics; per-sender state isolation; truncated-tx → Failed-receipt path (not crash); withdrawal happy-path produces valid `WithdrawalRequest` with deterministic txHash-derived nonce; zero-amount withdrawal rejected; message happy-path produces routable `CrossChainMessage` via canonical `MessageBuilder.Build`; self-routed (source==target) rejected; oversized message body builder-rejected at MaxMessageBytes cap; unknown-opcode + empty-tx → Failed; SPEC.md determinism pin (two fresh executors + identical inputs → identical receipts + state); mixed-opcode batch smoke test; **executor ctor null-state / null-emittingContract guards; ExecuteAsync null-batchContext guard; cooperative-cancellation pin (cancelled token → OperationCanceledException, not a wasted receipt); `KeyedStateStoreAdapter` round-trip Put/Get + missing-key returns false + adapter writes flow through to `KeyedStateStore.ComputeRoot` parity vs direct writes; adapter ctor null-store + Put null-key/null-value + TryGet null-key all reject with ArgumentNullException at the call boundary so a misconfigured DI wiring fails at composition, not later with an unattributed NRE** |

## Public testnet evidence (NeoHub L1)

| Field | Value |
|-------|-------|
| Status | **Current for NeoHub L1 + registerChain + TokenRegistry + ForcedInclusion + fixed SharedBridge Deposit** (2026-07-21 session20) |
| Network | Neo N3 testnet magic `894710606` |
| RPC | `https://n3seed1.ngd.network:20332` |
| Signer | `NLtL2v28d7TyMEaXcPqtekunkFRksJ7wxu` |
| L2 domain id | `20260716` (`isActive=true`, Validity + L1 DA, Zk) |
| Contracts | 24 original bundle re-verified (24/24 reuse + smoke 2026-07-21 session20); SharedBridge **fixed** redeployed as new hash |
| registerChain | tx `0xb3d02a5f…9f26` HALT; genesis root `0x59be9f14…5130` |
| TokenRegistry | GAS+NEO mappings for chain `20260716` live (`0xc1f44721…e06a`, `0xb51c8e4f…7daa`) |
| ForcedInclusion | nonce 1 enqueued (`0x73924dce…f412`, HALT); needs `WitnessScope.Global` for fee transfer |
| SharedBridge deposit | **live fixed**: bridge `0xf64548c2…1bae`; Deposit nonces 1–21 HALT with `WitnessScope.Global` (… session18 n19 `0x54f21a9f…b614`, session19 n20 `0xa8503477…8532`). Session19 reverify 24/24 reuse + 29 postdeploy + 42 smoke (2026-07-20). Session20 n21 `0x56e06c9d…291e`. Session20 reverify 24/24 reuse + 29 postdeploy + 42 smoke (2026-07-21). Legacy `0xf2f5114b…b241` still broken. Chain `20260716` config still points at legacy until governance retarget (**funded/governance gate**). |
| Local Multisig DA | **code-complete**: `PersistentDAWriter.OpenLocalFromChainDirectory` → `data/settlement/da` |
| Host WireProduction | **code-complete** (incl. `PipelineHealthFailures / IsMetricsHttpHealthy / MetricsHttpHealthFailures / IsLocalHostHealthy / LocalHostHealthFailures / IsBatcherCheckpointAligned / IsBatcherCheckpointAlignedAsync / IsOpenBatchPastMaxAge / OpenBatchAgeMillis / IsLocalHostHealthyAsync / IsPipelineHealthyAsync / GetPipelineHealthFailuresAsync` / `IsSettlementRuntimeIdle / IsSettlementPoisonedState / IsSettlementRetryingState / IsSettlementRuntimeIdleAsync / IsSettlementPoisonedAsync / IsSettlementRetryingAsync / OfflinePassportFailures / BuildOfflinePassportFailures` / `IsOutboxRuntimeIdle / PublicationHealthFailures / BuildPublicationHealthFailures` / **`HasMetricsHealthProbe` / `HasMetricsOperatorStatus` / `GetHealthProbeAsync` / `GetHealthProbe` / `FormatOperatorStatusJsonAsync` / `FormatOperatorStatusJson` / `FormatHealthProbeJson` (LocalHost + Gateway) / `WriteOperatorStatusAsync` / `WriteHealthProbeAsync`** / metrics HTTP **`GET /healthprobe`** + **`GET /operatorstatus`** (`LocalHostHealthProbeDocument` with chain/proof/DA/security/expectedNetwork identity + Sequencer/Exit + durable roots (initial/rpc/checkpoint post) + message outbox roots + TrackedForcedInclusionNonceCount + NeoHub hash presence + scanner deploy heights + batcher inbox sources + batcher max limits + SupportsLocalDaReader/GatewayEnabled/BridgeAssetCount + metrics enable/wiring/port/bind/entry-count + Recovery (LocalHostRecoveryDocument) + IsSettlementIdle + ReadyDepositCount + offline passport wiring flags (incl. security-level consistency) + open-batch counts + batcher ack/next + durable checkpoint + settlement retry/lag + FI/inbox + deposit queues + message outbox + readiness/healthprobe/operatorstatus flags; LocalHost `BuildMetricsHttpHealthFailures` requires all three body providers when metrics enabled; **Gateway `StartMetricsHttp`/`StopMetricsHttp`/`metricsPlugin:` Open\* + `HasMetricsPlugin`/`IsMetricsHttpHealthy`/`BuildMetricsHttpHealthFailures`/`BuildGatewayHostHealthFailures`**; `GatewayHostHealthProbeDocument` with chainDirectory + passport wiring flags + proofSystem/aggregationBackendId + replay/vk/settlement/message hashes + pending-epoch/retry/lag/state + metrics HTTP flags + MetricsConfiguredPort/BindAddress/MaxConcurrentConnections) diagnostics): batch/settlement/bridge/prover/metrics/gateway factories + local DA + RPC proofs + gateway outbox + L1 inbox bundle + `WireProductionFromLayout` + **`MultisigLocalHostComposition` / `OptimisticLocalHostComposition` / `ZkLocalHostComposition`** (offline ProcessDeposit+StageWithdrawal+EnqueueOutbound covered for all three) (bridge deposit source + metrics + **`InMemoryL2RpcStore`/`data/rpc/proofs`** + **metrics-wrapped DA** via `MetricsEmittingDAWriter` / `MetricsEmittingProductionDAWriter`; Zk also binds state + Sp1 stack + production-DA marker) + public **`IsProductionWired` / `ProductionDepositSource` / `ProductionMessageRouter` / `ProductionForcedInclusionSource` / `ProductionForcedInclusionFinalizer` / `ProductionSettlementClient` / `ProductionTransactionSender`** after WireProduction (also on batcher: **`HasSealedBatchSink` / `ForcedInclusionSource` / DepositSource / MessageRouter**) + LocalHost host-level **`DepositSource` / `MessageRouter` / `ForcedInclusionFinalizer` / `SettlementClient` / `TransactionSender` / `MetricsBoundPort` / `IsMetricsHttpListening`** + **`ChainId` / **`BatcherConfiguredChainId`** / **`SettlementConfiguredChainId`** / **`SettlementConfiguredProofType`** / **`IsChainIdConfigConsistent`** / **`IsProofTypeConfigConsistent`** / **`RpcDaMode`** / **`IsDaModeConfigConsistent`** / **`RpcChainId`** / **`IsNeoHubHashWiringComplete`** / **`IsBatcherInboxWiringComplete`** / **`IsSecurityLevelProofTypeConsistent`** / **`IsSecurityLevelDaModeConsistent`** / **`IsMetricsWiringComplete`** / **`HasMetricsReadinessCheck`** / **`IsDepositPipelineWiringComplete`** / **`IsMessagePipelineWiringComplete`** / **`IsForcedInclusionPipelineWiringComplete`** / **`IsSettlementClientWiringComplete`** / **`IsPipelineEnabled`** / **`IsSettlementPoisoned / IsSettlementRetrying`** / **`IsSettlementIdle`** / **`IsPipelineHealthy`** / **`HasExpectedNetwork`** / **`HasScannerDeployHeights`** / **`IsOfflinePassportComplete`** / **`OfflinePassportFailures`** / `ProofType` / `DaMode`/`HasSealedBatchSink`/`NextExpectedBlock`/`LastAcknowledgedBatchNumber`/`LastAcknowledgedBlock`/`NextBatchNumber`/`OnBatchSealed`/`HasPendingSealedBatch`/`PendingSealedBatchNumber`/`PendingSealedBatchLastBlock`/`IsBatcherEnabled`/`MaxBlocksPerBatch`/`MaxTransactionsPerBatch`/`MaxBatchAgeMillis`/`MaxForcedTransactionsPerBatch`/`MaxL1MessagesPerBatch`/`PendingSealedBatch`/`HasOpenBatch`/`InProgressTxCount`/`OpenBatchFirstBlock`/`OpenBatchLastBlock`/`OpenBatchBlockCount`/`OpenBatchL1MessageCount`/`OpenBatchL2ToL1MessageCount` / **`OpenBatchL2ToL2MessageCount`** / **`OpenBatchForcedInclusionCount`** / **`OpenBatchWithdrawalCount`** / `ProcessCommittedBlock`/`TryRetryPendingSealedBatch`/`IsOperatorReady`/`PeekSharedBridgeDeposits`/`GetOperatorStatusAsync`(`LocalHostOperatorStatus`) + SettlementRetryCount/SettlementConfirmationLagBatches** + RPC store **`GetLatestRpcStateRoot`/`GetRpcStateRootAtBatch`/`AddRpcBatch`/`FinalizeRpcBatch`/`RecordRpcDeposit`/`GetRpcL1DepositStatus`/`GetRpcBatch`/`GetRpcBatchStatus`/`RegisterRpcAsset`/`GetRpcCanonicalAsset`/`GetRpcBridgedAsset`/`RecordRpcWithdrawalProof`/`RecordRpcMessageProof`/`GetRpcWithdrawalProof`/`GetRpcMessageProof`** + MessageRouter **`MessageOutbox`/`MessageOutboxL2ToL1Root`/`MessageOutboxL2ToL2Root`/`EnqueueOutboundMessagesAsync`/`RegisterInboundMessageNonce`/`InvalidateInboundMessageCache`/`KnownInboundNonceCount`/`RecordMessageRouterFinalizedProof`/`GetMessageRouterProofAsync`** + FI **`RegisterForcedInclusionNonce`/`InvalidateForcedInclusionCache`** + DA **`PublishDaAsync` / `IsDaAvailableAsync` / **`SupportsLocalDaReader`** / `CreateLocalDaReader`(local Multisig/Optimistic)** + metrics **`CaptureMetricsSnapshot`/`ExportPrometheusMetrics`** + bridge registry **`BridgeAssetRegistry`/`RegisterBridgeAsset`/`SnapshotBridgeAssets`/`BridgeAssetCount`** + bridge processors **`ProcessDeposit`/`ProcessReadyDeposits`/`ScanSharedBridgeDepositsAsync`/`ScanAndProcessReadyDepositsAsync`/`HasConsumedDeposit`/`ConsumedDepositCount`/`DepositSourceReadyCount`/`DepositSourceReservedCount`/`DepositSourceSoftConsumedCount`/`IsSettlementEnabled` / **`HasL1RpcEndpoint`** / **`ExpectedNetwork`** / **`HasSettlementManagerHash`** / **`HasSharedBridgeHash`** / **`HasMessageRouterHash`** / **`HasL2BridgeHash`** / **`HasMessageOutbox`** / `L1FinalityDepth`/`IsMetricsEnabled`/`MetricsConfiguredPort`/`MetricsBindAddress`/`ForcedInclusionDeploymentHeight`/`SharedBridgeDeploymentHeight`/`MessageRouterDeploymentHeight`/`L1InboxPendingCount`/`L1InboxConsumedCount`/`KnownInboundNonceCount`/`HasForcedInclusionFinalizer`/`HasSettlementClient`/`HasTransactionSender`/`HasOverdueForcedInclusionAsync`/`StageWithdrawal`/`StagedWithdrawalCount`/`SealWithdrawalBatch`/`ProveAsync`/`BatchProver`** + **`WriteOperatorStatusAsync`(`LocalHostOperatorStatusDocument` JSON) + **`WritePrometheusMetricsAsync`** + Gateway **`WriteOperatorStatusAsync`(`GatewayHostOperatorStatusDocument`)** + Gateway **`Metrics`/`ExportPrometheusMetrics`/`WritePrometheusMetricsAsync`**** + optional **`startMetricsHttp`** / **`StopMetricsHttp`** / deferred **`StartMetricsHttp`** + default **`/readyz`** readiness (**IsOfflinePassportComplete**) + **`CreateRpcPlugin()`** + **`ReconcileAsync`/`SubmitNextAsync`/`GetPendingCountAsync`/`PersistAsync`/`EnqueueAsync`** + recovery **`GetRecoveryStatusAsync`/`RecoverPoisonedBatchAsync`/`GetTrackedForcedInclusionNoncesAsync`/`GetLatestCheckpointAsync` / **`GetLatestDurableCheckpointAsync`** / `GetInitialStateRootAsync` helpers + Multisig/Optimistic/Zk wired provers; Zk still needs funded executor binary + VK + production DA credentials + prove-batch daemon; Gateway: durable factories + **`GatewayHostComposition.OpenMerkle` / `OpenMultisig` / `OpenSp1`** (publisher + publication profile + optional **`IL2Metrics`**) + host ops **`HasPendingPublication` / `PendingPublicationEpoch` / `AggregatorPendingCount` / `HasDurableOutbox` / `IsPublicationConfigured` / `IsEnabled` / `MaxAutomaticRetries` / **`ProofSystem`** / **`AggregationBackendId`** / **`ExpectedNetwork`** / **`HasL1RpcEndpoint`** / **`IsPublicationProfileReady`** / **`HasExpectedNetwork`** / **`IsOfflinePassportComplete`** / **`OfflinePassportFailures`** / **`IsOutboxPoisoned`** / **`IsOutboxIdle`** / **`IsPublicationHealthy / IsGatewayHostHealthy / GatewayHostHealthFailures`** / **`ReplayDomain`** / **`VerificationKeyId`** / **`SettlementManagerHash`** / **`MessageRouterHash`** / `OutboxStatus` / `Aggregator` / `ReceiveBatch` / `PullAggregate` / `PublishAggregateAsync` / `RecoverPoisonedPublication` / `GetOperatorStatus`(`GatewayHostOperatorStatus` with HasMetrics/MetricsEntryCount + MetricsConfiguredPort/BindAddress/MaxConcurrentConnections)** (gateway/batch daemons + funded L1 still operator-supplied). Integration: `UT_E2E_HostComposition_FromDeployReport` (factory inventory + Multisig+Gateway Merkle/Multisig/Sp1 with shared metrics + Optimistic/Zk one-shots + Zk+Gateway OpenSp1 from deploy report + offline ProcessDeposit/StageWithdrawal/EnqueueOutbound + soft-offline-bridge status/probe (ScanSharedBridge no-op + ConsumedDeposit/outbox pins) + soft-offline-bridge-rpc-surface + soft-fi status/probe + soft-inbound status/probe + soft-seal-da-surface + soft-seal-second-batch while pending L1 (batch 2 seal before inbound register; pending≥2 + durable soft-seal-second-batch status/probe) + soft-seal-multi-batch RPC finalize batch1+2 + Gateway dual-chain ReceiveBatch both (AggregatorPendingCount≥4, soft-seal-multi-batch-rpc-gateway.json; host settle still Retrying pending≥2) + soft-seal-after-recover multi-batch RPC retention (Finalized batch1+2 + tip + durable soft-seal-after-recover-multi-batch-rpc.json; unit Gateway backlog≥4 survives recover/SubmitNext) + soft-seal-after-recover multi-batch DA re-publish batch1+2 + second offline deposit n2 IncludedInBatch=2 (ConsumedDepositCount=2, soft-seal-after-recover-da-offline.json + host.prom) + soft-seal-after-recover second outbound+FI/inbound (outbox=2, FI/inbound known=2, soft-seal-after-recover-second-outbound.json) + second-outbound RPC proofs (MerkleProofSerializer wd proof + message/router proofs, soft-seal-after-recover-second-outbound-rpc.json) + soft-seal second poison→recover retention (full multi-batch soft state + dual deposit/outbox/FI known + RPC tip/proofs survive second Poisoned cycle; soft-seal-second-poison-recover.json) + soft-seal after-second-recover DA re-publish + third offline deposit n3 IncludedInBatch=2 (ConsumedDepositCount=3, soft-seal-after-second-recover-da-deposit.json + host.prom) + soft-seal after-second-recover third outbound+FI/RPC (outbox=3, FI/inbound known=3, Merkle/message proofs, soft-seal-after-second-recover-third-outbound.json + -rpc.json) + soft-seal third poison→recover retention (triple deposit/outbox/FI known + multi-batch RPC tip/proofs survive third Poisoned cycle; soft-seal-third-poison-recover.json) + soft-seal after-third-recover DA re-publish + fourth offline deposit n4 IncludedInBatch=2 (ConsumedDepositCount=4, soft-seal-after-third-recover-da-deposit.json + host.prom) + soft-seal after-third-recover fourth outbound+FI/RPC (outbox=4, FI/inbound known=4, Merkle/message proofs, soft-seal-after-third-recover-fourth-outbound.json + -rpc.json) + soft-seal fourth poison→recover retention (quadruple deposit/outbox/FI known + multi-batch RPC tip/proofs survive fourth Poisoned cycle; soft-seal-fourth-poison-recover.json) + soft-seal after-fourth-recover DA re-publish + fifth offline deposit n5 IncludedInBatch=2 (ConsumedDepositCount=5, soft-seal-after-fourth-recover-da-deposit.json + host.prom) + soft-seal after-fourth-recover fifth outbound+FI/RPC (outbox=5, FI/inbound known=5, Merkle/message proofs, soft-seal-after-fourth-recover-fifth-outbound.json + -rpc.json) + soft-seal fifth poison→recover retention (quintuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive fifth Poisoned cycle; soft-seal-fifth-poison-recover.json) + soft-seal after-fifth-recover DA re-publish + sixth offline deposit n6 IncludedInBatch=2 (ConsumedDepositCount=6, soft-seal-after-fifth-recover-da-deposit.json + host.prom) + soft-seal after-fifth-recover sixth outbound+FI/RPC (outbox=6, FI/inbound known=6, Merkle/message proofs, soft-seal-after-fifth-recover-sixth-outbound.json + -rpc.json) + soft-seal sixth poison→recover retention (sextuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive sixth Poisoned cycle; soft-seal-sixth-poison-recover.json) + soft-seal after-sixth-recover DA re-publish + seventh offline deposit n7 IncludedInBatch=2 (ConsumedDepositCount=7, soft-seal-after-sixth-recover-da-deposit.json + host.prom) + soft-seal after-sixth-recover seventh outbound+FI/RPC (outbox=7, FI/inbound known=7, Merkle/message proofs, soft-seal-after-sixth-recover-seventh-outbound.json + -rpc.json) + soft-seal seventh poison→recover retention (septuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive seventh Poisoned cycle; soft-seal-seventh-poison-recover.json) + soft-seal after-seventh-recover DA re-publish + eighth offline deposit n8 IncludedInBatch=2 (ConsumedDepositCount=8, soft-seal-after-seventh-recover-da-deposit.json + host.prom) + soft-seal after-seventh-recover eighth outbound+FI/RPC (outbox=8, FI/inbound known=8, Merkle/message proofs, soft-seal-after-seventh-recover-eighth-outbound.json + -rpc.json) + soft-seal eighth poison→recover retention (octuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive eighth Poisoned cycle; soft-seal-eighth-poison-recover.json) + soft-seal after-eighth-recover DA re-publish + ninth offline deposit n9 IncludedInBatch=2 (ConsumedDepositCount=9, soft-seal-after-eighth-recover-da-deposit.json + host.prom) + soft-seal after-eighth-recover ninth outbound+FI/RPC (outbox=9, FI/inbound known=9, Merkle/message proofs, soft-seal-after-eighth-recover-ninth-outbound.json + -rpc.json) + soft-seal ninth poison→recover retention (nonuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive ninth Poisoned cycle; soft-seal-ninth-poison-recover.json) + soft-seal after-ninth-recover DA re-publish + tenth offline deposit n10 IncludedInBatch=2 (ConsumedDepositCount=10, soft-seal-after-ninth-recover-da-deposit.json + host.prom) + soft-seal after-ninth-recover tenth outbound+FI/RPC (outbox=10, FI/inbound known=10, Merkle/message proofs, soft-seal-after-ninth-recover-tenth-outbound.json + -rpc.json) + soft-seal tenth poison→recover retention (decuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive tenth Poisoned cycle; soft-seal-tenth-poison-recover.json) + soft-seal after-tenth-recover DA re-publish + eleventh offline deposit n11 IncludedInBatch=2 (ConsumedDepositCount=11, soft-seal-after-tenth-recover-da-deposit.json + host.prom) + soft-seal after-tenth-recover eleventh outbound+FI/RPC (outbox=11, FI/inbound known=11, Merkle/message proofs, soft-seal-after-tenth-recover-eleventh-outbound.json + -rpc.json) + soft-seal eleventh poison→recover retention (undecuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive eleventh Poisoned cycle; soft-seal-eleventh-poison-recover.json) + soft-seal after-eleventh-recover DA re-publish + twelfth offline deposit n12 IncludedInBatch=2 (ConsumedDepositCount=12, soft-seal-after-eleventh-recover-da-deposit.json + host.prom) + soft-seal after-eleventh-recover twelfth outbound+FI/RPC (outbox=12, FI/inbound known=12, Merkle/message proofs, soft-seal-after-eleventh-recover-twelfth-outbound.json + -rpc.json) + soft-seal twelfth poison→recover retention (duodecuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive twelfth Poisoned cycle; soft-seal-twelfth-poison-recover.json) + soft-seal after-twelfth-recover DA re-publish + thirteenth offline deposit n13 IncludedInBatch=2 (ConsumedDepositCount=13, soft-seal-after-twelfth-recover-da-deposit.json + host.prom) + soft-seal after-twelfth-recover thirteenth outbound+FI/RPC (outbox=13, FI/inbound known=13, Merkle/message proofs, soft-seal-after-twelfth-recover-thirteenth-outbound.json + -rpc.json) + soft-seal thirteenth poison→recover retention (tredecuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive thirteenth Poisoned cycle; soft-seal-thirteenth-poison-recover.json) + soft-seal after-thirteenth-recover DA re-publish + fourteenth offline deposit n14 IncludedInBatch=2 (ConsumedDepositCount=14, soft-seal-after-thirteenth-recover-da-deposit.json + host.prom) + soft-seal after-thirteenth-recover fourteenth outbound+FI/RPC (outbox=14, FI/inbound known=14, Merkle/message proofs, soft-seal-after-thirteenth-recover-fourteenth-outbound.json + -rpc.json) + soft-seal fourteenth poison→recover retention (quattuordecuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive fourteenth Poisoned cycle; soft-seal-fourteenth-poison-recover.json) + soft-seal after-fourteenth-recover DA re-publish + fifteenth offline deposit n15 IncludedInBatch=2 (ConsumedDepositCount=15, soft-seal-after-fourteenth-recover-da-deposit.json + host.prom) + soft-seal after-fourteenth-recover fifteenth outbound+FI/RPC (outbox=15, FI/inbound known=15, Merkle/message proofs, soft-seal-after-fourteenth-recover-fifteenth-outbound.json + -rpc.json) + soft-seal fifteenth poison→recover retention (quindecuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive fifteenth Poisoned cycle; soft-seal-fifteenth-poison-recover.json) + soft-seal after-fifteenth-recover DA re-publish + sixteenth offline deposit n16 IncludedInBatch=2 (ConsumedDepositCount=16, soft-seal-after-fifteenth-recover-da-deposit.json + host.prom) + soft-seal after-fifteenth-recover sixteenth outbound+FI/RPC (outbox=16, FI/inbound known=16, Merkle/message proofs, soft-seal-after-fifteenth-recover-sixteenth-outbound.json + -rpc.json) + soft-seal sixteenth poison→recover retention (sexdecuple deposit/outbox/FI known + multi-batch RPC tip/proofs survive sixteenth Poisoned cycle; soft-seal-sixteenth-poison-recover.json) + soft-seal after-sixteenth-recover DA re-publish + seventeenth offline deposit n17 IncludedInBatch=2 (ConsumedDepositCount=17, soft-seal-after-sixteenth-recover-da-deposit.json + host.prom) + soft-seal-offline-bridge + soft-seal-fi-inbound + soft-seal-after-recover retention (deposit/FI/inbound/outbox survive RecoverPoisoned; checkpoint tip 2 + pending≥2) while Retrying (RegisterForced/Inbound + open-batch counts 0) while Retrying (ProcessDeposit/outbox + IncludedInBatch RPC) (PublishDa/Local reader after SoftSeal Multisig/Opt) (RegisterInboundMessageNonce offline + KnownInboundNonceCount/openBatch L1=0/inbox pending 0) (RegisterForcedInclusionNonce offline + KnownForcedInclusionNonceCount/openBatch FI=0/overdue false) (RecordRpcDeposit/withdrawal MerkleProofSerializer proof/message+router proofs) + soft AddRpcBatch/FinalizeRpcBatch/ReceiveBatch/RPC proofs + ProcessCommittedBlock open-batch (no seal) unit+E2E multi-block when MaxBlocks>2 + E2E Multisig/Opt/Zk soft-open-batch operator surface (passport/pipeline healthy/settlement idle + durable status/probe); soft RPC store finalize + dual-chain Gateway ReceiveBatch unit/E2E Multisig/Opt/Zk with PullAggregate fail-closed + AggregatorPendingCount publication backlog + durable soft-rpc gateway status/probe/prom; Multisig/Optimistic soft seal→local PersistAsync checkpoint unit+E2E + Gateway ReceiveBatch after checkpoint (PendingSettlementCount=1; Multisig/Opt unit+E2E pin pipeline `IsSettlementRetrying` preferred over generic `IsSettlementIdle` when mock L1 fails + host `FormatOperatorStatusJson`/LocalHostHealthFailures + durable `WriteOperatorStatusAsync`/`WriteHealthProbeAsync`/`FormatHealthProbeJson` + checkpoint JSON fields + post-`FinalizeRpcBatch` `LatestRpcStateRoot` + host/gateway Prometheus scrape files; Multisig/Opt unit+E2E RPC `AddRpcBatch`+`FinalizeRpcBatch` tip + Gateway ReceiveBatch with `AggregatorPendingCount` backlog + `PullAggregate` fail-closed on durable outbox + `HasPendingPublication=false` + durable gateway status/probe writers; mock `ReconcileAsync` fail-closed → `SubmitNext` escalates to **Poisoned** (preferred over Retrying) with pending retained; Multisig/Opt unit+E2E Multisig/Opt local `RecoverPoisonedBatchAsync` (wrong hash/batch fail-closed) resets Retrying (`RetryCount=0`) + poison/recover probe+status files + post-recover passport/aligned/operator-ready + `IsLocalHostHealthy`/pipeline helpers still Retrying with tip retained + Gateway aggregator backlog independent of host recover; no PublishAggregate; L1 settle after recover / publish still funded; Zk soft seal still funded)). + status **HasBatchProver** / **LatestCheckpoint\*** / **InitialStateRoot** + recovery failure timestamps in operator JSON |
| Evidence | [`docs/audit/testnet-deployment-20260716-live.json`](./docs/audit/testnet-deployment-20260716-live.json), [`docs/audit/testnet-deployment-20260717-reverify.json`](./docs/audit/testnet-deployment-20260717-reverify.json), [`docs/audit/testnet-deployment-20260717-full-reverify.json`](./docs/audit/testnet-deployment-20260717-full-reverify.json), [`docs/audit/testnet-deployment-20260717-sharedbridge-fix.json`](./docs/audit/testnet-deployment-20260717-sharedbridge-fix.json), [`docs/audit/testnet-deployment-20260717-session-reverify.json`](./docs/audit/testnet-deployment-20260717-session-reverify.json), [`docs/audit/testnet-deployment-20260717-session3-reverify.json`](./docs/audit/testnet-deployment-20260717-session3-reverify.json), [`docs/audit/testnet-deployment-20260717-session4-reverify.json`](./docs/audit/testnet-deployment-20260717-session4-reverify.json), [`docs/audit/testnet-deployment-20260717-session5-reverify.json`](./docs/audit/testnet-deployment-20260717-session5-reverify.json), [`docs/audit/testnet-deployment-20260718-session6-reverify.json`](./docs/audit/testnet-deployment-20260718-session6-reverify.json), [`docs/audit/testnet-deployment-20260718-session7-reverify.json`](./docs/audit/testnet-deployment-20260718-session7-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-17.json`](./docs/audit/testnet-evidence-status-2026-07-17.json), [`docs/audit/testnet-evidence-status-2026-07-17-session.json`](./docs/audit/testnet-evidence-status-2026-07-17-session.json), [`docs/audit/testnet-evidence-status-2026-07-17-session2.json`](./docs/audit/testnet-evidence-status-2026-07-17-session2.json), [`docs/audit/testnet-evidence-status-2026-07-17-session3.json`](./docs/audit/testnet-evidence-status-2026-07-17-session3.json), [`docs/audit/testnet-evidence-status-2026-07-17-session4.json`](./docs/audit/testnet-evidence-status-2026-07-17-session4.json), [`docs/audit/testnet-evidence-status-2026-07-17-session5.json`](./docs/audit/testnet-evidence-status-2026-07-17-session5.json), [`docs/audit/testnet-evidence-status-2026-07-18-session6.json`](./docs/audit/testnet-evidence-status-2026-07-18-session6.json), [`docs/audit/testnet-evidence-status-2026-07-18-session7.json`](./docs/audit/testnet-evidence-status-2026-07-18-session7.json) , [`docs/audit/testnet-deployment-20260718-session8-reverify.json`](./docs/audit/testnet-deployment-20260718-session8-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-18-session8.json`](./docs/audit/testnet-evidence-status-2026-07-18-session8.json), [`docs/audit/testnet-deployment-20260718-session9-reverify.json`](./docs/audit/testnet-deployment-20260718-session9-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-18-session9.json`](./docs/audit/testnet-evidence-status-2026-07-18-session9.json), [`docs/audit/testnet-deployment-20260718-session10-reverify.json`](./docs/audit/testnet-deployment-20260718-session10-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-18-session10.json`](./docs/audit/testnet-evidence-status-2026-07-18-session10.json), [`docs/audit/testnet-deployment-20260718-session11-reverify.json`](./docs/audit/testnet-deployment-20260718-session11-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-18-session11.json`](./docs/audit/testnet-evidence-status-2026-07-18-session11.json), [`docs/audit/testnet-deployment-20260719-session12-reverify.json`](./docs/audit/testnet-deployment-20260719-session12-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-19-session12.json`](./docs/audit/testnet-evidence-status-2026-07-19-session12.json), [`docs/audit/testnet-deployment-20260719-session13-reverify.json`](./docs/audit/testnet-deployment-20260719-session13-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-19-session13.json`](./docs/audit/testnet-evidence-status-2026-07-19-session13.json), [`docs/audit/testnet-deployment-20260719-session14-reverify.json`](./docs/audit/testnet-deployment-20260719-session14-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-19-session14.json`](./docs/audit/testnet-evidence-status-2026-07-19-session14.json), [`docs/audit/testnet-deployment-20260719-session15-reverify.json`](./docs/audit/testnet-deployment-20260719-session15-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-19-session15.json`](./docs/audit/testnet-evidence-status-2026-07-19-session15.json), [`docs/audit/testnet-deployment-20260720-session16-reverify.json`](./docs/audit/testnet-deployment-20260720-session16-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-20-session16.json`](./docs/audit/testnet-evidence-status-2026-07-20-session16.json), [`docs/audit/testnet-deployment-20260720-session17-reverify.json`](./docs/audit/testnet-deployment-20260720-session17-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-20-session17.json`](./docs/audit/testnet-evidence-status-2026-07-20-session17.json), [`docs/audit/testnet-deployment-20260720-session18-reverify.json`](./docs/audit/testnet-deployment-20260720-session18-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-20-session18.json`](./docs/audit/testnet-evidence-status-2026-07-20-session18.json), [`docs/audit/testnet-deployment-20260720-session19-reverify.json`](./docs/audit/testnet-deployment-20260720-session19-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-20-session19.json`](./docs/audit/testnet-evidence-status-2026-07-20-session19.json), [`docs/audit/testnet-deployment-20260721-session20-reverify.json`](./docs/audit/testnet-deployment-20260721-session20-reverify.json), [`docs/audit/testnet-evidence-status-2026-07-21-session20.json`](./docs/audit/testnet-evidence-status-2026-07-21-session20.json) |

Key hashes: ChainRegistry `0x65201c54…2d23`, SettlementManager `0x11448868…bb51`, SharedBridge **fixed** `0xf64548c2…1bae` (legacy `0xf2f5114b…b241`), MessageRouter `0x3caf3c6e…fe90`, ForcedInclusion `0x962829ae…55a9`, TokenRegistry `0x96ae4655…505b`, Sp1Groth16Verifier `0x1004bb51…0c4d`. Scanner deploy heights (legacy bundle): ForcedInclusion `17729309`, SharedBridge `17729307`, MessageRouter `17729303`.

Still **not** closed (funded / operator binary gates): full L2 node process stack against a reviewed
Neo.CLI binary, 4-SDK live fixture, production DA credentials, real SP1 proof vectors, governance
retarget of chain `20260716` bridge hash to the fixed SharedBridge (or new chain registration).

**Code-complete operator path** (local layout + L1 registration encoding/broadcast/verify):

```bash
neo-stack create-chain --chain-id 20260716 --output ./my-l2 --template zk-rollup
neo-stack init-l2 --chain-id 20260716 --output ./my-l2 \
  --from-deploy-report docs/audit/testnet-deployment-20260716-live.json
# settlement config: ProofType=Zk, *DeploymentHeight from evidence blockIndex,
# data/settlement/* durable store dirs + l1.wireproduction-notes.json
neo-stack bootstrap-genesis --chain-id 20260716 --output ./my-l2
neo-stack register-chain --chain-id 20260716 --output ./my-l2 \
  --from-deploy-report docs/audit/testnet-deployment-20260716-live.json
# genesis-manifest auto-detected; --broadcast signs+confirms then isActive/genesis verify
```

## Production integrations still operator-supplied

These are explicit deployment seams rather than missing protocol algorithms:

- **Settlement and operator signer custody** — `L2SettlementPlugin.WireProduction` closes the
  production RPC composition root around `RpcTransactionSender`, `RpcSettlementClient`,
  forced-inclusion finalization, optionally an owned `RpcSharedBridgeDepositSource` when
  `SharedBridgeHash` is configured, and optionally an owned `RpcMessageRouter` +
  `RpcMessageRouterEventScanner` when `MessageRouterHash` is configured.
  Hosts construct plugins via `L2SettlementPlugin.CreateFromChainDirectory(chainDir)`,
  `L2BatchPlugin.CreateFromChainDirectory(chainDir)`,
  `L2BridgePlugin.CreateFromChainDirectory(chainDir)`; L1 inbox via
  `L2SettlementPlugin.WireL1InboxFromChainDirectory(chainDir, batch)` or
  `L1InboxFromChainDirectory.Open(chainDir).WireBatch(batch)` (shared RPC for deposit +
  ForcedInclusion + MessageRouter; per-type `Create*FromChainDirectory` still available),
  `L2ProverPlugin.CreateFromChainDirectory(chainDir)` then `Wire(...)`,
  `L2MetricsPlugin.CreateFromChainDirectory(chainDir)`, for Multisig/Optimistic local DA
  `L2DAPlugin.CreateLocalFromChainDirectory(chainDir)` (public DAMode uses
  `WithProductionBackend`), L2 RPC
  `InMemoryL2RpcStore.OpenFromChainDirectory(chainDir)` then `NeoSystem.AddService(store)`
  (durable proofs under `data/rpc/proofs`), and Gateway
  `L2GatewayPlugin.CreateMerkleDurableFromChainDirectory` /
  `CreateMultisigDurableFromChainDirectory` / `CreateSp1DurableFromChainDirectory` (or generic
  `CreateDurableFromChainDirectory`) for settings → aggregator → durable outbox under
  `data/gateway/outbox`, then `Sp1GatewayProofProver.OpenFromChainDirectory(chainDir, vk)`
  (queue `prover/gateway-inbox`) + `ProofBoundRpcGlobalRootPublisher.OpenFromChainDirectory`
  + `ConfigureGlobalRootPublicationFromChainDirectory` (MessageRouter via
  `L1DeployedEndpoints.FromChainDirectory`) or explicit `ConfigureGlobalRootPublication`
  (gateway host daemon + funded L1 remain operator-supplied; settings-only
  `CreateFromChainDirectory` when the host will call `UseAggregator` /
  `AttachOutboxFromChainDirectory` separately). Multisig one-shot host:
  `MultisigLocalHostComposition.Open(chainDir, executor, signers, signer)` (local DA +
  Multisig prover + WireProductionFromLayout + `InMemoryL2RpcStore`). Optimistic one-shot host:
  `OptimisticLocalHostComposition.Open(chainDir, executor, sequencerKey, bondContract,
  bondTxHash, signer)` (+ RPC store). Zk one-shot host:
  `ZkLocalHostComposition.Open(chainDir, executorPath, executorSha256, vk, productionDaWriter,
  signer)` after bootstrap-genesis (+ RPC store; funded executor binary + VK +
  IProductionDAWriter + prove-batch). Gateway one-shot: `GatewayHostComposition.OpenMerkle` /
  `OpenSp1`. Multisig batch provers alone:
  `L2ProverPlugin.CreateMultisigWiredFromChainDirectory(chainDir, signers)`. Optimistic:
  `CreateOptimisticWiredFromChainDirectory(chainDir, sequencerKey, bondContract, bondTxHash)`.
  Zk batch provers: `CreateZkWiredFromChainDirectory(chainDir, vk)` /
  `Sp1BatchProofProver.OpenFromChainDirectory(chainDir, vk)` (queue `prover/inbox`).
  Deploy-report materialization writes settlement + batch + bridge + prover + metrics + DA +
  gateway plugin configs (ProofType/DAMode/gatewayEnabled from chain.config).
  Genesis root via `L2GenesisManifest.ReadInitialStateRootFromChainDirectory`; Multisig/Optimistic
  profile via `ProofWitnessPipelineProfile.LegacyFromChainDirectory`; Zk via
  `state = Sp1SettlementExecutionStack.OpenStateFromChainDirectory(chainDir)` then
  `CreateFromChainDirectory(chainDir, state, executorPath, executorSha256, verificationKeyId)`
  (binds genesis + `prover/inbox` + executor scratch; reviewed binary pin remains
  operator-supplied). Local signer via
  `LocalKeyTransactionSigner.FromEnvironmentVariable` / `FromWif` or
  `FromEnvironmentVariableWithGlobalScope` for nested NEP-17 (production uses HSM/KMS).
  `L2SettlementStoreLayout.Open(chainDir)` opens the canonical durable RocksDB stores under
  `data/settlement/*`; Multisig hosts may call
  `WireProductionFromLayout(chainDir, layout, batch, executor, da, prover, signer)` to bind
  stores, static committee hash, and the legacy profile (pass Sp1 stack Profile for Zk;
  `prover` from `L2ProverPlugin.Prover` after Wire or direct Attestation/Optimistic/Sp1 instance).
  Deploy heights and `L1FinalityDepth` come from plugin config (materialized by
  `--from-deploy-report` when evidence has `blockIndex`) with optional per-scanner
  WireProduction overrides.
  L1 inbox: `WireProduction` defaults `l1FinalizedHeight` from the production RPC +
  `L1FinalityDepth` when omitted; `sequencerCommitteeHash` is required via
  `SequencerCommitteeConfig.CreateStaticHashProviderFromChainDirectory` (when
  `chain.config.json` `validators` is non-empty) or
  `SequencerCommitteeHasher.CreateSyncProvider` over `RpcSequencerCommitteeProvider`
  (registry hash in `l1.deployed.json` / wireproduction notes). Local/dev hosts can also
  bootstrap `InMemorySequencerCommitteeProvider` via
  `CreateInMemoryProviderFromChainDirectory`. Seal-time deposit `ScanAsync` via
  `L1MessageDrain.FromDeposits`.
  `neo-stack --signer-command` provides a provider-neutral, deadline-bounded executable
  boundary with pinned account/script, canonical sign data, and fee-witness-shape validation.
  Operators still select and own the reviewed wallet, HSM, or KMS adapter; no private key is
  stored in plugin configuration.
- **Real NeoFS client** — `NeoFsLikeDAWriter` remains a development semantic simulator and
  cannot satisfy a production NeoFS profile. Production injects `NeoFsRestDAWriter` +
  `NeoFsRestDAReader` through `L2DAPlugin.WithProductionBackend` (or an equivalent
  reviewed SDK adapter) with real REST credentials, container/object locators, and
  independent retrieval validation. Provider-specific remote rehearsal evidence is still
  operator-supplied.
- **L1 and DAC credentials** — `JsonRpcL1DAWriter` and
  `CommitteeAttestedDAWriter` implement the protocols, while transaction keys and
  committee signer callbacks remain deployment secrets supplied through DI.
- **Recursive-ZK Gateway operations** — the SP1 6.2.1 guest/daemon and strict queue protocol
  ship in-repo. Ordinary PR/`master` CI runs the fast compatibility aggregate
  (`SP1 compatibility and manual release proof gate`) and requires the expensive real-proof
  matrix to stay **skipped**. Release owners manually dispatch the three
  `sp1-release-gates` lanes (workspace release, terminal batch proof, recursive Gateway
  proof); the aggregate gate then requires every lane to succeed without mock/dummy
  fallback. Operators still supply canonical compressed batch-proof sidecars, proving
  hardware, and retain exact-build deployment/audit evidence; `test-only-vk` builds cannot
  prove.
- **Neo.CLI/DBFTPlugin release bundle** — consensus selection is wired in the r3e Neo core,
  but this repository does not publish Neo.CLI or DBFTPlugin binaries. Operators must provide
  a reviewed deployment built against the pinned r3e core; `neo-stack` fails closed if its
  required assembly/config layout or protocol validator configuration is inconsistent.

## How to run

```bash
# Type-check + run all unit + integration tests
dotnet test Neo.L2.sln /p:NuGetAudit=false

# Build smart contracts (type-check only without nccs)
dotnet build contracts/NeoHub.ChainRegistry /p:NuGetAudit=false /p:DisableNccs=true

# Run the in-process devnet demo with real state-root continuity
dotnet run --project tools/Neo.L2.Devnet -- 5

# Same demo plus live HTTP /metrics scrape on port 9090
dotnet run --project tools/Neo.L2.Devnet -- 5 --metrics-port 9090

# Persist devnet state to disk via RocksDB (state survives restart)
dotnet run --project tools/Neo.L2.Devnet -- 5 --data-dir /tmp/neo-l2-devnet

# Generate a NeoHub deploy bundle
dotnet run --project tools/Neo.Hub.Deploy -- scaffold --output deploy-plan.json
dotnet run --project tools/Neo.Hub.Deploy -- plan --plan deploy-plan.json --output bundle.json

# Build the SP1 prover (real Stage-2 ZK validity prover daemon)
CPATH=~/.local/include cargo build --release -p neo-zkvm-host

# Run the prover daemon (consumes *.batch.bin from --watch dir,
# emits matching *.proof.bin + *.proof.vk)
target/release/prove-batch daemon \
    --watch /var/lib/neo-l2/batches \
    --archive /var/lib/neo-l2/proven \
    --poll-secs 5

# Use the launcher CLI
dotnet run --project tools/Neo.Stack.Cli -- help
```
