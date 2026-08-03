#Requires -Version 7
param([string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

$required = @('POSTGRES_LIVE_TEST_HOST', 'POSTGRES_LIVE_TEST_DATABASE', 'POSTGRES_LIVE_TEST_USER', 'POSTGRES_LIVE_TEST_PASSWORD')
$connectString = [Environment]::GetEnvironmentVariable('POSTGRES_LIVE_TEST_CONNECT_STRING')
$missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
if ([string]::IsNullOrWhiteSpace($connectString) -and $missing.Count -gt 0) {
    if ([string]::Equals($env:POSTGRES_LIVE_TEST_REQUIRED, 'true', [StringComparison]::OrdinalIgnoreCase)) {
        throw "POSTGRES_LIVE_TEST_REQUIRED=true but missing: $($missing -join ', ')"
    }
    Write-Host "PostgreSQL live proof soft-skipped; missing: $($missing -join ', ')" -ForegroundColor Yellow
    exit 0
}

Write-Host 'POSTGRES_LIVE_TEST_* variables are set (values not printed).' -ForegroundColor Cyan
$project = '.\tests\JustyBase.NetezzaSql.PostgreSqlLiveTests\JustyBase.NetezzaSql.PostgreSqlLiveTests.csproj'
dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test $project -c $Configuration --no-build --filter 'FullyQualifiedName~PostgreSqlLive'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host 'Run-PostgreSqlLiveProof: PostgreSQL live parser tests passed.' -ForegroundColor Green
