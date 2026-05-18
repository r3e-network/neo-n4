<#
.SYNOPSIS
Runs the Neo N4 private-network verification matrix.

.DESCRIPTION
This harness stands up a deterministic in-process private network using the
operator CLIs and Neo.L2.Devnet, then runs the repository's build, unit,
integration, contract, SDK, bridge, zkVM, foreign-contract, and docs checks.
Logs and machine-readable summaries are written under artifacts/private-network.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$ArtifactsRoot = "",
    [int]$Batches = 3,
    [switch]$SkipContracts,
    [switch]$SkipRust,
    [switch]$SkipRealSp1Proof,
    [switch]$SkipTypeScript,
    [switch]$SkipForeignContracts,
    [switch]$SkipDocs,
    [switch]$SkipSupplyChainAudit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Batches -lt 1) {
    throw "-Batches must be >= 1. The harness uses a separate 0-batch run for rehydration."
}

if ([string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
    $ArtifactsRoot = Join-Path $RepoRoot "artifacts\private-network"
}

$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$RunDir = Join-Path $ArtifactsRoot $runId
$LogDir = Join-Path $RunDir "logs"
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

$summary = [ordered]@{
    runId = $runId
    repoRoot = $RepoRoot
    artifactsRoot = $ArtifactsRoot
    runDir = $RunDir
    startedAtUtc = [DateTime]::UtcNow.ToString("O")
    batches = $Batches
    steps = @()
}

function Save-Summary {
    param([string]$Status)
    $summary.status = $Status
    $summary.finishedAtUtc = [DateTime]::UtcNow.ToString("O")
    $summaryPath = Join-Path $RunDir "summary.json"
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $ArtifactsRoot "latest-run.txt") -Value $RunDir -Encoding UTF8
}

function Convert-ToLogName {
    param([string]$Name)
    return (($Name -replace "[^A-Za-z0-9_.-]", "_").Trim("_") + ".log")
}

function Format-Command {
    param([string]$FilePath, [string[]]$Arguments)
    return ($FilePath + " " + (($Arguments | ForEach-Object {
        if ($_ -match "\s") { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join " ")).Trim()
}

function Invoke-LoggedNative {
    param(
        [string]$Name,
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory = $RepoRoot,
        [hashtable]$Environment = @{}
    )

    $logPath = Join-Path $LogDir (Convert-ToLogName $Name)
    $step = [ordered]@{
        name = $Name
        command = Format-Command $FilePath $Arguments
        cwd = $WorkingDirectory
        log = $logPath
        startedAtUtc = [DateTime]::UtcNow.ToString("O")
    }

    Write-Host "==> $Name"
    $oldValues = @{}
    $oldErrorActionPreference = $ErrorActionPreference
    Push-Location $WorkingDirectory
    try {
        foreach ($key in $Environment.Keys) {
            $oldValues[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
            [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key], "Process")
        }

        $ErrorActionPreference = "Continue"
        & $FilePath @Arguments 2>&1 |
            Tee-Object -FilePath $logPath |
            ForEach-Object { Write-Host $_ }
        $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
        $step.exitCode = $exitCode
        if ($exitCode -ne 0) {
            throw "Step '$Name' failed with exit code $exitCode. See $logPath"
        }
        $step.status = "passed"
    }
    catch {
        $step.status = "failed"
        $step.error = $_.Exception.Message
        $summary.steps += $step
        Save-Summary "failed"
        throw
    }
    finally {
        $ErrorActionPreference = $oldErrorActionPreference
        foreach ($key in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $oldValues[$key], "Process")
        }
        Pop-Location
    }

    $step.finishedAtUtc = [DateTime]::UtcNow.ToString("O")
    $summary.steps += $step
}

function Invoke-LoggedScript {
    param(
        [string]$Name,
        [scriptblock]$Body,
        [string]$WorkingDirectory = $RepoRoot
    )

    $logPath = Join-Path $LogDir (Convert-ToLogName $Name)
    $step = [ordered]@{
        name = $Name
        command = "<PowerShell script block>"
        cwd = $WorkingDirectory
        log = $logPath
        startedAtUtc = [DateTime]::UtcNow.ToString("O")
    }

    Write-Host "==> $Name"
    $oldErrorActionPreference = $ErrorActionPreference
    Push-Location $WorkingDirectory
    try {
        $ErrorActionPreference = "Continue"
        & $Body 2>&1 |
            Tee-Object -FilePath $logPath |
            ForEach-Object { Write-Host $_ }
        $step.status = "passed"
        $step.exitCode = 0
    }
    catch {
        $step.status = "failed"
        $step.exitCode = 1
        $step.error = $_.Exception.Message
        $summary.steps += $step
        Save-Summary "failed"
        throw
    }
    finally {
        $ErrorActionPreference = $oldErrorActionPreference
        Pop-Location
    }

    $step.finishedAtUtc = [DateTime]::UtcNow.ToString("O")
    $summary.steps += $step
}

function Quote-Bash {
    param([string]$Value)
    return "'" + ($Value -replace "'", "'\''") + "'"
}

function Get-WslPath {
    param([string]$Path)
    $oldErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& wsl.exe -e wslpath -a $Path 2>$null)
        $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    }
    finally {
        $ErrorActionPreference = $oldErrorActionPreference
    }
    $converted = if ($output.Count -gt 0) { [string]$output[0] } else { "" }
    $converted = $converted.Trim()
    if ($exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($converted)) {
        throw "wslpath failed for '$Path'"
    }
    return $converted
}

