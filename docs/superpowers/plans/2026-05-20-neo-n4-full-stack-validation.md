# Neo N4 Full-Stack Validation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for inline execution, or superpowers:subagent-driven-development if the user explicitly authorizes parallel agents. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Systematically validate Neo N4 across code, contracts, docs, diagrams, private devnet, RISC-V execution, zkVM proving, cross-chain components, and Neo N3 testnet deployment evidence.

**Architecture:** Validation is evidence-first. Each phase produces machine-checkable output under `docs/audit/` or existing test output, and no phase may be marked complete unless its commands exit successfully or its gap is recorded with exact cause and remediation.

**Tech Stack:** .NET 10, Neo C# compiler (`nccs`), Neo N3 RPC, NeoHub deployable contracts, r3e Neo core fork, NeoVM2/RISC-V via PolkaVM, SP1 zkVM, Rust/Cargo, Foundry, Node/Vitest, mdBook, WSL2.

---

## Evidence Policy

- [ ] Create a fresh evidence folder: `docs/audit/full-stack-validation-2026-05-20/`.
- [ ] Save reusable validation evidence as Markdown/JSON files in that folder.
- [ ] Keep raw command transcripts in ignored scratch output or the active terminal session; do not commit them under `docs/audit`.
- [ ] Never write WIF/private keys to disk. Pass secrets only through process environment variables and clear them immediately.
- [ ] Do not mark a phase complete from prior memory. Fresh command output is required.

## Phase 0: Repository And Environment Baseline

**Files:**
- Read: `global.json`
- Read: `.github/workflows/build.yml`
- Read: `Cargo.toml`
- Read: `Neo.L2.sln`
- Record in: `docs/audit/full-stack-validation-2026-05-20/README.md`

- [ ] Record repository state.

Run:

```powershell
git status --short --branch
git submodule status --recursive
dotnet --info
node --version
npm --version
nccs --version
wsl.exe -d Ubuntu -- env -i HOME=/home/dministrator USER=dministrator PATH=/home/dministrator/.cargo/bin:/home/dministrator/.sp1/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin bash -lc "cargo --version; rustc --version; cargo clippy --version; mdbook --version; cargo audit --version; cargo prove --help >/dev/null && echo cargo-prove-ok"
```

Expected:
- Current branch is known.
- Submodules resolve.
- .NET 10 SDK available.
- Node/npm available.
- `nccs` available.
- WSL2 Rust, clippy, mdBook, cargo-audit, cargo-prove available.

## Phase 1: First-Party .NET Build, Format, And Tests

**Files:**
- Covers: `src/**`
- Covers: `tools/**`
- Covers: `tests/**`
- Covers: `external/neo/src/**` through project references
- Record in: `docs/audit/full-stack-validation-2026-05-20/README.md`

- [ ] Restore and build all .NET projects.

Run:

```powershell
dotnet restore .\Neo.L2.sln /p:NuGetAudit=false --nologo
dotnet build .\Neo.L2.sln -c Release /p:NuGetAudit=false --nologo
```

Expected:
- `Build succeeded.`
- `0 Error(s)`.

- [ ] Verify formatting for first-party projects.

Run:

```powershell
dotnet format .\Neo.L2.sln --verify-no-changes --exclude external/
```

Expected:
- Exit code `0`.

- [ ] Run all solution tests.

Run:

```powershell
dotnet test .\Neo.L2.sln -c Release --no-build /p:NuGetAudit=false --nologo
```

Expected:
- Every test project reports `Failed: 0`.

## Phase 2: NeoHub Contract Compile And Manifest Invariants

**Files:**
- Covers: `contracts/NeoHub.*/*.cs`
- Covers: `samples/contracts/Sample.*/*.cs`
- Covers: `tests/Neo.Hub.Deploy.UnitTests/**`
- Record in: `docs/audit/full-stack-validation-2026-05-20/README.md`

- [ ] Recompile every NeoHub and sample deployable contract with `nccs`.

Run:

