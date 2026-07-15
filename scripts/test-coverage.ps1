param(
    [string]$Configuration = "Release",
    [string]$ResultsDirectory = "coverage/dotnet",
    [double]$Threshold = 90.0,
    [double]$OverallThreshold = 80.0
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$resultsPath = Join-Path $repoRoot $ResultsDirectory
$settingsPath = Join-Path $repoRoot "coverage.runsettings"
$solutionPath = Join-Path $repoRoot "Neo.L2.sln"
$riscVManifestPath = Join-Path $repoRoot "external/neo-riscv-vm/Cargo.toml"
$riscVTargetPath = Join-Path $repoRoot "external/neo-riscv-vm/target/release"
$riscVTestOutputPath = Join-Path $repoRoot "tests/Neo.L2.Executor.RiscV.UnitTests/bin/$Configuration"
$isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
$isMacOSPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::OSX)
$nativeLibraryName = if ($isWindowsPlatform) {
    "neo_riscv_host.dll"
} elseif ($isMacOSPlatform) {
    "libneo_riscv_host.dylib"
} else {
    "libneo_riscv_host.so"
}
$nativeLibraryPath = Join-Path $riscVTargetPath $nativeLibraryName
$previousNativeTests = $env:NEO_RISCV_NATIVE_TESTS

