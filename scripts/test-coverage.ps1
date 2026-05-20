param(
    [string]$Configuration = "Release",
    [string]$ResultsDirectory = "coverage/dotnet",
    [double]$Threshold = 90.0,
    [double]$OverallThreshold = 80.0
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$resultsPath = Join-Path $repoRoot $ResultsDirectory
$settingsPath = Join-Path $repoRoot "coverage.runsettings"
$solutionPath = Join-Path $repoRoot "Neo.L2.sln"

if (Test-Path -LiteralPath $resultsPath) {
    Remove-Item -LiteralPath $resultsPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $resultsPath | Out-Null

Push-Location $repoRoot
try {
    dotnet test $solutionPath `
        -c $Configuration `
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
    Pop-Location
}
