---
feature: production-ready-audit-wave
status: delivered
updated: 2026-09-09
branch: master
commits: 13f91058..335485ba
---

# Production-Ready Audit Wave

## Report

**What was built** — This wave stabilized the uncommitted Wave-1/2 architecture iteration into a single green revision and closed the last cross-language public-inputs wire gap. `neo_execution_core::PublicInputs` now carries `forced_inclusion_count`; Rust wire encode/decode matches the C# 352-byte `BatchSerializer` layout; host `prove_compressed` / `prove-batch` rebuild the expected PI hash with the artifact's FI count instead of a hard-coded zero. Shared guest fixtures were regenerated, the out-of-band golden hash was recomputed (`805b89e2…dd22c` with FI count 0), and `SubmitBatchCore` clears CS8604 without weakening runtime length asserts. Documentation gates are green: no invented fifth `ChainMode` member, and every English markdown/figure required by `UT_ProductionGapClosure` has a Chinese counterpart with CJK text (including a localized architecture SVG). CHANGELOG and IMPLEMENTATION_STATUS record the delivered state without claiming mainnet-ready.

**Verification** — `dotnet build Neo.L2.sln /p:NuGetAudit=false` exit 0 (0 warnings). Full suite `dotnet test Neo.L2.sln` **3,105 passed / 0 failed / 5 PRE-EXISTING skips** (one intermediate full-suite run flaked a single `UT_Sp1BatchProofProver` case under parallel load; three isolated re-runs and the final full suite were 86/86 and 0 failed). Hub.Deploy 111/111, Proving 86/86. `cargo test -p neo-execution-core` and `cargo test -p neo-zkvm-guest` pass. Host crates (`neo-zkvm-host` / `neo-zkvm-gateway-host`) do not compile on Windows because `sp1-jit` requires unix — PRE-EXISTING platform limit; the FI-hash source fix is in-tree.

**Journey log** — 1) MSBuild node-reuse can carry a process env missing `ProgramFiles`, which makes NuGet `_GetRestoreSettings` throw `path1` null; `dotnet build-server shutdown` + explicit env is required on this machine. 2) Wave 2 extended the PI **hash** preimage to 352 bytes but left the Rust **wire** encoding at 348 — the C# artifact decoder then truncated `contentHash` by 4 bytes; hash and wire must move together. 3) The dual API `hash_public_inputs` (FI=0) vs `hash_public_inputs_with_forced` is a live footgun; host/gateway call sites must pass the artifact count. 4) Doc-counterpart gates skip untracked files via `git ls-files`; new English docs need zh twins before commit. 5) Figure CJK checks only scan `docs/zh/figures/**`, so `docs/zh/*.svg` can silently stay English.

## [S1] Problem

`neo-n4` is architecturally complete for a local/devnet elastic-chain stack, but the
working tree currently holds a large uncommitted Wave-1/2 architecture iteration
(~58 modified files, plus new persistence/settlement sources) that has not been
stabilized as a single reviewed revision. A full "production-ready" claim remains
bounded by operator/funded gates and upstream Neo core items. This wave must:

1. Stabilize the dirty Wave-1/2 tree so build + tests are green on the current revision.
2. Close remaining in-repo structure/consistency gaps that this repository owns
   (not upstream core, not live funded deployment).
3. Leave a durable feature document, CHANGELOG/status updates, and a reviewable
   evidence trail.

## [S2] Design

### Workspace decision

- Continue in the main worktree `D:\Git\neo-n4` on `master` (user-approved).
- No new linked worktree for this iteration.
- Treat the existing uncommitted Wave-1/2 work as the base of this feature.

### Environment constraint (Windows)

This machine's MSBuild node-reuse cache can carry a broken process environment
missing `ProgramFiles` / `ALLUSERSPROFILE`, which makes NuGet `_GetRestoreSettings`
throw `Value cannot be null. (Parameter 'path1')`. Before any restore/build:

1. `dotnet build-server shutdown`
2. Set `ProgramFiles`, `ProgramW6432`, `ProgramFiles(x86)`, `PROGRAMDATA`,
   `ALLUSERSPROFILE`, `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `MSBUILDDISABLENODEREUSE=1`

### Stabilization contract

- `dotnet restore` + `dotnet build Neo.L2.sln /p:NuGetAudit=false` must succeed.
- `dotnet test Neo.L2.sln /p:NuGetAudit=false` must have **zero failed tests**.
  Known production-environment skipped tests (live SDK / real native) remain skipped
  and are labeled `PRE-EXISTING` if they appear as skipped.
- Rust crates that participate in the Wave-1/2 change surface (`bridge/neo-execution-core`,
  `bridge/neo-zkvm-guest` tests where toolchain permits) must compile and pass their
  unit tests when cargo is available.

### In-repo gap closure (this wave)

Priority order after green build:

1. **Encoding / witness consistency** — confirm Wave-1/2 public-input (352-byte),
   inventory-hash, and witness splits stay pinned by tests (`UT_PublicInputsWireDigestGate`,
   canonical encoding vectors, Rust witness tests).
2. **Persistence split completeness** — `ProofWitnessStore.cs` was split into
   `KeyValueProofWitnessStore` + models + serializers; ensure no dangling references
   and the project still builds clean under TreatWarningsAsErrors.
3. **Settlement composition consistency** — lean LiveDeploy + production composition
   must reject deleted micro-contracts and keep optimistic advisory-only.
4. **Docs/status honesty** — `IMPLEMENTATION_STATUS.md`, `CHANGELOG.md`, and the
   architecture-iteration audit docs must describe the delivered Wave-1/2/3 state
   without claiming mainnet-ready.
5. **If tests reveal defects** — fix in the smallest change that preserves the
   Wave-1/2 contracts; add regression tests for behavior bugs.

### Explicitly out of scope

- Upstream Neo core `ChainMode` items in `r3e-network/neo`.
- Live funded testnet/mainnet deployment evidence and HSM/KMS operator credentials.
- General NeoVM multi-tx fraud-proof protocol (still restricted v4).
- Go SDK and additional sample dApps.
- Halo2/Risc0 alternate round provers.

## [S3] Out of Scope

See above. This wave does not change `doc.md` wire formats beyond what the
already-uncommitted Wave-1/2 work already pins (352-byte public-input domain,
shared-bridge lean mappings). Any further encoding change requires a new spec.

## Tasks

- [x] T1: Write and keep this feature document current — acceptance: spec exists under `docs/compose/spec/` with stable S1/S2/S3 anchors (covers: S1)
- [x] T2: Stabilize restore/build of `Neo.L2.sln` on this dirty tree — acceptance: clean restore + build exit 0 (covers: S2)
- [x] T3: Run full .NET test inventory and fix failures — acceptance: zero failed tests; skips labeled PRE-EXISTING (covers: S2; depends: T2)
- [x] T4: Run Rust surface tests for changed crates if toolchain allows — acceptance: `cargo test` for `neo-execution-core` passes (covers: S2; depends: T2)
- [x] T5: Close residual in-repo consistency gaps found by tests/review — acceptance: 352-byte PI wire domain aligned C#/Rust; fixtures regenerated; docs/zh gates green; CS8604 cleared (covers: S2; depends: T3)
- [x] T6: Update CHANGELOG + IMPLEMENTATION_STATUS + iteration audit notes — acceptance: docs match delivered state, no false production-ready claim (covers: S2; depends: T5)
- [x] T7: Independent review of the complete change — acceptance: reviewer conclusions on spec compliance, correctness, consistency; criticals fixed (covers: S1; depends: T5)
- [x] T8: Finalize this document (`status: delivered`, report, commit range) — acceptance: Report filled and tasks checked (covers: S1; depends: T7)