function Join-WslPath {
    param(
        [string]$Base,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Parts
    )
    $path = $Base.TrimEnd("/")
    foreach ($part in $Parts) {
        $path += "/" + $part.Trim("/")
    }
    return $path
}

function Invoke-WslBash {
    param(
        [string]$Name,
        [string]$Command
    )

    $normalizedCommand = $Command -replace "`r`n", "`n" -replace "`r", "`n"
    $scriptDir = Join-Path $RunDir "wsl-scripts"
    New-Item -ItemType Directory -Path $scriptDir -Force | Out-Null
    $scriptName = [IO.Path]::ChangeExtension((Convert-ToLogName $Name), ".sh")
    $scriptPath = Join-Path $scriptDir $scriptName
    [IO.File]::WriteAllText($scriptPath, $normalizedCommand, [Text.UTF8Encoding]::new($false))

    $wslScriptPath = Get-WslPath $scriptPath
    Invoke-LoggedNative -Name $Name -FilePath "wsl.exe" -Arguments @(
        "-e", "bash", "-lc", "bash $(Quote-Bash $wslScriptPath)"
    )
}

function Get-StableRustPrefix {
    param([string]$WslRepoRoot)
    return @"
set -euo pipefail
TOOLCHAIN_NAME=`$(rustup toolchain list | awk '/stable/ {print `$1; exit}' | sed 's/(.*//')
if [ -z "`$TOOLCHAIN_NAME" ]; then
  echo "No stable Rust toolchain is installed in WSL." >&2
  exit 1
fi
TOOLBIN="`$HOME/.rustup/toolchains/`$TOOLCHAIN_NAME/bin"
export PATH="`${TOOLBIN}:`$HOME/.cargo/bin:`$PATH"
export RUSTC="`${TOOLBIN}/rustc"
cd $(Quote-Bash $WslRepoRoot)
"@
}

function Ensure-Nccs {
    if (-not (Get-Command nccs -ErrorAction SilentlyContinue)) {
        Invoke-LoggedNative -Name "install Neo.Compiler.CSharp" -FilePath "dotnet" -Arguments @(
            "tool", "install", "-g", "Neo.Compiler.CSharp"
        )
    }
    Invoke-LoggedNative -Name "nccs version" -FilePath "nccs" -Arguments @("--version")
}

