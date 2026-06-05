# System verification ledger — 2026-06-05

This ledger records the first module-gated validation pass after adding the
Neo N4 system verification plan.

## Run context

| Field | Value |
| --- | --- |
| Date | 2026-06-05 |
| Commit | `9d44d4d` |
| Workspace | `/Users/jinghuiliao/git/r3e/neo-n4` |
| .NET SDK | `10.0.107` initially; `10.0.108` after Homebrew remediation |
| Node.js | `v24.4.1` |
| Rust | `rustc 1.91.0-nightly (523d3999d 2025-08-30)` |

## L0 smoke gate

| Module | Command | Result | Evidence |
| --- | --- | --- | --- |
| .NET solution baseline | `dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo` | ✅ Pass | 0 warnings, 0 errors; all solution projects built. |
| .NET unit/integration tests | `dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo --no-build` | ✅ Pass | All discovered test projects passed with 0 failures. |
| Devnet smoke | `dotnet run --project tools/Neo.L2.Devnet -- 3` | ✅ Pass | 3 batches sealed; audit passed; 3 deposits; 3 withdrawals; 3 multisig proofs; NeoFS-like DA payloads available. |
| Documentation site | `mdbook build` | ✅ Pass | HTML book written to `/Users/jinghuiliao/git/r3e/neo-n4/book`. |

## Devnet evidence highlights

- Chain `1001`, 3 batches.
- Final state root:
  `0x09cd9533c2aa48aca97b768db68f2e43bcac1f92efb9a27acad239a88d78e0fa`.
- Security label:
  `securityLevel=Optimistic daMode=NeoFS sequencer=DbftCommittee exit=Permissionless gateway=False`.
- Final Alice balance: `5940000`, matching expected value.
- Audit checks passed:
  continuity, non-zero proof, proof verification, batch ranges, DA availability.
- Metrics emitted:
  `l2.batch.sealed=3`, `l2.bridge.deposits=3`,
  `l2.bridge.withdrawals=3`, `l2.proving.generated{kind=Multisig}=3`.

## Follow-up gates

## Environment remediation

| Tool | Action | Result |
| --- | --- | --- |
| PowerShell | Installed with `brew install powershell` after `pwsh` was missing. | ✅ `pwsh 7.6.2` available. Homebrew also installed `dotnet 10.0.108`, so subsequent .NET commands used SDK `10.0.108`. |

## L1 daily gate

| Module | Command | Result | Evidence |
| --- | --- | --- | --- |
| .NET format | `dotnet format Neo.L2.sln --verify-no-changes --exclude external/` | ✅ Pass | No formatting changes required. |
| .NET coverage | `pwsh ./scripts/test-coverage.ps1 -Configuration Release -ResultsDirectory coverage/dotnet-local -Threshold 90 -OverallThreshold 80` | ✅ Pass | Gate line coverage `4242 / 4651 = 91.21%`; overall reported line coverage `6026 / 7194 = 83.76%`; 34 Cobertura files. |
| TypeScript SDK | `npm ci --no-audit --no-fund && npm test && npm run build` in `sdk/typescript` | ✅ Pass | 76 packages installed; Vitest `16 / 16` passed; `tsc` build passed. |
| Rust SDK | `cargo build && cargo test` in `sdk/rust` | ✅ Pass | Build passed; integration tests `10 / 10` passed. |
| zkVM guest host-mode | `cargo build && cargo test` in `bridge/neo-zkvm-guest` | ✅ Pass | Build passed; guest tests `8 / 8` passed. |

Coverage summary:
`/Users/jinghuiliao/git/r3e/neo-n4/coverage/dotnet-local/dotnet-line-coverage-summary.json`.

## L2 release gate

