# Spec-gap fix plan

A systematic plan to close every remaining `doc.md` § gap that this consolidation
repo can address. Items are grouped by scope (in-repo / upstream / operator) and
ordered by priority within each group. Each item lists: (a) what the spec says,
(b) what's currently in the code, (c) the smallest meaningful change that closes
the gap, (d) acceptance criteria.

Tracking: each closed item gets a one-line `CHANGELOG.md` entry referencing this
plan ID (e.g. `[plan: §16.1-admission]`) so reviewers can cross-check.

## In-repo, fixable now (priority order)

### §16.1-admission — ChainRegistry consults GovernanceController.GetAdmissionMode()

**Spec.** §16.1 defines three admission phases — permissioned (owner approves),
semi-permissionless (any caller if verifier+bridge are approved), permissionless
(any caller). GovernanceController already stores the mode via
`SetAdmissionMode(0..2)`.

**Code today.** `ChainRegistry.RegisterChain` requires `Runtime.CheckWitness(GetOwner())`
unconditionally — i.e., always permissioned regardless of GovernanceController's
configured mode.

**Fix.** Add `KeyGovernanceController` storage to ChainRegistry (set via owner-only
`SetGovernanceController(hash)`). Add `RegisterChainPublic(chainId, configBytes)`
that:
  1. Reads the GovernanceController hash; rejects if unset.
  2. Calls `GovernanceController.GetAdmissionMode()`.
  3. Branches: mode 2 → any caller; mode 1 → defer (needs approved-set wiring,
     filed below as §16.1-approved-sets); mode 0 → reject with "use RegisterChain".

The original owner-only `RegisterChain` stays as the permissioned path for
backwards compatibility.

**Acceptance.** Type-check clean. Existing 879 tests still green. New tests pin
mode 0 rejects on RegisterChainPublic; mode 2 accepts.

### §16.1-approved-sets — Approved verifier + bridge sets on GovernanceController

**Spec.** Semi-permissionless admission requires the L2's verifier and
bridgeAdapter to be in governance-approved sets.

**Code today.** No approved-set storage anywhere.

**Fix.** Add to GovernanceController:
  - `KeyApprovedVerifier = 0x0A` + `0x0A + verifierHash(20B) → 1`
  - `KeyApprovedBridgeAdapter = 0x0B` + `0x0B + bridgeHash(20B) → 1`
  - `[Safe] IsApprovedVerifier(UInt160)` / `IsApprovedBridgeAdapter(UInt160)`
  - `ApproveVerifier(UInt160)` / `ApproveBridgeAdapter(UInt160)` (owner-only,
    or M-of-N council if the council framework can be invoked)
  - `RevokeVerifier(UInt160)` / `RevokeBridgeAdapter(UInt160)`

Then update `ChainRegistry.RegisterChainPublic` mode 1 path to call these.

**Acceptance.** Tests for approve/revoke/IsApproved + the integration through
ChainRegistry.

### §16.2-config-bytes — Encode SequencerModel + ExitModel into L2ChainConfig wire format

**Spec.** §16.2 says every L2 must publish 5 security label dimensions. Two
dimensions (Sequencer, Exit) were added to the off-chain `L2ChainConfig` record
in commit `340951a` but the on-chain encoding (89 bytes in
`ChainRegistry.ConfigSize`) only carries 3 dimensions in its 5×1 byte fields.

**Code today.** Off-chain record has the new fields with init-defaults.
On-chain bytes don't carry them. RPC `getsecuritylevel` returns only the existing
SecurityLevel byte.

**Fix.** Bump `ChainRegistry.ConfigSize` from 89 to 91. Document the layout:
`[4B chainId][20B operator][20B verifier][20B bridge][20B msg]` plus
`[1B securityLevel][1B daMode][1B gatewayEnabled][1B permissionlessExit]
[1B sequencer][1B exit][1B active]` (7×1 byte fields). Update the deploy planner
+ the 1 test that constructs configs. Add a new RPC `getsecuritylabel` that
returns the full 5-dimension label.

**Acceptance.** Contract compiles; `getsecuritylabel` callable; existing
`getsecuritylevel` still works.

### §16-council-veto — Verifier/bridge upgrade behind council multisig

**Spec.** §16 calls for "verifier upgrade" + "shared bridge upgrade" gated behind
a security council with M-of-N approval and a timelock.

**Code today.** GovernanceController has council members + `Approve(proposalId)`
+ a stored `KeyTimelockSeconds` value. But no contract reads the proposal-vote
result to gate execution; the timelock isn't enforced anywhere. VerifierRegistry
+ SharedBridge upgrade methods aren't gated.

**Fix.**
  1. Add `KeyExecutedAt` per proposal in GovernanceController; once threshold
     hit, store the unix-timestamp of threshold-reaching.
  2. Add `[Safe] IsApprovedAndTimelocked(proposalId)` returning true if approval
     count ≥ threshold AND `now ≥ executedAt + timelock`.
  3. VerifierRegistry.UpdateVerifier and SharedBridge.UpgradeAsset etc. consult
     this method via Contract.Call before applying.

