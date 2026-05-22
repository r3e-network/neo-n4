param(
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$python = Get-Command python -ErrorAction Stop

Push-Location $repoRoot
try {
    & $python.Source "tools/docs/generate_legacy_paper_figures.py"
    & $python.Source "tools/docs/generate_paper_figures.py"
}
finally {
    Pop-Location
}

Write-Output "Regenerated paper-style Neo N4 figures under docs/figures and docs/zh/figures."
