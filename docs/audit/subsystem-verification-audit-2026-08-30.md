# Neo N4 — Subsystem Verification Audit (2026-08-30)

This pass is the execution half of the [2026-08-29 full-system audit](./full-system-audit-2026-08-29.md).
That report was a read-and-cross-check pass over all 26 `contracts/` projects and the off-chain
libraries; this one takes the subsystems `neo-n4` owns uniquely — the PolkaVM RISC-V execution core,
the SP1 zkVM settlement stack, the asset-bearing bridge path, the batch/state-root/DA pipeline, the
optimistic challenge and anti-censorship machinery, and the operator surface (governance locks,
deployer, CLI, telemetry, RPC) — and drives them: build, run, instrument, and compare the artifact
under test against the source that claims to produce it. All seven tracks are closed in this
revision.

Two conventions carry over and one is new.

- **Evidence tiers** are unchanged: **[E1]** executed and observed, **[E2]** read and cross-checked
  against a second site, **[E3]** counted.
- **Finding numbering continues** the 2026-08-29 report: new Criticals are `C3`+, new Highs are
  `H14`+, new verification-integrity findings are `V1`+ (the prior report's class-3 items were
  `§3.n`). Prior IDs (`C1`, `C2`, `H1`–`H13`, `A4`–`A6`) keep their meaning and are re-statused in
  §7 rather than renumbered.
- **New in this pass**: every headline finding below was re-verified by the author of this report
  against the file and line cited, not accepted from a track run. Where a track result did not
  survive that re-check, it appears in §8 (corrections) instead of §3–§5.

## 1. Scope and method

Seven tracks, deliberately non-overlapping, each briefed to build on the 2026-08-29 report rather
than restate it:

| Track | Subsystem | Disposition |
| --- | --- | --- |
| T1 | `external/neo-riscv-vm` (PolkaVM host + guest + adapter plugin) | §3, §4 |
| T2 | `bridge/neo-zkvm-{guest,host}`, `neo-zkvm-executor` pin, `Sp1SettlementExecutionStack`, `NeoHub.Sp1Groth16Verifier` | §5 V3, V7, V8, §7 |
| T3 | `NeoHub.SharedBridge` / `ExternalBridgeEscrow` / `MpcCommitteeVerifier` / `VerifierRegistry`, foreign EVM + Solana programs, `L2NativeContracts.cs` | §5 V5, §7 |
| T4 | `Neo.L2.Batch`, `Neo.Plugins.L2Batch`, `Neo.L2.State`, `Neo.Plugins.L2DA` | §4 H15, §6 |
| T5 | `NeoHub.SettlementManager` / `OptimisticChallenge` / `ForcedInclusion` / `Censorship` | §3 C4, §4 H16, H19, §5 V5, §6 |
| T6 | Governance locks across 26 contracts, `Neo.Hub.Deploy`, `Neo.Stack.Cli`, telemetry + RPC operator surface | §4 H17, H18, §5 V6, §6 |
| T7 | CI topology, test-skip taxonomy, docs/ledger consistency | §5 V1, V2, V4 |

"Verification" here means the specific question: *is the artifact that the green checkmark ran on
the artifact that the source describes?* On the RISC-V path the answer is no, and that is the
headline of this report (§3).

## 2. What actually ran

Executed locally on Windows (`win-x64`), all commands exit 0 unless stated:

| Gate | Result |
| --- | --- |
| `dotnet test tests/Neo.Hub.Deploy.UnitTests` | 113 passed, 0 failed |
| `dotnet test --filter FullyQualifiedName~CurrentDocumentation` | 8 passed, 0 failed, 0 skipped |
| `mdbook build` (repo root `book.toml`, `src = "docs"`) | exit 0 |
| `NEO_RISCV_NATIVE_TESTS=1` RISC-V native suite | 10 tests executed and passed locally |
| Batch / state / DA suites (5 projects) | `Neo.L2.Batch` 68/68 · `Neo.Plugins.L2Batch` 65 pass + 1 skipped · `Neo.L2.State` 120/120 · `Neo.Plugins.L2DA` 109/109 · `Neo.L2.Abstractions` 79 pass + 1 skipped — all exit 0 |
| `dotnet test tests/Neo.Hub.Deploy.UnitTests` after §10 item 4 | 115 passed, 0 failed (the 113 above plus two Gateway parser tests) |
| `dotnet test Neo.L2.sln`, twice, after §10 items 1–4 | run 1: 2,897 total, **1 failed** — §5 V7, in a project that branch does not touch; run 2: 2,897 total, 0 failed, 5 skipped, exit 0 |
| `dotnet test tests/NeoHub.Contracts.VmTests` after §10 item 5 | 575 passed, 0 failed (`UT_ForcedInclusion_Vm` 17/17) |
| `dotnet test Neo.L2.sln`, after §10 item 5 | 38 assemblies, 2,899 total, 0 failed, 5 skipped, exit 0 |
| `cargo audit --file Cargo.lock` vs the Dependabot API, for §5 V8 | audit: `found: false, count: 0`, exit 0 — while the same lockfile carries 3 open alerts, one High |

The two skips are self-skips of the §5 V4 class, not failures. Track-reported counts for these five
projects were reproduced independently and matched exactly. (`V4` was repaired on this branch after
this table was recorded: those two are now `Neo.Plugins.L2Batch` 66/66 and `Neo.L2.Abstractions` 80/80
with `Skipped: 0` — the projects got one test bigger each, because the previously-skipping test now
runs.)

Against CI on `master`, the relevant topology is in §5 (V1).

## 3. Critical

### C3 — The RISC-V execution core is tested against a guest binary that its own source superseded, and ships a different one [E1 proven]

`external/neo-riscv-vm` embeds the guest module into the host library at compile time:

```
crates/neo-riscv-host/src/runtime_cache.rs:250
    include_bytes!("../../../crates/neo-riscv-guest-module/guest.polkavm")
```

`guest.polkavm` is committed (`external/neo-riscv-vm/.gitignore:3` force-un-ignores it) and there is
no `build.rs` — the blob is whatever a human last regenerated and committed. Git provenance, read
inside the submodule (`git log` from the parent repo returns nothing for submodule paths, which is
how this stays invisible):

| Event | Commit | Date |
| --- | --- | --- |
| `guest.polkavm` last regenerated | `efc3791` | 2026-05-20 |
| guest source: `static mut` → `AtomicU32`, callback lifetime docs | `2d1a6e7` | 2026-05-26 |
| guest source: "resolve critical/high security findings across 4 audit rounds" | `d18298b` | 2026-05-27 |
| guest source: "harden RISC-V runtime for Rust 2024" | `03e1139` | 2026-06-05 |

The working tree is clean, so this is the committed state, not local drift. **The binary that every
RISC-V test executes — including CI's dedicated native step — predates three rounds of guest-side
security and runtime hardening, and those changes have never been exercised.**

Nothing can notice. A regeneration path exists — `scripts/regenerate-guest-blob.sh`, requiring a
nightly cargo and `polkatool 0.32.0` — but it is unconditional: it builds, links, overwrites and
echoes `Wrote …`. It never compares the rebuilt blob against the committed one, so it cannot report
drift. A grep for `regenerate-guest-blob` and `package-adapter-plugin` across `.github/workflows/`
returns zero hits: no workflow calls it, and no test asserts that the blob matches the source that
produces it.

Worse, the two paths disagree about what the artifact *is*. `external/neo-riscv-vm/scripts/package-adapter-plugin.sh:20-25`
regenerates `guest.polkavm` from current guest source immediately before `cargo build -p
neo-riscv-host --release`. So the release plugin operators install contains a freshly compiled guest
whose behavior the test suite has never run, and the test suite certifies a blob nobody ships. This
inverts the repo's own standard elsewhere: the SP1 executor is hash-pinned (§5 V3) precisely to
avoid this, and the pin is the thing that fails; the PolkaVM guest has no pin at all.

Why this is Critical rather than High: `neo-n4`'s validity story for `L2RiscV`/PolkaVM rests on
"the same runtime re-executes inside the prover". That claim is about a binary. For the guest, the
binary in the tests is not the binary in the source tree and not the binary in the package, and no
gate — CI, test, or build — can tell.

Fix, in order of value: (1) add a freshness gate that rebuilds the guest in CI and fails if the
blob's SHA-256 differs from the committed one; (2) record the guest blob's SHA-256 as a constant and
assert it in a test, the way `Sp1StatefulBatchExecutor` asserts its executor digest; (3) make
`package-adapter-plugin.sh` either commit-verify or build into a staging copy rather than mutating
the tracked source tree.

### C4 — A successful fraud proof permanently kills the chain it just protected [E2]

`OpenWindow` refuses to re-arm a window that already exists, and the window key is written in one
place and deleted in one place:

```
OptimisticChallengeContract.cs:647-650   deadlineKey = DeadlineKey(…) → Assert(Get == null, "window already open") → Put
OptimisticChallengeContract.cs:781-782   the only Storage.Delete of deadlineKey + sequencerKey, inside FinalizeIfPastWindow
```

`grep -n "Storage.Delete" contracts/NeoHub.OptimisticChallenge/OptimisticChallengeContract.cs`
returns three hits: `:446` (an approved-verifier entry) and `:781-782`. And `FinalizeIfPastWindow` is
unreachable once a challenge succeeds, because `:776-777` asserts
`AcceptedFraudKey == null` with the message `"batch was challenged; cannot finalize"`.

The accept path of `Challenge` (`:722-762`) writes `AcceptedFraudKey` (`:737`), consumes the claim
(`:738`), calls `revertBatch`, slashes the bond and emits `OnChallengeAccepted` — and never deletes
`deadlineKey` or `SequencerKey`. So after one legitimate, correctly-verified fraud proof:

1. `SettlementManager.RevertBatch:542` → `RevertBatchCore` marks the slot `StatusReverted` (`:669`)
   and rewinds `latestFinalizedBatch` (`:660`).
2. `SubmitBatch:337-341` explicitly invites a corrected resubmit of that slot — its own comment says
   "the chain is never permanently wedged by a revert".
3. The resubmit is optimistic, so `SubmitBatch:391-395` calls `openWindow` for the same
   `(chainId, batchNumber)` — and `:648` faults `"window already open"`. Because
   `:335` requires `batchNumber == latest + 1`, no other slot is reachable either.

The chain cannot advance again. The revert-recovery path the settlement contract advertises is
unreachable on every optimistic chain, and no admin path in the challenge contract can clear the
stale window, so the practical recovery is "register a new chain".

Why this is [E2] and not [E1]: the mechanism is four assertions I read end to end across two
contracts, but no test executes the sequence. The repro is three steps in
`UT_OptimisticChallenge_Vm` — submit optimistic, challenge successfully, resubmit the corrected batch
— and that missing test is a large part of why this shipped.

Fix: delete `deadlineKey` and `SequencerKey` at the end of a successful `Challenge`. The
accepted-fraud marker is the durable record and is alone sufficient to keep `FinalizeIfPastWindow`
closed, so clearing the window costs nothing semantically; the alternative (letting `OpenWindow`
overwrite an expired window) is weaker, because it would also re-open the finalize path for a batch
that was *not* challenged but merely left un-finalized.

Coupling to `H18` is what makes this urgent rather than theoretical. The deployer registers only the
ZK verifier today, so an optimistic chain cannot submit at all and the wedge is masked by a
higher-priority failure. The moment someone repairs `H18` by registering the optimistic verifier —
the fix §10 recommends — this becomes live on the template the CLI calls "the safe default". Fix
`C4` first, then `H18`, and land the resubmit test with both.

**Status — fixed on this branch, and re-tiered to [E1].** `Challenge` now deletes `deadlineKey` and
`SequencerKey` in the same state-transition block that writes the accepted-fraud marker
(`OptimisticChallengeContract.cs:744-745`), before the external `revertBatch`. Three VM tests cover
the change and the two rails it must not weaken:
`Challenge_AcceptedProof_ConsumesWindow_SoResubmitCanReArm`,
`Challenge_AcceptedProof_ReArmedWindow_StillRejectsSecondChallenge` (a distinct `claimId` so the
batch-level `"already accepted"` guard is the decider, not the earlier claim guard) and
`Challenge_AcceptedProof_ReArmedWindow_StillCannotFinalize` (the accepted marker still closes
`FinalizeIfPastWindow` behind the fresh window, which is what separates this fix from the weaker
"let `OpenWindow` overwrite" alternative).

The negative control is what earns the tier: with the contract source reverted and its NEF re-emitted
by the pinned `nccs` 3.9.1, all three fail with `ABORTMSG is executed. Reason: window already open`.
That is the wedge executed on-chain rather than inferred from four assertions, so the [E2] marking
above stands as the audit-time state and is retired here. With the fix in,
`tests/NeoHub.Contracts.VmTests` is 571/571, the fresh-manifest gate passes 19/19 under
`NEO_N4_REQUIRE_FRESH_MANIFESTS=1`, and the full solution is 2,893 tests with 0 failed and 45 skipped.
None of the 45 came from this fix, and 40 of them were Windows-only: the §5 V4
evidence-file walk (27 in `Neo.Plugins.L2Settlement`, 9 in `Neo.L2.IntegrationTests`, 4 in four other
projects). The other 5 are platform-independent env gates, named in §11. This paragraph first
labelled those 9 `IntegrationTests` skips "env gates", which was wrong — see §8 item 14. `V4` was
repaired the same day; the same full-solution run is now 2,893 tests, 0 failed, 5 skipped.

What still does not exist is a two-real-contract test: `UT_SettlementManager_Vm.cs:185` wires
`OptimisticChallenge` as a mock, so `SubmitBatch`'s own resubmit branch is exercised only through the
`OpenWindow` call these tests make directly. The cross-contract seam that the finding names in
`SubmitBatch:391-395` therefore remains [E2].

## 4. High

### H14 — `panic = "abort"` in both profiles makes every FFI panic boundary dead code [E1]

`external/neo-riscv-vm/Cargo.toml:42-48` sets `panic = "abort"` under **both** `[profile.release]`
and `[profile.dev]`. `crates/neo-riscv-host/src/ffi.rs` guards the host callback surface with ten
`std::panic::catch_unwind(AssertUnwindSafe(…))` arms — `:682`, `:756`, `:859`, `:968`, `:1073`,
`:1159`, `:1216`, `:1329`, `:1416`, `:1492` — each shaped to convert a Rust panic into a `FAULT`
receipt returned to Neo. Under `abort` there is no unwinding to catch: the process dies at the first
panic in any of those ten arms. The dev profile matters as much as release, because it is what a
local run and most CI-adjacent debugging use.

This is the Rust-side twin of H1: an ordinary fault inside the execution core becomes sequencer
outage rather than a rejected block. Fix: drop `panic = "abort"` (measure the unwind cost; if it
matters, keep `abort` only for the guest crate, which has no FFI boundary to cross), or replace the
ten arms with an explicit `extern "C"` catch surface that cannot be configured away by a profile.

### H15 — Every block in a batch executes with the same `Runtime.Block.Index` — an L1 height — and the frozen first-block timestamp [E1]

`BatchSealer.SealBatch` builds exactly one context per batch:

```
src/Neo.Plugins.L2Batch/BatchSealer.cs:387-394
    builder.WithBlockContext(new BatchBlockContext {
        L1FinalizedHeight = _l1FinalizedHeight?.Invoke() ?? 0,
        FirstBlockTimestamp = _firstBlockTimestamp, … });
```

With `MaxBlocksPerBatch = 50` (`src/Neo.Plugins.L2Batch/Settings.cs:33`), both executors then map
that one context onto the persisting block header for every block it executes:

```
src/Neo.L2.Executor/ApplicationEngineTransactionExecutor.cs:237-238   Index = ctx.L1FinalizedHeight
                                                                      Timestamp = ctx.FirstBlockTimestamp
src/Neo.L2.Executor.RiscV/RiscVHostExecutionContext.cs:605-606        (identical mapping)
```

`Runtime.Block.Index` therefore reports an **L1** finalized height as the L2 block index, and
`Runtime.Time` is the batch's first block timestamp for all 50 blocks. The two executors agree, so
there is no proposer/settlement divergence *today*, and the divergence stays latent only because no
in-scope consumer hashes those header fields (`L2NativeContracts.cs` reads neither
`Runtime.Time` nor `CurrentIndex`).

The finding is that the seam is uncomposed and the code contradicts its own safety comment:
`ApplicationEngineTransactionExecutor.cs:227-230` states "for L2 chains, the L2 block height +
timestamp drive contract behavior, not L1's" and then assigns the L1 height. The first
time- or height-sensitive system contract, or any consumer of the persisted header, turns this into
a consensus split. Fix: thread the per-block index and timestamp into execution (the batch plugin
already has both — `L2BatchPlugin.cs:501-505` passes them to `ProcessCommittedBlock`), and pin the
mapping with a test on both executors.

### H16 — Pausing a chain does not stop finalization: `isActive` is consulted in one of the two mutation paths [E1]

Delta to H13, which covered the `EmergencyManager` global flag. The *per-chain* pause has the same
hole and a narrower blast radius but a more convincing doc-argument:

- `SettlementManager.SubmitBatch:330-331` calls `ChainRegistry.isActive` and asserts it.
- `SettlementManager.FinalizeBatch:479-533` — the function that records the canonical state root,
  advances `latestFinalized`, updates the gateway watermark and emits `BatchFinalized` — never reads
  it. `isActive` occurs exactly once in the file.
- `ChainRegistry.PauseChain:482-499` does nothing but flip that byte.

So the incident-response primitive that *is* wired (`RegisterPauser` + `PauseChain`, deployed at
`LiveDeployCommand.cs:801-802`) stops new submissions while every already-`Pending` or
`Challengeable` batch continues to finalize and roll forward the root that `SharedBridge` payouts
commit to. An operator who pauses a chain during an incident gets a UI state, not a halt. Fix:
assert `isActive` in `FinalizeBatch` (and `RevertBatch` stays callable while paused, or recovery is
impossible), and add the VM test that pauses then attempts both.

**Status — fixed on this branch, and re-tiered to [E1].** `FinalizeBatch` now asserts the same byte
`SubmitBatch` does (`SettlementManagerContract.cs:509-510`), reusing the `chainRegistry` handle the
function already loads for its security-level revalidation, so the pause costs one read-only
cross-contract call and adds no storage slot. `isActive` therefore occurs **twice** in the file as of
this change; the "exactly once" reading above stands as the audit-time state. `RevertBatch:551`
deliberately keeps no guard, which is the asymmetry the fix had to preserve — a paused chain must
still be revertible, or the one action that undoes a wrong root becomes impossible exactly when it is
needed.

The blast radius turned out to be smaller than the finding assumed, and that is worth recording
because it bounds the risk of the change. `finalizeBatch` has exactly one caller in-repo,
`OptimisticChallenge.FinalizeIfPastWindow:791`, and that caller deletes `deadlineKey` and
`SequencerKey` *before* the external call — so a fault rolls the deletion back atomically and the
finalization is simply retryable once `ResumeChain` runs. This is not a second `C4`: pausing cannot
strand a batch. `finalizeIfPastWindow` itself has no off-chain driver at all (zero hits under `src/`
and `tools/`), so the guard changes only the path the VM tests execute.

Two tests land with it: `FinalizeBatch_RejectsPausedChain` asserts the fault message rather than just
the exception type, then resumes and finalizes the same batch so the pause cannot be terminal; and
`RevertBatch_StillWorksOnPausedChain` pins the unguarded recovery path. The negative control
distinguishes them, and it is the reason the second test is worth having: with the contract source
reverted and its NEF re-emitted by the pinned `nccs` 3.9.1, `FinalizeBatch_RejectsPausedChain` fails
with `Expected exception of exact type TestException but no exception was thrown` — the paused chain
finalizing, executed rather than read — while the revert test passes on both builds, which is what a
guard-absence test must do. The regenerated artifact moved its NEF bytes and method offsets and left
the 103-entry ABI name set intact. `tests/NeoHub.Contracts.VmTests` is 573/573 with 0 skipped, and the
full solution is 2,895 tests, 0 failed, 5 skipped — §5 V4's 2,893 plus these two, with the skip count
unchanged, which is the arithmetic that confirms nothing else moved.