if (Test-Path -LiteralPath $resultsPath) {
    Remove-Item -LiteralPath $resultsPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $resultsPath | Out-Null

Push-Location $repoRoot
try {
    Push-Location (Split-Path -Parent $riscVManifestPath)
    try {
        cargo build `
            --release `
            --locked `
            -p neo-riscv-host `
            --manifest-path $riscVManifestPath
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path -LiteralPath $nativeLibraryPath -PathType Leaf)) {
        throw "neo-riscv-host build did not produce $nativeLibraryPath"
    }

    dotnet build $solutionPath `
        -c $Configuration `
        /p:NuGetAudit=false `
        --nologo

    $riscVTestAssemblies = @(Get-ChildItem -Path $riscVTestOutputPath -Recurse -File `
        -Filter "Neo.L2.Executor.RiscV.UnitTests.dll" | `
        Where-Object { $_.FullName -notmatch "[\\/]ref[\\/]" })
    if ($riscVTestAssemblies.Count -ne 1) {
        throw "Expected exactly one RISC-V test assembly under $riscVTestOutputPath; found $($riscVTestAssemblies.Count)."
    }

    Copy-Item -LiteralPath $nativeLibraryPath `
        -Destination $riscVTestAssemblies[0].Directory.FullName `
        -Force
    $env:NEO_RISCV_NATIVE_TESTS = "1"

    dotnet test $solutionPath `
        -c $Configuration `
        --no-build `
        --settings $settingsPath `
        --collect:"XPlat Code Coverage" `
        --results-directory $resultsPath `
        /p:NuGetAudit=false `
        --nologo

    $coverageFiles = @(Get-ChildItem -Path $resultsPath -Recurse -Filter coverage.cobertura.xml)
    if ($coverageFiles.Count -eq 0) {
        throw "No coverage.cobertura.xml files were produced."
    }

    $sourceRoots = @("src", "tools", "samples/executors")
    $actualFiles = foreach ($sourceRoot in $sourceRoots) {
        if (Test-Path -LiteralPath $sourceRoot) {
            Get-ChildItem -Path $sourceRoot -Recurse -Filter *.cs -File |
                Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
                ForEach-Object { $_.FullName.Substring($repoRoot.Path.Length + 1) -replace "\\", "/" }
        }
    }

    function Normalize-CoverageFile([string]$name, [string[]]$knownFiles) {
        $normalized = $name -replace "\\", "/"
        foreach ($prefix in @("/mnt/d/Git/neo-n4/", "D:/Git/neo-n4/")) {
            if ($normalized.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $normalized.Substring($prefix.Length)
            }
        }

        $matches = @($knownFiles | Where-Object { $_.EndsWith($normalized, [System.StringComparison]::OrdinalIgnoreCase) })
        if ($matches.Count -eq 1) { return $matches[0] }

        $basename = Split-Path -Leaf $normalized
        $baseMatches = @($knownFiles | Where-Object { (Split-Path -Leaf $_) -eq $basename })
        if ($baseMatches.Count -eq 1) { return $baseMatches[0] }

        return $normalized
    }

    $lineMap = @{}
    foreach ($coverageFile in $coverageFiles) {
        [xml]$xml = Get-Content -Path $coverageFile.FullName -Raw
        foreach ($class in $xml.coverage.packages.package.classes.class) {
            $filename = Normalize-CoverageFile ([string]$class.filename) $actualFiles
            if ([string]::IsNullOrWhiteSpace($filename)) { continue }

            foreach ($line in $class.lines.line) {
                $key = "${filename}:$($line.number)"
                $hits = [int]$line.hits
                if (-not $lineMap.ContainsKey($key)) {
                    $lineMap[$key] = [pscustomobject]@{
                        file = $filename
                        number = [int]$line.number
                        hits = $hits
                    }
                } elseif ($hits -gt $lineMap[$key].hits) {
                    $lineMap[$key].hits = $hits
                }
            }
        }
    }

    $records = @($lineMap.Values)
    $reportedTotal = $records.Count
    $reportedCovered = @($records | Where-Object { $_.hits -gt 0 }).Count
    if ($reportedTotal -eq 0) { throw "Coverage reports did not contain source lines." }
    $reportedPercent = [math]::Round(100.0 * $reportedCovered / $reportedTotal, 2)
    $reportedFiles = @($records | Select-Object -ExpandProperty file -Unique | Sort-Object)
    $unreportedFiles = @($actualFiles | Where-Object { $reportedFiles -notcontains $_ } | Sort-Object)

    $gateSourceRoots = @("src", "samples/executors")
    function Test-InSourceRoots([string]$file, [string[]]$roots) {
        foreach ($root in $roots) {
            if ($file.StartsWith("$root/", [System.StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
        return $false
    }

    $gateRecords = @($records | Where-Object { Test-InSourceRoots $_.file $gateSourceRoots })
    $gateTotal = $gateRecords.Count
    $gateCovered = @($gateRecords | Where-Object { $_.hits -gt 0 }).Count
    if ($gateTotal -eq 0) { throw "Coverage reports did not contain gate source lines." }
    $gatePercent = [math]::Round(100.0 * $gateCovered / $gateTotal, 2)

    $byFile = @($records | Group-Object file | ForEach-Object {
        $fileTotal = $_.Count
        $fileCovered = @($_.Group | Where-Object { $_.hits -gt 0 }).Count
        [pscustomobject]@{
            file = $_.Name
            covered = $fileCovered
            total = $fileTotal
            percent = [math]::Round(100.0 * $fileCovered / $fileTotal, 2)
        }
    } | Sort-Object percent, file)

    $gateByFile = @($gateRecords | Group-Object file | ForEach-Object {
        $fileTotal = $_.Count
        $fileCovered = @($_.Group | Where-Object { $_.hits -gt 0 }).Count
        [pscustomobject]@{
            file = $_.Name
            covered = $fileCovered
            total = $fileTotal
            percent = [math]::Round(100.0 * $fileCovered / $fileTotal, 2)
        }
    } | Sort-Object percent, file)

    $byModule = @($records | ForEach-Object {
        $parts = $_.file -split "/"
        $module = if ($parts.Count -ge 2) { "$($parts[0])/$($parts[1])" } else { $parts[0] }
        [pscustomobject]@{ module = $module; hits = $_.hits }
    } | Group-Object module | ForEach-Object {
        $moduleTotal = $_.Count
        $moduleCovered = @($_.Group | Where-Object { $_.hits -gt 0 }).Count
        [pscustomobject]@{
            module = $_.Name
            covered = $moduleCovered
            total = $moduleTotal
            percent = [math]::Round(100.0 * $moduleCovered / $moduleTotal, 2)
        }
    } | Sort-Object percent, module)

    $summary = [pscustomobject]@{
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        configuration = $Configuration
        gateThreshold = $Threshold
        overallThreshold = $OverallThreshold
        gateSourceRoots = $gateSourceRoots
        coberturaFiles = $coverageFiles.Count
        nativeRiscVHost = [pscustomobject]@{
            required = $true
            library = $nativeLibraryPath.Substring($repoRoot.Path.Length + 1) -replace "\\", "/"
            sha256 = (Get-FileHash -LiteralPath $nativeLibraryPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        sourceFileAudit = [pscustomobject]@{
            knownSourceFiles = $actualFiles.Count
            reportedSourceFiles = $reportedFiles.Count
            unreportedSourceFiles = $unreportedFiles.Count
            unreportedFiles = $unreportedFiles
        }
        lineCoverage = [pscustomobject]@{
            covered = $gateCovered
            total = $gateTotal
            percent = $gatePercent
        }
        reportedLineCoverage = [pscustomobject]@{
            covered = $reportedCovered
            total = $reportedTotal
            percent = $reportedPercent
        }
        lowestFiles = @($byFile | Select-Object -First 30)
        lowestGateFiles = @($gateByFile | Select-Object -First 30)
        modules = $byModule
    }

    $summaryPath = Join-Path $resultsPath "dotnet-line-coverage-summary.json"
    $summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath -Encoding UTF8

    Write-Host "Gate line coverage: $gateCovered / $gateTotal = $gatePercent%"
    Write-Host "Overall reported line coverage: $reportedCovered / $reportedTotal = $reportedPercent%"
    Write-Host "Source file audit: $($reportedFiles.Count) / $($actualFiles.Count) files reported by Cobertura; $($unreportedFiles.Count) not reported"
    Write-Host "Coverage summary: $summaryPath"
    Write-Host "Lowest gate files:"
    $gateByFile | Select-Object -First 10 | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "Lowest reported files:"
    $byFile | Select-Object -First 10 | Format-Table -AutoSize | Out-String | Write-Host

    if ($gatePercent -lt $Threshold) {
        throw "Gate line coverage $gatePercent% is below required threshold $Threshold%."
    }
    if ($reportedPercent -lt $OverallThreshold) {
        throw "Overall reported line coverage $reportedPercent% is below required threshold $OverallThreshold%."
    }
}
finally {
    if ($null -eq $previousNativeTests) {
        Remove-Item Env:NEO_RISCV_NATIVE_TESTS -ErrorAction SilentlyContinue
    } else {
        $env:NEO_RISCV_NATIVE_TESTS = $previousNativeTests
    }
    Pop-Location
}
