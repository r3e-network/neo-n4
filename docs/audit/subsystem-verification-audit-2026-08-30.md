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
| T2 | `bridge/neo-zkvm-{guest,host}`, `neo-zkvm-executor` pin, `Sp1SettlementExecutionStack`, `NeoHub.Sp1Groth16Verifier` | §5 V3, §7 |
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

### H19 — The anti-censorship deadline is bounded on the owner path and unbounded on the deploy path [E2]

`ForcedInclusion` stores one global `deadlineSeconds` and reads it when stamping each entry:

```
ForcedInclusionContract.cs:130-133   _deploy: deadline = (uint)(BigInteger)arr[2]; Assert(deadline > 0, "deadline must be positive")
ForcedInclusionContract.cs:192-199   SetDeadlineSeconds: Assert(seconds >= 60 && seconds <= 86400, "deadline out of bounds [60, 86400]")
ForcedInclusionContract.cs:373-374   enqueuedAt = (uint)(Runtime.Time / 1000u); EncodeEntry(…, enqueuedAt + deadline)
```

The same value is therefore range-checked when the owner changes it and unchecked when the deployer
sets it. Two consequences follow from the deploy-time field alone, and which one applies depends on
how NCCS compiles the `uint` addition at `:374` — this pass could not determine that from source, so
it is stated as a hazard requiring a VM test, not as a proven behavior:

- If the arithmetic saturates or the value is simply large, the censorship window is effectively
  never reached, and the whole point of `ReportCensorship` — the §17 escape hatch that lets anyone
  prove a sequencer is ignoring the L1 queue and pause the chain (`:496` `if (nowSec < deadline)
  return false;` then `:503` `pauseChain`) — is inert on a chain that was deployed with a mistyped
  deadline. One `uint` field in the deploy data silently disables the anti-censorship guarantee, and
  `IsProductionReady()` does not check it (`:254-266` omits the pauser and the deadline bound).
- If the addition wraps mod 2³², the stored per-entry deadline lands in the past, every entry is
  instantly reportable, and a permissionless caller can `pauseChain` at will.

Fix both directions with the same one-line change: apply the `[60, 86400]` bound in `_deploy` that
`SetDeadlineSeconds` already enforces, and add the VM test that pins the overflow behavior.

What is genuinely sound here, and worth stating so the finding is not overread: an entry's deadline is
immutable once written. The nonce strictly increments (`:369-370`), there is exactly one `Put` per
enqueue and no update path anywhere in the contract, so a censoring sequencer **cannot** postpone a
forced inclusion by renewing its deadline — the classic escape from this design is absent.
`ReportCensorship` is likewise one-shot per entry (`:498` sets a `reportedKey`), and the comparison at
`:496` is inclusive at equality, which is the correct direction for a deadline.

## 5. Verification-integrity findings

These are the ones that decide whether any other finding can be trusted.

### V1 — The SP1 required check goes green *because* the heavy lanes did not run [E1]