| Module | Command | Result | Evidence |
| --- | --- | --- | --- |
| NeoHub + sample contract artifacts | `dotnet build` + `nccs --output bin/sc` for `contracts/NeoHub.*` and `samples/contracts/Sample.*` | ✅ Pass | All 26 deployable contracts produced `.nef` and `.manifest.json` artifacts. |
| NeoHub manifest invariants | `NEO_N4_REQUIRE_FRESH_MANIFESTS=1 dotnet test tests/Neo.Hub.Deploy.UnitTests/Neo.Hub.Deploy.UnitTests.csproj --filter "FullyQualifiedName~UT_ContractManifestInvariants\|FullyQualifiedName~UT_OptimisticChallengeAllowlist"` | ✅ Pass | `17 / 17` tests passed. |
| Neo core L2 native contracts | `dotnet test external/neo/tests/Neo.UnitTests/Neo.UnitTests.csproj --filter FullyQualifiedName~UT_L2NativeContracts /p:NuGetAudit=false --nologo` | ✅ Pass after fix | `10 / 10` tests passed. |
| Neo core mempool regression | `dotnet test external/neo/tests/Neo.UnitTests/Neo.UnitTests.csproj --filter FullyQualifiedName~UT_MemoryPool /p:NuGetAudit=false --nologo` | ✅ Pass | `28 / 28` tests passed, including the two new lock-regression tests. |
| RISC-V host | `cd external/neo-riscv-vm && cargo check -p neo-riscv-host` | ✅ Pass | `neo-riscv-host` checked successfully with the workspace toolchain. |
| ETH watcher default | `cd watchers/neo-bridge-watcher-eth && cargo build --release && cargo test --release` | ✅ Pass | Library, daemon smoke, parity, and preflight tests passed; `95` total tests. |
| ETH watcher live-rpc | `cd watchers/neo-bridge-watcher-eth && cargo build --release --features live-rpc && cargo test --release --features live-rpc` | ✅ Pass | Same release test matrix passed with `live-rpc` enabled. |
| TRON watcher | `cd watchers/neo-bridge-watcher-tron && cargo build --release && cargo test --release` | ✅ Pass | Unit, parity, and doc tests passed; `7` total tests. |
| Solana watcher | `cd watchers/neo-bridge-watcher-sol && cargo build --release && cargo test --release` | ✅ Pass | Unit and parity tests passed; `9` total tests. |
| ETH foreign router | `cd external/foreign-contracts/eth && forge fmt --check && forge test -vv` | ✅ Pass | Installed Homebrew Foundry `1.7.1`; cloned gitignored `lib/forge-std`; Foundry passed `39 / 39` tests. |
| Solana foreign router | `cd external/foreign-contracts/sol && cargo test` | ✅ Pass | Anchor/Solana router tests `22 / 22` passed. |

## L2 expensive proof gate

| Module | Command | Result | Evidence |
| --- | --- | --- | --- |
| SP1 guest ELF | `cd bridge/neo-zkvm-guest && cargo prove build` | ✅ Pass | Built RISC-V ELF at `target/elf-compilation/riscv64im-succinct-zkvm-elf/release/neo-zkvm-guest`. |
| Top-level Rust workspace | `cargo fmt --all -- --check && cargo clippy --workspace --all-targets --locked -- -D warnings && cargo test --workspace --release --locked` | ✅ Pass | Format, clippy, release tests, and doc tests passed; non-ignored zkVM host end-to-end tests passed. |
| Real SP1 proof | `cd bridge/neo-zkvm-host && cargo test --release --locked -- --ignored --nocapture` | ✅ Pass | `2 / 2` ignored real-proof tests passed; proof generation `28.30s`, proof verification `13.05s`, tampered public-input hash rejected. |

## Fixes made during verification

- Fixed `MemoryPool.Count` in `external/neo/src/Neo/Ledger/MemoryPool.cs` so a read lock is released with `ExitReadLock`, not `ExitWriteLock`.
- Fixed `MemoryPool.Clear` in `external/neo/src/Neo/Ledger/MemoryPool.cs` so a write lock is released with `ExitWriteLock`, not `ExitReadLock`.
- Added regression coverage in `external/neo/tests/Neo.UnitTests/Ledger/UT_MemoryPool.cs` for empty-pool block persist and `Clear()` lock release.

## Final regression sweep

| Module | Command | Result | Evidence |
| --- | --- | --- | --- |
| .NET solution baseline | `dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo` | ✅ Pass | Re-run after the `external/neo` MemoryPool fix; 0 warnings, 0 errors. |
| .NET unit/integration tests | `dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo --no-build` | ✅ Pass | Re-run after the `external/neo` MemoryPool fix; all discovered test projects passed with 0 failures. |
| Documentation site | `mdbook build` | ✅ Pass | Re-run after ledger updates; HTML book written to `/Users/jinghuiliao/git/r3e/neo-n4/book`. |
| Diff hygiene | `git diff --check && git -C external/neo diff --check` | ✅ Pass | No whitespace errors in the top-level repo or the `external/neo` submodule diff. |
