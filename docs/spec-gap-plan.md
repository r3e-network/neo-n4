# Spec-gap fix plan

A systematic plan to close every remaining `doc.md` § gap that this consolidation
repo can address. Items are grouped by scope (in-repo / upstream / operator) and
ordered by priority within each group. Each item lists: (a) what the spec says,
(b) what's currently in the code, (c) the smallest meaningful change that closes
the gap, (d) acceptance criteria.

Tracking: each closed item gets a one-line `CHANGELOG.md` entry referencing this
plan ID (e.g. `[plan: §16.1-admission]`) so reviewers can cross-check.

## In-repo (status by §)

### §16.1-admission ✅ closed

`ChainRegistryContract` exposes the §16.1 admission policy via
`RegisterChainPublic(chainId, configBytes)`:

1. Reads the wired GovernanceController hash (set via owner-only
   `SetGovernanceController`); rejects if unset with a clear "owner must wire
   first" hint.
2. Calls `GovernanceController.GetAdmissionMode()`.
3. Mode 0 (permissioned) → reject with "use RegisterChain"; mode 1
   (semi-permissionless) → enforce `IsApprovedVerifier` + `IsApprovedBridgeAdapter`
   on the verifier (offset 24..43) and bridge (offset 44..63) bytes; mode 2
   (permissionless) → any caller; falls through to the same
   `WriteChainConfig` write path used by the owner-only path.

The owner-only `RegisterChain` stays as the §16.1 "permissioned" path.

**Files.** `contracts/NeoHub.ChainRegistry/ChainRegistryContract.cs`.
Tests cover modes 0/1/2 + the unwired-controller rejection.

### §16.1-approved-sets ✅ closed

`GovernanceControllerContract` carries the approved-verifier + approved-bridge
sets used by `ChainRegistry.RegisterChainPublic` mode 1:

  - `PrefixApprovedVerifier = 0x0A` (`0x0A + verifierHash(20B) → 1`)
  - `PrefixApprovedBridge = 0x0B` (`0x0B + bridgeHash(20B) → 1`)
  - `[Safe] IsApprovedVerifier(UInt160)` / `[Safe] IsApprovedBridgeAdapter(UInt160)`
  - `ApproveVerifier(UInt160)` / `RevokeVerifier(UInt160)` and the bridge
    counterparts (owner-only mutators)

ChainRegistry mode-1 path consults these via `Contract.Call(...,
"isApprovedVerifier"/"isApprovedBridgeAdapter", CallFlags.ReadOnly, ...)`
with assertion errors that name the failing dimension.

**Files.** `contracts/NeoHub.GovernanceController/GovernanceControllerContract.cs`,
integration through `ChainRegistryContract.RegisterChainPublic`.

### §16.2-config-bytes ✅ closed

`ChainRegistry.ConfigSize` is now 91 bytes (4 + 20×4 + 7), carrying all 5
§16.2 security-label dimensions in distinct byte fields:

  - `OffsetSecurityLevel = 84`, `OffsetDAMode = 85`, `OffsetGatewayEnabled = 86`,
    `OffsetPermissionlessExit = 87`, `OffsetSequencerModel = 88`,
    `OffsetExitModel = 89`, active flag at `ConfigSize - 1` (= 90).
  - Single-purpose `[Safe] Get*` readers for each dimension.
  - `Neo.L2.Abstractions` ships `SequencerModel` / `ExitModel` enums + a
    canonical `L2ChainConfigSerializer` + `L2ChainConfigJsonReader`.
  - Off-chain `getsecuritylabel` RPC exposes the full 5-dimension label
    (existing `getsecuritylevel` preserved for back-compat).

### §16-council-veto ✅ closed