### H17 — The documented Gateway global-root path faults on every deployment the deployer produces [E1]

`MessageRouter.PublishGlobalRoot` refuses a *first* publication unless global-root governance is
locked:

```
contracts/NeoHub.MessageRouter/MessageRouterContract.cs:269-270
    ExecutionEngine.Assert(IsGlobalRootGovernanceLocked(), "global root governance not locked");
```

The only in-repo caller chain is operator-facing:
`SettlementManager.PublishGatewayGlobalRoot:778` → validates the constituent frontier →
`Contract.Call(messageRouter, "publishGlobalRoot", …)` at `:866-881`. `docs/launching-an-l2.md:1076`
instructs the operator to submit exactly that call.

`MessageRouter.LockGlobalRootGovernance` (`:338`) is owner-only and has no caller anywhere in the
product: zero hits for `GlobalRoot` in `tools/Neo.Hub.Deploy/*.cs`, none in `Neo.Stack.Cli`, and
`external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs` has no counterpart. It is exercised
only by VM tests that call it directly (`UT_MessageRouter_Vm.cs:118`, `:296`), which is exactly why
the gap is green. So Phase-5 cross-chain finality relay is inoperable as deployed unless an operator
knows to hand-issue an undocumented lock — and the deployer's smoke pass never notices, because it
does not attempt a global-root publication.

This is the same failure mode H12 fixed for the other trust roots: the lock exists, is correct, and
is not wired into the sequence an operator runs. Fix: order `LockGlobalRootGovernance` into
`LiveDeployCommand` next to the other locks, add it to the CLI plan text, and extend the smoke pass
to one end-to-end `PublishGatewayGlobalRoot`.

**Status — fixed on this branch, with one deliberate deviation from the suggested fix.** The
deployer now performs the three-call bootstrap as post-deploy steps `:894-900`, between
`SettlementManager.SetMessageRouter` (`:892`) and `ChainRegistry.LockGovernance` (`:901`):
`MessageRouter.SetGovernanceController` → `SetGlobalRootVerifier` → `LockGlobalRootGovernance`. Each
carries a read-back completion check (`getGovernanceController`, `getGlobalRootVerifier`,
`isGlobalRootGovernanceLocked`), so a re-run after a crash skips exactly the calls already applied
instead of re-issuing owner-signed transactions.

The order is not a style choice, it is enforced by the contract: `SetGovernanceController:329` and
`SetGlobalRootVerifier:315` both assert `!IsGlobalRootGovernanceLocked()`, while
`LockGlobalRootGovernance:341-343` asserts a non-zero controller *and* a configured verifier. Locking
first therefore faults, and locking without the controller faults with `"wire GovernanceController
before locking"`. This is the same sequence `UT_MessageRouter_Vm.cs:109-119` (`ConfigureAndLockGateway`)
already used in tests — the deployer had simply never been told about it.

The profile verifier is `Sp1Groth16Verifier`, not `ContractZkVerifier`, because
`PublishGlobalRoot:286-296` dispatches `verifyZkProof(byte,byte[],byte[],byte[])` and
`ContractZkVerifier` exposes a different ABI. `proofSystem` is SP1 (`1`) and the backend is `0xC2`,
the recursive Gateway backend `Sp1GatewayProofProver` stamps; `tools/Neo.Hub.Deploy` does not
reference the gateway plugin, so the literal is paired locally exactly as `ProofSystemSp1` and
`ProofTypeZk` already are.

Fixing the wiring surfaced a second gap: the profile cannot be derived in-repo. The Gateway guest
program's vkey and replay domain are operator-supplied at
`GatewayHostComposition.OpenSp1(chainDir, gatewayVk, signer, replayDomain, verificationKeyId, …)`
and persisted nowhere, so `deploy-testnet` gained two required switches — `--gateway-program-vkey`
and `--gateway-replay-domain` — that share the existing parsers (`ParseProgramVKey`,
`ParseRequiredReplayDomain`) with the SP1/fraud arguments, are written into the deployment report as
`gatewayProgramVKeyRaw` / `gatewayReplayDomain` / `gatewayAggregationBackend`, and are printed in raw
byte order so the operator can paste the same hex to the Gateway host. While documenting them, the
usage text also gained `--sp1-program-vkey`, which the tool had required since it was written without
ever listing it in `Program.cs`.

The deviation: the smoke pass does **not** attempt an end-to-end `PublishGatewayGlobalRoot`. That
would need a real SP1 recursive aggregate proof — a compiled guest ELF and a live prover in the
deploy path — so instead the smoke pass reads back the entire compared surface (`:984-989`): the
verifier hash, `getGlobalRootProofSystem`, `getGlobalRootAggregationBackend`,
`getGlobalRootVerificationKeyId`, `getGlobalRootReplayDomain`, and `isGlobalRootGovernanceLocked`.
Those are exactly the six values `PublishGlobalRoot:269-278` asserts before it dispatches, so a
Gateway host configured with the reported tuple can no longer fault on the governance gate. That half
is executed, not inferred: `UT_MessageRouter_Vm.PublishGlobalRoot_BindsEpochRootConstituentsBackendDomainAndProof:383`
locks the profile via `ConfigureAndLockGateway`, asserts `IsTrue` on a publication carrying the exact
registered tuple, and asserts a fault on each mismatched element of it. What remains uncovered by
deploy smoke is the *proof* itself — `verifyZkProof` on a genuine Gateway
aggregate — which is covered gateway-side (`Sp1GatewayProofProver` re-verification, VM tests), not
here. Recording that boundary matters: the six read-backs prove the profile is correct, not that a
publication will succeed.

`plan` emits the same three steps as hints (`ScaffoldPlan.cs`, guarded on MessageRouter +
GovernanceController + Sp1Groth16Verifier all existing) with `GATEWAY_PROGRAM_VKEY_REPLACE_ME` /
`GATEWAY_REPLAY_DOMAIN_REPLACE_ME` placeholders and a pointer to
`SetGlobalRootVerifierViaProposal` for post-lock rotation, so plan text and live sequence stay in one
order; `PostDeployActions` therefore went 41 → 44 and the positional tail of
`PostDeployActions_DefaultPlan_EmitsAllWiringHints` was renumbered with it.

Tests: two new parser tests (`ParseGatewayProgramVKey_*`, `ParseRequiredGatewayReplayDomain_*`)
assert the shared helpers fault naming the **Gateway** switch rather than the SP1 one, and the H12
"every wired surface is also locked" loop was generalized to accept MessageRouter's differently named
gate instead of exempting it. The ordering test pins the three new steps as contiguous and immediately
before `ChainRegistry.LockGovernance`, and asserts the `setGlobalRootVerifier` script byte-for-byte.
The negative smoke test adds six mismatched entries,
one of them the pass-through backend `0xFE`. `tests/Neo.Hub.Deploy.UnitTests` is 115/115 (113 before,
+2 parser tests) and `tests/NeoHub.Contracts.VmTests` stays 573/573 because no contract source changed.
Full-solution totals are in §10 item 4.

One measurement caveat worth keeping: the first full-solution run after this change reported a single
failure in `tests/Neo.L2.Proving.UnitTests` — `ProveAsync_TamperedExecutionSemantic_IsRejected`
expected `InvalidDataException` and caught `IOException` (`The process cannot access the file
'…f0712…proof.result.json' because it is being used by another process`) from
`AtomicFileQueueTransport.ReadBoundedPathAsync:265`. That project is untouched by this branch and the
test passes 3/3 when run alone. See §5 V7.

### H18 — The `rollup` template emits Optimistic commitments against a deployment that registers only the ZK verifier [E1]

`tools/Neo.Stack.Cli/Commands/TemplateCatalog.cs:30-36` makes `rollup` the **first** template —
`All[0]`, and `Resolve(string name)` falls back to it for an unknown name (`:63-64`) — with
`ProofType: "Optimistic"` and a tag line calling it "the safe default". Its ZK sibling is
`zk-rollup` (`:38`-`:41`). But `tools/Neo.Hub.Deploy/LiveDeployCommand.cs:36` declares only
`ProofTypeZk = 3` and `:833-834` is the sole `RegisterVerifier` call in the deployer, registering the
ZK verifier and nothing else. A chain created from the default template and deployed by the
documented deployer therefore submits batches whose proof type `VerifierRegistry` has no entry for,
and `submitBatch` faults at `VerifierRegistryContract.cs:256` (`"no verifier for proof type"`). The
operator sees a settlement-side rejection with no hint that the mismatch is template-vs-deployer.

Either the template should default to ZK, or the deployer should register the optimistic verifier
for chains declared optimistic, and `neo-stack` should cross-check the template's `ProofType`
against the deployment plan. Fix is small; the point is that the two tools disagree about the
default security posture of the flagship template.

**Status — fixed on this branch, and the root cause is not the template line.** Reconciling
`TemplateCatalog` against the deployer showed the accept rule existed in three layers, they were
copies of each other, and only one of them is the thing that actually enforces anything:

| Layer | Location (pre-fix) | Rule it encoded |
|---|---|---|
| On-chain authority | `SettlementManagerContract.IsProofTypeCompatible` — `private static`, body unchanged by this fix | `Sidechain`/`Settled ⇒ {Multisig, Optimistic, Zk}`; `Optimistic ⇒ {Optimistic, Zk}`; `Validity`/`Validium ⇒ {Zk}`; everything else `false` |
| Operator-status heuristic | `LocalHostOperatorStatus.cs` (pre-fix `:578-590`) | `Optimistic ⇒ {Optimistic, **Multisig**}`; `Sidechain`/`Settled ⇒ {**None**, Multisig}` |
| `neo-stack validate` | `ValidateChainConfigCommand.cs` (pre-fix `:94-114`) | the same two wrong rows as four per-level `if`s — and **no row naming `Settled` at all** |
| `doc.md` §3.2 | — | the rule was never written down, and the method was not in the interface list |

The two off-chain copies agreed with each other because one was copied from the other; they disagree
with the contract on exactly three pairs — `Optimistic+Multisig`, `Sidechain+None`, `Settled+None`.
All three are writable in a `chain.config.json`, all three passed `validate`, and all three fault at
`submitBatch` (`SettlementManagerContract.cs:370`) before the verifier is ever consulted. The
`Settled` gap is the sharper one: because the CLI tested `sec` with four independent `if`s, a
`Settled` chain matched none of them and validated silently clean at *any* proof type.

What this finding could not see from `TemplateCatalog.cs:32` alone: `Optimistic+Optimistic` is legal
on-chain, so **no correct version of the accept table would have warned about the flagship template
either.** The missing knowledge is a second, orthogonal axis — which routes a deployment actually
registers — and no layer in the repo encoded it. The real defect was therefore two tables that
disagreed about legality, *and* one legal-but-unserved axis nobody tracked.

The fix is structural, in four parts.

1. **Ask the authority instead of copying it.** `IsProofTypeCompatible` is now
   `[Safe] public static` (`SettlementManagerContract.cs:403-425`). Its body is untouched, so no
   settlement behavior moved; what changed is reachability. The same function is the enforcement
   point at `:370` (`submitBatch`) and `:523` (`finalizeBatch`), which is what makes it the correct
   thing to expose. Contract artifacts re-emitted with the pinned nccs.
2. **One off-chain mirror.** `src/Neo.L2.Abstractions/Models/ProofRouting.cs` is now the only
   `SecurityLevel ⇒ ProofType` table outside the contract: `AcceptedProofTypes` /
   `AcceptsProofType` (`:39-51`) for the legality axis, and `ProductionVerifierRoutes` /
   `HasProductionVerifierRoute` (`:29`, `:53`) for the registration axis. Both hand-written tables are
   deleted; `LocalHostOperatorStatus.IsSecurityLevelPairedWithProofType` (`:584`) and
   `ValidateChainConfigCommand` (`:100-108`) delegate to it. `validate` now emits two distinct
   warnings: `accepts only proofType=…` for a pair the contract rejects, and
   `… has no verifier route in the shipped production bundle` for a legal pair the deployer freezes
   without a route.
3. **A third reference neither implementation can see.** `tests/Shared/ProofRoutingExpectations.cs`
   is compiled into *every* test assembly via `tests/Directory.Build.props`, and it is the table both
   sides are checked against — `UT_SettlementManager_ProofRouting` pushes all 36 pairs (6 levels
   including the out-of-range `5`, 6 proof types including `4` and `255`) through the deployed NEF,
   and `UT_ProofRouting` checks `ProofRouting` against the same list. Editing either the contract or
   the mirror now fails a test that references neither. It is a compiled-in file rather than a project
   reference because `NeoHub.Contracts.VmTests.csproj` deliberately holds no `<ProjectReference>` at
   all — adding `Neo.L2.Abstractions` would resolve `$(NeoCorePath)\Neo\Neo.csproj` alongside
   `Neo.SmartContract.Testing`'s own `Neo`.
4. **Templates and samples now name routes that exist.** `rollup` emits `Zk` under the `Optimistic`
   floor (over-delivery is legal and is the only pairing the shipped hub can both accept and verify);
   `sidechain` emits `Multisig`, never `None` — `VerifierRegistry.WriteVerifier` rejects proof type
   `0` (`VerifierRegistryContract.cs:233`) and `ProofWitnessSerializers` refuses to build a `None`
   artifact, so a `None` config cannot produce a batch anywhere. `sidechain` / `privacy-sidechain` are
   shipped as the documented legal-but-unserved case, `samples/README.md` says so, and
   `ShippedConfigWarningPolicy` makes the three shipped-config guards (per-template `create-chain`,
   per-template `new-l2`, and the `samples/*.config.json` walk) assert one class-aware policy instead
   of three copies of "zero `⚠`". `UT_ListTemplatesCommand` adds the catalog-level version: every
   template's pair must be legal, and every non-sidechain template must name a route the deployer
   registers.

The same drift existed in prose, and four published documents were corrected: `docs/launching-an-l2.md`
and its Chinese mirror both advertised `rollup = L1 DA + Optimistic` and `sidechain = External + None`
after the code said otherwise (and their "Optimistic-rollup operators" lead rested on a premise the
shipped template no longer satisfies), `doc.md` §3.2 never stated the accept rule at all, `doc.md:169`
contradicted its own §12 on the `SecurityLevel` numbering, and
`docs/zh/specification/08-neohub-contracts.md` listed `ProofType.Gateway` as a registerable route
although `VerifierRegistry.WriteVerifier` rejects it. Because a corrected table is only as durable as
the test that holds it, both published template tables are now parsed and compared cell-by-cell against
`TemplateCatalog` by `LaunchingGuide_TemplateTable_MatchesTheCatalog` and its Chinese twin.

Test fixtures that paired `Multisig` with `securityLevel: "Optimistic"` — five of them across
`UT_E2E_HostComposition_FromDeployReport` and `UT_MultisigLocalHostComposition` — described a chain
`SettlementManager` would fault. They are corrected to `Settled`, whose definition is exactly "batches
commit to L1 but no fraud or validity proof is checked", rather than the table being loosened to
accommodate them.

**What this does not fix, stated so it is not overread.** The two routes named by `doc.md` §7.5 stages
0/1 remain unimplemented on L1: of the 26 `contracts/NeoHub.*` projects, exactly one implements the
registry's `verify(commitmentBytes)` interface (`ContractZkVerifierContract.cs:302`), so `Multisig`
and `Optimistic` are operator-supplied routes by construction and the `sidechain` caveat is accurate,
not cosmetic. The honest closure is to implement those verifiers and register them in
`LiveDeployCommand` *before* `lockGovernance` (`:866-869`); when that lands,
`ShippedConfigWarningPolicy` is the tripwire that says delete the caveat. Also left as found:
`SecurityLevel.Settled` is legal at four pairs but no shipped template emits it. The devnet half of
this list closed after the audit: `tools/Neo.L2.Devnet` now reads the config's `proofType` (a missing
or malformed value falls back to the floor route `ProofRouting.AcceptedProofTypes` allows for the
label), aborts an incompatible pairing with exit code 2, and wires the matching prover — committee
attestations for `Multisig`, a sequencer-signed optimistic payload for `Optimistic`, and the disclosed
`MockRiscVProver` preview for `Zk` (`Program.cs:86`, `:132`, `:430`, `:448`). The default run itself
changed route, and that is the finding's own lesson applied to the tool: the devnet's no-config label
is `Optimistic`, under which the old hardcoded `Multisig` was never an acceptable commitment.

**Left alone on evidence, not assumption.** The DA-mode twin
`LocalHostOperatorStatus.IsSecurityLevelPairedWithDaMode` (`:591-599`) was re-derived against
`SettlementManager.AssertSecurityConfigurationCompatible` (`:427-441`) and `ChainRegistry`'s
registration check (`:594-595`): the `Validity ⇒ L1` and `Validium ⇒ ¬L1` rows match the contract
byte for byte, levels `Sidechain`/`Settled`/`Optimistic` are unconstrained on-chain and return `true`
off-chain, and `DAMode.Local = byte.MaxValue` — which the heuristic's default arm accepts — cannot
reach a batch because both `ChainRegistry` and `SettlementManager` cap `daMode` at `3`. It is a
different rule with a different domain, and it was already correct.

**Verification on this branch.** `dotnet test Neo.L2.sln -p:NuGetAudit=false`: **38 assemblies,
2,916 passed, 0 failed, 5 skipped** = 2,921 total (baseline 2,910). The +11 are eleven new
`[TestMethod]` methods, with no `DataRow` expansion changed: `UT_ProofRouting` ×4, the 36-pair
`UT_SettlementManager_ProofRouting` ×1, two new catalog guards plus two new
`launching-an-l2.md`-table guards in `UT_ListTemplatesCommand`, and two new warning-class tests in
`UT_ValidateChainConfigCommand`. `NeoHub.Contracts.VmTests` (585, was 584) runs the deployed NEF, so
the 36-pair check exercises compiled bytecode rather than a C# re-read of the source.
`Neo.Plugins.L2Settlement.UnitTests` (168) and `Neo.L2.IntegrationTests` (40) cover the re-paired
fixtures; `Neo.Stack.Cli.UnitTests` (195) covers the templates, the samples walk, both warning
classes, and both published tables. `NEO_N4_REQUIRE_FRESH_MANIFESTS=1 dotnet test
tests/Neo.Hub.Deploy.UnitTests` passes 115/115, so the re-emitted `SettlementManager` NEF/manifest
satisfies the authoritative artifact-freshness gate. The doc-table guard was negative-controlled:
drifting one `proofType` cell in the English table produced exactly one failure
(`rollup: proofType column disagrees with TemplateCatalog / expected: "Zk" / actual: "Optimistic"`)
with the Chinese guard unaffected, and the file was then restored byte-identically.
`dotnet format --verify-no-changes` clean.

### H19 — The anti-censorship deadline is bounded on the owner path and unbounded on the deploy path [E2]

`ForcedInclusion` stores one global `deadlineSeconds` and reads it when stamping each entry. The excerpt
below quotes the pre-fix source at its pre-fix line numbers; every citation in the prose that follows
has been re-resolved against the fixed file.

```
ForcedInclusionContract.cs:130-133   _deploy: deadline = (uint)(BigInteger)arr[2]; Assert(deadline > 0, "deadline must be positive")
ForcedInclusionContract.cs:192-199   SetDeadlineSeconds: Assert(seconds >= 60 && seconds <= 86400, "deadline out of bounds [60, 86400]")
ForcedInclusionContract.cs:373-374   enqueuedAt = (uint)(Runtime.Time / 1000u); EncodeEntry(…, enqueuedAt + deadline)
```

The same value is therefore range-checked when the owner changes it and unchecked when the deployer
sets it. Two consequences follow from the deploy-time field alone, and which one applies depends on
how NCCS compiles the `uint` addition at `:389` — this pass could not determine that from source, so
it is stated as a hazard requiring a VM test, not as a proven behavior:

- If the arithmetic saturates or the value is simply large, the censorship window is effectively
  never reached, and the whole point of `ReportCensorship` — the §17 escape hatch that lets anyone
  prove a sequencer is ignoring the L1 queue and pause the chain (`:511` `if (nowSec < deadline)
  return false;` then `:518` `pauseChain`) — is inert on a chain that was deployed with a mistyped
  deadline. One `uint` field in the deploy data silently disables the anti-censorship guarantee, and
  `IsProductionReady()` does not check it (`:269-281` omits the pauser and the deadline bound).
- If the addition wraps mod 2³², the stored per-entry deadline lands in the past, every entry is
  instantly reportable, and a permissionless caller can `pauseChain` at will.

Fix both directions with the same one-line change: apply the `[60, 86400]` bound in `_deploy` that
`SetDeadlineSeconds` already enforces, and add the VM test that pins the overflow behavior.

What is genuinely sound here, and worth stating so the finding is not overread: an entry's deadline is
immutable once written. The nonce strictly increments (`:384-385`), there is exactly one `Put` per
enqueue and no update path anywhere in the contract, so a censoring sequencer **cannot** postpone a
forced inclusion by renewing its deadline — the classic escape from this design is absent.
`ReportCensorship` is likewise one-shot per entry (`:513` sets a `reportedKey`), and the comparison at
`:511` is inclusive at equality, which is the correct direction for a deadline.

**Status — fixed on this branch, and the question the finding refused to guess is now closed.**
`_deploy` applies the same `[60, 86400]` window `SetDeadlineSeconds` already enforced
(`ForcedInclusionContract.cs:140-146`), and both guards now read one pair of constants
(`MinDeadlineSeconds` / `MaxDeadlineSeconds`, `:70` and `:73`), so the two sites cannot drift apart
again.

The VM test settled the arithmetic by measurement, not by reading NCCS: **the value truncates mod 2³²
and the VM halts.** A control enqueue at the legal maximum deadline stores `1,468,681,716`; the same
enqueue advanced by `4,294,880,900` s stores `1,468,595,320`; the difference between the two stored
fields is exactly the advance, which is only possible if both `(uint)(Runtime.Time / 1000u)` (`:388`)
and `enqueuedAt + deadline` (`:389`) truncate rather than check.
`EnqueueDeadlineSum_TruncatesModuloTwoTo32InsteadOfFaulting` pins that relation.

So both hazard branches above are live, split by magnitude rather than by which instruction NCCS
emits: an out-of-window value that still fits (`100,000,000`) leaves the censorship window effectively
unreachable and `ReportCensorship` inert, while one that does not (`3,000,000,000`) wraps the stored
deadline back into 1984, makes every entry instantly reportable, and hands a permissionless
`pauseChain` to the first caller. The same truncation is why the upper bound is what keeps the whole
problem out of reach: with `deadline ≤ 86400`, `enqueuedAt + deadline < 2³²` holds for the entire
pre-2106 life of a chain, so no further change is warranted and the test exists to document the
residual rather than to gate it.

Not a breaking change for anything in the tree, because nobody supplies the third deploy element. The
deployer passes two (`resolvedDeployData: ["OWNER_REPLACE_ME", <settlement manager>]` in
`artifacts/local-deployment-rehearsal/*/hub/deploy-bundle.json`), so `DefaultDeadlineSeconds = 7200`
(`:63`) applies — inside the window — and no `.json` template or C# caller passes a deadline at all.

Negative control, so the [E1] claim is real: reverting *only* the tracked artifact
(`TestingArtifacts/NeoHubForcedInclusion.artifacts.cs`) to its pre-fix NEF makes the new deploy test
fail naming deadline `1`. The guard therefore executes on-chain, not in C#. Re-emission changed
exactly two lines (the `Manifest` and `Nef` properties) and the ABI name set is identical to `HEAD`'s.

