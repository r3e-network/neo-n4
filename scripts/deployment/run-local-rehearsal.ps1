param(
    [string]$OutputRoot = "",
    [int]$AnvilPort = 18545,
    [switch]$SkipAnvil
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputRoot = Join-Path $RepoRoot "artifacts\local-deployment-rehearsal\$stamp"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$HubDir = Join-Path $OutputRoot "hub"
$BridgeDir = Join-Path $OutputRoot "external-bridge"
$DataDir = Join-Path $OutputRoot "data"
$LogDir = Join-Path $OutputRoot "logs"
New-Item -ItemType Directory -Force -Path $HubDir, $BridgeDir, $DataDir, $LogDir | Out-Null

function Assert-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found on PATH: $Name"
    }
}

function Convert-ToWslPath {
    param([string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path)
    if ($full -notmatch "^([A-Za-z]):\\(.*)$") {
        throw "Cannot convert non-drive path to WSL path: $full"
    }
    $drive = $Matches[1].ToLowerInvariant()
    $rest = $Matches[2] -replace "\\", "/"
    return "/mnt/$drive/$rest"
}

function Join-ProcessArguments {
    param([string[]]$Arguments)
    return ($Arguments | ForEach-Object {
        if ($_ -match "[\s`"]") {
            '"' + ($_ -replace '"', '\"') + '"'
        }
        else {
            $_
        }
    }) -join " "
}

function Invoke-Logged {
    param(
        [string]$Name,
        [string]$File,
        [string[]]$Arguments,
        [string]$WorkingDirectory = $RepoRoot
    )

    $safe = ($Name -replace "[^A-Za-z0-9_.-]", "-").Trim("-")
    $log = Join-Path $LogDir "$safe.log"
    "==> $Name" | Tee-Object -FilePath $log
    "cwd: $WorkingDirectory" | Tee-Object -FilePath $log -Append
    "cmd: $File $($Arguments -join ' ')" | Tee-Object -FilePath $log -Append
    Push-Location $WorkingDirectory
    try {
        $stdout = Join-Path $LogDir "$safe.stdout.tmp"
        $stderr = Join-Path $LogDir "$safe.stderr.tmp"
        try {
            $process = Start-Process `
                -FilePath $File `
                -ArgumentList (Join-ProcessArguments $Arguments) `
                -WorkingDirectory $WorkingDirectory `
                -NoNewWindow `
                -Wait `
                -PassThru `
                -RedirectStandardOutput $stdout `
                -RedirectStandardError $stderr
            $exitCode = $process.ExitCode

            foreach ($stream in @($stdout, $stderr)) {
                if (Test-Path -LiteralPath $stream) {
                    $bytes = [System.IO.File]::ReadAllBytes($stream)
                    if ($bytes.Length -gt 0) {
                        $cleanBytes = $bytes | Where-Object { $_ -ne 0 }
                        if ($cleanBytes.Count -gt 0) {
                            $text = [System.Text.Encoding]::UTF8.GetString([byte[]]$cleanBytes)
                            $text | Tee-Object -FilePath $log -Append
                        }
                    }
                }
            }

            if ($exitCode -ne 0) {
                throw "$Name failed with exit code $exitCode; see $log"
            }
        }
        finally {
            Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
        }
    }
    finally {
        Pop-Location
    }
}

function Get-ProjectFromArtifact {
    param([string]$ArtifactPath)
    $artifactFull = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $ArtifactPath))
    $contractDir = Split-Path (Split-Path (Split-Path $artifactFull -Parent) -Parent) -Parent
    $name = Split-Path $contractDir -Leaf
    return Join-Path $contractDir "$name.csproj"
}

Assert-Command "dotnet"
Assert-Command "nccs"

$summary = [ordered]@{
    startedAt = (Get-Date).ToString("o")
    repoRoot = $RepoRoot
    outputRoot = $OutputRoot
    steps = New-Object System.Collections.Generic.List[object]
}

function Add-StepResult {
    param([string]$Name, [string]$Status, [string]$Detail = "")
    $summary.steps.Add([ordered]@{ name = $Name; status = $Status; detail = $Detail }) | Out-Null
}