`GovernanceController` enforces the §16 council-multisig + timelock gate:

  - `Approve(proposalId, memberKey)` records each council vote; when the
    threshold is first hit, the unix-timestamp is stored at `PrefixApprovedAt
    = 0x0C`.
  - `[Safe] IsApprovedAndTimelocked(proposalId)` returns true once
    `approvalCount ≥ threshold` AND `now ≥ approvedAt + timelockSeconds`.
  - `VerifierRegistry.SetGovernanceController` + `RegisterVerifierViaProposal`
    consult this gate before applying verifier upgrades.

### §12-l1-da-default ✅ closed

`Neo.Plugins.L2DA.JsonRpcL1DAWriter` ships as the default L1-DA writer:

  - Wraps a `JsonRpcClient` + a `SignAndSendAsync` delegate (same shape as
    `RpcSettlementClient`).
  - Implements `IDAWriter` by invoking the configured L1 contract method
    (`NeoHub.DARegistry.RecordDACommitment` is the closest match by default).
  - 13 unit tests against an in-process fake RPC client.
  - `L2DAPlugin.BuildDefaultWriter(DAMode.L1, ...)` no longer throws when the
    writer is wired.

### §8-witness-canonical ⏭ deferred

Originally proposed `Neo.L2.Proving.WitnessRecord` to pin the §8.4 witness
layout (ordered txs / bytecode / storage R/W / native state / L1 messages /
DA data / trace).

**Decision.** Premature without a real prover targeting it — different
backends (SP1, Halo2) want different formats. Re-evaluate when the SP1
toolchain integration lands and the guest ELF defines its expected witness
shape. `ProofRequest.Witness` stays as opaque `ReadOnlyMemory<byte>` until
then.

### §state-tree-convention ✅ closed

`KeyedStateMerkleTree.ComputeRoot` / `Prove` / `Verify` previously used a
promote-unchanged convention for odd-cardinality state trees, while the
canonical `MerkleTree` (used by `KeyedStateStore.ComputeRoot` and pinned by
`UT_OnChainMerkleVerifyParity` against the on-chain
`SettlementManager.VerifyStateLeafWithProof` verifier) used the Neo classic
odd-leaf-duplication convention. The two produced different roots for any
state tree with `N == odd > 1`, so an operator wiring the production
`MerkleStatePostStateRootOracle` and following the parity-test code pattern
to generate proofs would have hit failed escape-hatch verification.

**Fix.** `KeyedStateMerkleTree` now delegates tree composition entirely to
`MerkleTree` (Neo classic). `Prove` returns siblings in leaf-to-root order;
`Verify` walks leaf-to-root using leaf-index bits, matching the on-chain
fold loop byte-for-byte. New parity test
(`UT_KeyedStateMerkleTree_NeoClassicParity`) pins `KeyedStateMerkleTree.ComputeRoot(pairs)`
== `MerkleTree.ComputeRoot(pairs.Select(HashEntry).ToArray())` across
cardinalities 1, 2, 3, 4, 5, 7, 8, 9, 15, 16 (including the previously-divergent
odd cases), plus a `HashLeaf` ↔ `HashEntry` byte-identity pin.

### §v4-fraud-verifier ✅ restricted profile / ⏭ general NeoVM

`NeoHub.RestrictedExecutionFraudVerifier` preserves v3 as advisory-only
structural evidence and adds canonical v4. `OptimisticChallenge` rejects v3
before dispatch even with governance witness. V4 reads SettlementManager's stored
`Challengeable` optimistic header, binds chain/batch/pre/post/tx roots,
transaction index, replay domain, semantic id, transcript/witness/claim hashes,
verifies the single-leaf transaction proof, and executes one existing-key
Counter Increment transition over a canonical old/new storage proof. Honest
committed execution returns false; only a wrong committed post root returns true.

The remaining gap is intentionally narrower: the batch commitment has no
transaction count or intermediate trace root, and L1 has no general restricted
NeoVM snapshot executor. Therefore v4 accepts only one transaction at index 0,
interval `[0,1]`, semantic id
`Hash256("neo4-executor:counter-increment-existing-key:v1")`. Multi-transaction
bisection, arbitrary opcodes/custom executors, and key insertion/deletion fail
closed until the commitment and execution engine expose those anchors/semantics.