function Invoke-ContractCompilation {
    Invoke-LoggedScript -Name "compile Neo contracts with nccs" -Body {
        Ensure-Nccs
        $dirs = @()
        $dirs += Get-ChildItem -Path (Join-Path $RepoRoot "contracts") -Directory -Filter "NeoHub.*" | Sort-Object Name
        $dirs += Get-ChildItem -Path (Join-Path $RepoRoot "samples\contracts") -Directory -Filter "Sample.*" | Sort-Object Name

        foreach ($dir in $dirs) {
            $project = Join-Path $dir.FullName "$($dir.Name).csproj"
            if (-not (Test-Path -LiteralPath $project)) {
                throw "Missing project file: $project"
            }

            & dotnet build $project /p:NuGetAudit=false --nologo --verbosity quiet
            if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $project" }

            $output = Join-Path $dir.FullName "bin\sc"
            & nccs $project --output $output
            if ($LASTEXITCODE -ne 0) { throw "nccs failed: $project" }

            foreach ($ext in @("nef", "manifest.json")) {
                $artifact = Join-Path $output "$($dir.Name).$ext"
                if (-not (Test-Path -LiteralPath $artifact)) {
                    throw "Missing artifact: $artifact"
                }
            }
        }

        Write-Host "Compiled $($dirs.Count) deployable contracts and verified $($dirs.Count * 2) artifacts."
        & dotnet test (Join-Path $RepoRoot "external\neo\tests\Neo.UnitTests\Neo.UnitTests.csproj") --filter "FullyQualifiedName~UT_L2NativeContracts" /p:NuGetAudit=false --nologo
        if ($LASTEXITCODE -ne 0) { throw "N4 L2 native contract verification failed" }
    }
}

function New-PrivateChain {
    param(
        [string]$Name,
        [int]$ChainId,
        [string]$Template
    )
    $chainDir = Join-Path $RunDir $Name
    Invoke-LoggedNative -Name "neo-stack create-chain $Name" -FilePath "dotnet" -Arguments @(
        "run", "--project", "tools\Neo.Stack.Cli", "--",
        "create-chain", "--chain-id", [string]$ChainId, "--template", $Template, "--output", $chainDir
    )
    Invoke-LoggedNative -Name "neo-stack validate $Name" -FilePath "dotnet" -Arguments @(
        "run", "--project", "tools\Neo.Stack.Cli", "--",
        "validate", (Join-Path $chainDir "chain.config.json")
    )
    return $chainDir
}

function Invoke-OperatorPreflight {
    param(
        [string]$ChainDir,
        [int]$ChainId
    )

    Invoke-LoggedNative -Name "neo-stack init-l2" -FilePath "dotnet" -Arguments @(
        "run", "--project", "tools\Neo.Stack.Cli", "--",
        "init-l2", "--chain-id", [string]$ChainId, "--output", $ChainDir
    )
    Invoke-LoggedNative -Name "neo-stack register-chain plan" -FilePath "dotnet" -Arguments @(
        "run", "--project", "tools\Neo.Stack.Cli", "--",
        "register-chain", "--chain-id", [string]$ChainId, "--output", $ChainDir
    )
    Invoke-LoggedNative -Name "neo-stack deploy-bridge-adapter plan" -FilePath "dotnet" -Arguments @(
        "run", "--project", "tools\Neo.Stack.Cli", "--",
        "deploy-bridge-adapter", "--chain-id", [string]$ChainId, "--output", $ChainDir
    )
    foreach ($cmd in @("start-sequencer", "start-batcher", "start-prover")) {
        Invoke-LoggedNative -Name "neo-stack $cmd preflight" -FilePath "dotnet" -Arguments @(
            "run", "--project", "tools\Neo.Stack.Cli", "--",
            $cmd, "--chain-id", [string]$ChainId, "--output", $ChainDir
        )
    }
}

function Invoke-DevnetRun {
    param(
        [string]$Name,
        [int]$BatchCount,
        [string]$ConfigPath,
        [string]$Executor,
        [string]$DataDir = "",
        [switch]$Metrics
    )

    $args = @(
        "run", "--project", "tools\Neo.L2.Devnet", "--",
        [string]$BatchCount,
        "--config", $ConfigPath,
        "--executor", $Executor
    )
    if (-not [string]::IsNullOrWhiteSpace($DataDir)) {
        $args += @("--data-dir", $DataDir)
    }
    if ($Metrics) {
        $args += @("--metrics-port", "0")
    }

    Invoke-LoggedNative -Name "neo-l2-devnet $Name" -FilePath "dotnet" -Arguments $args
}