**Acceptance.** Type-check + a deploy-planner test scaffolding the wiring.

### §12-l1-da-default — Provide a JsonRpc-backed L1 DA writer scaffold

**Spec.** §12.1 lists L1 DA as one of three DA tiers. The bridge writer is
operator-supplied today.

**Code today.** `L2DAPlugin.BuildDefaultWriter(DAMode.L1, ...)` throws
NotSupportedException unless the operator pre-injected a writer. NeoFS/External/
DAC have built-in defaults; L1 doesn't.

**Fix.** Add `Neo.Plugins.L2DA.JsonRpcL1DAWriter` that takes a `JsonRpcClient`
+ a target contract hash + a sign-and-send delegate (same shape as
`RpcSettlementClient.SignAndSendAsync`). Implements `IDAWriter` by calling
`publishDABlob` (or whatever the L1 DA contract's method is — that's a separate
spec point — `NeoHub.DARegistry.RecordDACommitment` is closest match). Wire as
the `BuildDefaultWriter(DAMode.L1, ...)` default with a clear message that
operators still need to configure the L1 RPC + signer.

**Acceptance.** New writer compiles + has unit tests against an in-process fake
RPC client. `BuildDefaultWriter(DAMode.L1, dataDir=null)` no longer throws when
the writer is wired via `WithWriter()`.

### §8-witness-canonical — Pin a canonical Witness wire format

**Spec.** §8.4 lists witness contents (ordered txs, contract bytecode, storage
read/write witness, native contract state witness, L1 messages consumed, DA data,
execution trace).

**Code today.** Witness in `ProofRequest.Witness` is opaque
`ReadOnlyMemory<byte>`. No serializer pins the layout. Different prover backends
(SP1, Halo2) might want different formats today.

**Fix.** Add `Neo.L2.Proving.WitnessRecord` with the seven sections + a
canonical encoder/decoder following the `BatchSerializer` pattern. Provers can
opt into the canonical format or wrap their own. Tests pin the layout +
round-trip + truncation rejection.

**Acceptance.** WitnessRecord serializer + 3-5 tests; existing proof tests still
green.

## Upstream / out-of-repo (track but don't fix here)

### §13.2-native-adjustments — GAS / NEO / Oracle / Policy adjustments at L2 mode

Lives in neo-project/neo Neo 4 core. The L2 mode (per §6 ChainMode) needs core
changes: GAS supply gated by bridge, NEO governance still on L1, optional Oracle.
Track as upstream coordination work; nothing actionable in this repo until Neo 4
core ships ChainMode hooks.

### §14.1-rpcserver-wrapper — `[RpcMethod]`-decorated wrapper class

Pending Neo 4's RpcServer plugin source. The 9 L2 RPC methods exist as plain
methods in `Neo.Plugins.L2Rpc.L2RpcMethods`; the wrapper that registers them with
neo's RpcServer dispatcher needs that source. Track as a pending integration —
when neo-modules (or wherever Neo 4 RpcServer lands) is available, generate the
partial class.

### §4-recursive-zk — Real Neo Gateway round prover

SP1 Compress / Halo2 accumulator / Risc0 STARK fold. Substantial ZK work
including bridge ABI extension (bridge today exposes prove/verify, not
combine/aggregate). Track as Phase 5; would need its own multi-iteration plan.

## Operator-specific (won't fix in repo)

### §14.2-wallet-integration

`neo-stack` `register-chain`, `deploy-bridge-adapter`, `submit-batch` print
operator plans. Auto-signing requires a Neo wallet SDK choice (NEP-6 keystore /
Ledger / Metamask-Snap-style) that's the operator's call.

### §11-l1-signer-for-submitbatch

`RpcSettlementClient.SignAndSendAsync` is a delegate; concrete signing is
operator-supplied. Same reasoning as above.

### §16.3-dbft-consensus-integration

Wiring `Neo.L2.Sequencer` into Neo's `DBFTPlugin` consensus selector is
deployment-specific.

## Summary

**6 in-repo items** in priority order:
  1. §16.1-admission (smallest, highest value — unlocks the 3-phase roadmap)
  2. §16.1-approved-sets (needed for mode 1)
  3. §16.2-config-bytes (wire-format extension; medium-invasive)
  4. §16-council-veto (security-critical for mainnet)
  5. §12-l1-da-default (operator convenience)
  6. §8-witness-canonical (premature without a real prover targeting it)

**3 upstream items** tracked but blocked on external dependencies.

**3 operator-specific items** explicitly not in this repo's scope.

Cadence: fix one in-repo item per loop iteration; commit message references
this plan's ID. Re-audit after each item to catch second-order gaps.