## Upstream / out-of-repo (track but don't fix here)

### §13.2-native-adjustments — GAS / NEO / Oracle / Policy adjustments at L2 mode

Lives in the `r3e-network/neo` core fork on branch `r3e/neo-n4-core`. The L2 mode
(per §6 ChainMode) needs core changes: GAS supply gated by bridge, NEO governance
still on L1, optional Oracle.
Track as upstream coordination work; nothing actionable in this repo until Neo 4
core ships ChainMode hooks.

### §14.1-rpcserver-wrapper — `[RpcMethod]`-decorated wrapper class

Pending Neo 4's RpcServer plugin source. The 9 L2 RPC methods exist as plain
methods in `Neo.Plugins.L2Rpc.L2RpcMethods`; the wrapper that registers them with
neo's RpcServer dispatcher needs that source. Track as a pending integration —
when neo-modules (or wherever Neo 4 RpcServer lands) is available, generate the
partial class.

### §4-recursive-zk — Real Neo Gateway round prover

**Status update**: Phase 5 aggregation now ships **two production-grade
`IRoundProver` implementations** — `MultisigRoundProver` (Secp256r1
threshold-attested rounds) + `MerklePathRoundProver` (per-constituent
inclusion proofs against the aggregate root) — alongside the
`PassThroughRoundProver` reference. Real cryptography, no toolchain
dependency. The remaining recursive-ZK fold variants (SP1 Compress / Halo2
accumulator / Risc0 STARK fold) plug into the same `IRoundProver` seam
when the operator brings the SP1 toolchain.

## Operator-specific (won't fix in repo)

### §14.2-wallet-integration

**Status update**: `docs/wallet-integration.md` now documents two production
patterns — paste-into-wallet hex (cold-key flows) + delegate signing
(hot-wallet automation) — with worked examples for NeoLine / Neon / NEP-6 /
Ledger / KMS. Every CLI emits canonical hex; production hot-paths
(`RpcSettlementClient`, etc.) take a `SignAndSendAsync` delegate the
operator wires to their preferred signing path. Framework never holds
private keys, but the integration-pattern documentation is shipped.

### §11-l1-signer-for-submitbatch

`RpcSettlementClient.SignAndSendAsync` is a delegate; concrete signing is
operator-supplied. Same reasoning as above.

### §16.3-dbft-consensus-integration

Wiring `Neo.L2.Sequencer` into Neo's `DBFTPlugin` consensus selector is
deployment-specific.

## Summary

**6 in-repo items** in priority order:
  1. ✅ §16.1-admission — closed: `ChainRegistry.SetGovernanceController` +
     `RegisterChainPublic` mode 0/1/2 branches.
  2. ✅ §16.1-approved-sets — closed:
     `GovernanceController.{Approve,Revoke}{Verifier,BridgeAdapter}` +
     `IsApproved*` consulted by `RegisterChainPublic` mode 1.
  3. ✅ §16.2-config-bytes — closed: `ConfigSize` 89 → 91 bytes,
     `OffsetSequencerModel` / `OffsetExitModel` constants, single-purpose
     `[Safe] Get*` readers, `SequencerModel` / `ExitModel` enums in
     Abstractions.
  4. ✅ §16-council-veto — closed: `GovernanceController.GetApprovedAt` +
     `IsApprovedAndTimelocked`, `VerifierRegistry.SetGovernanceController` +
     `RegisterVerifierViaProposal` (consults the timelock gate).
  5. ✅ §12-l1-da-default — closed: `Neo.Plugins.L2DA.JsonRpcL1DAWriter`
     (`JsonRpcClient` + signed-tx delegate, 13 unit tests).
  6. ⏭ §8-witness-canonical — **deferred** (plan note: "premature without
     a real prover targeting it" — re-evaluate when the SP1 toolchain lands
     and the guest ELF defines its expected witness shape).

