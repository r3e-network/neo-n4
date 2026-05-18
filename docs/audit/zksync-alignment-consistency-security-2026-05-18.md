# ZKsync Alignment, Consistency, and Security Pass - 2026-05-18

This pass verifies the current Neo N4 repository against three boundaries:

- implementation and documentation must describe the same architecture;
- the architecture must remain aligned with ZKsync Elastic Chain where the
  design intentionally borrows its shared L1 + L2 system-contract model;
- security-sensitive paths must have explicit authorization, replay, and
  supply-chain evidence.

## Official ZKsync Baseline

Official sources checked during this pass:

- ZKsync L1 ecosystem contracts:
  https://docs.zksync.io/zksync-protocol/contracts/l1-contracts/l1-ecosystem-contracts
- ZKsync shared bridges:
  https://docs.zksync.io/zksync-protocol/contracts/l1-contracts/shared-bridges
- ZKsync EraVM system contracts:
  https://docs.zksync.io/zksync-protocol/era-vm/contracts/system-contracts

The alignment baseline is unchanged: ZKsync uses shared L1 contracts for chain
registration, bridge routing, proof verification, governance, DA, and shared
liquidity, while L2 system contracts exist from genesis and own privileged L2
state. Neo N4 maps this to `NeoHub.*` L1 anchor contracts plus Neo core native
L2 system contracts in the r3e `external/neo` fork.

## Findings Closed

### F1 - Stale NeoHub deploy-count documentation

Current implementation state:

- `contracts/NeoHub.*`: 23 projects.
- `ScaffoldPlan.Default()`: 22 production deploy steps.
- `NeoHub.ExternalBridgeStubVerifier`: test-only project, intentionally
  excluded from the production deploy bundle.
- `contracts/L2Native.*`: 0 projects.

The following current docs had stale "20 contracts" wording or incomplete
contract inventory:

- `README.md`
- `docs/README.md`
- `docs/architecture-l2-lifecycle.md`
- `docs/zh/architecture-l2-lifecycle.md`
- `contracts/README.md`
- `docs/audit/deployment-rehearsal-2026-05-17/README.md`

Fix: all current docs now use the 22 production / 23 total NeoHub boundary, and
`contracts/README.md` lists every `NeoHub.*` project.

### F2 - Chinese ZKsync comparison lagged the implementation

`docs/zh/zksync-comparison.md` still marked several already-implemented
features as gaps. It now matches the English comparison for:

- `MessageRouter.SetL1TxFilter` + `NeoHub.L1TxFilter`;
- `NeoHub.DAValidator`;
- staged governance windows in `GovernanceController`;
- Neo core native `BridgedNep17Contract`;
- Neo core native `L2AccountAbstraction`;
- Neo core native `L2InteropVerifier`.

The remaining tracked ZKsync comparison gaps are intentionally product-surface
gaps, not protocol correctness gaps: more app samples and Python / Go SDKs.

## Security Review Notes

Threat model reviewed in this pass:

- L1 anchor contracts must preserve owner/governance authorization.
- Deploy tooling must not include dev/test verifiers in production bundles.
- Bridge and message paths must reject replays and invalid bridge kinds.
- L2 system contracts must be native genesis contracts, not later-deployed
  DevPack projects.
- Package ecosystems must not report known high-severity vulnerable
  dependencies in .NET or TypeScript; RustSec is covered by CI when local WSL
  network access cannot fetch the advisory database.

Evidence observed:

- `ExternalBridgeRegistry` accepts only bridge kinds `1`, `2`, and `3`.
  `ExternalBridgeStubVerifier.BridgeKind()` returns `0`, so the devnet stub is
  rejected by the production registry path.
- `ScaffoldPlan.Default()` excludes `ExternalBridgeStubVerifier`.
- `UT_ProductionGapClosure` now verifies 10 native contracts in
  `external/neo/src/Neo/SmartContract/Native/`, 0 active `contracts/L2Native.*`
  projects, the 23/22 NeoHub boundary, and current-doc count consistency.
- `dotnet list Neo.L2.sln package --vulnerable --include-transitive` reported
  no vulnerable packages for all listed projects.
- `npm audit --audit-level=high` under `sdk/typescript` reported
  `found 0 vulnerabilities`.
- `dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo` completed with
  0 warnings and 0 errors.
- `dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo --no-build` completed
  with all test projects passing.
- `mdbook build` completed successfully.
- `nccs` compiled all 23 `NeoHub.*` projects and 2 `Sample.*` projects to
  `.nef` + `.manifest.json` artifacts.
- Local `cargo audit` could not fetch RustSec from WSL because the environment
  could not reach GitHub from WSL NAT mode; the repository CI keeps `cargo
  audit` as the Rust supply-chain gate.

## Verification Commands

Commands executed locally:

```powershell
dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj /p:NuGetAudit=false --nologo --filter FullyQualifiedName~UT_ProductionGapClosure
dotnet test external\neo\tests\Neo.UnitTests\Neo.UnitTests.csproj /p:NuGetAudit=false --nologo --filter FullyQualifiedName~UT_L2NativeContracts
dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo
dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo --no-build
dotnet list Neo.L2.sln package --vulnerable --include-transitive
npm audit --audit-level=high
wsl.exe -e bash -lc "cd /mnt/d/Git/neo-n4 && mdbook build"
```

The full build, test, docs, and CI matrix should be read together with this
report before release tagging.