function Invoke-RiscVDevnetRun {
    param(
        [string]$Name,
        [int]$BatchCount,
        [string]$ConfigPath
    )

    $publishDir = Join-Path $RunDir "devnet-linux-x64"
    Invoke-LoggedNative -Name "publish neo-l2-devnet linux-x64" -FilePath "dotnet" -Arguments @(
        "publish", "tools\Neo.L2.Devnet\Neo.L2.Devnet.csproj",
        "-c", "Release", "-r", "linux-x64", "--self-contained", "true",
        "/p:NuGetAudit=false", "/p:PublishSingleFile=false", "--nologo",
        "-o", $publishDir
    )

    $wslRepo = Get-WslPath $RepoRoot
    $wslPublish = Get-WslPath $publishDir
    $wslConfig = Get-WslPath $ConfigPath
    Invoke-WslBash -Name "cargo build neo-riscv-host release" -Command @"
set -euo pipefail
export PATH="`$HOME/.cargo/bin:`$PATH"
cd $(Quote-Bash (Join-WslPath $wslRepo "external" "neo-riscv-vm"))
cargo build -p neo-riscv-host --release
"@

    Invoke-WslBash -Name "neo-l2-devnet $Name" -Command @"
set -euo pipefail
cd $(Quote-Bash $wslRepo)
chmod +x $(Quote-Bash (Join-WslPath $wslPublish "neo-l2-devnet"))
export LD_LIBRARY_PATH=$(Quote-Bash (Join-WslPath $wslRepo "external" "neo-riscv-vm" "target" "release")):$(Quote-Bash $wslPublish)
$(Quote-Bash (Join-WslPath $wslPublish "neo-l2-devnet")) $BatchCount --config $(Quote-Bash $wslConfig) --executor riscv
"@
}

function Ensure-RustSecDb {
    foreach ($name in @("advisory-db-main", "rustsec-advisory-db-main")) {
        $candidate = Join-Path $ArtifactsRoot $name
        if (Test-Path -LiteralPath (Join-Path $candidate "crates")) {
            return $candidate
        }
    }

    New-Item -ItemType Directory -Path $ArtifactsRoot -Force | Out-Null
    $zip = Join-Path $ArtifactsRoot "rustsec-advisory-db.zip"
    Invoke-LoggedNative -Name "download RustSec advisory DB" -FilePath "curl.exe" -Arguments @(
        "-L", "--retry", "5", "--retry-delay", "5", "--connect-timeout", "30", "--max-time", "300",
        "-o", $zip,
        "https://codeload.github.com/RustSec/advisory-db/zip/refs/heads/main"
    )
    Expand-Archive -LiteralPath $zip -DestinationPath $ArtifactsRoot -Force
    $dbRoot = Get-ChildItem -LiteralPath $ArtifactsRoot -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "crates") } |
        Where-Object { $_.Name -like "*advisory-db*" } |
        Select-Object -First 1 -ExpandProperty FullName
    if ([string]::IsNullOrWhiteSpace($dbRoot)) {
        throw "RustSec advisory DB did not extract under $ArtifactsRoot"
    }
    return $dbRoot
}

Save-Summary "running"