```powershell
$ErrorActionPreference = 'Stop'
$dirs = @()
$dirs += Get-ChildItem -Path contracts -Directory -Filter 'NeoHub.*' | Sort-Object Name
$dirs += Get-ChildItem -Path samples\contracts -Directory -Filter 'Sample.*' | Sort-Object Name
foreach ($d in $dirs) {
  $name = $d.Name
  $project = Join-Path $d.FullName "$name.csproj"
  dotnet build $project -c Release /p:NuGetAudit=false --nologo
  nccs $project --output (Join-Path $d.FullName 'bin\sc')
}
foreach ($d in $dirs) {
  $name = $d.Name
  if (-not (Test-Path (Join-Path $d.FullName "bin\sc\$name.nef"))) { throw "missing nef: $name" }
  if (-not (Test-Path (Join-Path $d.FullName "bin\sc\$name.manifest.json"))) { throw "missing manifest: $name" }
}
```

Expected:
- All `NeoHub.*` and `Sample.*` projects produce `.nef` and `.manifest.json`.

- [ ] Verify manifest invariants against fresh artifacts.

Run:

```powershell
$env:NEO_N4_REQUIRE_FRESH_MANIFESTS='1'
dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj -c Release --filter "FullyQualifiedName~UT_ContractManifestInvariants|FullyQualifiedName~UT_OptimisticChallengeAllowlist|FullyQualifiedName~UT_DeployPlanner|FullyQualifiedName~UT_PlanCommand" /p:NuGetAudit=false --nologo
Remove-Item Env:\NEO_N4_REQUIRE_FRESH_MANIFESTS -ErrorAction SilentlyContinue
```

Expected:
- `Failed: 0`.
- `ContractZkVerifier` manifest exposes deployable verifier route.
- No `NativeZkVerifier`, `setNativeAccelerator`, or `getNativeAccelerator` route exists.

## Phase 3: r3e Neo Core Fork And L2 Native Contracts

**Files:**
- Covers: `external/neo/**`
- Covers: `src/Neo.L2.Executor.RiscV/**`
- Covers: `tests/Neo.L2.Executor.RiscV.UnitTests/**`
- Record in: `docs/audit/full-stack-validation-2026-05-20/README.md`

- [ ] Verify r3e Neo core L2 native contracts.

Run:

```powershell
dotnet test external\neo\tests\Neo.UnitTests\Neo.UnitTests.csproj -c Release --filter FullyQualifiedName~UT_L2NativeContracts /p:NuGetAudit=false --nologo
```

Expected:
- `Failed: 0`.
- L2 native contract set matches the forked Neo core implementation.

- [ ] Verify RISC-V executor unit tests.

Run:

```powershell
dotnet test tests\Neo.L2.Executor.RiscV.UnitTests\Neo.L2.Executor.RiscV.UnitTests.csproj -c Release /p:NuGetAudit=false --nologo
```

Expected:
- `Failed: 0`.

## Phase 4: Private Devnet, NeoFS DA, And Execution Modes

**Files:**
- Covers: `tools/Neo.L2.Devnet/**`
- Covers: `samples/*.config.json`
- Covers: `src/Neo.L2.Batch/**`
- Covers: `src/Neo.L2.Audit/**`
- Covers: `src/Neo.L2.Persistence/**`
- Covers: `src/Neo.L2.Proving/**`
- Record in: `docs/audit/full-stack-validation-2026-05-20/README.md`

- [ ] Run all published sample configs with the default reference executor.

Run:

