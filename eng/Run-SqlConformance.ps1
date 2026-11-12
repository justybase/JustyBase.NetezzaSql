[CmdletBinding()]
param(
    [string]$CorpusPath = (Join-Path (Split-Path -Parent $PSScriptRoot) '..\JustyBase.SqlConformance'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Report
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$corpusRoot = (Resolve-Path $CorpusPath).Path
$env:JUSTYBASE_SQL_CONFORMANCE_PATH = $corpusRoot

Push-Location $repoRoot
try {
    if ($Report) {
        Write-Host '==> shared SQL conformance report' -ForegroundColor Cyan
        python (Join-Path $corpusRoot 'scripts/run_dotnet.py') --dotnet-repo $repoRoot --output $Report
        if ($LASTEXITCODE -ne 0) { throw 'shared SQL conformance report failed.' }
    }
    else {
        Write-Host '==> shared SQL conformance tests' -ForegroundColor Cyan
        dotnet test .\JustyBase.NetezzaSql.sln -c $Configuration --filter 'Category=SqlConformance'
        if ($LASTEXITCODE -ne 0) { throw 'shared SQL conformance tests failed.' }
    }
}
finally {
    Pop-Location
}
