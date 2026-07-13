# Test Coverage

Neo N4 uses an evidence-first test strategy rather than a single percentage claim.

## Gates

- `.NET`: `scripts/test-coverage.ps1` builds the platform `neo_riscv_host` library with locked Cargo dependencies, places it beside the Release RISC-V test assembly, requires every real-native test to execute, runs the full `Neo.L2.sln` suite with `coverlet.collector`, merges Cobertura reports by source file/line, and applies two gates: 90% line coverage for protocol/runtime source roots and 80% overall reported line coverage. A missing or unloadable native library fails the run; it is never converted into a skipped coverage result.
- `Contracts`: CI builds every `contracts/NeoHub.*` and `samples/contracts/Sample.*` project, compiles each with `nccs`, and verifies `.nef` plus `.manifest.json` artifacts.
- `Neo core fork`: CI runs `UT_L2NativeContracts` in the r3e Neo core submodule.
- `Rust`: CI runs workspace tests, clippy, PolkaVM host checks, watcher feature checks, and SP1 host/guest checks.
- `Foreign chains`: CI runs Foundry tests for the EVM router and Cargo tests for the Solana router.
- `Experience Hub`: CI runs the Node test suites for hub state, manifest generation, and report schema redaction.
- `Docs`: CI runs `mdbook build`; production-gap tests enforce English/Chinese documentation counterparts.

## Local Commands

```powershell
dotnet test .\Neo.L2.sln -c Release /p:NuGetAudit=false --nologo
.\scripts\test-coverage.ps1 -Configuration Release -ResultsDirectory coverage/dotnet-local -Threshold 90
```

The raw coverage output is written under `coverage/`, which is intentionally ignored. The script writes `dotnet-line-coverage-summary.json` in the selected results directory for audit review.
The summary records the exact platform-native library path and SHA-256 under `nativeRiscVHost`. Local runs therefore require the Rust toolchain in addition to .NET 10.

## Scope

The .NET coverage gate reports line coverage for first-party source files emitted by Coverlet from the full solution test run. The strict 90% gate is scoped to `src/` and `samples/executors`, where protocol, runtime, proof, bridge, DA, state, and executor logic live. CLI and deployment tools are still measured in `reportedLineCoverage`, but they use an 80% overall reported gate because live deployment commands include RPC and operational entry points that are better validated by planner tests, dry-runs, manifests, and testnet evidence.

The summary also contains a `sourceFileAudit` section that lists first-party `src/`, `tools/`, and `samples/executors` files that were not reported by Cobertura, so review does not confuse measured line coverage with total repository file coverage.

Neo smart contracts are not judged primarily by line coverage. They are validated by compiler artifacts, manifest invariants, deployment planner tests, and testnet RPC evidence.