**Second-order gaps closed during the same window** (additive, not in the
original 6):
  - Canonical 91-byte `L2ChainConfigSerializer` + `L2ChainConfigJsonReader`
    in Abstractions; CLI's `register-chain` emits the hex directly when
    operator hashes are supplied.
  - `ChainRegistry` rounds out the §16.2 reader API (`GetSecurityLevel` /
    `GetDAMode` / `GetGatewayEnabled` / `GetPermissionlessExit`) for
    symmetry with the existing `GetSequencerModel` / `GetExitModel`.
  - `GovernanceController.GetApprovalCount` `[Safe]` public reader.
  - Off-chain RPC `getsecuritylabel` exposes the full 5-dimension §16.2
    label.
  - `ScaffoldPlan.PostDeployActions` surfaces the
    `ChainRegistry.SetGovernanceController` +
    `VerifierRegistry.SetGovernanceController` post-deploy wiring (+
    existing `SequencerBond.RegisterSlasher`).

**Production-readiness audit follow-ups** (later iterations after the
honest "is everything correctly and completely implemented?" audit):
  - `IMPLEMENTATION_STATUS.md` gains an explicit "Production-readiness
    audit" section catalogueing what's production-ready vs. MVP shapes
    vs. reference scaffolding (operator must replace) vs. plan-printers
    (CLI doesn't actually sign/submit) vs. out-of-repo-by-design.
  - `NeoHub.ForcedInclusion` ships a real configurable spam-control fee
    (`SetFee` / `SetFeeRecipient` / `SetGasToken`); default 0 = fee-free
    legacy preserved. Closes the "fee-free MVP" callout.
  - `NeoHub.GovernanceFraudVerifier` ships as an advisory structural fraud
    verifier for offline audit tooling. It decodes the canonical 101-byte `FraudProofPayload`,
    validates length / version / claims-a-real-discrepancy, emits
    accept/reject events with reason codes, but cannot authorize revert/slash and
    is deliberately excluded from `ScaffoldPlan.Default()` and live post-deploy wiring.
  - 13 parity tests (`UT_GovernanceFraudVerifierParity`) simulate the
    contract's decision tree in C# so a refactor that changes constants
    / order / offsets is caught at unit-test time.
  - "MVP" comment label cleanup: `ChallengeOrchestrator.InspectAsync`
    (the narrowing path exists in `*WithBisection`),
    `SettlementManager.VerifyWithdrawalLeaf{,At}` and
    `EmergencyManager.EscapeHatchExit` (intentional single-leaf fast
    paths, not incomplete MVP), `BatchSealer.SealBatch` (sealer is the
    tx-collector phase; executor pass produces real roots).
  - `CountApprovals` private helper renamed to
    `IncrementAndCountApprovals` so the bump-and-return semantics aren't
    misleading; `[Safe] GetApprovalCount` is the pure-read companion.
  - `samples/` ships 4 ready-to-run chain configs covering distinct use
    cases (general-rollup / gaming-rollup / exchange-validium /
    privacy-sidechain) verified end-to-end via
    `neo-l2-devnet --config`.
  - `samples/contracts/` ships 2 dApp examples (`CrossChainGreeter`,
    `WithdrawalDemo`) compiled by CI as the 21st + 22nd contracts.
  - `docs/tech-stack-coverage.md` honest 5-layer gap analysis: **61/62
    components ✅, 1 🟡 (SP1 toolchain offline), 0 🔴.** Phase 5 aggregation
    + every previously-out-of-repo Layer-4/5 row (TS/Rust SDKs, web app,
    mdBook, faucet, wallet docs) all ship in-tree.

**3 upstream items** tracked but blocked on external dependencies.

**3 operator-specific items** explicitly not in this repo's scope.

Cadence: fix one in-repo item per loop iteration; commit message references
this plan's ID. Re-audit after each item to catch second-order gaps.
