# Repository-wide consistency, localization, and security pass

Date: 2026-05-18

## Scope

This pass reviewed the neo-n4 repository responsibility boundary:

- 840 tracked main-repository files after this pass.
- 71 English Markdown documents in the main repository scope.
- 48 English SVG / diagram assets under the maintained documentation tree.
- 23 `contracts/NeoHub.*` deployable L1 projects.
- 22 production NeoHub deploy-plan steps, excluding the test-only `NeoHub.ExternalBridgeStubVerifier`.
- 10 N4 L2 native contracts in the r3e Neo core fork at `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`.
- .NET solution projects, TypeScript SDK, Rust workspace crates, watcher crates, foreign bridge contracts, and mdBook docs.

Out of scope for the localization gate: generated `artifacts/`, vendored `external/foreign-contracts/eth/lib/forge-std`, and external submodule internals (`external/neo`, `external/neo-devpack-dotnet`, `external/neo-riscv-vm`, `external/neo-zkvm`). Those are upstream or generated inputs, not primary neo-n4 maintained documentation.

## Corrections made

- Added Chinese counterparts for every maintained English Markdown document that did not already have one.
- Added Chinese counterparts for the visual-guide SVG set under `docs/zh/figures/visual-guide/`.
- Updated `docs/zh/visual-guide.md` to reference the Chinese SVG assets rather than English SVG assets.
- Added unit-test enforcement that every maintained English Markdown document has a `docs/zh/...` counterpart.
- Added unit-test enforcement that Chinese Markdown counterparts contain Chinese text.
- Added unit-test enforcement that every maintained English figure / diagram asset has a Chinese counterpart.
- Added unit-test enforcement that Chinese SVG counterparts contain Chinese text.
- Removed generated `TODO` markers from the `neo-stack scaffold-executor` production template.
- Hardened `neo-external-bridge genkey` private-key output to use atomic `FileMode.CreateNew` instead of a check-then-write sequence.

## Security review notes

No new reportable high-impact security issue remains from this pass.

Checked high-risk surfaces included:

- Private key generation and output handling.
- JSON-RPC HTTP clients and response-id validation.
- Metrics HTTP listener request bounds and status text handling.
- Contract inventory, deploy-plan boundaries, and native-contract placement.
- Secret-pattern scan across maintained source and docs.
- Placeholder / incomplete-code scan across maintained production code.
- Markdown link integrity for maintained docs.
- Dependency advisory checks for .NET and TypeScript, plus Rust workspace test coverage.

Residual external gate:

- Local WSL `cargo audit` could not fetch RustSec due the WSL-to-GitHub network path. The CI workflow includes `cargo audit`; this pass records the local failure as an environment limitation rather than a code finding.

## Verification evidence

Commands run during this pass:

```powershell
dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj /p:NuGetAudit=false --nologo --filter FullyQualifiedName~UT_ProductionGapClosure
dotnet test tests\Neo.External.Bridge.Cli.UnitTests\Neo.External.Bridge.Cli.UnitTests.csproj /p:NuGetAudit=false --nologo
dotnet test tests\Neo.Stack.Cli.UnitTests\Neo.Stack.Cli.UnitTests.csproj /p:NuGetAudit=false --nologo --filter FullyQualifiedName~UT_ScaffoldExecutorCommand
dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo
dotnet test external\neo\tests\Neo.UnitTests\Neo.UnitTests.csproj /p:NuGetAudit=false --nologo --filter FullyQualifiedName~UT_L2NativeContracts
dotnet list Neo.L2.sln package --vulnerable --include-transitive
npm audit --audit-level=high
npm test
npm run build
```

```bash
cargo test --workspace --all-targets --locked
cd external/foreign-contracts/sol && cargo test --all --locked
cd external/foreign-contracts/eth && PATH=/mnt/d/Git/neo-n4/CODEX_DEEP_AUDIT/tools/foundry/bin:$PATH forge test
mdbook build
cargo audit
```

Observed results:

- `UT_ProductionGapClosure`: 9 passed, 0 failed.
- `Neo.External.Bridge.Cli.UnitTests`: 16 passed, 0 failed.
- `UT_ScaffoldExecutorCommand`: 16 passed, 0 failed.
- Full `dotnet test Neo.L2.sln`: passed across all solution test projects.
- Neo core L2 native contract tests: 6 passed, 0 failed.
- `nccs`: compiled 25 deployable contract/sample projects and emitted `.nef` + `.manifest.json` artifacts.
- TypeScript SDK: 15 passed, 0 failed; `tsc` build passed.
- .NET vulnerable package audit: no vulnerable packages reported for listed projects.
- `npm audit --audit-level=high`: 0 vulnerabilities.
- Rust workspace tests: passed; real proof tests marked ignored as designed, host/guest execution smoke passed.
- Solana foreign bridge router tests: 4 passed, 0 failed.
- Foundry `forge test`: 21 passed, 0 failed.
- `mdbook build`: passed.
- Local `cargo audit`: blocked by the WSL-to-GitHub network path while fetching RustSec advisory DB.