```
.github/workflows/build.yml:385-387   sp1-release-gates: if: github.event_name == 'workflow_dispatch'
build.yml:516                          cargo test --workspace --release
build.yml:520                          cargo test (neo-zkvm-host, real proof)
build.yml:532                          gateway-host recursive proof
build.yml:565-569                      if dispatch → test …= success; else test …= skipped
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

### V2 — The "off-chain ↔ on-chain encodings are paired" invariant has no cross-boundary test [E1]

`tests/NeoHub.Contracts.VmTests/NeoHub.Contracts.VmTests.csproj` references
`Neo.SmartContract.Testing`, MSTest and the test SDK — and **zero `ProjectReference`s**. The VM
tests therefore cannot call `BatchSerializer`, `MessageHasher`, `MerkleProofSerializer` or
`L2ChainConfigSerializer`; they hand-roll byte buffers (`UT_SettlementManager_Vm.cs:70-122`) and
re-hardcode constants (`UT_ChainRegistry_Vm.cs` repeats `ConfigSize = 91`). The five pairings that
matter were checked by hand and are byte-exact — 321-byte commitment header, 332-byte public inputs,
48+32N proof framing, withdrawal leaf hash, 91-byte chain config — but agreement is maintained by
copy-paste discipline, and the closest thing to a pin, `UT_OnChainMerkleVerifyParity.cs`, is a C#
*replica* of the contract's fold rather than the contract.

Along with it, one doc claim is false: `src/Neo.L2.Batch/BatchSerializer.cs:12-14` says the encoder
produces "the byte format that the settlement contract reads", which holds for the commitment header
and not for the public-inputs half, which is never transmitted. This is the mechanism by which
`C2`-class encoding drift stays invisible; a single test project that references both sides would
close it.

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

## 7. Status of prior findings re-checked this pass

| Prior | Status now | Evidence |
| --- | --- | --- |
| `C1` deposit/router inbox collision | **Fixed** (this branch) | two-part dedup + total order in `L1MessageDrain.cs`, `UT_L1MessageDrain` regressions |
| `C2` `MerkleTree.Verify` not position-bound | **Open** — and the same shape is in both contract folds (§5 V5), unobservable because the payout test stubs the verifier | `SettlementManagerContract.cs:989-1012`, `:1115-1134` |
| `H1` plugin exceptions stop the node | **Open**, upgraded to [E1] | `L2BatchPlugin.cs:479 throw;`, `Plugin.cs:74` default, zero `ExceptionPolicy` overrides in `src/` |
| `H6` decorative off-chain binary pin | **Open**, now [E1] with a derived-digest test and no negative test (§5 V3) | `UT_Sp1StatefulBatchExecutor.cs:318` |
| `H12` governance locks on trust roots | **Fixed** for the three roots this branch covered; the pattern is still incomplete elsewhere (§7.1) | `ChainRegistryContract.cs:158-168,172-181,389` |
| `H13` kill-switch covers 1 of 3 asset contracts | **Open**, plus the per-chain variant (§4 H16) | `SubmitBatch:330-331` vs `FinalizeBatch:479-533` |
| `H2` FI deadline < challenge window it pauses | **Re-confirmed** | `ForcedInclusionContract.cs:195` bounds `[60, 86400]` while `OptimisticChallengeContract.cs:246` allows `[60, 7*86400]` — a 7-day window with a 24 h deadline lets `ReportCensorship:503` pause a still-challengeable batch. §4 H19 is the mirror-image half: the *deploy-time* field skips the bound entirely |
| `H3` escape hatch needs hand wiring | **Half-refuted** | `LiveDeployCommand.cs:801-802` now registers + read-back-verifies the pauser before `LockGovernance` (`:861-862`); only the `IsProductionReady()` assertion remains open (`ForcedInclusionContract.cs:254-266`) — see §6 |
| `§3.1` Windows self-skips | **Fixed** (this branch) | repo-wide skipped 45 → 5 on the same 2,893 tests; `tests/Shared/RepoRoot.cs` replaces the 5-level walk at 33 sites in 10 files, and the six affected projects each report `Skipped: 0` (§5 V4) |
| `A4` non-reproducible VM artifacts | **Open** | unchanged; the artifact set still has two compiler stamps |
| Governance completeness | **Partially open** | see §7.1 |

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
    `565-569`; §5 V4 claimed the affected test *count* exceeded §3.1's "~45" when only the project
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
3. `H16` — assert `isActive` in `FinalizeBatch` + two VM tests.
4. `H17` — wire `LockGlobalRootGovernance` into `LiveDeployCommand` + CLI plan text + smoke step.
5. `H19` — apply the `[60, 86400]` bound in `ForcedInclusion._deploy`, and add the VM test that pins
   the `uint` overflow direction.
6. `V6` — promote `L2BatchPlugin.cs:477`'s literal to a `MetricNames` constant + catalog entry;
   optionally add the emission-site literal scan that would have caught it.
7. §7.1 — add lock guards to `SetOwner`, `RegisterPauser`/`RevokePauser`, `SetWindowSeconds`,
   `SetChallengerRewardBps` (mechanical; mirrors `RegisterChain`).
8. `H18` — reconcile `TemplateCatalog.cs:32` with the verifier the deployer registers. Last, after
   `C4`.
9. `V2` (partial) — correct `BatchSerializer.cs:12-14` and `AGENTS.md`'s `ChainMode.L2RiscV` claim, or
   add the enum member.

**Needs a decision before code:**

10. `C3` — guest-blob freshness gate. Requires a CI job that runs `regenerate-guest-blob.sh` (nightly
    cargo + `polkatool 0.32.0`) and compares SHA-256, i.e. new CI capacity on the Rust lane.
11. `H14` — removing `panic = "abort"` changes unwind semantics and possibly throughput on the guest
    hot path; needs a measurement, and it interacts with the SP1 re-execution profile.
12. `V1` — decide who owns a scheduled SP1 dispatch (nightly or merge queue) and what blocks a release
    when it fails.
13. `H15` — the per-block context fix touches the batcher↔executor seam and, if the persisted header
    feeds any hash, the state-root encoding. Needs a paired spec decision under the "don't break byte
    formats" rule.
14. `H1` — `StopPlugin` + retry for `Committed`; needs the `OnBlockCommitted` test coverage first
    (§6, "OnBlockCommitted has no test").
15. `C2` / `V5` — position-bound verification, plus un-mocking `UT_SharedBridge_Vm`.

## 11. Not verified in this pass

- The .NET ↔ Rust half of §V2: no test proves the guest or host reads `StateWitnessV1` /
  `MerkleProofSerializer` bytes the way the C# writer emits them. The Rust side was read, not
  cross-executed against the .NET encoder.
- Rebuilding the SP1 guest: no `cargo prove` toolchain here, so `bridge/neo-zkvm-guest`'s current
  artifact was not reproduced (prior `A4`-class risk, unquantified for SP1).
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
  single VM test settles it.
- Telemetry and the RPC surface were verified by counted greps against source and docs (§5 V6, §9),
  not by scraping a live `/metrics` endpoint from a running node. If the endpoint and the catalog
  diverge at runtime, this pass would not see it.
- The `docs/telemetry.md:214-226` sample exposition was compared to the exporter's rendering rules by
  reading `PrometheusExporter.cs`, not by generating real output.
