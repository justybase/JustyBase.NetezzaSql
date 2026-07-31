#Requires -Version 7
<#
.SYNOPSIS
  Runs Db2 live parser/linter proof tests against DB2_LIVE_TEST_*.

.DESCRIPTION
  Soft-skips when required env vars are missing unless DB2_LIVE_TEST_REQUIRED=true.
  Soft-skips when Net.IBM.Data.Db2 / clidriver cannot connect (unless REQUIRED=true).
  Uses tests/JustyBase.NetezzaSql.Db2LiveTests (isolated from solution-wide Verify-Local).
#>
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

$required = @('DB2_LIVE_TEST_HOST', 'DB2_LIVE_TEST_DATABASE', 'DB2_LIVE_TEST_USER', 'DB2_LIVE_TEST_PASSWORD')
$missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
if ($missing.Count -gt 0) {
    if ([string]::Equals($env:DB2_LIVE_TEST_REQUIRED, 'true', [StringComparison]::OrdinalIgnoreCase)) {
        throw "DB2_LIVE_TEST_REQUIRED=true but missing: $($missing -join ', ')"
    }
    Write-Host "Db2 live proof soft-skipped; missing: $($missing -join ', ')" -ForegroundColor Yellow
    exit 0
}

Write-Host 'DB2_LIVE_TEST_* variables are set (values not printed).' -ForegroundColor Cyan
Write-Host '==> build Db2LiveTests' -ForegroundColor Cyan
dotnet build .\tests\JustyBase.NetezzaSql.Db2LiveTests\JustyBase.NetezzaSql.Db2LiveTests.csproj -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '==> Db2 live parser/linter tests' -ForegroundColor Cyan
dotnet test .\tests\JustyBase.NetezzaSql.Db2LiveTests\JustyBase.NetezzaSql.Db2LiveTests.csproj `
    -c $Configuration `
    --no-build `
    --filter 'FullyQualifiedName~Db2Live'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Run-Db2LiveProof: Db2 live parser/linter tests passed.' -ForegroundColor Green