```powershell
$ErrorActionPreference='Stop'
foreach ($cfg in @(
  'samples/general-rollup.config.json',
  'samples/gaming-rollup.config.json',
  'samples/exchange-validium.config.json',
  'samples/privacy-sidechain.config.json'
)) {
  $out = dotnet run --project tools\Neo.L2.Devnet\Neo.L2.Devnet.csproj -c Release --no-build -- 3 --config $cfg
  $out
  if (($out -join "`n") -notmatch 'devnet run complete') { throw "devnet failed: $cfg" }
  if (($out -join "`n") -notmatch 'DA writer = NeoFsLikeDAWriter') { throw "NeoFS DA not used: $cfg" }
  if (($out -join "`n") -notmatch 'da_availability') { throw "DA availability audit missing: $cfg" }
}
```

Expected:
- All four configs complete.
- NeoFS DA writer appears.
- DA availability audit appears.

- [ ] Run canonical NeoVM2/RISC-V via PolkaVM devnet smoke.

Run:

```powershell
$env:PATH = "C:\Program Files\Rust stable LLVM 1.95\bin;D:\Git\neo-n4\external\neo-riscv-vm\target\release;D:\Git\neo-n4\external\neo-riscv-vm\target\release\deps;$env:PATH"
dotnet run --project tools\Neo.L2.Devnet\Neo.L2.Devnet.csproj -c Release --no-build -- 3 --config samples\general-rollup.config.json --executor riscv
```

Expected:
- Output includes `RiscVTransactionExecutor`.
- Output includes `NeoVM2/RISC-V via PolkaVM`.
- Audit passes and DA availability remains available on NeoFS.

## Phase 5: Rust Execution Core, SDK, Watchers, And zkVM

**Files:**
- Covers: `bridge/neo-execution-core/**`
- Covers: `bridge/neo-zkvm-guest/**`
- Covers: `bridge/neo-zkvm-host/**`
- Covers: `sdk/rust/**`
- Covers: `watchers/neo-bridge-watcher-eth/**`
- Covers: `watchers/neo-bridge-watcher-tron/**`
- Covers: `watchers/neo-bridge-watcher-sol/**`
- Record in: `docs/audit/full-stack-validation-2026-05-20/README.md`

- [ ] Run Rust formatting, workspace tests, clippy, and watcher feature tests.

Run:

```powershell
wsl.exe -d Ubuntu -- env -i HOME=/home/dministrator USER=dministrator PATH=/home/dministrator/.cargo/bin:/home/dministrator/.sp1/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin bash -lc '
set -euo pipefail
cd /mnt/d/Git/neo-n4
cargo fmt --all -- --check
cargo test --workspace --release --locked
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo clippy --release --features live-rpc --all-targets -p neo-bridge-watcher-eth --locked -- -D warnings
cargo clippy --release --all-targets -p neo-bridge-watcher-tron -p neo-bridge-watcher-sol --locked -- -D warnings
'
```

Expected:
- Format check exits `0`.
- Workspace release tests pass.
- Clippy exits `0` with `-D warnings`.

- [ ] Verify PolkaVM-backed RISC-V host crate.

Run:

```powershell
wsl.exe -d Ubuntu -- env -i HOME=/home/dministrator USER=dministrator PATH=/home/dministrator/.cargo/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin bash -lc '
set -euo pipefail
cd /mnt/d/Git/neo-n4/external/neo-riscv-vm
cargo check -p neo-riscv-host
'
```

Expected:
- `Finished` with exit code `0`.

- [ ] Build the SP1 guest ELF and run real proof tests.

Run:

```powershell
wsl.exe -d Ubuntu -- env -i HOME=/home/dministrator USER=dministrator PATH=/home/dministrator/.sp1/bin:/home/dministrator/.cargo/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin bash -lc '
set -euo pipefail
cd /mnt/d/Git/neo-n4/bridge/neo-zkvm-guest
cargo prove build
cd /mnt/d/Git/neo-n4/bridge/neo-zkvm-host
cargo test --release --locked -- --ignored --nocapture
'
```

Expected:
- `cargo prove build` succeeds.
- Real proof generation test passes.
- Tampered public input hash rejection test passes.

## Phase 6: Cross-Chain Foreign Contracts

**Files:**
- Covers: `external/foreign-contracts/eth/**`
- Covers: `external/foreign-contracts/sol/**`
- Covers: `watchers/**`
- Record in: `docs/audit/full-stack-validation-2026-05-20/README.md`

- [ ] Run Foundry EVM router formatting and tests.

Run:

```powershell
wsl.exe -d Ubuntu -- env -i HOME=/home/dministrator USER=dministrator PATH=/mnt/d/Git/neo-n4/CODEX_DEEP_AUDIT/tools/foundry/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin bash -lc '
set -euo pipefail
cd /mnt/d/Git/neo-n4/external/foreign-contracts/eth
if [ ! -d lib/forge-std ]; then forge install --no-git foundry-rs/forge-std; fi
forge fmt --check
forge test -vv
'
```

Expected:
- `forge fmt --check` exits `0`.
- EVM router tests pass.

- [ ] Run Solana foreign router tests.

Run:

```powershell
wsl.exe -d Ubuntu -- env -i HOME=/home/dministrator USER=dministrator PATH=/home/dministrator/.cargo/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin bash -lc '
set -euo pipefail
cd /mnt/d/Git/neo-n4/external/foreign-contracts/sol
cargo test --locked
'
```

Expected:
- `Failed: 0`.

## Phase 7: SDKs, Experience Hub, Docs, And Diagram Consistency

**Files:**
- Covers: `sdk/typescript/**`
- Covers: `docs/experience-hub/**`
- Covers: `tests/experience-hub/**`
- Covers: `docs/**/*.md`
- Covers: `docs/**/*.svg`
- Covers: `docs/zh/**`
- Record in: `docs/audit/full-stack-validation-2026-05-20/README.md`

- [ ] Run TypeScript SDK tests, build, and audit.

Run:

```powershell
Push-Location sdk\typescript
npm install --no-audit --no-fund
npm test
npm run build
npm audit --audit-level=moderate
Pop-Location
```

Expected:
- Vitest passes.
- `tsc` exits `0`.
- `npm audit` reports no moderate-or-higher vulnerabilities.

- [ ] Run Experience Hub report-schema tests.

Run:

```powershell
foreach ($f in Get-ChildItem tests\experience-hub -Filter '*.test.mjs' | Sort-Object Name) {
  node --test $f.FullName
}
```

Expected:
- All hub, manifest generator, and report schema tests pass.

- [ ] Build the documentation site.

Run:

```powershell
wsl.exe -d Ubuntu -- env -i HOME=/home/dministrator USER=dministrator PATH=/home/dministrator/.cargo/bin:/home/dministrator/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin bash -lc '
set -euo pipefail
cd /mnt/d/Git/neo-n4
mdbook build
'
```

Expected:
- `mdbook build` exits `0`.

- [ ] Verify English docs and SVGs have Chinese counterparts.

Run:

```powershell
dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj -c Release --filter FullyQualifiedName~UT_ProductionGapClosure /p:NuGetAudit=false --nologo
```

Expected:
- Localization and production-readiness invariant tests pass.

## Phase 8: Security And Supply Chain Validation

**Files:**
- Covers: `Directory.Packages.props`
- Covers: `Cargo.lock`
- Covers: `sdk/typescript/package-lock.json`
- Record in: `docs/audit/full-stack-validation-2026-05-20/README.md`

- [ ] Scan NuGet vulnerabilities.

Run:

```powershell
dotnet list .\Neo.L2.sln package --vulnerable --include-transitive
```

Expected:
- Every project reports no vulnerable packages.

- [ ] Scan npm vulnerabilities.

Run:

```powershell
Push-Location sdk\typescript
npm audit --audit-level=moderate
Pop-Location
```

Expected:
- `found 0 vulnerabilities` or no moderate-or-higher vulnerabilities.

- [ ] Scan Rust vulnerabilities.

Run:

```powershell
wsl.exe -d Ubuntu -- env -i HOME=/home/dministrator USER=dministrator PATH=/home/dministrator/.cargo/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin bash -lc '
set -euo pipefail
cd /mnt/d/Git/neo-n4
cargo audit --json
'
```

Expected:
- `vulnerabilities.found=false`.

If advisory DB fetch fails because WSL cannot reach GitHub, run the cached database fallback:

```powershell
wsl.exe -d Ubuntu -- env -i HOME=/home/dministrator USER=dministrator PATH=/home/dministrator/.cargo/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin bash -lc '
set -euo pipefail
cd /mnt/d/Git/neo-n4
cargo audit --db /mnt/d/Git/neo-n4/artifacts/private-network/advisory-db-main --no-fetch --stale --json
cargo tree -i lru --locked --workspace
'
```

Expected:
- Vulnerability count is `0`.
- Any warning such as `sp1-prover -> lru` is recorded as an upstream SP1 dependency risk, not hidden.

- [ ] Scan for secret leakage.

Run:

```powershell
if (-not $env:NEO_N4_TESTNET_WIF) { throw 'NEO_N4_TESTNET_WIF must be set only in the current process before secret scanning' }
git grep -n $env:NEO_N4_TESTNET_WIF -- .
$secret=$env:NEO_N4_TESTNET_WIF
$roots=@('docs','tools','contracts','src','tests','.github','README.md','ARCHITECTURE.md','WHITEPAPER.md','doc.md')
$hits=@()
foreach ($root in $roots) {
  if (Test-Path $root) {
    if ((Get-Item $root).PSIsContainer) {
      $hits += Get-ChildItem $root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|target|node_modules)\\' } |
        Select-String -SimpleMatch $secret -ErrorAction SilentlyContinue
    } else {
      $hits += Select-String -Path $root -SimpleMatch $secret -ErrorAction SilentlyContinue
    }
  }
}
if ($hits.Count) { $hits | ForEach-Object { "HIT $($_.Path):$($_.LineNumber)" }; exit 1 }
```

Expected:
- No tracked or source/docs secret hits.

## Phase 9: Testnet Deployment And Chain-State Validation

**Files:**
- Covers: `tools/Neo.Hub.Deploy/**`
- Covers: `docs/audit/testnet-deployment-*.json`
- Produce: `docs/audit/full-stack-validation-2026-05-20/09-testnet-rpc.json`
- Record transcript locally only when rerunning deployment commands.

- [ ] Generate, plan, and verify the deployment plan.

Run:

```powershell
$plan = Join-Path $env:TEMP 'neo-n4-full-stack-testnet-plan.json'
$bundle = Join-Path $env:TEMP 'neo-n4-full-stack-testnet-bundle.json'
dotnet run --project tools\Neo.Hub.Deploy\Neo.Hub.Deploy.csproj -c Release -- scaffold --output $plan
dotnet run --project tools\Neo.Hub.Deploy\Neo.Hub.Deploy.csproj -c Release -- plan --plan $plan --output $bundle
dotnet run --project tools\Neo.Hub.Deploy\Neo.Hub.Deploy.csproj -c Release -- verify --plan $plan --rpc https://testnet1.neo.coz.io:443
```

Expected:
- 23 production steps.
- 23 ok / 0 missing.
- Required post-deploy actions mention `ContractZkVerifier.RegisterVerificationKey`, `ContractZkVerifier.RegisterProofVerifier`, and `VerifierRegistry.RegisterVerifier(ProofType.Zk=3, ContractZkVerifier)`.

- [ ] Dry-run testnet deployment with WIF only in environment.

Run:

```powershell
if (-not $env:NEO_N4_TESTNET_WIF) { throw 'Set NEO_N4_TESTNET_WIF in the current process only; do not write it to disk' }
dotnet run --project tools\Neo.Hub.Deploy\Neo.Hub.Deploy.csproj -c Release -- deploy-testnet --rpc https://testnet1.neo.coz.io:443 --output docs\audit\full-stack-validation-2026-05-20\testnet-dry-run.json --dry-run
Remove-Item Env:\NEO_N4_TESTNET_WIF -ErrorAction SilentlyContinue
```

Expected:
- No transaction is sent.
- Report predicts deploy/reuse plan.
- WIF is not written to report.

- [ ] Execute live deployment only if current report is stale or missing a required contract.

Run:

```powershell
if (-not $env:NEO_N4_TESTNET_WIF) { throw 'Set NEO_N4_TESTNET_WIF in the current process only; do not write it to disk' }
dotnet run --project tools\Neo.Hub.Deploy\Neo.Hub.Deploy.csproj -c Release -- deploy-testnet --rpc https://testnet1.neo.coz.io:443 --output docs\audit\full-stack-validation-2026-05-20\testnet-live.json
Remove-Item Env:\NEO_N4_TESTNET_WIF -ErrorAction SilentlyContinue
```

Expected:
- Deployment and postdeploy transactions HALT.
- Smoke checks all pass.

- [ ] Independently verify chain state through RPC.

Run:

```powershell
$rpc='https://testnet1.neo.coz.io:443'
$report=Get-Content docs\audit\full-stack-validation-2026-05-20\testnet-live.json -Raw | ConvertFrom-Json
function Invoke-NeoRpc($method, $params=@()) {
  $body=@{jsonrpc='2.0';id=1;method=$method;params=$params} | ConvertTo-Json -Depth 20 -Compress
  $resp=Invoke-RestMethod -Uri $rpc -Method Post -ContentType 'application/json' -Body $body
  if ($resp.error) { throw "$method RPC error: $($resp.error | ConvertTo-Json -Compress)" }
  return $resp.result
}
foreach ($r in @($report.records | Where-Object { $_.txHash })) {
  $log=Invoke-NeoRpc 'getapplicationlog' @($r.txHash)
  if ($log.executions[0].vmstate -ne 'HALT') { throw "tx not HALT: $($r.name)" }
}
foreach ($r in @($report.records | Where-Object { $_.category -eq 'deploy' })) {
  $state=Invoke-NeoRpc 'getcontractstate' @($r.contractHash)
  if (-not $state.manifest.name) { throw "missing contract state: $($r.name)" }
}
```

Expected:
- Every deployment/postdeploy tx is HALT.
- Every planned contract has chain state.

## Phase 10: ContractZkVerifier Production-Safety Validation

**Files:**
- Covers: `contracts/NeoHub.ContractZkVerifier/**`
- Covers: `tools/Neo.Hub.Deploy/LiveDeployCommand.cs`
- Produce: `docs/audit/full-stack-validation-2026-05-20/10-contract-zk-verifier.json`

- [ ] Verify chain ABI and ZK routing.

Run:

```powershell
$czk='0xd52484a842b97555c56bd93ecf919df3f78366f7'
$registry='0x3b96ba201a2ef32f98da7b72e14acb0329b6e017'
$rpc='https://testnet1.neo.coz.io:443'
function Invoke-NeoRpc($method, $params=@()) {
  $body=@{jsonrpc='2.0';id=1;method=$method;params=$params} | ConvertTo-Json -Depth 20 -Compress
  $resp=Invoke-RestMethod -Uri $rpc -Method Post -ContentType 'application/json' -Body $body
  if ($resp.error) { throw "$method RPC error: $($resp.error | ConvertTo-Json -Compress)" }
  return $resp.result
}
$state=Invoke-NeoRpc 'getcontractstate' @($czk)
$methods=@($state.manifest.abi.methods | Select-Object -ExpandProperty name)
foreach ($m in @('registerVerificationKey','isVerificationKeyRegistered','registerProofVerifier','getProofVerifier','setEnvelopeOnlyAllowed','isEnvelopeOnlyAllowed','verify')) {
  if ($m -notin $methods) { throw "missing ABI method: $m" }
}
foreach ($m in @('setNativeAccelerator','getNativeAccelerator')) {
  if ($m -in $methods) { throw "forbidden ABI method present: $m" }
}
```

Expected:
- Required ABI exists.
- Native accelerator ABI does not exist.

- [ ] Verify safe default state.

Run:

```powershell
foreach ($ps in 1..4) {
  $pv=Invoke-NeoRpc 'invokefunction' @($czk,'getProofVerifier',@(@{type='Integer';value=[string]$ps}))
  $env=Invoke-NeoRpc 'invokefunction' @($czk,'isEnvelopeOnlyAllowed',@(@{type='Integer';value=[string]$ps}))
  $vk=Invoke-NeoRpc 'invokefunction' @($czk,'isVerificationKeyRegistered',@(@{type='Integer';value=[string]$ps},@{type='Hash256';value='0x1111111111111111111111111111111111111111111111111111111111111111'}))
  if ([bool]$env.stack[0].value) { throw "envelope-only enabled for $ps" }
  if ([bool]$vk.stack[0].value) { throw "sample VK unexpectedly registered for $ps" }
}
$malformed=Invoke-NeoRpc 'invokefunction' @($czk,'verify',@(@{type='ByteArray';value=''}))
if ($malformed.state -ne 'FAULT') { throw 'malformed verify did not FAULT' }
```

Expected:
- No proof system has envelope-only enabled.
- No sample VK is registered.
- Malformed proof faults.

## Phase 11: Documentation, README, And Architecture Consistency Review

**Files:**
- Review: `README.md`
- Review: `ARCHITECTURE.md`
- Review: `WHITEPAPER.md`
- Review: `doc.md`
- Review: `docs/README.md`
- Review: `docs/neohub-architecture-and-workflows.md`
- Review: `docs/security-model.md`
- Review: `docs/technical-roadmap.md`
- Review: `docs/zh/**`
- Review: `docs/figures/**/*.svg`
- Review: `docs/figures/experience-hub/neo-n4-experience-hub.png`
- Produce: `docs/audit/full-stack-validation-2026-05-20/11-docs-consistency.md`

- [ ] Search for obsolete native-ZK route terminology.

Run:

```powershell
git grep -n -E "NativeZkVerifier|SetNativeAccelerator|GetNativeAccelerator|L1_NATIVE_ZK_VERIFIER_HASH|native-zk" -- README.md docs contracts tests tools .github/workflows/build.yml Neo.L2.sln
```

Expected:
- No matches.

- [ ] Search for potentially misleading `native accelerator` language.

Run:

```powershell
git grep -n "native accelerator" -- README.md docs contracts tests tools .github/workflows/build.yml Neo.L2.sln
```

Expected:
- Matches are allowed only in regression tests that assert the old route is forbidden.

- [ ] Verify VM terminology.

Manual pass:
- `NeoVM2/RISC-V` remains the canonical default N4 L2 executor.
- Additional VMs are described as N4 Layer-2 execution profiles/executors.
- Do not describe EVM support as NeoX.

Expected:
- Any violating sentence is patched before final sign-off.

## Phase 12: Final Full-Stack Sign-Off Report

**Files:**
- Create: `docs/audit/full-stack-validation-2026-05-20/README.md`

- [ ] Write a concise validation report with:
  - Commit/branch/submodule status.
  - Exact commands run.
  - Pass/fail counts.
  - Testnet RPC network and block height.
  - Contract hashes and tx hashes.
  - Known residual production gaps.
  - Security warnings that remain upstream-controlled.

- [ ] Required residual-gap wording:
  - `ContractZkVerifier` is deployed and wired.
  - It is safe by default because no proof verifier, VK, or envelope-only mode is enabled.
  - It does not yet verify real SP1 proofs on L1 until a production proof-system verifier contract and VK are registered.

- [ ] Run one final secret scan after writing the report.

Run:

```powershell
if (-not $env:NEO_N4_TESTNET_WIF) { throw 'NEO_N4_TESTNET_WIF must be set only in the current process before final secret scanning' }
git grep -n $env:NEO_N4_TESTNET_WIF -- .
```

Expected:
- No matches.

## Execution Order

1. Phase 0
2. Phase 1
3. Phase 2
4. Phase 3
5. Phase 4
6. Phase 5
7. Phase 6
8. Phase 7
9. Phase 8
10. Phase 9
11. Phase 10
12. Phase 11
13. Phase 12

## Stop Conditions

- Stop immediately if a private key or WIF appears in any file.
- Stop immediately if testnet deployment predicts a different owner than the intended wallet.
- Stop immediately if `VerifierRegistry.getVerifier(3)` points anywhere other than the intended `ContractZkVerifier`.
- Stop immediately if `ContractZkVerifier` has envelope-only enabled on public testnet without explicit written approval.
- Stop immediately if the old native-ZK route appears in production code, deployment plan, README, or diagrams.
- Do not stop for upstream warnings such as SP1-owned transitive dependency advisories; record them with dependency chain and mitigation status.

## Completion Criteria

- All phases have fresh evidence.
- Every command either exits `0` or has a documented failure with exact remediation.
- The final report exists under `docs/audit/full-stack-validation-2026-05-20/README.md`.
- Testnet state is independently verified through RPC, not only through deploy-tool output.
- The WIF/private key does not appear in tracked files or source/docs working tree.
- Any remaining production gap is explicitly stated and not marketed as complete.