try {
    Invoke-LoggedNative -Name "git status" -FilePath "git" -Arguments @("status", "--short", "--branch")
    Invoke-LoggedNative -Name "dotnet info" -FilePath "dotnet" -Arguments @("--info")

    Invoke-LoggedNative -Name "dotnet build solution" -FilePath "dotnet" -Arguments @(
        "build", "Neo.L2.sln", "/p:NuGetAudit=false", "--nologo"
    )
    Invoke-LoggedNative -Name "dotnet test solution" -FilePath "dotnet" -Arguments @(
        "test", "Neo.L2.sln", "/p:NuGetAudit=false", "--nologo", "--no-build"
    )

    if (-not $SkipContracts) {
        Invoke-ContractCompilation
    }

    $rollupDir = New-PrivateChain -Name "private-rollup" -ChainId 1099 -Template "rollup"
    Invoke-OperatorPreflight -ChainDir $rollupDir -ChainId 1099
    Invoke-DevnetRun -Name "rollup reference memory" -BatchCount $Batches `
        -ConfigPath (Join-Path $rollupDir "chain.config.json") -Executor "reference"

    $validiumDir = New-PrivateChain -Name "private-validium" -ChainId 1101 -Template "validium"
    $validiumData = Join-Path $RunDir "private-validium-data"
    Invoke-DevnetRun -Name "validium counter persistent metrics" -BatchCount $Batches `
        -ConfigPath (Join-Path $validiumDir "chain.config.json") -Executor "counter" `
        -DataDir $validiumData -Metrics
    Invoke-DevnetRun -Name "validium counter rehydrate" -BatchCount 0 `
        -ConfigPath (Join-Path $validiumDir "chain.config.json") -Executor "counter" `
        -DataDir $validiumData

    $zkDir = New-PrivateChain -Name "private-zk-rollup" -ChainId 1102 -Template "zk-rollup"
    if (-not $SkipRust) {
        Invoke-RiscVDevnetRun -Name "zk-rollup riscv memory" -BatchCount 1 `
            -ConfigPath (Join-Path $zkDir "chain.config.json")
    }
    else {
        Invoke-DevnetRun -Name "zk-rollup reference memory (SkipRust)" -BatchCount 1 `
            -ConfigPath (Join-Path $zkDir "chain.config.json") -Executor "reference"
    }

    $sidechainDir = New-PrivateChain -Name "private-sidechain" -ChainId 1103 -Template "sidechain"
    Invoke-LoggedNative -Name "neo-stack validate sidechain" -FilePath "dotnet" -Arguments @(
        "run", "--project", "tools\Neo.Stack.Cli", "--",
        "validate", (Join-Path $sidechainDir "chain.config.json")
    )

    $wslRepo = Get-WslPath $RepoRoot
    $stablePrefix = Get-StableRustPrefix $wslRepo

    if (-not $SkipRust) {
        Invoke-WslBash -Name "cargo check neo-riscv-host" -Command @"
$stablePrefix
cd $(Quote-Bash (Join-WslPath $wslRepo "external" "neo-riscv-vm"))
"`$TOOLBIN/cargo" check -p neo-riscv-host
"@

        Invoke-WslBash -Name "cargo build test neo-zkvm-guest" -Command @"
$stablePrefix
cd $(Quote-Bash (Join-WslPath $wslRepo "bridge" "neo-zkvm-guest"))
"`$TOOLBIN/cargo" build
"`$TOOLBIN/cargo" test
"@

        Invoke-WslBash -Name "cargo build test rust sdk" -Command @"
$stablePrefix
cd $(Quote-Bash (Join-WslPath $wslRepo "sdk" "rust"))
"`$TOOLBIN/cargo" build
"`$TOOLBIN/cargo" test
"@

        Invoke-WslBash -Name "cargo build test eth watcher" -Command @"
$stablePrefix
cd $(Quote-Bash (Join-WslPath $wslRepo "watchers" "neo-bridge-watcher-eth"))
"`$TOOLBIN/cargo" build --release
"`$TOOLBIN/cargo" test --release
"`$TOOLBIN/cargo" build --release --features live-rpc
"`$TOOLBIN/cargo" test --release --features live-rpc
"`$TOOLBIN/cargo" clippy --release --all-targets -- -D warnings
"`$TOOLBIN/cargo" clippy --release --features live-rpc --all-targets -- -D warnings
"@

        Invoke-WslBash -Name "cargo build test tron sol watchers" -Command @"
$stablePrefix
cd $(Quote-Bash $wslRepo)
"`$TOOLBIN/cargo" build --release -p neo-bridge-watcher-tron -p neo-bridge-watcher-sol
"`$TOOLBIN/cargo" test --release -p neo-bridge-watcher-tron -p neo-bridge-watcher-sol
"`$TOOLBIN/cargo" clippy --release --all-targets -p neo-bridge-watcher-tron -p neo-bridge-watcher-sol -- -D warnings
"@

        Invoke-WslBash -Name "cargo prove build guest elf" -Command @"
set -euo pipefail
export PATH="`$HOME/.sp1/bin:`$HOME/.cargo/bin:`$PATH"
cd $(Quote-Bash (Join-WslPath $wslRepo "bridge" "neo-zkvm-guest"))
cargo prove build
"@

        Invoke-WslBash -Name "cargo fmt clippy workspace" -Command @"
$stablePrefix
export PATH="`$HOME/.sp1/bin:`$HOME/.cargo/bin:`${TOOLBIN}:`$PATH"
cd $(Quote-Bash $wslRepo)
"`$TOOLBIN/cargo" fmt --all -- --check
"`$TOOLBIN/cargo" clippy --workspace --all-targets --locked -- -D warnings
"@

        Invoke-WslBash -Name "cargo test workspace release" -Command @"
$stablePrefix
export PATH="`$HOME/.sp1/bin:`$HOME/.cargo/bin:`${TOOLBIN}:`$PATH"
cd $(Quote-Bash $wslRepo)
"`$TOOLBIN/cargo" test --workspace --release --locked
"@

        if (-not $SkipRealSp1Proof) {
            Invoke-WslBash -Name "cargo test real SP1 proof" -Command @"
$stablePrefix
export PATH="`$HOME/.sp1/bin:`$HOME/.cargo/bin:`${TOOLBIN}:`$PATH"
cd $(Quote-Bash (Join-WslPath $wslRepo "bridge" "neo-zkvm-host"))
"`$TOOLBIN/cargo" test --release --locked -- --ignored --nocapture
"@
        }
    }

    if (-not $SkipSupplyChainAudit) {
        $rustSecDb = Ensure-RustSecDb
        $wslRustSecDb = Get-WslPath $rustSecDb
        Invoke-WslBash -Name "cargo audit RustSec" -Command @"
set -euo pipefail
cd $(Quote-Bash $wslRepo)
cargo audit --db $(Quote-Bash $wslRustSecDb) --no-fetch --stale --json > $(Quote-Bash (Join-WslPath (Get-WslPath $RunDir) "cargo-audit.json"))
"@
    }

    if (-not $SkipTypeScript) {
        Invoke-LoggedNative -Name "npm install typescript sdk" -FilePath "npm" -Arguments @(
            "install", "--no-audit", "--no-fund"
        ) -WorkingDirectory (Join-Path $RepoRoot "sdk\typescript")
        Invoke-LoggedNative -Name "npm test typescript sdk" -FilePath "npm" -Arguments @("test") `
            -WorkingDirectory (Join-Path $RepoRoot "sdk\typescript")
        Invoke-LoggedNative -Name "npm build typescript sdk" -FilePath "npm" -Arguments @("run", "build") `
            -WorkingDirectory (Join-Path $RepoRoot "sdk\typescript")
        Invoke-LoggedNative -Name "npm audit typescript sdk" -FilePath "npm" -Arguments @("audit", "--audit-level=moderate") `
            -WorkingDirectory (Join-Path $RepoRoot "sdk\typescript")
    }

    if (-not $SkipForeignContracts) {
        Invoke-WslBash -Name "forge test foreign evm" -Command @"
set -euo pipefail
export PATH="`$HOME/.foundry/bin:`$PATH"
cd $(Quote-Bash (Join-WslPath $wslRepo "external" "foreign-contracts" "eth"))
if [ ! -d lib/forge-std ]; then
  forge install --no-git foundry-rs/forge-std
fi
forge test -vv
"@

        Invoke-WslBash -Name "cargo test foreign solana" -Command @"
$stablePrefix
cd $(Quote-Bash (Join-WslPath $wslRepo "external" "foreign-contracts" "sol"))
"`$TOOLBIN/cargo" test
"@
    }

    if (-not $SkipDocs) {
        Invoke-WslBash -Name "mdbook build docs" -Command @"
$stablePrefix
export PATH="`$HOME/.cargo/bin:`${TOOLBIN}:`$PATH"
if ! command -v mdbook >/dev/null 2>&1; then
  "`$TOOLBIN/cargo" install mdbook --version 0.4.40 --locked
fi
cd $(Quote-Bash $wslRepo)
mdbook build
"@
    }

    Invoke-LoggedNative -Name "git status final" -FilePath "git" -Arguments @("status", "--short", "--branch")
    Save-Summary "passed"
    Write-Host "Private-network verification passed. Summary: $(Join-Path $RunDir 'summary.json')"
}
catch {
    Write-Error $_
    exit 1
}