`UT_ForcedInclusion_Vm` 17/17 (was 15), `NeoHub.Contracts.VmTests` 575/575, full solution
38 assemblies / 2,899 tests / 0 failed / 5 skipped (the H17 run's 2,897 plus these two).

## 5. Verification-integrity findings

These are the ones that decide whether any other finding can be trusted.

### V1 — The SP1 required check goes green *because* the heavy lanes did not run [E1]

```
.github/workflows/build.yml:394-396   sp1-release-gates: if: github.event_name == 'workflow_dispatch'
build.yml:527                          cargo test --workspace --release
build.yml:529                          cargo test (neo-zkvm-host, real proof)
build.yml:541                          gateway-host recursive proof
build.yml:574-578                      if dispatch → test …= success; else test …= skipped
```

`sp1-release-gates` — the only job that compiles the workspace and produces real batch and recursive
SP1 proofs, including the tamper gates — is `workflow_dispatch`-only. The aggregate job `sp1-host`,
which `master` branch protection requires, asserts on every non-dispatch event that the result
equals `skipped`. The 2026-08-29 report noted this only in passing (§10's "CI's Linux-only
`sp1-release-gates`"); it is understated. On PRs and on pushes to `master`, the SP1 proving stack is
not merely unobserved, it is *required to be absent* for the required check to pass, so a real
regression in `bridge/neo-zkvm-host` cannot redden anything an author sees. Nightly or
merge-queue-scheduled dispatch would keep the resource envelope while making the assertion
meaningful.

This pass produced a live instance rather than a read-only inference. PR #52's head `20d7ce80`
(workflow run `33301282516`, merged to `master` as `6116d659` on 2026-08-30) reported all 14 required
contexts `completed/success` — including `SP1 compatibility and manual release proof gate`, check-run
`99230673595` — inside the same run that recorded the heavy lane as `skipped`, `matrix.name`, check-run
`99229821429`. That PR touched `contracts/`, `tests/`, `tools/` and `src/` and re-emitted VM contract
artifacts; the SP1 execution and proof stack was not exercised by any check that gated its merge, and
the required check that appears to cover it passed *because* of that absence.

One audit-trail fact, recorded because this report would be inconsistent if it skipped it: that merge
reached `master` with zero approvals. `master` sets `enforce_admins: true`, so an admin merge is
refused outright ("New changes require approval from someone other than the last pusher") and the
setting had to be toggled off and immediately back on for it to land. No `V1`..`V6` finding was
implicated in that decision, and nothing about the toggle is a code defect — but a release path whose
only review control can be removed in one API call by the same identity that pushes to it is the same
class of gate-that-checks-the-wrong-thing this section is about, and §10's remediation order should
value it accordingly.

**Status — decided and wired on this branch (2026-08-31): the nightly schedule owns the SP1
dispatch, and the release checklist carries the blocking rule.** `build.yml` gains a nightly
`schedule` trigger (cron `47 3 * * *`, staggered from `sdk-conformance`'s `37 3`), and the two
places that keyed on `workflow_dispatch` alone now accept `schedule` the same way — the
`sp1-release-gates` job's `if`, and the `sp1-host` aggregate's success assertion — following the
precedent `sdk-conformance.yml:88` already set. On ordinary PRs and `master` pushes the envelope is
unchanged: the heavy lanes still report `skipped`, which is still what the required check asserts.
What changes is that the assertion is now exercised nightly: a regression in `bridge/neo-zkvm-host`
or the Gateway recursion reddens the scheduled run within a day, with `sp1-host` itself failing —
the required context no longer passes *because* the heavy lanes were absent. The release-blocking
half of the decision lives in `docs/release-readiness-checklist.md` §6 (EN and zh): a failed or
never-completed nightly blocks a release until a manual dispatch on the exact release-candidate
commit passes all three lanes — the nightly makes the failure visible, and the green dispatch on
the release commit is what releases it. A merge-queue-owned dispatch was rejected: this repository
does not use merge queues, and per-PR heavy-lane runs would multiply the resource cost the finding
explicitly wanted kept.

### V2 — The "off-chain ↔ on-chain encodings are paired" invariant has no cross-boundary test [E1]

`tests/NeoHub.Contracts.VmTests/NeoHub.Contracts.VmTests.csproj` references
`Neo.SmartContract.Testing`, MSTest and the test SDK — and **zero `ProjectReference`s**. The VM
tests therefore cannot call `BatchSerializer`, `MessageHasher`, `MerkleProofSerializer` or
`L2ChainConfigSerializer`; they hand-roll byte buffers (`UT_SettlementManager_Vm.cs:70-122`) and
re-hardcode constants (`UT_ChainRegistry_Vm.cs` repeats `ConfigSize = 91`). The five pairings that
matter were checked by hand and are byte-exact — 321-byte commitment header, 332-byte public inputs,
48+32N proof framing, withdrawal leaf hash, 91-byte chain config — but **each side was pinned only
against its own copy of the layout, and no test ever fed an encoder's bytes to a deployed contract**.
`UT_BatchSerializer` checks the encoders against the encoders' own documented offsets, the contract
side was exercised only through hand-rolled buffers nothing compared to encoder output, and even the
encoder-side pins are partial: `PublicInputs_ByteLayout_MatchesDocumentedOffsets` covers offsets 0/4,
`12..44` and `300..332`, leaving eight of the ten mid-buffer roots unpinned, and
`UT_MerkleProofSerializer.Encode_LayoutMatchesSpec` pins the length plus offsets 32 and 44. The two
closest things to a cross-check are not one: `UT_MerkleProofDecoder` pairs the serializer with the
CLI decoder (two off-chain sides), and `UT_Mvp_Phase3_RestrictedFraudProofV4.cs:95-102` does call
`BatchSerializer.Encode` but hands the bytes to the *off-chain* v4 verifier.
`UT_OnChainMerkleVerifyParity.cs`, the other candidate, is a C# replica of the contract's fold rather
than the contract.

Along with it, one doc claim is false: `src/Neo.L2.Batch/BatchSerializer.cs:12-14` says the encoder
produces "the byte format that the settlement contract reads", which holds for the commitment header
and not for the public-inputs half, which is never transmitted. This is the mechanism by which
`C2`-class encoding drift stays invisible.

**Status — both halves are now closed: the documentation half on the earlier branch, the
cross-boundary test half on this one.** What is left is a different boundary, and §11's first bullet
says which.

*BatchSerializer.* `:12-14` now separates the two boundaries instead of collapsing them: the
commitment header is `SettlementManager.submitBatch`'s ABI, while the 348-byte public-inputs form
(since the block-range binding on this branch — §6's last item — grew the preimage from 332 bytes)
never reaches L1 — the contract sees only its digest at commitment offset 284 — yet it is still the
signed preimage (`src/Neo.L2.Proving/Attestation/AttestationProver.cs:36-40`,
`src/Neo.L2.Proving/Optimistic/OptimisticProver.cs:81-83`), the digest recorded in each durable
artifact (`src/Neo.L2.Persistence/ProofWitnessStore.cs:1090-1091`), the check that gates execution
(`src/Neo.L2.Executor/Witness/Sp1StatefulBatchExecutor.cs:271-272`) and the buffer the Rust side
rebuilds (`bridge/neo-execution-core/src/hashing.rs:297`). Each citation was re-read at those line
numbers on this branch rather than carried over.

*The `ChainMode` claim, which §10 item 9 asked about.* **The docs were wrong, and the enum was
right.** Three pieces of evidence close the "or add the enum member" alternative: `doc.md` §6
(476-486) lists exactly the four declared members; `doc.md:1343` selects the engine with
`--vm neovm2-riscv` *alongside* `--template rollup`, so the spec itself treats VM and mode as
independent axes; and `ChainMode` has no byte in the 91-byte `L2ChainConfigSerializer` format
(offsets 84-90 are securityLevel/daMode/gatewayEnabled/permissionlessExit/sequencerModel/exitModel/
active) with `neo-stack validate` as its only consumer. A fifth member would therefore have wired
nothing while making the label look like a dispatch key. Fixed instead: ten documentation sites
(five English + five Chinese mirrors: `AGENTS.md`, `WHITEPAPER.md`,
`docs/architecture-{l2-lifecycle,walkthrough}.md`, `docs/tech-stack-coverage.md` and their
`docs/zh/` counterparts) now say the PolkaVM profile is selected by the devnet's `--executor riscv`
(`tools/Neo.L2.Devnet/DevnetArgs.cs:61-76`) and labelled `vm: "neovm2-riscv"`;
`ChainMode`'s own `<summary>`, which asserted it "drives consensus, batching, settlement, and DA
behavior", now says it is an operator-facing label that dispatches nothing; and
`tests/Neo.Stack.Cli.UnitTests/UT_BootstrapGenesisCommand.cs:36`'s fixture — which carried
`"chainMode": "L2RiscV"` and passed only because nothing parses that key on the bootstrap path —
carries `L2RollupMode`, the value the same `zk-rollup` template ships.

Two guards replace the copy-paste discipline that let it drift.
`CurrentDocumentation_NamesOnlyDeclaredChainModeMembers` scans every tracked
`.md`/`.cs`/`.json`/`.yaml`/`.yml`/`.toml` file for both spellings (`ChainMode.<Member>` and
`"chainMode": "<value>"`) and rejects any label outside `Enum.GetNames<ChainMode>()`, exempting only
dated narrative and evidence (`docs/audit/**`, `CHANGELOG.md`, `TASKS.md`) whose stale labels are
deliberate quotes rather than claims about today's tree; it caught its own comment on the first run,
and a control line added to `README.md` produced
`README.md:471 ChainMode.L2RiscV` + `README.md:471 "chainMode": "L2RiscV"` before the file was
restored byte-identical. `Catalog_EveryTemplateNameADeclaredChainMode` pins the four
`TemplateCatalog` `ChainMode` strings, the one catalog field no earlier guard parsed.

*The cross-boundary test.* `NeoHub.Contracts.VmTests` still has zero `ProjectReference`s and still
cannot get one: the finding text above proposed "a single test project that references both sides",
and that project cannot exist, because `Neo.SmartContract.Testing` brings its own `Neo` assembly and
a `ProjectReference` to `Neo.L2.Batch` would resolve `$(NeoCorePath)\Neo\Neo.csproj` beside it. The
lock therefore runs through **data neither side owns** instead of through a shared binary — the
`tests/Shared/CanonicalEncodingVectors.cs` pattern `H18` already established with
`ProofRoutingExpectations.cs`, which `tests/Directory.Build.props` compiles into every test assembly.

`CanonicalEncodingVectors` carries golden bytes for all four boundary formats (321-byte commitment
header, 348-byte public inputs, 91-byte chain config, 48+32·N proof framing) plus a five-leaf
withdrawal tree with per-leaf siblings, so the header layout and the Merkle fold bind each other. The
bytes were generated by a throwaway third implementation in a language neither side uses — not by
running the C# encoders — so they are a spec, not a snapshot of the code under test. Every 32-byte
field carries a distinct fill, and `firstBlock`/`lastBlock` deliberately differ from `batchNumber`,
because every hand-rolled header in the repo sets all three equal.

- `tests/Neo.L2.IntegrationTests/UT_CanonicalEncodingParity.cs` (12 tests) pins each **encoder** to
  the vectors: `BatchSerializer.Encode` and `EncodePublicInputs` must reproduce them byte for byte,
  `Decode` of the vector must yield the documented model, `publicInputHash` at offset 284 must be
  `Hash256` of the public-inputs vector, `L2ChainConfigSerializer.Decode` must round-trip it, and
  each of the seven single-byte config fields must move exactly one byte at its own offset.
- `tests/NeoHub.Contracts.VmTests/UT_CanonicalEncodingParity_Vm.cs` (8 tests) pins the **deployed
  NEF** to the same vectors through this assembly's own copy of the offset table — one of **seven**
  places the 321-byte header layout is restated (`SettlementManagerContract.cs:42-53`,
  `RestrictedExecutionFraudVerifierContract.cs:101-106`, `ContractZkVerifierContract.cs:41-44`,
  `RestrictedFraudProofV4.cs:513-518`, `BatchSerializer.cs:27-46` as a doc table plus sequential
  writes, and two test copies). Through that table: real
  `ChainRegistry.registerChain` must read every semantic byte of the golden config; real
  `SettlementManager.submitBatch` → `finalizeBatch` must settle the golden commitment and return its
  roots from `GetCanonicalStateRoot`, `GetFinalizedTxRoot`, `GetL2ToL1MessageRoot` and
  `GetL2ToL2MessageRoot`; both on-chain Merkle folds must accept all five leaves and reject a
  tampered sibling, a wrong `leafIndex` and an unknown batch; and both of `RegisterChainPublic`'s
  never-executed admission branches must behave — the semi-permissionless one has to ask governance
  about the slots the serializer writes, the permissioned one has to reject and persist nothing.
- A Rust crate can take no reference to a .NET project at all, so the third leg runs through the same
  bytes as **data**: `tests/Shared/canonical_encoding_vectors.hex` exports the vectors,
  `SharedHexExport_MatchesTheVectors` pins that export to `CanonicalEncodingVectors` field by field
  (and fails if the file declares a key no assertion reads), and
  `bridge/neo-execution-core/tests/canonical_encoding_parity.rs` (3 tests) `include_str!`s it and
  asserts what no test had ever compared to anything outside Rust — that the twelve-parameter
  `hash_public_inputs` (`src/hashing.rs:283-314`) concatenates in the order
  `EncodePublicInputs` writes, and that `merkle_root` (`:36-54`) folds the five withdrawal leaves to
  the same root `MerkleTree` does. That is the repo's first cross-language vector held in one file:
  the three digests that already pair the languages are pasted twice, once in
  `native.rs::outbound_v1_roots_bind_native_abi_order_and_parameters` and again in
  `UT_CanonicalNativeExecutionAdapter.cs:88-99`
  (`OutboundV1_MatchesRustRootsAndBindsOrderAndParameters`), which holds only while neither copy is
  edited alone.

Six controls were run, and one of them falsified this finding's own wording:

1. Swapping `txRoot`/`receiptRoot` inside `BatchSerializer.Encode` **and** `Decode` did *not* pass
   silently — the pre-existing `UT_BatchSerializer.Commitment_ByteLayout_MatchesDocumentedOffsets`
   failed. Each side already had a self-pin; what no test did was feed one side's bytes to the other.
   §5's opening paragraph and §8 item 15 now say that, replacing both "round-trip tests stay green"
   and the broader "nothing executed both sides of a pairing".
2. Swapping the same two roots in the shared vector failed `BatchSerializer_Commitment_MatchesGoldenVector`
   and `BatchSerializer_DecodeOfGoldenVector_KeepsEveryField` off-chain *and*
   `HandRolledBuilders_MatchGoldenVectors` in the VM assembly — the vectors are a live hinge on both
   sides, not a comment both sides ignore.
3. Moving `OffTxRoot`/`OffReceiptRoot` in the VM assembly's own table — the position that stands in
   for a contract-side edit — made `SettlementManager_SettlesTheGoldenCommitmentAndKeepsItsRoots`
   abort inside the contract with its own `publicInputHash not bound to commitment roots`, and took
   the withdrawal-fold test down with it. The contract's constants are pinned by executing the
   contract.
4. `SettlementManager_RejectsTheGoldenCommitmentWhenOneRootOffsetMoves` keeps that control as a
   permanent test rather than a one-off: it submits the golden header with the two roots exchanged
   and requires the fault.
5. Changing **one byte** of the shared export (`tx_root`'s first pair, `03` → `05`) failed both
   languages at once: `SharedHexExport_MatchesTheVectors` reported
   `export.tx_root: byte 0 is 0x05, the vector says 0x03`, and the Rust test failed with its own
   message because the fields no longer hash to the exported `public_input_hash`. The VM assembly
   stayed green, which is the expected shape — it reads the .NET vectors, not the export — and it is
   what proves the export is a separate leg rather than a rendering of one.
6. Exchanging two arguments at the Rust **call site** (`tx_root` for `receipt_root`) failed
   `hash_public_inputs_assembles_the_bytes_the_dotnet_encoder_writes` alone. That is the control that
   shows the assertion binds Rust's parameter order to the .NET byte order rather than merely
   re-checking the fixture against itself.

Four things surfaced only because the pairings were executed for the first time.

`src/Neo.L2.State/MerkleProofSerializer.cs:4-7` asserted that "the L1 `NeoHub.SharedBridge` contract
reads this format off the wire when verifying user withdrawal proofs" — it does not:
`FinalizeWithdrawalWithProof:310-337` forwards structured `byte[][] siblings, ulong leafIndex`
arguments to `SettlementManager.verifyWithdrawalLeafWithProof` and never parses the framing. The only
on-chain consumer is `RestrictedExecutionFraudVerifier`, and it checks the blob's *length* against
`MerkleProofHeaderSize = 48` (`:544`) rather than any field inside it. The doc now names the real
consumers, which is also the accurate statement of what a framing change would break: the fraud
verifier's length gate and the off-chain relayer/CLI, not the payout path.

`ChainRegistryContract.cs:309-310` slices `verifier` at literal `24` and `bridgeAdapter` at literal
`44`. The off-chain serializer names the same two numbers (`L2ChainConfigSerializer.cs:43-44`), so the
defect is not a missing name — it is that nothing executable links the two statements *and* the branch
had never run: existing tests cover only the permissionless mode and the invalid-mode rejects, so mode
1's approved-set check and mode 0's reject were both dead. A one-sided layout shift there makes the
admission gate test the approval-set membership of the wrong field and lets an unapproved verifier
register. Same failure mode as `C2`, one gate earlier. Both branches now execute, and the test asserts
the sliced bytes are the vector's `0x22`-filled and `0x33`-filled regions — fill **values** marking
verifier and bridgeAdapter, not a second pair of offsets — rather than repeating the contract's
arithmetic.

`ComputePublicInputHash:452-474` rebuilds the 332-byte preimage from header bytes `0..11` and eight
header roots, and `IsProofTypeCompatible` reads offset 316 — so the submit path pins those positions
and nothing else in the tail. `firstBlock` (offset 12) and `lastBlock` (offset 20) are bound by
**neither** the digest nor any assert: `SubmitBatch` stores the whole header (`:384`) and no read site
indexes 12 or 20, so the L1 block range a batch claims to cover is opaque to L1. The vectors give
those two fields values distinct from `batchNumber` precisely because every hand-rolled header in the
repo sets all three equal; §6 records the binding gap as its own item.

Writing the Rust leg surfaced the fourth thing: **the crate it belongs in had no pull-request lane to
run in.** `grep -rn "neo-execution-core" .github/workflows` was empty. The only command that reaches
its tests is `cargo test --workspace` at `build.yml:527`, inside `sp1-release-gates`, and that job is
`if: ${{ github.event_name == 'workflow_dispatch' }}` (`:396`) — the same `V1` finding, one crate
nearer the money path: `bridge/neo-execution-core`'s 17 tests, including the pre-existing
`batch_core.rs` parity suite, never ran on a merge. So `build.yml` gained
`cargo test --locked -p neo-execution-core` in the `bridge` job (`:302-309`), which is what turns
this leg from documentation into a gate. What that lane's deferred formatting check actually looks
like, measured rather than assumed: `cargo fmt --all -- --check` on this tree flags exactly one
ordering defect, `bridge/neo-execution-core/src/wire.rs:1277`, whose `use super::{ExecutionError,
Reader, MAX_PAYLOAD_ITEMS}` is not in rustfmt's sorted order — the check wants `MAX_PAYLOAD_ITEMS`
before `Reader`. It has gone unnoticed because the check sits in the dispatch-only lane
(`build.yml:517-519`, toolchain pinned to 1.88.0 at `:456`, and no `rust-toolchain` file anywhere
outside the vendored `external/` submodules, so the `bridge` workspace builds on whatever the lane
installs); whether 1.88's rustfmt agrees with the local 1.9.0-stable verdict was not measured here.
The same command also prints `Incorrect newline style` for every file under `external/neo-vm-rs`,
which is this Windows working tree's CRLF rather than a repo defect. This branch leaves `wire.rs`
untouched, and its own new Rust file is clean under that command.

The earlier §11 bullet overstated what was open. `StateWitnessV1` is **already** two-sided: the
tracked golden `bridge/neo-zkvm-guest/tests/fixtures/stateful_batch_v1.hex` is pulled in with
`include_str!` by `neo-zkvm-guest/tests/stateful_execution.rs:11` and
`neo-zkvm-host/tests/end_to_end.rs:5`, and read by C# in `UT_StateWitnessV1Serializer.cs:112`, which
re-encodes it byte-identically
(`RustGoldenFixture_DecodesAndReencodesByteIdentically`). What remains open is narrower and now
stated in §11.

### V3 — The SP1 executor "funded release pin" is decorative, and its rejection path has no test [E1]

Prior `H6`, now confirmed by execution. `Sp1SettlementExecutionStack.cs:46,127` and
`ZkLocalHostComposition.cs:87,110` take `executorSha256` as a **caller-supplied parameter** — there
is no pinned constant anywhere in the repo — and the constructor only rejects a zero or
wrong-length digest (`src/Neo.L2.Executor/Witness/Sp1StatefulBatchExecutor.cs:31,41,65-69`). The real
comparison happens later at `:390-393` (`"Native SP1 execution binary SHA-256 differs from the
pinned operator digest"`). The only test that reaches it computes the expected value from the binary
under test:

```
tests/Neo.L2.Executor.UnitTests/UT_Sp1StatefulBatchExecutor.cs:318
    SHA256.HashData(File.ReadAllBytes(executable!))   // passed as the expected pin
```

A pin derived from the artifact it authenticates cannot fail, and no test supplies a wrong digest, so
the rejection branch is unexercised. Contrast `ChainRegistry`'s `RegisterChain` (§7), which is a
correct guard found by reading. Fix: a committed constant digest + a negative test + a CI step that
recomputes it.

### V4 — 27 settlement tests self-skip on Windows because the evidence-file walk ignores the RID subdirectory [E1]

The 2026-08-29 report's §3.1 ("~45 tests silently self-skip on Windows") is still open, and the root
cause is now identified exactly:

```
tests/Neo.Plugins.L2Settlement.UnitTests/UT_MultisigLocalHostComposition.cs:26-34
    Path.Combine(AppContext.BaseDirectory, "..","..","..","..","..", "docs","audit", …)   // 5 levels
tests/Directory.Build.props:4-5   injects RuntimeIdentifier=win-x64 on Windows only
```

With a RID set, `AppContext.BaseDirectory` gains a `win-x64/` segment, so five levels up lands in
`tests/`, and the observed message is verbatim
`repo evidence file not found at D:\Git\neo-n4\tests\docs\audit\testnet-deployment-20260716-live.json`
— 27 tests `Inconclusive` in that one project, on Windows only, and green on Linux CI.

The same wrong path is confirmed in two further projects, so the *spread* is wider than §3.1
described: `Neo.Plugins.L2Batch.UnitTests.FromChainDirectory_LiveDeployReport_LoadsChainId` and
`Neo.L2.Abstractions.UnitTests.Parse_RealTestnetEvidenceReport_IfPresent` both skip with that exact
message, and the fragile walk appears in 10 files across 6 test projects. This pass did not re-count
the repository-wide total, so §3.1's "~45" stands as unverified — the 27 in `L2Settlement` are what
was measured here. Note the second name: a test written to tolerate a *missing* evidence file cannot
locate one that *is* present, so on Windows it reports "not found" forever and nobody notices the
lie.

Fix is one helper — resolve the repo root by probing upward for `Neo.L2.sln` — applied at all ten
sites. It should not wait on any decision in §10.

**Status — fixed on this branch, and §3.1's "~45" is now a measured number rather than an estimate.**
`tests/Shared/RepoRoot.cs` resolves the root once by walking ancestors of `AppContext.BaseDirectory`
until it finds `Neo.L2.sln` — RID subdirectory or no RID subdirectory — and exposes the one evidence
path the tests use. It is delivered by a compile link in `tests/Directory.Build.props`
(`<Compile Include="$(MSBuildThisFileDirectory)Shared/RepoRoot.cs" Link="Shared\RepoRoot.cs" />`)
rather than a project reference, so every test assembly gets its own `internal` copy and no production
project gains a test-only dependency. All 33 walk expressions in the 10 files now read
`RepoRoot.LiveTestnetEvidence`; the per-file site counts were 8/8/7/2/2/2/1/1/1/1.

The measured effect, from one full-solution run of the same 2,893 tests: repository-wide skipped went
**45 → 5**. Each of the six affected projects now reports `Skipped: 0` — `Neo.L2.Abstractions` 80,
`Neo.L2.IntegrationTests` 40, `Neo.Plugins.L2Batch` 66, `Neo.Plugins.L2Prover` 21,
`Neo.Plugins.L2Settlement` 168, `Neo.Stack.Cli` 189 — so 40 tests that had never executed on Windows
now do, and all 40 pass. The 2,893 total is unchanged, which is what confirms the delta is re-enabled
coverage rather than removed tests. That closes §3.1's "~45" as exact, and closes the "the total was
never re-counted" limit recorded in §8 item 12.

Two cross-checks on that number. The 2026-08-29 report counted the affected surface independently as
"40 test methods across 11 files", and its method count is exactly the delta measured here — its 55
skips minus the 10 RISC-V skips that its §3.2 records as already closed is 45, the pre-fix figure. Its
file count was one high: re-counting the pre-fix commit gives 10 files containing 33 walk expressions.
The two counts reconcile per project — 27 expressions ↔ 27 tests in `Neo.Plugins.L2Settlement`, 2 ↔ 9
in `Neo.L2.IntegrationTests` (one walk in a test body at `UT_E2E_HostComposition_FromDeployReport.cs:47`
and one in `ResolveDeployReportPath():3458`, which the other eight tests call), and 1 ↔ 1 at each of the
four remaining sites.
And the helper generalizes a pattern this repository already had rather than inventing a third one:
`FindRepositoryRoot()` at `tests/Neo.Hub.Deploy.UnitTests/UT_ProductionGapClosure.cs:706`, plus three
private copies in `NeoHub.Sp1Groth16Verifier.UnitTests`, all probe upward for `Neo.L2.sln` exactly as
`RepoRoot` now does.
`tests/` now contains no hand-written walk at all — no test builds a repository path out of
`AppContext.BaseDirectory` plus `".."` any more, and the dot-dot literals left in that tree are
traversal inputs fed to negative tests and the relative csproj references the scaffolder asserts on —
so the class of defect cannot be reintroduced by the next test that needs a repo file.

The guard is still a guard, which is the direction a fix like this can silently break: hiding
`docs/audit/testnet-deployment-20260716-live.json` makes `Parse_RealTestnetEvidenceReport_IfPresent`
skip again, and its message now names `D:\Git\neo-n4\docs\audit\testnet-deployment-20260716-live.json`
— the correct path — where the pre-fix message named `D:\Git\neo-n4\tests\docs\audit\…`. With the file
restored the same test reports Passed, and it is not a vacuous pass: it asserts
`L2ChainId == 20260716`, `Contracts.Count == 24` and the exact `ChainRegistry` hash, so this is the
first time a Windows run has compared the parser against the real evidence file. `dotnet format
Neo.L2.sln --verify-no-changes` is clean.

### V5 — The payout path's Merkle verifier is mocked in the test that is supposed to catch a forged leaf [E1]

`tests/NeoHub.Contracts.VmTests/UT_SharedBridge_Vm.cs:69-72` installs
`VerifyWithdrawalLeafWithProof(It.IsAny<…>) → true` into the fixture for every SharedBridge test.
Consequence: **no VM test anywhere exercises on-chain withdrawal inclusion on the path that pays
money.** Every accept/reject assertion in `UT_SharedBridge_Vm` passes with the verifier stubbed to a
constant, so a regression that makes the fold accept anything would not be caught.

Independently, both on-chain folds are position-unbound. `VerifyWithdrawalLeafWithProof:989-1012` and
`VerifyStateLeafWithProof:1115-1134` share the same shape and both end with
`return storedRoot.Equals((UInt256)current);` after shifting `index` once per supplied sibling, with
**no** check that `index == 0` when the loop finishes. So a proof valid at leaf index `i` is equally
valid at `i + 2^k` for any `k ≥ depth` — inclusion is bound to the leaf hash, not to a unique
position, which is the `C2` class in contract form.

The blast radius is smaller than `C2` alone would suggest, and it is worth being precise about why.
`SharedBridge.FinalizeWithdrawalWithProof` re-derives the leaf from the claimed fields
(`ValidateWithdrawalLeafBinding`, `:326-328`) and dedups on
`WithdrawalKey(chainId, withdrawalLeafHash)` (`:329`), so the payout is anchored to leaf *content*
and is replay-protected without reference to position. The position gap therefore does not yield
theft on that path today. What it does break is any consumer that treats `(root, index)` as an
identity — a relayer deduping by index, or an index-addressed "has withdrawal #n been proven" query —
which can be shown two positions for one leaf. Fix both folds by adding the terminator (the stored
root carries no depth, so the `index == 0` check is what binds position), un-stub
`UT_SharedBridge_Vm`, and add a negative VM test per fold. `C2` remains open and is the
highest-value unfixed item in either report.

### V6 — The best-guarded surface in the repo has one bypass, and it is on the crash path [E1 counted]

Telemetry deserves credit before the defect. `MetricNames.cs` declares 39 metric constants,
`MetricCatalog.Descriptions` has exactly 39 entries keyed by those constants, and
`tests/Neo.L2.Telemetry.UnitTests/UT_MetricCatalog.cs:13-26` / `:32-43` enforce the mapping in **both**
directions by reflection — a new constant without a description, or a description pointing at a
deleted constant, fails the build. All 39 names also appear in the operator catalog
(`docs/telemetry.md` §"Metric catalog"; a per-name grep over the doc found zero missing), and the
exposition sample at `docs/telemetry.md:214-226` matches what the code actually renders:
`PrometheusExporter.cs:15` documents the `.` → `_` mapping (Prometheus forbids dots) and `:129`
implements it, counters gain `_total` (`:39`), histograms render as `summary` with `_count` / `_sum`
/ `_max` (`:42-59`).

The guard's blind spot is that it reflects over *constants*, so it cannot see a call site that never
uses one. Exactly one such site exists:

```
$ git grep -nE '(Safe)?(IncrementCounter|SetGauge|RecordSummary|Observe)\("[^"]+"' -- src tools
src/Neo.Plugins.L2Batch/L2BatchPlugin.cs:477:  _metrics.SafeIncrementCounter("l2_batch_on_block_committed_error");
```

That name is not in `MetricNames`, so `MetricCatalog.GetHelp` falls through to the generic string at
`MetricCatalog.cs:18` and the metric is documented nowhere — while step 1 of the repo's own procedure
for adding a metric (`docs/telemetry.md:231`) says to declare a constant first. The consequence is
small in isolation and badly-placed in particular: `:477` is the counter incremented in the
`OnBlockCommitted` catch block that `H1` identifies as the path which can stop the node. It is the
one number an operator would chart during exactly that incident, and it renders with a placeholder
HELP line and no catalog entry.

Fix in two parts. Immediately: promote the literal to a `MetricNames` constant and give it a catalog
entry, at which point the existing reflection test keeps it honest. Durably: add a check that scans
emission sites for string literals, because the current completeness test walks *constants* and so is
structurally unable to see code that skips the registry. That is the generalizable half of this
finding — a completeness check keyed on a registry cannot detect a caller that bypasses it.

**Status — fixed on this branch, both halves.** `MetricNames.BatchOnBlockCommittedError =
"l2.batch.on_block_committed_error"` now sits in the Batch group, `MetricCatalog.Descriptions` carries
its sentence, and `L2BatchPlugin.cs:477` passes the constant. The registry is 40 constants and 40
entries, and the two existing reflection tests — which had been walked by 39 names each — now walk 40
and still pass in both directions.

The promotion is provably not an operator-visible rename, and that claim is now a test rather than an
argument. The exporter maps `.` → `_` at format time (`PrometheusExporter.cs:15`, implemented `:129`)
and appends `_total` to counters (`:39`), so the stored key is the only thing that changed: the raw
literal and the dotted constant both render as `l2_batch_on_block_committed_error_total`.
`PrometheusExporter_BatchErrorCounter_RendersTheSameSeriesAsTheLiteral` pins both halves of that — the
`# HELP` line now carrying the catalog sentence instead of the `"L2 telemetry metric"` placeholder, and
the sample line carrying the series name — so a future rename of the constant breaks a test instead of
a dashboard.

The durable half is `EmissionSites_UseMetricNamesConstants_NotRawLiterals` in the same class. It walks
every `.cs` file under `src/` and `tools/` for an emission call whose first argument is a literal —
`(Safe)?(IncrementCounter|SetGauge|RecordSummary|Observe)` followed by `"` through any `@`/`$` string
prefix — and fails listing `file:line`. Two details make it trustworthy rather than decorative. It
resolves the tree through the `RepoRoot` probe added by `V4`, so it cannot self-skip on the Windows
`win-x64/` output segment the way a fixed-count `".."` walk does; and it excludes `bin/`, `obj/` and
`//` comment lines, since neither generated copies nor prose are emission sites.

Negative control executed, not assumed: reverting only `L2BatchPlugin.cs:477` to the literal fails that
one test with `Metric emitted with a string literal bypasses MetricNames and its catalog guard —
declare a constant instead: src\Neo.Plugins.L2Batch\L2BatchPlugin.cs:477`. Restoring the constant
returns `Neo.L2.Telemetry.UnitTests` to **117/117** (115 before these two tests), with
`Neo.Plugins.L2Batch.UnitTests` 66/66 and the eight `CurrentDocumentation_*` tests 8/8. Full solution on
the same branch: **38 assemblies / 2,901 tests / 0 failed / 5 skipped** — §10 item 5's 2,899 plus these
two, with `V7`'s SP1-queue race not reproducing on this run. `docs/telemetry.md` gained the catalog line
under Batch and step 3 of "Adding a new metric" now says plainly that a literal at an emission site is
itself a build failure; `docs/zh/telemetry.md` mirrors both.

The limit of the guard, stated rather than left implicit: it reads source text, so it sees literals and
not values. A name assembled in a variable and passed in one hop away still escapes it, as does any
emission helper this pattern set does not name. That is a strictly smaller blind spot than the one being
closed here — the guard's whole point is that the *default* way to skip the registry was to type a
string at the call site, and that route now fails the build.

### V7 — The SP1 queue's existence-then-read window tolerates no sharing violation, and the escaping exception is untyped [E1]

Found while measuring H17, not while looking for it. Two consecutive full-solution runs on Windows:
the first reported `2,897 total / 1 failed`, the second `2,897 / 0`. The failure was in
`tests/Neo.L2.Proving.UnitTests`, a project neither branch touches:

```
Failed ProveAsync_TamperedExecutionSemantic_IsRejected [60 ms]
  Assertion failed. Expected exception of exact type InvalidDataException but caught IOException.
  System.IO.IOException: The process cannot access the file
  '…\neo-n4-batch-prover-tests\f73f636e…\f07120bf…78d5c.proof.result.json'
  because it is being used by another process.
    at AtomicFileQueueTransport.ReadBoundedPathAsync (AtomicFileQueueTransport.cs:265)
    at Sp1BatchProofProver.ReadAndValidateResultAsync (Sp1BatchProofProver.cs:208)
```

Reproduction rate measured, not assumed: 1 of 2 full-solution runs; `3/3` passes when
`tests/Neo.L2.Proving.UnitTests` is run alone, which is the signature of contention rather than logic.

The code shape that allows it is a check-then-use window with no tolerance. `ProveAsync` waits for the
result file to exist (`Sp1BatchProofProver.cs:154`) and then reads it
(`AtomicFileQueueTransport.cs:249-269`); `ReadBoundedPathAsync` converts *missing* (`:256-258`) and
*oversize* (`:262-264`) and *changed while read* (`:266-268`) into `InvalidDataException`, but
`File.ReadAllBytesAsync` at `:265` sits outside every one of those guards, so a sharing violation at
that instant propagates as a raw `IOException`. On Windows that window is real: writers here use
`FileShare.None` (`:137`, `:294`) and `File.Move` into place (`:106`), and a file just renamed into a
temp directory can be held briefly by a filter driver (antivirus is the usual suspect under a
38-assembly parallel run). That the window is real is conceded by the repo itself: sharing violations
are already caught and retried or swallowed in four other places —

```
$ git grep -n "catch (IOException" -- src
src/Neo.L2.Executor/Witness/Sp1StatefulBatchExecutor.cs:437:  catch (IOException) { }
src/Neo.L2.Proving/RiscVZk/AtomicFileQueueTransport.cs:108:   catch (IOException) when (File.Exists(path))
src/Neo.L2.Proving/RiscVZk/AtomicFileQueueTransport.cs:147:   catch (IOException) when (stopwatch.Elapsed < _resultTimeout)
src/Neo.Plugins.L2Gateway/Sp1GatewayProofProver.cs:377:       catch (IOException) when (File.Exists(path))
```

— two of them in this same transport, on its write and lock-acquisition paths. So the read path is not
missing a tolerance nobody thought of; it is the one site where the established idiom was not applied,
and the exception that escapes is the only class outside the transport's own
`InvalidDataException` convention.

The consequence is bounded and worth stating precisely: this is a *transient* becoming a *typed-failure
gap*, not a demonstrated settlement outage. The test caught it only because it asserts the exact
exception type rather than "some exception". Fix: apply the existing retry idiom to the read in
`ReadBoundedPathAsync` (the file is already known to exist, so this is a wait, not a semantics change),
and decide explicitly whether an exhausted `IOException` belongs in the protocol's
`InvalidDataException` family. `src/Neo.Plugins.L2Gateway/Sp1GatewayProofProver.cs:415-432`
ships a structurally identical helper — same `File.Exists` check, same unguarded
`File.ReadAllBytesAsync` at `:429` — so whichever answer is right should be applied to both.

**Status — fixed on this branch, both halves, with one shared answer.** The retry idiom the write and
lock-acquisition paths already carried is now applied to both read funnels: `ReadBoundedPathAsync`
routes the read through a `ReadAllBytesWithSharingViolationRetryAsync` helper that retries transient
`IOException`s within a 2-second window polled at 50 ms (a proportionate budget for a filter-driver
hold, deliberately narrower than the operator-tuned `_resultTimeout`), converts an in-flight
`FileNotFoundException` to the same missing-artifact verdict the pre-read existence check emits, and
— the explicit decision §10 item 16 asked for — wraps an *exhausted* `IOException` into the
transport's `InvalidDataException` family with the inner exception preserved, so every read failure
the transport can detect is typed and every caller's structured-rejection path owns it;
`OperationCanceledException` still propagates. `Sp1GatewayProofProver.ReadBoundedFileAsync` ships the
identical helper with the same constants — one answer, applied to both, as the finding required. Four
new tests hold artifacts exclusively through both public paths: release inside the window is retried
to success; a hold that outlives the window is typed, never raw. `Neo.L2.Proving.UnitTests` 86/86,
`Neo.Plugins.L2Gateway.UnitTests` 105/105.

### V8 — The only Rust dependency gate in CI cannot see the advisories Dependabot reports, and the High one is live [E1 gate-blindness + reachability]

GitHub lists three open Dependabot alerts on this repository. All three are Rust, all three resolve
out of the *same* `Cargo.lock`, and all three were opened on the same day:

```
$ gh api "repos/r3e-network/neo-n4/dependabot/alerts?state=open"
3 | high | p3-challenger  | < 0.4.3        | first patched 0.4.3 | GHSA-vj64-rjf3-w3v7  | created 2026-07-15
2 | low  | p3-symmetric   | <= 0.5.2       | no patch            | GHSA-3g92-f9ch-qjcm  | created 2026-07-15
1 | low  | lru            | >=0.9.0,<0.16.3| first patched 0.16.3| GHSA-rhfx-m35p-ff5j  | created 2026-07-15
```

Two of the three are already written up: `docs/audit/sp1-transitive-advisories-2026-08-28.md` assesses
`p3-challenger` and `lru`, records that RustSec carries no matching record so the CI gate stays green,
names the remediation as a coordinated SP1 upgrade rather than a lockfile bump, and cites the closed
Dependabot sp1-6.3.1 attempt (PR #23, 4 failing checks). That note is good work and this finding does
not restate it. What V8 adds is four things the note could not say on 2026-08-28, each measured here
against the pinned source rather than inferred: which of the challenger advisory's *two* mechanisms
actually survives in the `0.3.3-succinct` fork (the note explicitly declined to guess, calling the
backport "not publicly recorded"); that no SP1 release carries the fix, which is also the correction of
a claim this section first published and then measured wrong (see "The fix path has no name", below);
an assessment of `p3-symmetric`, which is not covered there at all; and one loose end in the
accepted-risk bookkeeping, flagged at the end of this section.

The repository's answer to "are our Rust dependencies audited?" is one CI job:

```
$ grep -n "cargo audit" .github/workflows/build.yml
590:      - name: cargo audit (production Rust lockfiles)
600:          for lockfile in \
601-            Cargo.lock \
...
607-            cargo audit --file "$lockfile" --ignore RUSTSEC-2026-0258 --json
```

That loop starts at `Cargo.lock` — the exact manifest Dependabot flags — and it passes locally
against a same-day advisory database:

```
$ cargo audit --file Cargo.lock --json | head -c 300
{"database":{"advisory-count":1226,"last-updated":"2026-08-29T08:11:09+02:00"},
 "lockfile":{"dependency-count":614},"vulnerabilities":{"found":false,"count":0,"list":[]}
```

So the green `cargo audit` check is not evidence about these three advisories: the job reads RustSec,
Dependabot reads the GitHub Advisory Database, and for `p3-challenger` the two databases disagree. This
is the §5 shape again — a check that is green for a reason unrelated to the property it appears to
assert — with one twist worth naming: unlike the other V findings, nothing here is miswritten. The repo
already carries the same kind of discrepancy once, deliberately, with an explanatory comment and an
`--ignore` for `RUSTSEC-2026-0258` (`build.yml:601-608`). The difference is that the h2 case is
disclosed and this one is invisible.

**The High is not noise, and a grep of this tree would have cleared it wrongly.** `MultiField32Challenger`
is named in the advisory title, and the only hits for it in the repository are audit prose:

```
$ git grep -ln "MultiField32Challenger"
docs/audit/sp1-transitive-advisories-2026-08-28.md      ← and this report; no .rs, no .cs
$ sed -n '6p' ~/.cargo/…/slop-challenger-6.2.1/src/lib.rs
pub use p3_challenger::*;
```

The flagged type is re-exported under a renamed crate (`slop-challenger` → the `slop_*` family) and is
then used as the transcript challenger of the recursion configs that the bundled SP1 path proves with:

```
~/.cargo/…/slop-basefold-6.2.1/src/config.rs:13,50   MultiField32Challenger<F, Bn254Fr, OuterPerm, …>
~/.cargo/…/slop-bn254-6.2.1/src/lib.rs:17,75,104     type Challenger = MultiField32Challenger<…>
Cargo.lock:2718                                       p3-challenger 0.3.3-succinct
```

**What does and does not apply at the pinned pairing.** The advisory text describes newer Plonky3 code
(`reduce_32`, `num_f_elms = PF::bits() / 64`); the pinned fork is different, so the two claims in its
title have to be checked separately against the code that actually ships:

- *Challenge entropy loss* — **does not apply.** The pinned `num_f_elms` is
  `PF::bits() / F::bits() / 2` (`p3-challenger-0.3.3-succinct/src/multi_field_challenger.rs:47`), which
  at the BN254 pairing is 4 limbs of a 2⁶⁴ base = 256 bits, and `split_32` (`:77`) therefore covers the
  full 254-bit field element. The advisory's 3-limb version (192 bits) would not.
- *Transcript malleability* — **does apply.** `duplexing()` absorbs
  `input_buffer.chunks(num_duplex_elms)` through `reduce_31` (`:66-67`, `p3-field-0.3.3-succinct/src/helpers.rs:134`)
  with no chunk-length marker, and `sample()` duplexes whatever partial buffer exists (`:172-175`). A
  trailing zero observation therefore absorbs to the same state as no observation: the sponge input is
  not injective, so two transcripts that differ only in appended zero elements sample identically.

That is a real property of the live code path, and its consequence is bounded the way transcript
malleability always is: it lets a prover rewrite its own public inputs without changing the challenges,
which matters for anything that treats a transcript or its committed public inputs as unique. It is not
a forged-proof result, and I did not attempt to build one against the settlement path — the remaining
question is whether any N4 consumer relies on transcript/proof-input uniqueness across the batch or
Gateway sidecars, which is an analysis of `AtomicFileQueueTransport` and the sidecar binding, not of the
crate.

**The fix path has no name.** The 08-28 note correctly concluded that no version inside the pinned graph
can express the fix, and asked for "a release whose dependency graph pins `p3-challenger >= 0.4.3`"
without identifying one. The first version of this section answered that question, and the answer was a
release:

```
$ curl -s https://crates.io/api/v1/crates/slop-challenger/6.5.0/dependencies | grep -A1 p3-challenger
"crate_id":"p3-challenger"  "req":"=0.4.3-succinct"
```

That answer was wrong, and *how* it was wrong is the finding. `0.4.3-succinct` is a fork tag, not
upstream Plonky3's `0.4.3`, and the tag bump never carried the security change. Hashing the two files
the advisory names, across every build in question:

```
$ sha256sum …/p3-challenger-{0.3.3-succinct,0.4.3-succinct,0.4.3}/src/multi_field_challenger.rs
f0f8351c60f76364…   0.3.3-succinct     ← what SP1 6.2.1 pins
f0f8351c60f76364…   0.4.3-succinct     ← what SP1 6.2.2 and 6.5.0 pin — identical
b6dfd6ca82fb2ec5…   0.4.3 (upstream)   ← patched: 623 lines, absorb/squeeze radices split

$ sha256sum …/p3-field-{0.3.3-succinct,0.4.3-succinct}/src/helpers.rs
e28cb64e3b73b567…   0.3.3-succinct
e28cb64e3b73b567…   0.4.3-succinct     ← identical
```

Both files the advisory names are bit-for-bit the same across the fork bump. Everything established
above under "What does and does not apply at the pinned pairing" therefore transfers unchanged to
`0.4.3-succinct`: the transcript-malleability mechanism is live there too. And there is nothing higher to
move to — `0.4.3-succinct` is the highest `-succinct` build of `p3-challenger` ever published, and
`slop-challenger 6.5.0`, itself the newest, pins exactly it. **No SP1 release remediates
GHSA-vj64-rjf3-w3v7.** The only builds that carry the fix are upstream `0.4.3` and `0.5.3`, which SP1
cannot adopt while it consumes the Succinct fork. The remediation is not unscheduled; it is unavailable
in this dependency graph, and the honest posture is the accepted-risk one the 08-28 note already took.

One durable note for whoever does eventually bump, because the attempt found a trap the repo has never
had to describe. `=6.2.2` pins the SDK, not the family: SP1's internal requirements are caret ranges, so
moving the eight `sp1-sdk` / `sp1-zkvm` / `sp1-verifier` pins in
`bridge/neo-zkvm-{guest,gateway-guest,host,gateway-host}/Cargo.toml` re-resolved the lock into a stack
with `sp1-{sdk,prover,verifier,zkvm,recursion-gnark-ffi}` at 6.2.2 and **44 sibling crates — including
`sp1-core-machine`, `sp1-core-executor` and `sp1-recursion-compiler` — at 6.5.0.** That combination has
never been published or tested by SP1, and `sp1-release-gates` would have caught it only by failing
against the derived ELF/VK pins. A uniform 6.2.2 lock is reachable (every `sp1-*` crate I checked on
crates.io publishes 6.2.2) but only by pinning every family member, not by editing four manifests. That
attempt is unwound — the branch carried no commit, and the working tree is back on `=6.2.1`.

What a bump still costs, if it is ever scheduled for a reason other than these advisories, is unchanged
from the scoping: `doc.md:372` pins "SP1 6.2.1 compressed proof" as the requirement text, `AGENTS.md`,
`ARCHITECTURE.md` and `IMPLEMENTATION_STATUS.md:266-267` all name 6.2.1, the build scripts derive
SHA-256/VK from one Docker ELF snapshot and panic on mismatch, and
`NeoHub.Sp1Groth16Verifier` is an immutable SP1-v6.1-compatible wrapper verified through the BN254
interops. Every one of those has to be re-established against the new release, the guest ELF can only be
re-derived where Docker runs (this repo has no local Docker, so that loop is a `workflow_dispatch` of
`sp1-release-gates`), and the vendored submodule lives in `r3e-network/neo-zkvm` — so the change is
cross-repo. None of that work moves the vulnerable bytes.

The semver question this section previously left open — `0.4.3-succinct` sorts *below* `0.4.3`, so does
the alert survive its own fix? — is now moot, and worth closing rather than carrying. Because the bytes
are identical, the alert outcome would be cosmetic either way: if Dependabot resolves `0.4.3-succinct`
as satisfying `< 0.4.3` and closes the High, that dismissal is a **false green** on a code path this
section has shown to be live and unchanged. Either result, the Security tab is not a usable signal for
this dependency, which is the same gate-blindness the section opened with.

The `lru` alert needs no new analysis: `lru 0.12.5` is pulled in by `sp1-prover 6.2.1`
(`[dependencies.lru] version = "0.12.4"`), and no release inside that requirement is patched — which is
what the 08-28 note already concluded.

`p3-symmetric` is the alert the repo has never assessed, and my first pass on it was wrong in the
instructive direction: grepping the advisory's package name against a symbol guessed from its title
suggests the vulnerable construct is absent from `0.3.3-succinct`. It is not. The advisory concerns
`PaddingFreeSponge::hash_iter`, which pads a final partial block by leaving stale state elements in
place, and both sponge variants plus their unfixed-ness are right there in the pinned crate:

```
$ grep -n "pub struct" ~/.cargo/…/p3-symmetric-0.3.3-succinct/src/sponge.rs
15:pub struct PaddingFreeSponge<P, const WIDTH: usize, const RATE: usize, const OUT: usize>
52:pub struct MultiField32PaddingFreeSponge<
$ grep -rn "Pad10Sponge" ~/.cargo/…/p3-symmetric-0.3.3-succinct/src/     # → no matches
```

`Pad10Sponge` is the upstream half of the fix, so the pinned fork carries the vulnerable behavior.
It is reachable and it is live in the BN254 config, which sets `type Hasher =
MultiField32PaddingFreeSponge<…>` (`slop-bn254-6.2.1/src/lib.rs:83`). What narrows it — from the
advisory's own impact section, not from an excuse made up here: *"in circumstances where the number of
elements to be hashed is known and fixed in advance (as is the case for most STARKS), the method is
collision resistant. This vulnerability only applies if a malicious user is able to manipulate the
number of elements to be hashed."* In this tree the two `hash_iter` call sites in the prover stack are
`slop-merkle-tree-6.2.1/src/p3sync.rs:137`, hashing a fixed literal array, and
`slop-merkle-tree-6.2.1/src/tcs.rs:146`, hashing `vec![claimed_values_slices]` for a FRI batch
decommitment — a length that follows the query shape. Whether an SP1 adversary can steer *that* length
is the one open question, and I did not chase it: the crate is operator-side only, and the 08-28 note's
threat model already places a malicious operator on this boundary, which is where alert #3's own impact
statement lands too. Low, reachable, and — like the challenger — unpatchable by any SP1 release:
`p3-symmetric-0.4.3-succinct/src/sponge.rs` hashes `8398352ffe347f52…`, identical to the `0.3.3-succinct`
file, and `Pad10Sponge` is absent from both. The fork bump carries no security change in this crate
either.

One loose end in the bookkeeping, and it is the kind this §5 keeps finding. `.github/dependabot.yml:26-35`
ignores `lru` and `p3-challenger` for the cargo ecosystem, pointing at the note, and its stated purpose
is to stop the security-update jobs failing. That worked — and it is also reasonable to read that block
as "these two are handled." They are not closed. All three alerts are still open in the Security tab
today, six weeks after they were filed, and a fourth ignore would not dismiss them either: `ignore`
suppresses pull requests, not alerts. The recorded accepted-risk decision and the visible alert state
disagree, and only the alert state is legible to someone who has not read the note.

## 6. Medium / Low findings (new this pass)

- **`SealedBatch` drops the message side of the batch** [E1]. `BatchBuilder.AddWithdrawal`,
  `AddL2ToL1Message`, `AddL2ToL2Message` (`src/Neo.L2.Batch/BatchBuilder.cs:85-106`) stage into
  `_batch`, but `SealArtifact` (`:138-170`) returns a `SealedBatch` carrying only transactions, L1
  messages and forced inclusions (`src/Neo.L2.Batch/SealedBatch.cs:15-17`). A caller that hands off a
  `SealedBatch` cannot reconstruct what the withdrawal root committed to. Latent today because the
  plugin path uses `BatchExecutionResult` / `ToCommitment`; it is an API that will silently lose
  data the first time it is used as a transport.
- **`ContractManifest.ToJson()` bytes enter the state-root leaf** [E1].
  `Sp1StateWitnessSource.cs:271` serializes each contract's manifest to UTF-8 JSON and feeds it to
  `StateWitnessV1Serializer.ContractBindingHash` (call site `:73`), so the canonical root now depends
  on upstream manifest JSON ordering. Any change to `nccs`' manifest emission — including a
  compiler-sync commit that touches no N4 source — moves every root.
- **A reorg stops the node instead of rewinding** [E1]. `RecoverAndProcessCommittedBlocks` throws
  `"committed L2 block {index} is missing from the local ledger; recovery cannot skip it"`
  (`src/Neo.Plugins.L2Batch/L2BatchPlugin.cs:497-500`), the handler rethrows (`:479`), and
  `Plugin.ExceptionPolicy` defaults to `StopNode`
  (`external/neo/src/Neo/Plugins/Plugin.cs:74`) with **no** first-party override — a source-scoped
  grep for `ExceptionPolicy` under `src/` returns nothing at all, so this applies to every L2
  plugin, not just the batcher.
- **`WithWriter` silently downgrades DA profile** [E1].
  `src/Neo.Plugins.L2DA/L2DAPlugin.cs:163-175` unconditionally sets `_profile = Development` (`:169`)
  and clears `_productionBackendOverridden` (`:174`). That matters because the rest of the plugin is
  fail-closed by design: `ResolveProfile:218-221` defaults every non-`Local` mode to `Production`, and
  `BuildDefaultWriter:134-136` throws under `Production` rather than materialize a simulated writer. A
  host that calls `WithWriter` steps around both guards at once — `Configure:102-109` then runs
  `ValidateConfiguredBackend` with the Development profile, so the mandatory-independent-reader
  requirement is waived, and the semantic-simulation writer that Production refuses becomes reachable.
  The doc-comment scopes the method to "development and integration environments" but nothing
  enforces that, and no log line says the guarantee was dropped.
- **Sync-over-async on the commit thread** [E1]. Five `.AsTask().GetAwaiter().GetResult()` sites in
  `src/Neo.Plugins.L2Batch/L2BatchPlugin.cs` — `:385`, `:387`, `:583`, `:652`, `:655` — block on L1
  I/O from the `Committed` path, which the 2026-08-29 report's robustness verdict already flags; it
  is the delivery mechanism for H1's remote-expressible outage.
- **`JsonRpcL1DAWriter.IsAvailableAsync` conflates four states into one `false`** [E1]
  (`src/Neo.Plugins.L2DA/JsonRpcL1DAWriter.cs:127-158`): a pointer/metadata that does not match this
  writer's mode (`:133-136`), a non-object response, `state != "HALT"` — i.e. the DA contract itself
  faulted — and a genuine "not available" all return the same `false`, so a misconfigured DA layer is
  indistinguishable from data that has genuinely vanished, and the node silently prefers its
  fallback. A transport failure is the one case that does surface, because `CallAsync` throws rather
  than being caught into `false`.
- **Every built-in DA writer is a simulation, and no real backend ships here** [E1].
  `NeoFsLikeDAWriter` is an in-process `ConcurrentDictionary` whose own header says it "does not
  contact NeoFS or survive process restarts" (`src/Neo.Plugins.L2DA/NeoFsLikeDAWriter.cs:1-27`,
  `ReceiptKind = SemanticSimulation` at `:26`); `CommitteeAttestedDAWriter` carries the same receipt
  kind (`:46`, `:163`). A production-tier type does exist — `MetricsEmittingProductionDAWriter:15`
  implements `IProductionDAWriter` — but it is a metrics decorator over an injected inner writer, not a
  backend. So for `DAMode.NeoFS`, `.DAC` and `.External` the repository ships no implementation at all:
  a chain advertising NeoFS data availability is, in this tree, entirely dependent on an operator
  supplying an adapter, and nothing validates that the adapter's claims are true rather than merely
  well-formed. That is a composition-boundary observation rather than a defect — the fail-closed
  default (§9) is the right response to it — but it should be stated in `doc.md` §12 rather than left
  to be discovered by reading the throw at `L2DAPlugin.cs:134-136`.
- **The intra-batch nonce gate is scoped to the executor object, not the batch or the state** [E1].
  `_consumedNonces` is a `readonly HashSet<(UInt160, uint)>` on both executors
  (`src/Neo.L2.Executor/ApplicationEngineTransactionExecutor.cs:60`, `.Add:133`;
  `src/Neo.L2.Executor.RiscV/RiscVTransactionExecutor.cs:52`, `.Add:126`) that is never cleared and
  never persisted. Outside tests the only production-style construction is
  `tools/Neo.L2.Devnet/Program.cs:204`/`:219`, i.e. one object for the life of the process — so prior
  `H10`'s growth term holds there, and the mirror risk is the one nobody would notice: the "duplicate
  sender nonce" rejection is only as durable as the object, so any host that ever builds an executor
  per batch loses replay detection silently. (The track report also asserted a comment claiming batch
  scope at `:126-128`; there is no such comment — corrected in §8.) Fix: read the account nonce from
  the state store as the single source of truth, or persist the gate with the batch checkpoint.
- **A guard defending against a scenario the host does not produce** [E1].
  `L2BatchPlugin.cs:457-460` keeps `_sealer` alive across `Configure()` "if Configure ever runs more
  than once (config-watcher re-fire, host re-init)". The core's config watcher does not re-invoke
  `Configure` — it logs `"File {File} is {ChangeType}, please restart node."`
  (`external/neo/src/Neo/Plugins/Plugin.cs:126`). So the branch that silently ignores updated
  settings exists for a trigger that never fires, and the operator-visible effect is that editing
  batch thresholds appears to succeed. Fix: drop the speculation and say plainly that a restart is
  required, at the point where the settings are read.
- **Undocumented per-batch state-witness ceiling** [E1].
  `src/Neo.L2.Batch/StateWitnessV1.cs:133` sets `MaxEntries = 65_536` and `:305` rejects any witness
  above it, so a batch touching more than 64K state keys hard-faults on the durable-artifact path with
  no operator-facing documentation of the limit.
- **`OnBlockCommitted` has no test** [E1]. `UT_L2BatchPlugin.cs:206`, `:229`, `:304`, `:351` pin the
  retry path only through `ProcessCommittedBlock`; no test references `OnBlockCommitted`, and
  `InvokeCommitted` appears in zero tests. The recovery behaviour that H1 depends on for its fix is
  the least-tested code on the critical path.
- **The forced-inclusion interface documents a gate that no code implements** [E1 counted].
  `src/Neo.L2.ForcedInclusion/IForcedInclusionSource.cs:36-38` says of `HasOverdueEntryAsync` that
  "the batcher uses this to decide whether to halt finalization for censorship reasons". Nothing uses
  it that way. Two greps, run separately: `git grep -n "HasOverdueEntry" -- src tools` gives exactly
  two non-test consumers — `CensorshipDetector.cs:79`, whose own doc at `:73-74` states the detector
  "does NOT consume the queue" and that reports are advisory until an operator submits them, and
  `LocalHostCompositionBase.cs:510`, a wrapper. `git grep -n "HasOverdueForcedInclusion\|HasOverdueCachedEntry" -- src tools`
  then shows where that wrapper ends up: operator *status* fields only —
  `LocalHostCompositionBase.cs:507-520,545,640,717,1707`, `LocalHostOperatorStatus.cs:299,747`,
  `LocalHostHealthProbeDocument.cs:348`, `LocalHostOperatorStatusDocument.cs:252,490`,
  `NeoHubDeployReport.cs:454,511` and `InitL2Command.cs:162`. `BatchSealer` and `L2BatchPlugin`
  appear in neither list, so no finalization path consults the flag. The real behavior is in fact stronger than
  documented: `BatchSealer.cs:236-240` drains the queue at the *start* of every fresh batch, before
  any block transaction, and `:338-359` fails closed on a null/oversize/empty drain, with
  `L2BatchPlugin.cs:642-663` draining all pending entries with **no** deadline check at all. So a
  censoring sequencer cannot skip a forced transaction, and the "halt finalization" mechanism the doc
  describes is both absent and unnecessary — but an operator reading the interface would look for the
  wrong safety property and, worse, a future refactor could "restore" a halt that would stall healthy
  chains. Fix is doc-only: describe the prepend-and-drain guarantee that exists.
- **The 2026-08-29 claim that the escape hatch "faults without manual pauser registration" is now
  refuted for the live deployer** [E1]. `ReportCensorship` pauses only through
  `ChainRegistryContract.cs:482-485` (`CheckWitness(owner) || IsPauser(callingScriptHash)`), and
  `ForcedInclusion` has no self-registration path — but `tools/Neo.Hub.Deploy/LiveDeployCommand.cs:801-802`
  now issues `registerPauser(ForcedInclusion)` with a `ChainRegistry.IsPauser` read-back assert, runs
  by default (`:57`), and does so *before* `ChainRegistry.LockGovernance` at `:861-862`. The residual
  half — `RegisterPauser` / `RevokePauser` surviving the lock — is already tracked in §7.1 and is the
  item worth fixing, since it means pause authority stays owner-mutable after the deployer has
  declared governance final.
- **`docs/zh/CHANGELOG.md` promises a sync it does not perform** [E1 counted]. Its own header states the
  rule (`:4`: when the English file changes structure, commands, paths, interfaces, contract counts,
  test evidence or security conclusions, the Chinese version must be updated in lockstep), and the
  localization gate is written to enforce exactly that pair — but the gate asserts only that a
  counterpart *exists*:

  ```
  $ grep -c "2026-08" docs/zh/CHANGELOG.md
  0
  $ git log --oneline -1 -- docs/zh/CHANGELOG.md
  a647886d Stabilize the coverage gate (#30) [skip ci]
  ```

  Nothing newer than 2026-07-15 is in it, while `CHANGELOG.md` gained nine dated entries on `master`
  across 2026-08-28 → 2026-08-30. The `C1`, `H12`, `C4`, `H16`, `H17` security and test-evidence records
  among them are precisely the category its own rule calls out as mandatory, and this branch's two
  entries (`H19`, `V8`) widen the same gap the moment they land. A Chinese reader is
  therefore told, in writing, that this file tracks security conclusions, and it has not tracked one
  for six weeks. The fix is a decision about which artifact is real: either backfill and let a test
  compare entry headers between the pair, or delete the promise from the header and label the file what
  it is — a stale summary. Leaving it as-is is the one option that keeps a documented invariant false
  while a green test certifies the pair as complete.

  **Relabeled on this branch (2026-08-31) — the digest contract is the real artifact, and the digest is
  no longer stale.** Sizing killed the backfill option: `CHANGELOG.md` is 10,076 lines with **724**
  dated `###` entries, so a header-compare mirror is a one-time ~700-line translation *plus* a
  permanent tax on every future entry, and it would make the zh page a table of contents with no
  content — worse for a Chinese reader than the digest it already is. The page's own 本页用途 section
  always claimed to be a major-change index, so the header was rewritten to say exactly that: not
  entry-for-entry lockstep, ordinary entries do not trigger updates, only security fixes / audit
  conclusions / production-readiness changes earn a digest entry, and English stays authoritative for
  security conclusions and test evidence — a digest entry must not weaken or expand what the English
  record states. The majors from 2026-08-28 → 2026-08-31 (C1, H12, the SP1 advisory records, C4, V4,
  the audit report itself, H16, H17, V6, V8, H19, §7.1, H18, both V2 halves, Fix A, Fix B) were
  backfilled at relabel time, so the page is current and the pre-existing 2026-07 summaries stay. The
  enforced pair property remains the existence gate
  (`CurrentDocumentation_EveryEnglishMarkdownHasChineseCounterpart`), and the zh page's 同步状态 now
  says in so many words that this is the only tested property — the header no longer promises an
  invariant no test enforces.

- **A batch's claimed L1 block range is authenticated by nothing on L1** [E1].
  `L2BatchCommitment.FirstBlock`/`LastBlock` occupy header offsets 12 and 20, and
  `SettlementManager.SubmitBatch` stores the whole 321-byte header (`:384`), but no read site indexes
  either offset and `ComputePublicInputHash:457` copies only bytes `0..11` before the roots — so the
  range a batch claims to cover reaches L1 as opaque bytes, outside the digest that binds the roots and
  outside every assert. The consequence is bounded (roots, not the range, gate settlement), but a
  sequencer can publish a header whose block range contradicts the state transition it committed to
  and no on-chain check notices. Found while pinning the layout; the golden vectors give both fields
  values distinct from `batchNumber` because every hand-rolled header in the repo sets all three
  equal, which is why no earlier test could have seen the gap. Fix: include the range in the digest —
  a byte-format change requiring a coordinated spec edit — or assert range continuity against the
  previous finalized batch, which needs no format change.

  **Fixed on this branch (2026-08-31), by the digest — the coordinated-spec-edit option the item
  named.** `PublicInputs` gains `FirstBlock`/`LastBlock` and the preimage grows 332 → 348 bytes
  (`chainId[4] ‖ batchNumber[8] ‖ firstBlock[8] ‖ lastBlock[8] ‖ ten 32-byte roots`, little-endian
  throughout), so `ComputePublicInputHash` now copies header bytes `0..27` before the roots and the
  recorded digest binds the range against every forgery the item described. The coordinated spec edit
  is in: `doc.md` §8.3 lists both fields in position and the Gateway recursion paragraph states the
  guest rebuilds the 348-byte form from the commitment plus the two supplements. Every consumer moved
  in the same change: the signed preimage (`AttestationProver`, `OptimisticProver`),
  `StateRootCalculator.HashPublicInputs`, the durable-artifact digest (`ProofWitnessStore`), the
  execution gate (`Sp1StatefulBatchExecutor`), the Rust `hash_public_inputs` (fourteen parameters now)
  with its call sites in the batch builder, the host daemon (`prove_batch.rs`) and both release-gate
  harnesses, and the Gateway guest's sidecar reconstruction
  (`bridge/neo-zkvm-gateway-guest/src/lib.rs`) — the one reader that reassembles the preimage from
  commitment bytes plus the `l1MessageHash`/`blockContextHash` supplements, whose own unit test
  rebuilds the 348-byte form and faults on a tampered supplement or commitment root.
  `Sp1Groth16Verifier` needs no change: `publicInputHash` is a parameter to it. Regenerated in
  lockstep: the golden vectors (`CanonicalEncodingVectors` + the shared hex export), both parity test
  assemblies' hand-rolled builders, the VM assembly's `BuildCommitment`/`BuildPublicInputs` mirrors,
  the three SP1 fixtures (artifact bodies 1892 → 1908 and 3307 → 3323 bytes; the native output keeps
  its 1291 bytes with the embedded digest swapped), and `SettlementManager`'s tracked NEF, re-emitted
  with the pinned nccs `3.9.1+5fa9566e`. Golden digests all moved and are re-pinned: the shared
  vectors' `publicInputHash` (`a56a616d…e4e3`), the stateful fixture's digest (`515c73cc…4cc7`) and
  the artifact content hash (`c3fc234d…671b`). The sweep itself demonstrated the pin discipline: the
  stale `Artifact_ContentHashHasStableGoldenValue` literal was the single failure in the 2,943-test
  full run after the encoder landed. Full solution 38 assemblies / **2,943 tests** / 0 failed /
  5 skipped, `NeoHub.Contracts.VmTests` 593/593 against the re-emitted NEF, `neo-execution-core`
  17/17, `neo-zkvm-gateway-guest` 13/13, `neo-zkvm-guest` 18/18. Not re-verified locally, as §11
  records: the guest ELF/VK manifest and the Groth16 positive vector still pin the old-formula proof,
  and regenerating them needs the Linux `cargo prove` lane.
- **`permissionlessExit` is a pinned wire field that one consumer discards and the validator checks
  in only one direction** [E1 counted]. `L2ChainConfigSerializer` writes it at offset 87 of the
  91-byte config, and `InMemoryL2RpcStore.cs:117-119` parses it from `chain.config.json` and
  immediately drops it (`_ = permissionlessExit;`), so the RPC chain descriptor derives exit policy
  from `exitModel` alone. Two CLI commands print the opposite projection from the same pair
  (`CreateChainCommand.cs:69`, `ListTemplatesCommand.cs:59`): `exit policy = permissionless` whenever
  the bool is set, which for the shipped `rollup` template (`TemplateCatalog.cs:39` —
  `ExitModel: "Delayed"`, `PermissionlessExit: true`) omits the challenge window that
  `ExitModel.Delayed`'s own doc calls the substance of that mode. `ValidateChainConfigCommand.cs:178`
  guards exactly one contradiction, `OperatorAssisted` + `true`; the mirror case
  `Permissionless` + `false` — a chain claiming the strongest exit guarantee on-chain while its
  config field says an operator must co-sign — passes clean. Fix: one check over both directions,
  with the CLI line naming the window.

## 7. Status of prior findings re-checked this pass

| Prior | Status now | Evidence |
| --- | --- | --- |
| `C1` deposit/router inbox collision | **Fixed** (this branch) | two-part dedup + total order in `L1MessageDrain.cs`, `UT_L1MessageDrain` regressions |
| `C2` `MerkleTree.Verify` not position-bound | **Open** — and the same shape is in both contract folds (§5 V5), unobservable because the payout test stubs the verifier | `SettlementManagerContract.cs:989-1012`, `:1115-1134` |
| `H1` plugin exceptions stop the node | **Open**, upgraded to [E1] | `L2BatchPlugin.cs:479 throw;`, `Plugin.cs:74` default, zero `ExceptionPolicy` overrides in `src/` |
| `H6` decorative off-chain binary pin | **Open**, now [E1] with a derived-digest test and no negative test (§5 V3) | `UT_Sp1StatefulBatchExecutor.cs:318` |
| `H12` governance locks on trust roots | **Fixed** for the three roots this branch covered; §7.1's two `contracts/` residuals were closed on the follow-up branch, leaving only the native-contract surface | `ChainRegistryContract.cs:158-168,172-181,389` |
| `H13` kill-switch covers 1 of 3 asset contracts | **Open** for the global flag; its per-chain variant (§4 H16) is **Fixed** (this branch) | audit-time `SubmitBatch:330-331` vs `FinalizeBatch:479-533`; `FinalizeBatch` now asserts `isActive` at `:509-510` |
| `H2` FI deadline < challenge window it pauses | **Re-confirmed** | `ForcedInclusionContract.cs:209` bounds `[60, 86400]` while `OptimisticChallengeContract.cs:246` allows `[60, 7*86400]` — a 7-day window with a 24 h deadline lets `ReportCensorship:518` pause a still-challengeable batch. §4 H19 is the mirror-image half: the *deploy-time* field skips the bound entirely |
| `H3` escape hatch needs hand wiring | **Half-refuted** | `LiveDeployCommand.cs:801-802` now registers + read-back-verifies the pauser before `LockGovernance` (`:861-862`); only the `IsProductionReady()` assertion remains open (`ForcedInclusionContract.cs:254-266`) — see §6 |
| `§3.1` Windows self-skips | **Fixed** (this branch) | repo-wide skipped 45 → 5 on the same 2,893 tests; `tests/Shared/RepoRoot.cs` replaces the 5-level walk at 33 sites in 10 files, and the six affected projects each report `Skipped: 0` (§5 V4) |
| `A4` non-reproducible VM artifacts | **Open** | unchanged; the artifact set still has two compiler stamps |
| Governance completeness | **Closed for `contracts/`**; open for the ten native L2 contracts | see §7.1 — every owner-rewritable post-launch surface in the deployable suite is now either lock-guarded with a payload-bound twin, or carries a recorded reason for not being (`SetOwner`, `PauseChain`/`ResumeChain`, `RegisterChain`'s new-chainId asymmetry) |

### 7.1 Lock pattern: correct where implemented, still absent on four surfaces

`ChainRegistryContract.RegisterChain:158-168` is a model guard — after `LockGovernance` it refuses to
rewrite an *existing* chain while still admitting new chainIds, which is the right asymmetry.
`SetGovernanceController:172-181` is correctly frozen. The same treatment has not yet reached:

- `ChainRegistry.SetOwner:146-153` — witness-only; ownership can be transferred after the lock.
- `ChainRegistry.RegisterPauser:193-199` / `RevokePauser:202-207` — witness-only; after the lock the
  set of contracts that may pause chains (H16's mechanism) remains owner-mutable forever.
- `OptimisticChallenge.SetWindowSeconds:243` (floor 60 s) and `SetChallengerRewardBps:253` — the
  challenge window, i.e. the time an attacker needs to be caught, stays owner-tunable post-lock.
- `L2NativeContracts.cs` — zero occurrences of `LockGovernance` / `IsGovernanceLocked`; the ten
  native L2 contracts have no lock concept at all, which is a core-fork decision (`r3e/neo-n4-core`)
  rather than a `contracts/` one.

**Status — the two `contracts/` bullets are fixed on this branch, `SetOwner` is refuted, and the
native-contract bullet stays open.** The first three bullets are listed as line numbers as of the
audit pass; the fixes are at `ChainRegistryContract.cs` `RegisterPauser:207` /
`RegisterPauserViaProposal:221` / `RevokePauser:231` / `RevokePauserViaProposal:244` /
`RequireApprovedProposal:489`, and at `OptimisticChallengeContract.cs` `SetWindowSeconds:253` /
`SetWindowSecondsViaProposal:266` / `SetChallengerRewardBps:289` /
`SetChallengerRewardBpsViaProposal:301` / `RequireApprovedProposal:509`.

The shape is the one `H12` established and `ExternalBridgeRegistry` already used: the instant path
keeps its witness check and adds `Assert(!IsGovernanceLocked(), "… use XViaProposal")`, and every
guard gets a twin rather than a freeze. A freeze was the smaller diff and is the wrong one — the
capabilities at stake are exactly the ones an operator needs *after* launch (retiring a compromised
pauser, raising a window stuck at its 60-second floor, cutting a bounty that is being farmed),
and a lock that strands them converts a governance hole into an availability incident.

Two consequences of that shape are load-bearing and both are now tested rather than asserted in
prose.

Bounds live in the apply step (`WriteWindowSeconds:272`, `WriteChallengerRewardBps:307`,
`WritePauser:250`), *after* the gate has written its consumption marker. NeoVM journals storage per
transaction, so a fault discards the marker with everything else and a mis-valued vote is not
destroyed: `SetChallengerRewardBpsViaProposal_KeepsBounds_AndDoesNotBurnAFaultedProposal` shows bps
`0` and `10001` faulting and then bps `500` applying under the *same* proposal id `51`. The reverse
order would have made every rejected application a council re-vote.

`ChainRegistry` consumes proposal ids from one namespace (`PrefixConsumedProposal = 0x06`), shared by
the config path and both pauser paths. That is deliberately stricter than one namespace per surface:
an id the council spent on a chain config can never be re-spent on a pauser, so "one proposal, one
application" holds contract-wide instead of per-method.
`UpdateChainViaProposal_StillApplies_AndSharesTheConsumedNamespace` pins both halves — a
`RegisterPauserViaProposal` under the id that just applied a config must fault.

`SetOwner:150-157` stays witness-only, and the finding's ask to guard it is **refuted**, not
deferred. `UT_ContractManifestInvariants.cs:81` (`OwnerManagedContracts_ExposeOwnershipTransfer`)
requires every owner-managed contract — `NeoHub.ChainRegistry` is in that set at `:85` — to expose
`setOwner`, and names the reason at `:116`: "so governance can rotate compromised or deprecated owner
keys". Guarding it post-lock denies an attacker nothing the compromise did not already grant, since
the only party who can call it is the party holding the owner witness, while removing the one
documented recovery path out of that state. The escalation surfaces were the parameters a locked
owner could rewrite silently — verifier route, window, bounty, pauser set, chain config — and those
are all closed now.

Also unchanged by decision: `PauseChain` / `ResumeChain` stay independent of the lock. They are the
mitigation `H16` protects, not a capability the lock should freeze; `RegisterChain` keeps its
new-chainId asymmetry for the same reason. The fourth bullet is unaffected by this branch — re-checked
this pass, `L2NativeContracts.cs` still has zero occurrences of either symbol, and that stays a
`r3e/neo-n4-core` decision.

The deployer is provably not stranded: `RegisterPauser(forcedInclusion)` is plan step
`ScaffoldPlan.cs:380`, before the lock at `:523`, and `LiveDeployCommand` already registers and
read-back-verifies the pauser before `LockGovernance` (§7's `H3` row). `SetWindowSeconds` and
`SetChallengerRewardBps` are called nowhere after a lock in-tree, so no path this branch guards is a
path the operator still needs to run instantly. Both plan descriptions were stale after the change
and now name the surfaces they freeze (`ScaffoldPlan.cs:430` for the window/bounty, `:523` for the
pauser set).

`doc.md` is deliberately untouched. Its `ChainRegistry` core-method list (`:185-192`) never
enumerated the pauser surface or the lock at all, and no occurrence of the window or bounty setters
exists anywhere in the spec — so nothing here contradicts it. What the spec *does* prescribe for
post-lock governance (`:1133-1138`, for escrow) is exactly the pattern reused: approved + timelocked,
action bytes bound to every argument, proposal id consumable once. This is a hardening that follows
the spec, not a spec change.

Evidence: `NeoHub.Contracts.VmTests` **584/584** (was 575 — nine new tests: seven in
`UT_ChainRegistry_Vm`, two in `UT_OptimisticChallenge_Vm`). Negative control executed with three
reverts applied at once — the two `ChainRegistry` pauser guards, the two `OptimisticChallenge`
window/bounty guards, and `ChainRegistry`'s payload-binding assert — after re-emitting both NEFs:
**6 failures, 578 passed, 0 skipped**, and each failure is attributable to exactly one reverted group
(`PauserSurface_RevertsOnceGovernanceLocked` to the pauser guards;
`LockGovernance_…_FreezeTheRest` and `SetWindowSecondsViaProposal_…_AndSurvivesLock` to the
window guard; `RegisterPauserViaProposal_PayloadMismatch_Faults`,
`PauserViaProposal_BindsVotedPauser_Replays_AndSurvivesLock` and
`UpdateChainViaProposal_StillApplies_AndSharesTheConsumedNamespace` to the binding). Nothing outside
the new tests failed, which is the point: no prior test was pinning this behaviour, because none of
these paths could previously fault. Sources restored, artifacts re-emitted, and the tracked files
proved byte-identical to a fresh `nccs` compilation of the restored sources. Full solution on this
branch: **38 assemblies / 2,910 tests / 0 failed / 5 skipped** — item 6's 2,901 plus these nine, with
the same five environment-gated skips (`Neo.L2.Sdk` 3, `Neo.Plugins.L2Metrics` 1,
`Neo.L2.Executor` 1). `UT_ContractManifestInvariants` under `NEO_N4_REQUIRE_FRESH_MANIFESTS=1` is
14/14 — after this gate correctly refused three contracts whose `bin/sc/*.manifest.json` predated the
source that this branch (and `H19` before it) had edited. Recompiling them is a local-only act, `bin/`
is gitignored, and the refreshed ChainRegistry manifest grew 4,773 → 5,391 bytes by acquiring
`registerPauserViaProposal`, `revokePauserViaProposal` and `buildRegisterPauserAction` — an
independent cross-check that the tracked artifact's new ABI matches a real compilation rather than a
hand-edged file.

One finding surfaced by the work rather than by the audit pass: `UpdateChainViaProposal` and
`BuildUpdateChainAction` reached this branch with **no VM tests and no off-chain driver**. The
contract had a payload-bound council path that nothing had ever executed — its binding, its bounds
and its consumption were all unverified until `UpdateChainViaProposal_StillApplies_AndSharesTheConsumedNamespace`
ran it. That is the `V`-class gap in this report's own terms, in a file the report had already read.

## 8. Corrections

Self-corrections and line-number drift found while re-verifying. Each of these was checked against
the file, not inferred.

1. **A claim in the 2026-08-29 report is wrong and I repeated it verbally**: CI *does* exercise the
   RISC-V native host — `build.yml:289-300` builds `neo-riscv-host`, copies `libneo_riscv_host.so`,
   and runs the `RealNative_` tests with `NEO_RISCV_NATIVE_TESTS=1 --minimum-tests 10`. What is true
   is narrower: those 10 tests are gated off everywhere else (H14/C3 above), and the artifact they
   run on is stale (C3).
2. `H1` cites `L2BatchPlugin.cs:478` for the rethrow; `:478` is the log call, `throw;` is `:479`.
3. `H1` cites dispatch at `Plugin.cs:280-284`; that is the `OnMessage` path. The `Committed`
   dispatch is `external/neo/src/Neo/Ledger/Blockchain.cs:490-520`.
4. `ffi.rs` panic arms: three were reported; the correct count is ten (§4 H14).
5. Prior §10's "CI's Linux-only `sp1-release-gates`" understated the case: the job is
   `workflow_dispatch`-only and the required aggregate *asserts* it is skipped (§5 V1).
6. `src/Neo.L2.DA/` and `tests/Neo.L2.DA.UnitTests` do not exist; the DA surface is
   `src/Neo.Plugins.L2DA/` only. Any doc that lists a `Neo.L2.DA` library is stale.
7. `AGENTS.md` and `docs/zh/AGENTS.md` describe two explicit execution profiles keyed on
   `ChainMode.L2RiscV`. `src/Neo.L2.Abstractions/Models/ChainMode.cs:9-21` declares only `L1Mode`,
   `SidechainMode`, `L2RollupMode`, `L2ValidiumMode`. In **code**, the only occurrence of the string
   `L2RiscV` in tracked sources is `tests/Neo.Stack.Cli.UnitTests/UT_BootstrapGenesisCommand.cs:36`,
   inside a JSON literal — the remaining 13 occurrences across 12 files are documentation
   (`AGENTS.md`, `docs/zh/AGENTS.md`, `WHITEPAPER.md` + zh, `TASKS.md`, `docs/architecture-*`,
   `docs/tech-stack-coverage.md` + zh) and one audit JSON label
   (`docs/audit/riscv-zkvm-local-verify-2026-07-22.json:71`). The PolkaVM path is real; the enum
   member the docs name is not.
8. Two dangling references in the 2026-08-29 report were fixed in place this pass: §1's "compounds
   with A2 below" → `H1` (no `A2` exists; §11 starts at `A4`), and §8's security row "custody credit
   (M)" → "§5, Medium, still open".
9. A track-supplied contrast did not survive re-verification and is **not** in this report as
   received: it reported `VerifyWithdrawalLeafWithProof:989-1012` as the position-bound fold with the
   terminator, set against a `VerifyStateLeafWithProof` that lacked it. Read end to end, both folds
   are identical in shape and *neither* has the terminator (§5 V5). It also reported three
   `catch_unwind` arms in `ffi.rs`; the count is ten (§4 H14). Both corrections lower the severity of
   one claim and raise another, which is the reason re-reading is part of this method.
10. The `H10` nonce item arrived with a defect that does not exist: it reported a comment at
    `RiscVTransactionExecutor.cs:126-128` asserting batch scope, and treated the mismatch as part of
    the bug. `:123-128` is the nonce key, the lock and the `Add` — there is no scope comment in the
    file. The unbounded-set half survived verification; the comment half did not, and §6 now states
    the finding with the two-sided consequence instead.
11. Two DA claims arrived backwards and are reversed here. The track reported that "no in-scope
    `IDAWriter` implements `IProductionDAWriter`" and that DAC "is selectable only under Development"
    as if the default path silently simulated data availability. `MetricsEmittingProductionDAWriter:15`
    does implement the interface, and the real default path is stricter than reported:
    `ResolveProfile:218-221` forces `Production` for any non-`Local` mode and
    `BuildDefaultWriter:134-136` throws there. What survives is narrower and stated in §6 — the
    built-in writers are all `SemanticSimulation`, no real backend ships in this tree, and
    `WithWriter` is the hole through which the refused simulation becomes reachable again (§9).
12. Five defects survived my own citation re-verification and were caught only when the Chinese
    mirror was built — that pass re-reads every line reference against disk and is the strictest
    reviewer this report got. All five are fixed in place above, so source and mirror now agree:
    §3 cited bare `.gitignore:3` for the force-un-ignore rule that lives in
    `external/neo-riscv-vm/.gitignore:3` (the wrong file, two lines from an argument about
    submodule invisibility); §4 H18 named `Find` where `TemplateCatalog.cs:63` declares
    `Resolve(string name)`; §5 V1's fenced range `build.yml:563-567` missed the assertion, which is
    `574-578` today (the correction landed against `565-569`; this branch's nine-line
    `cargo test (neo-execution-core)` step shifted every later `build.yml` citation by 9); §5 V4
    claimed the affected test *count* exceeded §3.1's "~45" when only the project
    *spread* was demonstrated and the total was never re-counted; and item 7 above said `L2RiscV`
    "occurs in exactly one place in the repository" while 14 occurrences exist across 13 files —
    true only of code, which is now how the sentence reads. I am keeping the mirror as a review step
    for future passes, not just a translation step.
13. Because CI is no longer available to catch doc drift, every `file.ext:line` citation in this
    report was then extracted mechanically and checked three ways: does the path resolve in tracked
    sources, is the cited line within the file's line count, and is that line non-blank and not a
    lone closing brace. 100 citations passed the last two; two failed the first, and they are a
    defect class neither my manual re-read nor the mirror pass can see, because the basename resolves
    and only the directory was dropped: the guest-packaging script is
    `external/neo-riscv-vm/scripts/package-adapter-plugin.sh`, not a top-level `scripts/` (this repo's
    `scripts/` holds only `ci`, `deployment`, `private-network`), and the `Committed` dispatch is
    `external/neo/src/Neo/Ledger/Blockchain.cs:490-520`, not `Blockchain.cs` at the core root. Both
    fixed above. Residual limits of the scan: bare second-mentions such as `:479` are not re-resolved
    and still depend on the prose adjacency being right, and the line-shape test catches an off-by-N
    that lands on a blank line but not one that lands on a different statement.
14. A sentence this report published about its own fix was wrong, and fixing `V4` is what exposed it.
    §3 C4's status paragraph characterised the full solution's 45 skips as "27 `Neo.Plugins.L2Settlement`
    + 9 `Neo.L2.IntegrationTests` env gates + 9 scattered". Only the 27 was right. `Neo.L2.IntegrationTests`
    reads no environment variable anywhere in its sources, so its 9 skips were `V4` evidence-file skips
    like the other 31; and `NEO_SDK_LIVE` / `NEO_N4_RPC_URL` — the two variables that paragraph implied
    gated that project — belong to `Neo.L2.Sdk.UnitTests`, a different project that was never in the
    sentence. The measured breakdown is 40 `V4` + 5 env gates, and §11 now names all five. The
    classification error is the interesting part: a skip whose message says "not found" is an evidence
    problem, not an environment problem, and reading the counts without reading the messages let me
    label 40 silently-disabled tests as deliberately-declined ones.
15. Closing `V2` falsified three of this report's own sentences, and three more arrived after the fact.
    (a) The finding's control claim was that "round-trip tests stay green" under a swapped
    `txRoot`/`receiptRoot`; the swap did redden the pre-existing
    `UT_BatchSerializer.Commitment_ByteLayout_MatchesDocumentedOffsets`, so the property was guarded —
    against the encoder's own documented offsets. (b) "Nothing executed both sides of a pairing" is
    likewise too wide: `UT_Mvp_Phase3_RestrictedFraudProofV4.cs:95-102` does run the encoder, it just
    hands the bytes to an off-chain verifier. What survives, and is what §5 now says, is narrower —
    no test fed an encoder's bytes to a **deployed contract**. (c) §11's bullet claimed the Rust side of
    `StateWitnessV1` and `MerkleProofSerializer` was "read, not cross-executed against the .NET
    encoder"; `StateWitnessV1` was already pinned through one tracked golden file read by both
    languages, and the three `outbound_v1` roots pair them too, though by pasting the same digest into
    `native.rs` and `UT_CanonicalNativeExecutionAdapter.cs` — which is the honest caveat on the "first
    cross-language vector held in one file" claim in §5. (d) After item 13's mechanical citation scan had
    passed, this branch added a nine-line step to `build.yml`, silently invalidating every `build.yml`
    reference at or after line 302 in both reports. Item 13's scan cannot catch that class, because it
    checks a citation against the tree as it was when the scan ran; every affected reference was
    renumbered by hand against disk (`385-387`→`394-396`, `516`→`527`, `520`→`529`, `532`→`541`,
    `565-569`→`574-578`, `592-599`→`601-608` in this report's §5 V1 fenced block and its §5 V8
    sentence, the same six in the mirror, item 12's sentence in both, and `600-607`→`609-616` in the
    2026-08-29 report plus its mirror). Any future edit to a file this report cites at fixed line numbers
    has the same effect, so the rule this pass learned is that a CI edit and a report edit do not belong in
    the same commit unless the renumbering rides with them. (e) The paragraph that presented itself as the
    measured replacement for the earlier `rustfmt` claim shipped three claims of its own that had not been
    measured either: a toolchain version copied forward as 1.98 when the local binary is 1.9.0-stable, the
    *rule* the diff violates inferred as "SCREAMING members sort first" when the printed diff wants
    `MAX_PAYLOAD_ITEMS` before `Reader` (ordinary alphabetical order), and "no `rust-toolchain` file exists
    anywhere in this repo", refuted by `external/neo-riscv-vm` and `external/neo-vm-rs`. All three are
    corrected in §5 and in the mirror. Writing "measured rather than assumed" does not make a paragraph
    measured; re-running the checker in the same session as the prose does. (f) This branch's pull-request
    description merged three distinct facts into one false bullet, claiming the fraud verifier reads
    verifier trust roots at `0x22`/`0x33` "while the off-chain writer puts them at `0x12`/`0x1e`". No such
    disagreement exists — `ChainRegistryContract.cs:309-310` and `L2ChainConfigSerializer.cs:43-44` name
    the same `24`/`44`, `0x22`/`0x33` are fill values (§5's second surfaced item), and "bound by neither
    digest nor assert" is the `firstBlock`/`lastBlock` property of the third. The body was fixed in place;
    commit `0dcc6e59`'s message carries the bad sentence and stays as pushed, because rewriting a published
    commit means a force-push.

## 9. What held up under execution

Balance matters for a report that will be read as a defect list.

- The batch/state/DA libraries are dense with invariants and the shipped ones behave: five suites
  pass with no failures, and `BatchSealer`'s ordering, forced-inclusion proof binding
  (`BatchBuilder.cs:146-158`) and continuity checks are all real, not decorative.
- `SettlementManager`'s gateway frontier reconstruction (`:799-852`) is a genuine position-bound
  rebuild that re-derives both roots from finalized records and rolls back watermarks on any Router
  failure — the strongest merkle code in the repo, and the template `VerifyStateLeafWithProof` should
  be rewritten against.
- The governance lock pattern, where implemented, is exactly right, including the subtle asymmetry in
  `RegisterChain` and the refusal to lock before a controller is wired.
- The DA plugin's default is fail-closed and the reasoning is documented in code:
  `L2DAPlugin.ResolveProfile:218-221` promotes every non-`Local` mode to `Production` when the config
  omits `Profile`, and `BuildDefaultWriter:134-136` then *throws* rather than hand back a simulation,
  with the message naming the exact requirement ("no local or simulated fallback is permitted"). A
  misconfigured public-DA node refuses to start instead of quietly degrading — which is precisely the
  property `WithWriter` (§6) should not have been allowed to bypass.
- The v4 fraud-verifier scope limit is honest and enforced:
  `RestrictedExecutionFraudVerifierContract.cs:566` rejects anything but a 29-byte
  `IncrementCounter` transaction and `:570-574` requires tx index 0 with a zero-depth proof, i.e.
  single-transaction batches. `Reject(…)` (`:684-687`) only emits an event and returns `false` — no
  bond is slashed inside the verifier, so an out-of-scope proof costs a challenger a transaction, not
  their stake.
- The SP1 Groth16 wrapper, BN254 interop parity, deposit/withdrawal CEI discipline and the atomic
  handoff in the state-root generator all carried the 2026-08-29 verdict and none of them regressed
  under the tests run here.
- The challenge contract has no round to attack, because there is no on-chain bisection. Its entire
  per-batch state is three keys — `:34-36` prefixes built by `BuildKey:873-881` as
  prefix ‖ chainId ‖ batchNumber, with no round or segment index anywhere — so there is no
  round-transition for either side to re-point. Segment agreement is instead enforced by hard equality
  in the v4 verifier (`RestrictedExecutionFraudVerifierContract.cs:510-516` rejects
  `disputedTxIndex != 0 || txCount != 1 || lowerBound != 0 || upperBound != 1` with
  `ReasonContextMismatch`) and those bounds are re-hashed into the transcript (`:699-704`); the claim
  id binds chain, batch and disputed tx index directly (`:736-738`) and pulls the bounds in
  transitively by folding that transcript hash (`:739`), and it is the claim id that `Challenge:695`
  consumes globally. Expiry is equally clean:
  `:704` (`now <= deadline`) and `:774` (`now > deadline`) are exact complements, and
  `FinalizeIfPastWindow` deletes both keys before its external call, so it cannot be re-entered after
  expiry. And the design is honestly labeled: `ChallengeOrchestrator.cs:23-27` states outright that
  the bisection is an off-chain narrowing optimization, that "there is currently no on-chain bisection
  contract", and that `Challenge` is single-shot.
- Telemetry's name → description → documentation triangle is complete and machine-enforced: 39
  constants, 39 catalog entries, a bidirectional reflection test, and zero undocumented names
  (§5 V6 states the one bypass; this states that everything else holds).
- The L2 RPC surface is exactly 10 handlers (`getl2batch`, `getl2batchstatus`, `getl2stateroot`,
  `getl2withdrawalproof`, `getl2messageproof`, `getl1depositstatus`, `getbridgedasset`,
  `getcanonicalasset`, `getsecuritylevel`, `getsecuritylabel` — the `Time("…")` wrapper at each entry
  point makes them enumerable) and all 10 are documented. The reverse direction is clean too: the only
  documented names with no RPC handler (`getCanonicalStateRoot`, `getGenesisStateRoot`,
  `getChallengeableBatchHeader`, `getproof`) are contract or Neo-core methods, not phantom RPC —
  `getChallengeableBatchHeader` really exists at `SettlementManagerContract.cs:739` and is tested at
  `UT_SettlementManager_Vm.cs:610`. The registration layer passes the same scan that found telemetry's
  one bypass (§5 V6): `L2RpcServerAdapter.cs:25-52` carries exactly ten `[RpcMethod]` attributes, one
  per handler, and `L2RpcPlugin.cs:150` / `:178-179` hand that single object to
  `RpcServerPlugin.RegisterMethods` — no handler is reachable by any other route.
- Documentation-to-code pointing is unusually good: nearly every finding in this report was located
  by following a `doc.md` section reference in an XML comment to the right file. The failures are
  narrower — claims that outlived their implementation, and gates that check the wrong thing.

## 10. Remediation order

Split by whether it can land now.

**Can land in the current governance branch (small, local, testable):**

1. `C4` — **done on this branch**: the window keys are cleared when a challenge succeeds and the
   submit → challenge → resubmit VM test landed with it (§3 C4 status). Cheapest Critical in either report, and it gates
   the `H18` fix below: repairing the template/deployer mismatch without this turns a broken
   optimistic chain into a permanently stuck one.
2. `V4` — **done on this branch**: the evidence-file walk is replaced by `tests/Shared/RepoRoot.cs` at
   all 33 sites (§5 V4). Un-hid 40 tests, not 27 — the 27 was one project's share.
3. `H16` — **done on this branch**: `FinalizeBatch` asserts `isActive` at `SettlementManagerContract.cs:509-510`
   while `RevertBatch` deliberately stays unguarded, and the two VM tests landed with it (§4 H16 status).
4. `H17` — **done on this branch**: `LiveDeployCommand` now runs
   `MessageRouter.SetGovernanceController` → `SetGlobalRootVerifier` → `LockGlobalRootGovernance`
   between `SettlementManager.SetMessageRouter` and `ChainRegistry.LockGovernance`, with read-back
   completion checks and six new smoke read-backs rather than the suggested end-to-end publication
   (§4 H17 status). `deploy-testnet` gained two required switches — `--gateway-program-vkey`,
   `--gateway-replay-domain` — because the Gateway profile tuple is operator-supplied and stored
   nowhere. Deploy tests 115/115, `NeoHub.Contracts.VmTests` 573/573, full solution 38 assemblies /
   2,897 tests / 0 failed / 5 skipped (the H16 run's 2,895 plus the two parser tests).
5. `H19` — **done on this branch**: `_deploy` now enforces the `[60, 86400]` window through the same
   constants `SetDeadlineSeconds` uses, and the `uint` overflow direction the finding refused to guess
   was measured, not assumed — mod-2³² truncation behind a halting VM. `UT_ForcedInclusion_Vm` 17/17,
   `NeoHub.Contracts.VmTests` 575/575, full solution 38 assemblies / 2,899 tests / 0 failed /
   5 skipped (the H17 run's 2,897 plus these two). Negative control: reverting only the tracked
   artifact fails the new deploy test naming deadline `1`. See §4 H19's status block.
6. `V6` — **done on this branch, both halves.** The literal is now
   `MetricNames.BatchOnBlockCommittedError` with its `MetricCatalog` entry, so the exported series name is
   unchanged and pinned by a test; and `EmissionSites_UseMetricNamesConstants_NotRawLiterals` is the scan
   that would have caught the bypass, negative-controlled by reverting `L2BatchPlugin.cs:477` alone.
   `Neo.L2.Telemetry.UnitTests` 117/117, full solution 38 assemblies / 2,901 tests / 0 failed /
   5 skipped (item 5's 2,899 plus these two). See §5 V6's status block.
7. §7.1 — **done for the `contracts/` half, with one ask refuted**: `RegisterPauser`,
   `RevokePauser`, `SetWindowSeconds` and `SetChallengerRewardBps` are lock-guarded and each got a
   payload-bound `*ViaProposal` twin sharing its contract's existing gate and single consumed-proposal
   namespace. `SetOwner` is **refuted**: `UT_ContractManifestInvariants.cs:81,85,116` requires it
   precisely so a compromised owner key can be rotated, and a post-lock guard denies an attacker
   nothing the witness they already hold does not give them — the reason is recorded rather than
   dropped. The `L2NativeContracts` bullet stays open as a `r3e/neo-n4-core` decision.
   `NeoHub.Contracts.VmTests` 575 → **584/584**, full solution 38 assemblies / **2,910 tests** / 0
   failed / 5 skipped, `UT_ContractManifestInvariants` 14/14 under the freshness gate. Negative
   control: three reverts with the NEFs re-emitted produce 6 failures / 578 passed / 0 skipped, each
   attributable to one reverted group. See §7.1's status block.
8. `H18` — **done on this branch, and the finding under-described the defect.** The accept rule lived in
   three layers (the contract, the operator-status heuristic, `neo-stack validate`); the two off-chain
   ones were copies of each other and both were wrong on `Optimistic+Multisig` and on `None` under
   `Sidechain`/`Settled`, and because the CLI tested `sec` with four independent `if`s it named no
   `Settled` row at all — every `Settled` config validated silently whatever its proof type.
   `SettlementManager.IsProofTypeCompatible` is now a `[Safe]` read with an unchanged body,
   `Neo.L2.ProofRouting` is the only off-chain copy, and a third reference compiled into both test
   assemblies checks contract against mirror pair-by-pair rather than each against itself. `validate`
   gained the axis the repo never tracked: a pairing that is legal but whose verifier route
   `neo-hub-deploy` freezes without registering. `rollup` now emits `Zk`, `sidechain` emits `Multisig`,
   and the three shipped-config guards share one class-aware policy. Full solution 38 assemblies /
   **2,921 tests** / 0 failed / 5 skipped (item 7's 2,910 plus eleven new methods), with
   `NEO_N4_REQUIRE_FRESH_MANIFESTS=1` passing 115/115 and both published template tables now
   guarded against `TemplateCatalog`. What is deliberately
   **not** closed: the `Multisig` and `Optimistic` on-chain verifiers remain unimplemented, which is
   `doc.md` §7.5 stage 0/1 work rather than a routing-table fix, and `ShippedConfigWarningPolicy` is the
   tripwire that says delete the caveat when it lands. See §4 H18's status block.
9. `V2` (both halves) — **the second half first read as uncloseable and then closed differently.**
   *Documentation half:* **the "or add the enum member" alternative is
   refuted rather than declined.** `BatchSerializer.cs:12-14` now separates the two boundaries it had
   collapsed: the commitment header is the only L1 ABI, while the 348-byte public-inputs form never
   reaches the contract yet is the signed preimage, the artifact digest, the gate on execution and the
   buffer Rust rebuilds (four cited sites, each re-read on this branch). `doc.md` §6 lists exactly the
   four declared members, and `doc.md:1343` selects the engine with `--vm neovm2-riscv` *alongside*
   `--template rollup`, so the fifth member the prose named was a documentation error, not a missing
   dispatch key — adding it would have wired nothing while making a label look like a switch. Ten doc
   sites, `ChainMode`'s own `<summary>` (which claimed it "drives consensus, batching, settlement, and
   DA behavior") and one fixture that passed only because nothing parses the key are corrected;
   `CurrentDocumentation_NamesOnlyDeclaredChainModeMembers` (repo-wide, both spellings, dated narrative
   and evidence exempted by path) plus `Catalog_EveryTemplateNameADeclaredChainMode` replace the
   copy-paste discipline. Full solution 38 assemblies / **2,923 tests** / 0 failed / 5 skipped
   (item 8's 2,921 plus the two new guards).
   *Cross-boundary half:* the finding is named for a missing test, and the test its own text proposed —
   "a single test project that references both sides" — cannot exist, because
   `NeoHub.Contracts.VmTests` pulls its own `Neo` assembly through `Neo.SmartContract.Testing`. So the
   lock runs through data neither side owns: `tests/Shared/CanonicalEncodingVectors.cs` golden bytes for
   all four boundary formats, then `UT_CanonicalEncodingParity.cs` (12 tests) against the encoders and
   `UT_CanonicalEncodingParity_Vm.cs` (8 tests) against the **deployed NEF** through the VM assembly's
   own offset table — the first time any encoder's bytes were executed by a contract. A Rust crate can
   take no .NET reference either, so a third leg exports the same vectors as
   `tests/Shared/canonical_encoding_vectors.hex`, pins that export field-by-field in
   `SharedHexExport_MatchesTheVectors`, and `include_str!`s it from
   `canonical_encoding_parity.rs` (3 tests) to bind `hash_public_inputs`' parameter order and
   `merkle_root`'s fold to the .NET bytes. That leg would have been decorative as written —
   `neo-execution-core` had no pull-request lane at all — so `build.yml:302-309` adds
   `cargo test --locked -p neo-execution-core`. Six controls were run; the fifth perturbed one byte of
   the export and reddened both languages while leaving the VM assembly green, and the first falsified
   this finding's own wording (§8 item 15). Two side effects worth keeping: the two `ChainRegistry`
   admission branches that had never run now do, and `MerkleProofSerializer.cs:4-7`'s claim that
   SharedBridge parses this framing was replaced by its real consumers. Full solution 38 assemblies /
   **2,943 tests** / 0 failed / 5 skipped, plus 17/17 in `neo-execution-core`. What remains open is
   narrower than the original bullet and is restated in §11. See §5 V2's status block.
9b. §6's block-range binding item — **done on this branch**, closing the last encoding gap §6
    recorded: `firstBlock`/`lastBlock` are now inside the digest the settlement contract verifies
    (332 → 348 bytes, `ComputePublicInputHash` copies header bytes `0..27`), with the coordinated
    `doc.md` spec edit, every .NET and Rust consumer, the regenerated golden vectors/fixtures and the
    re-emitted `SettlementManager` NEF in the same change. Full evidence and the dispatch-only
    staleness caveat in §6's status block.

**Needs a decision before code:**

10. `C3` — guest-blob freshness gate. Requires a CI job that runs `regenerate-guest-blob.sh` (nightly
    cargo + `polkatool 0.32.0`) and compares SHA-256, i.e. new CI capacity on the Rust lane.
11. `H14` — removing `panic = "abort"` changes unwind semantics and possibly throughput on the guest
    hot path; needs a measurement, and it interacts with the SP1 re-execution profile.
12. `V1` — **settled on this branch (2026-08-31): the nightly schedule owns the SP1 dispatch, and
    the release checklist owns the blocking rule.** `build.yml` gains a nightly `schedule` trigger
    and both `workflow_dispatch`-keyed places (`sp1-release-gates`'s `if`, `sp1-host`'s success
    assertion) accept `schedule` identically, on the precedent `sdk-conformance.yml` already set;
    PR/push behavior is byte-for-byte unchanged (heavy lanes skipped, which is still what the
    required check asserts), while a real SP1 regression now reddens a scheduled run within a day
    and fails `sp1-host` itself. The release-blocking rule is written into
    `docs/release-readiness-checklist.md` §6 (EN + zh): a failed or never-completed nightly blocks a
    release until a manual dispatch on the exact release-candidate commit passes all three lanes.
    Merge-queue ownership was rejected — this repo does not use one, and per-PR heavy-lane runs
    multiply the resource cost the finding wanted kept. See §5 V1's status block.
13. `H15` — the per-block context fix touches the batcher↔executor seam and, if the persisted header
    feeds any hash, the state-root encoding. Needs a paired spec decision under the "don't break byte
    formats" rule.
14. `H1` — `StopPlugin` + retry for `Committed`; needs the `OnBlockCommitted` test coverage first
    (§6, "OnBlockCommitted has no test").
15. `C2` / `V5` — position-bound verification, plus un-mocking `UT_SharedBridge_Vm`.
16. `V7` — **settled on this branch (2026-08-31), both decisions made once and applied to both read
    sites.** The read path gets the same bounded wait-and-retry the write and lock-acquisition paths
    already carried (2-second window, 50 ms poll), and the exhausted-`IOException` answer is **yes,
    it belongs in the protocol family**: the read funnels wrap it into `InvalidDataException` with the
    inner exception preserved, so the escaping exception the finding flagged no longer exists. See
    §5 V7's status block.
17. `V8` — **settled by measurement, and the answer is that nothing needs scheduling.** The SP1
    6.2.1 → 6.5.0 bump this queue used to name as the fix does not fix anything: `0.4.3-succinct` and
    `0.3.3-succinct` carry byte-identical copies of both files the advisory names, and `0.4.3-succinct`
    is the highest `-succinct` build of `p3-challenger` that has ever existed (§5 V8). The High stays
    open because it is unpatchable from this graph, not because work is pending. Two bookkeeping
    actions remain, and neither rotates a pin: reconcile `.github/dependabot.yml:26-35` with the
    Security tab (`ignore` suppresses update PRs, not alerts, so all three stay open while the comment
    reads as resolved), and decide whether to ask Succinct to merge Plonky3's `0.4.3` challenger fix
    into the fork. The third sub-action from the original item — assessing `p3-symmetric` in writing —
    is done in §5 V8.
18. `finalizeIfPastWindow` driver — **settled on this branch (2026-08-31): ownership is
    `Neo.Plugins.L2Settlement`'s reconcile cadence, and the driver is implemented.** The chosen
    shape mirrors the forced-inclusion finalizer seam: `ISettlementWindowFinalizer` (Abstractions,
    expiry + finalize), an optional `CanonicalSettlementPipeline` constructor seam, and the
    Challengeable branch of `ReconcileAsync` now checks expiry and submits
    `OptimisticChallenge.FinalizeIfPastWindow` on the first reconciliation pass after the on-chain
    deadline passes, re-reading status and durably marking `SettlementFinalized` when it lands.
    `InMemorySettlementClient` implements the capability against an injectable clock with the
    deadline anchored to submission time (matching `SettlementManagerContract.cs:395`, which opens
    the window inside SubmitBatch); `RpcSettlementWindowFinalizer` reads `getDeadline` via
    `invokefunction`, refuses to broadcast a still-open window, and treats a window that vanished
    mid-send (concurrent finalizer or accepted challenge) as benign so the next status read decides.
    Production wiring is config-gated: a new `OptimisticChallengeHash` plugin setting (validated
    distinct) constructs the RPC finalizer in `L2SettlementProductionComposition` and flows through
    `WireProduction`/`Wire`; empty leaves today's out-of-band behavior unchanged, which is also
    what the no-capability test pins. `ChallengeOrchestrator` deliberately stays adversarial-only.
    No contract change — the entry point was already permissionless; only nobody called it. Tests:
    6 window tests in `UT_InMemorySettlementClient`, 3 driver tests in `UT_CanonicalSettlementPipeline`
    (expired → finalized / window open → no send / no finalizer → legacy), 81 + 171 passing in the
    two touched projects. `docs/launching-an-l2.md` states the ownership and the config key.
19. The `docs/zh/CHANGELOG.md` sync-vs-relabel decision from §6 — **settled on this branch (relabel,
    2026-08-31)**: the header now describes the digest contract it actually operates under (major-change
    index, English authoritative, no entry-for-entry promise), the 2026-08-28 → 2026-08-31 majors are
    backfilled so the page is current, and §6's status block records why the header-compare option was
    rejected on sizing (724 entries).

## 11. Not verified in this pass

- What is left of the .NET ↔ Rust half of §V2 after the third leg landed: nothing that *can* be
  cross-pinned has been left out, but three of the four boundary formats have no Rust counterpart to
  pin against. `bridge/neo-execution-core` rebuilds the 348-byte public-inputs preimage
  (`src/hashing.rs:283-314`, now cross-pinned) and folds Merkle roots (`:36-54`, now cross-pinned); it
  has **no** encoder or parser for the 321-byte commitment header, none for the 91-byte
  `L2ChainConfigSerializer` form, and none for the 48+32·N `MerkleProofSerializer` framing — the only
  sibling array Rust reads is the forced-inclusion leg of the execution payload (`wire.rs:355-373`),
  which carries a `u64` nonce and no path bitmap and is therefore a different encoding. So for those
  three the risk is not drift between two readers, it is that only one implementation exists, and
  no amount of shared data changes that. `StateWitnessV1` was already two-sided before this branch.
- Rebuilding the SP1 guest: no `cargo prove` toolchain here, so `bridge/neo-zkvm-guest`'s current
  artifact was not reproduced (prior `A4`-class risk, unquantified for SP1). This branch widens that
  class with three dispatch-only pins that now describe the *old* public-inputs formula and can only
  be regenerated in the Linux `sp1-release-gates` lane: the guest ELF/VK manifest
  (`vk_manifest.rs`, whose SHA-256 pins cover guest source that this branch changed), the Groth16
  positive vector `tests/fixtures/sp1-groth16-positive-vector-v1.json` (its `publicInputHashHex`
  embeds the 332-byte-formula digest of a real SP1 proof), and the Gateway recursion VK. All three
  stay internally consistent — the vector's own verifier test passes because `Sp1Groth16Verifier`
  takes `publicInputHash` as a parameter — but until that lane re-runs, they prove the old format,
  not the new one.
- True growth curves for `H10`/`H11`: no benchmark harness exists, and creating one was out of
  scope. Devnet numbers on record (5 blocks → 5,624 ms, 20 → 4,803 ms, 40 → 4,161 ms with
  `state entries: 1`) show the per-batch constant cost dominating and cannot separate the ≈5·S state
  scans from persistence; `BatchSealer.cs:258-261`'s stopwatch excludes persistence entirely.
- End-to-end reorg through the `Committed` hook: needs a multi-node setup.
- The five skips that remain after `V4` was repaired are all env gates, and none of them was satisfied
  here, so those lanes stay unexercised: 3 in
  `tests/Neo.L2.Sdk.UnitTests/Conformance/UT_SdkConformance_Live.cs` (`NEO_SDK_LIVE` /
  `NEO_N4_RPC_URL` / `NEO_SDK_LIVE_FIXTURE`, i.e. the live-L1 paths), 1 in
  `tests/Neo.L2.Executor.UnitTests/UT_Sp1StatefulBatchExecutor.cs:303-305` (`NEO_ZKVM_EXECUTOR` must
  point at a real pinned executor binary), and 1 in
  `tests/Neo.Plugins.L2Metrics.UnitTests/UT_L2MetricsPlugin.cs:338-341`, which self-skips when the
  host's resolver answers `does-not-exist.invalid`. Note in particular that `Neo.L2.IntegrationTests`
  has **no** env gate at all — `grep -c Environment.GetEnvironmentVariable` over that project is 0 — so
  the 9 skips it used to report were entirely `V4`, which is what §3 C4 mislabelled.
- `C4` has no VM repro: the wedge is four assertions read end to end across two contracts, and the
  submit → challenge → resubmit sequence has never been executed. It is marked [E2] for that reason.
  **Superseded the same day** — the sequence now runs in `UT_OptimisticChallenge_Vm` and fails without
  the fix (§3 C4 status). The part that survives: no test deploys `SettlementManager` and
  `OptimisticChallenge` as two real contracts, so `SubmitBatch`'s resubmit branch is still read, not run.
- The NCCS compilation semantics of the `uint` addition at `ForcedInclusionContract.cs:374` — wrap,
  saturate or fault — were not determined, so `H19` states both branches instead of picking one. A
  single VM test settles it. **Settled the same day** — `EnqueueDeadlineSum_TruncatesModuloTwoTo32
  InsteadOfFaulting` measured mod-2³² truncation behind a halting VM, and §4 H19's status block
  records what that leaves live.
- Telemetry and the RPC surface were verified by counted greps against source and docs (§5 V6, §9),
  not by scraping a live `/metrics` endpoint from a running node. If the endpoint and the catalog
  diverge at runtime, this pass would not see it.
- The `docs/telemetry.md:214-226` sample exposition was compared to the exporter's rendering rules by
  reading `PrometheusExporter.cs`, not by generating real output.
- `V8` still stops short of one thing it could name but not settle: whether the claimed-value count
  hashed at `slop-merkle-tree-6.2.1/src/tcs.rs:146` is steerable by an SP1 adversary — the one
  precondition that would raise `p3-symmetric` from Low to something interesting. That needs a reading
  of SP1's recursion query shape, not of this repository.
- The second question this bullet used to carry was "would an SP1 6.5.0 bump close
  GHSA-vj64-rjf3-w3v7?" It has been settled, and settling it corrected a claim this same report had
  already published: §5 V8 originally named that bump as the fix path. It is not — the fork tags differ
  and the advisory's two source files do not (§5 V8). What was measured is the crate content; what was
  *not* measured is Dependabot's own range semantics for a `-succinct` prerelease against `< 0.4.3`, so
  the alert may still move on a bump. Given the identical bytes, that movement would carry no security
  meaning either way, which is why it did not get chased down.
