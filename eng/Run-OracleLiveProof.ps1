#Requires -Version 7
<#
.SYNOPSIS
  Runs Oracle live parser/linter proof tests against ORACLE_LIVE_TEST_*.

.DESCRIPTION
  Soft-skips when required env vars are missing unless ORACLE_LIVE_TEST_REQUIRED=true.
#>
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

$required = @('ORACLE_LIVE_TEST_HOST', 'ORACLE_LIVE_TEST_DATABASE', 'ORACLE_LIVE_TEST_USER', 'ORACLE_LIVE_TEST_PASSWORD')
$missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
if ($missing.Count -gt 0) {
    if ([string]::Equals($env:ORACLE_LIVE_TEST_REQUIRED, 'true', [StringComparison]::OrdinalIgnoreCase)) {
        throw "ORACLE_LIVE_TEST_REQUIRED=true but missing: $($missing -join ', ')"
    }
    Write-Host "Oracle live proof soft-skipped; missing: $($missing -join ', ')" -ForegroundColor Yellow
    exit 0
}

Write-Host 'ORACLE_LIVE_TEST_* variables are set (values not printed).' -ForegroundColor Cyan
Write-Host '==> build IntegrationTests' -ForegroundColor Cyan
dotnet build .\tests\JustyBase.NetezzaSql.IntegrationTests\JustyBase.NetezzaSql.IntegrationTests.csproj -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '==> Oracle live parser/linter tests' -ForegroundColor Cyan
dotnet test .\tests\JustyBase.NetezzaSql.IntegrationTests\JustyBase.NetezzaSql.IntegrationTests.csproj `
    -c $Configuration `
    --no-build `
    --filter 'FullyQualifiedName~OracleLive'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Run-OracleLiveProof: Oracle live parser/linter tests passed.' -ForegroundColor Green