try {
    $planPath = Join-Path $HubDir "deploy-plan.json"
    $bundlePath = Join-Path $HubDir "deploy-bundle.json"

    Invoke-Logged "hub-scaffold" "dotnet" @(
        "run", "--project", "tools\Neo.Hub.Deploy", "--",
        "scaffold", "--output", $planPath
    )
    Invoke-Logged "hub-plan" "dotnet" @(
        "run", "--project", "tools\Neo.Hub.Deploy", "--",
        "plan", "--plan", $planPath, "--output", $bundlePath
    )
    Add-StepResult "NeoHub deploy plan" "passed" "Generated scaffold and resolved bundle."

    $plan = Get-Content $planPath -Raw | ConvertFrom-Json
    $contractProjects = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($step in $plan.steps) {
        [void]$contractProjects.Add((Get-ProjectFromArtifact $step.nefPath))
    }
    foreach ($dir in @(Get-ChildItem -Directory (Join-Path $RepoRoot "samples\contracts\Sample.*"))) {
        [void]$contractProjects.Add((Join-Path $dir.FullName "$($dir.Name).csproj"))
    }

    foreach ($project in ($contractProjects | Sort-Object)) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
        $projectDir = Split-Path $project -Parent
        $outDir = Join-Path $projectDir "bin\sc"
        Invoke-Logged "build-$projectName" "dotnet" @("build", $project, "/p:NuGetAudit=false", "--nologo")
        Invoke-Logged "nccs-$projectName" "nccs" @($project, "--output", $outDir)
        $nef = Join-Path $outDir "$projectName.nef"
        $manifest = Join-Path $outDir "$projectName.manifest.json"
        if (-not (Test-Path -LiteralPath $nef)) { throw "Missing NEF after nccs: $nef" }
        if (-not (Test-Path -LiteralPath $manifest)) { throw "Missing manifest after nccs: $manifest" }
    }
    Add-StepResult "Contract artifacts" "passed" "Built and nccs-compiled $($contractProjects.Count) smart-contract projects."

    Invoke-Logged "l2-native-contracts" "dotnet" @(
        "test", "external\neo\tests\Neo.UnitTests\Neo.UnitTests.csproj",
        "--filter", "FullyQualifiedName~UT_L2NativeContracts",
        "/p:NuGetAudit=false", "--nologo"
    )
    Add-StepResult "L2 native contracts" "passed" "Verified N4 L2 native contracts are registered in the r3e Neo core fork."

    Invoke-Logged "hub-verify-artifacts" "dotnet" @(
        "run", "--project", "tools\Neo.Hub.Deploy", "--",
        "verify", "--plan", $planPath, "--rpc", "self-contained-local"
    )

    $devnetScratch = Join-Path $DataDir "devnet"
    Invoke-Logged "devnet-default-5" "dotnet" @("run", "--project", "tools\Neo.L2.Devnet", "--", "5")
    Invoke-Logged "devnet-persistent-5" "dotnet" @("run", "--project", "tools\Neo.L2.Devnet", "--", "5", "--data-dir", (Join-Path $devnetScratch "persistent-default"))
    Invoke-Logged "devnet-rehydrate-0" "dotnet" @("run", "--project", "tools\Neo.L2.Devnet", "--", "0", "--data-dir", (Join-Path $devnetScratch "persistent-default"))
    Invoke-Logged "devnet-counter-3" "dotnet" @("run", "--project", "tools\Neo.L2.Devnet", "--", "3", "--executor", "counter", "--data-dir", (Join-Path $devnetScratch "counter"))
    Invoke-Logged "devnet-neovm-3" "dotnet" @("run", "--project", "tools\Neo.L2.Devnet", "--", "3", "--executor", "neovm", "--data-dir", (Join-Path $devnetScratch "neovm"))
    Invoke-Logged "devnet-general-rollup-2" "dotnet" @("run", "--project", "tools\Neo.L2.Devnet", "--", "2", "--config", "samples\general-rollup.config.json", "--data-dir", (Join-Path $devnetScratch "general-rollup"))
    Add-StepResult "Local devnet flows" "passed" "Default, persistent, rehydrate, counter, neovm, and sample-config runs passed."

    $keyDir = Join-Path $DataDir "external-bridge-keys"
    New-Item -ItemType Directory -Force -Path $keyDir | Out-Null
    $pubs = New-Object System.Collections.Generic.List[string]
    $ethAddrs = New-Object System.Collections.Generic.List[string]
    for ($i = 1; $i -le 3; $i++) {
        $keyPath = Join-Path $keyDir "watcher-$i.priv"
        Invoke-Logged "external-bridge-genkey-$i" "dotnet" @(
            "run", "--project", "tools\Neo.External.Bridge.Cli", "--",
            "genkey", "--out", $keyPath
        )
        $log = Get-Content (Join-Path $LogDir "external-bridge-genkey-$i.log") -Raw
        $pub = [regex]::Match($log, "pub33\s*=\s*(0x[0-9a-fA-F]+)").Groups[1].Value
        $eth = [regex]::Match($log, "ethAddr\s*=\s*(0x[0-9a-fA-F]+)").Groups[1].Value
        if ([string]::IsNullOrWhiteSpace($pub) -or [string]::IsNullOrWhiteSpace($eth)) {
            throw "Could not parse watcher key output for watcher $i"
        }
        $pubs.Add($pub) | Out-Null
        $ethAddrs.Add($eth) | Out-Null
    }
    $pubsPath = Join-Path $BridgeDir "watchers.pubs"
    $ethPath = Join-Path $BridgeDir "watchers.eth-addresses.txt"
    Set-Content -LiteralPath $pubsPath -Value $pubs -Encoding ascii
    Set-Content -LiteralPath $ethPath -Value ($ethAddrs -join ",") -Encoding ascii

    $committeeOut = Join-Path $BridgeDir "committee-blob.out"
    Invoke-Logged "external-bridge-committee-blob" "dotnet" @(
        "run", "--project", "tools\Neo.External.Bridge.Cli", "--",
        "committee-blob", "--pubs-file", $pubsPath
    )
    Copy-Item -LiteralPath (Join-Path $LogDir "external-bridge-committee-blob.log") -Destination $committeeOut -Force
    $committeeText = Get-Content $committeeOut -Raw
    $committeeBlob = [regex]::Match($committeeText, "0x[0-9a-fA-F]{66,}").Value
    if ([string]::IsNullOrWhiteSpace($committeeBlob)) { throw "Could not parse committee blob." }

    $deployBundleOut = Join-Path $BridgeDir "deploy-bundle.out"
    Invoke-Logged "external-bridge-deploy-bundle" "dotnet" @(
        "run", "--project", "tools\Neo.External.Bridge.Cli", "--",
        "deploy-bundle",
        "--external-chain-id", "0xE0000002",
        "--verifier", "0x1111111111111111111111111111111111111111",
        "--registry", "0x2222222222222222222222222222222222222222",
        "--escrow", "0x3333333333333333333333333333333333333333",
        "--eth-router", "0x4444444444444444444444444444444444444444",
        "--threshold", "2",
        "--committee-blob", $committeeBlob,
        "--eth-addresses", ($ethAddrs -join ",")
    )
    Copy-Item -LiteralPath (Join-Path $LogDir "external-bridge-deploy-bundle.log") -Destination $deployBundleOut -Force
    Add-StepResult "External bridge bundle" "passed" "Generated local watcher committee and dual-side checklist."

    Invoke-Logged "test-hub-deploy" "dotnet" @("test", "tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj", "--no-restore", "--nologo", "/p:NuGetAudit=false")
    Invoke-Logged "test-external-bridge-cli" "dotnet" @("test", "tests\Neo.External.Bridge.Cli.UnitTests\Neo.External.Bridge.Cli.UnitTests.csproj", "--no-restore", "--nologo", "/p:NuGetAudit=false")
    Invoke-Logged "test-devnet" "dotnet" @("test", "tests\Neo.L2.Devnet.UnitTests\Neo.L2.Devnet.UnitTests.csproj", "--no-restore", "--nologo", "/p:NuGetAudit=false")
    Invoke-Logged "test-integration" "dotnet" @("test", "tests\Neo.L2.IntegrationTests\Neo.L2.IntegrationTests.csproj", "--no-restore", "--nologo", "/p:NuGetAudit=false")
    Invoke-Logged "test-bridge" "dotnet" @("test", "tests\Neo.L2.Bridge.UnitTests\Neo.L2.Bridge.UnitTests.csproj", "--no-restore", "--nologo", "/p:NuGetAudit=false")
    Add-StepResult ".NET deployment-adjacent tests" "passed" "Hub deploy, external bridge CLI, devnet, integration, and bridge tests passed."

    if (-not $SkipAnvil) {
        Assert-Command "wsl.exe"
        $wslRepo = Convert-ToWslPath $RepoRoot
        $wslOut = Convert-ToWslPath $OutputRoot
        $members = $ethAddrs -join ","
        $owner = "0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266"
        $pk = "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80"
        $bashTemplate = @'
set -euo pipefail
cd "__WSL_REPO__/external/foreign-contracts/eth"
mkdir -p "__WSL_OUT__/external-bridge"
anvil_pid=""
cleanup() {
  if [[ -n "$anvil_pid" ]]; then
    kill "$anvil_pid" >/dev/null 2>&1 || true
    wait "$anvil_pid" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT
"$HOME/.foundry/bin/anvil" --host 127.0.0.1 --port __ANVIL_PORT__ > "__WSL_OUT__/external-bridge/anvil.log" 2>&1 &
anvil_pid=$!
for i in {1..30}; do
  if "$HOME/.foundry/bin/cast" block-number --rpc-url http://127.0.0.1:__ANVIL_PORT__ >/dev/null 2>&1; then break; fi
  sleep 1
done
"$HOME/.foundry/bin/cast" block-number --rpc-url http://127.0.0.1:__ANVIL_PORT__ >/dev/null
router=$("$HOME/.foundry/bin/forge" create --rpc-url http://127.0.0.1:__ANVIL_PORT__ --private-key __PK__ --broadcast src/NeoExternalBridgeRouter.sol:NeoExternalBridgeRouter --constructor-args 0xE0000002 __OWNER__ | tee "__WSL_OUT__/external-bridge/anvil-forge-create.log" | awk '/Deployed to:/ {print $3}')
if [[ -z "$router" ]]; then
  echo "Failed to parse deployed router address." >&2
  exit 1
fi
echo "router=$router" | tee "__WSL_OUT__/external-bridge/anvil-router.txt"
"$HOME/.foundry/bin/cast" send "$router" 'setCommittee(address[],uint8)' '[__MEMBERS__]' 2 --private-key __PK__ --rpc-url http://127.0.0.1:__ANVIL_PORT__ | tee "__WSL_OUT__/external-bridge/anvil-setcommittee.log"
"$HOME/.foundry/bin/cast" call "$router" 'threshold()(uint8)' --rpc-url http://127.0.0.1:__ANVIL_PORT__ | tee "__WSL_OUT__/external-bridge/anvil-threshold.log"
"$HOME/.foundry/bin/cast" send "$router" 'lockETHAndSend(uint32,bytes20,bytes,uint64)' 0 0x0000000000000000000000000000000000000001 0x 0 --value 1 --private-key __PK__ --rpc-url http://127.0.0.1:__ANVIL_PORT__ | tee "__WSL_OUT__/external-bridge/anvil-lock.log"
"$HOME/.foundry/bin/cast" call "$router" 'lockedBalances(address)(uint256)' 0x0000000000000000000000000000000000000000 --rpc-url http://127.0.0.1:__ANVIL_PORT__ | tee "__WSL_OUT__/external-bridge/anvil-locked-balance.log"
"$HOME/.foundry/bin/forge" test -vv | tee "__WSL_OUT__/external-bridge/forge-test.log"
'@
        $bash = $bashTemplate.
            Replace("__WSL_REPO__", $wslRepo).
            Replace("__WSL_OUT__", $wslOut).
            Replace("__ANVIL_PORT__", [string]$AnvilPort).
            Replace("__PK__", $pk).
            Replace("__OWNER__", $owner).
            Replace("__MEMBERS__", $members)
        $anvilScript = Join-Path $BridgeDir "anvil-rehearsal.sh"
        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText($anvilScript, ($bash -replace "`r`n", "`n"), $utf8NoBom)
        Invoke-Logged "anvil-evm-rehearsal" "wsl.exe" @("bash", (Convert-ToWslPath $anvilScript))
        Add-StepResult "Local EVM Anvil rehearsal" "passed" "Router deploy, committee registration, lock path, and Foundry tests passed."
    }

    Assert-Command "wsl.exe"
    Invoke-Logged "foreign-solana-cargo-test" "wsl.exe" @("bash", "-lc", "cd $(Convert-ToWslPath $RepoRoot)/external/foreign-contracts/sol && cargo test")
    Add-StepResult "Foreign Solana tests" "passed" "cargo test passed."

    $summary.finishedAt = (Get-Date).ToString("o")
    $summary.status = "passed"
}
catch {
    $summary.finishedAt = (Get-Date).ToString("o")
    $summary.status = "failed"
    $summary.error = $_.Exception.Message
    throw
}
finally {
    $summaryPath = Join-Path $OutputRoot "summary.json"
    ($summary | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $summaryPath -Encoding utf8
    $md = Join-Path $OutputRoot "README.md"
    @(
        "# Local Deployment Rehearsal",
        "",
        "- Status: $($summary.status)",
        "- Started: $($summary.startedAt)",
        "- Finished: $($summary.finishedAt)",
        "- Output: `$OutputRoot`",
        "",
        "## Steps",
        ""
    ) + ($summary.steps | ForEach-Object { "- $($_.status): $($_.name) - $($_.detail)" }) |
        Set-Content -LiteralPath $md -Encoding utf8
    Write-Host "Local deployment rehearsal output: $OutputRoot"
}
