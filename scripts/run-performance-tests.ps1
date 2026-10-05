[CmdletBinding()]
param(
    [string]$Output
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Output)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $Output = Join-Path $repoRoot 'logs\performance-results.json'
}
$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) { throw 'Node.js is required to run BuildWise performance tests.' }

& $node.Source (Join-Path $PSScriptRoot 'performance-test.mjs') $Output
exit $LASTEXITCODE
