#Requires -Version 7
<#
.SYNOPSIS
  Runs MSSQL (SQL Server) live parser/linter proof tests against MSSQL_LIVE_TEST_*.

.DESCRIPTION
  Soft-skips when required env vars are missing unless MSSQL_LIVE_TEST_REQUIRED=true.
  Soft-skips when Microsoft.Data.SqlClient cannot connect (unless REQUIRED=true).
  Uses tests/JustyBase.NetezzaSql.MssqlLiveTests (isolated from solution-wide Verify-Local).
#>
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

$required = @('MSSQL_LIVE_TEST_HOST', 'MSSQL_LIVE_TEST_DATABASE', 'MSSQL_LIVE_TEST_USER', 'MSSQL_LIVE_TEST_PASSWORD')
$missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
if ($missing.Count -gt 0) {
    if ([string]::Equals($env:MSSQL_LIVE_TEST_REQUIRED, 'true', [StringComparison]::OrdinalIgnoreCase)) {
        throw "MSSQL_LIVE_TEST_REQUIRED=true but missing: $($missing -join ', ')"
    }
    Write-Host "MSSQL live proof soft-skipped; missing: $($missing -join ', ')" -ForegroundColor Yellow
    exit 0
}

Write-Host 'MSSQL_LIVE_TEST_* variables are set (values not printed).' -ForegroundColor Cyan
Write-Host '==> build MssqlLiveTests' -ForegroundColor Cyan
dotnet build .\tests\JustyBase.NetezzaSql.MssqlLiveTests\JustyBase.NetezzaSql.MssqlLiveTests.csproj -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '==> MSSQL live parser/linter tests' -ForegroundColor Cyan
dotnet test .\tests\JustyBase.NetezzaSql.MssqlLiveTests\JustyBase.NetezzaSql.MssqlLiveTests.csproj `
    -c $Configuration `
    --no-build `
    --filter 'FullyQualifiedName~MssqlLive'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Run-MssqlLiveProof: MSSQL live parser/linter tests passed.' -ForegroundColor Green
