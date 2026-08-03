#Requires -Version 7
param([string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

$required = @('MYSQL_LIVE_TEST_HOST', 'MYSQL_LIVE_TEST_DATABASE', 'MYSQL_LIVE_TEST_USER', 'MYSQL_LIVE_TEST_PASSWORD')
$missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
if ($missing.Count -gt 0) {
    if ([string]::Equals($env:MYSQL_LIVE_TEST_REQUIRED, 'true', [StringComparison]::OrdinalIgnoreCase)) {
        throw "MYSQL_LIVE_TEST_REQUIRED=true but missing: $($missing -join ', ')"
    }
    Write-Host "MySQL live proof soft-skipped; missing: $($missing -join ', ')" -ForegroundColor Yellow
    exit 0
}

Write-Host 'MYSQL_LIVE_TEST_* variables are set (values not printed).' -ForegroundColor Cyan
$project = '.\tests\JustyBase.NetezzaSql.MySqlLiveTests\JustyBase.NetezzaSql.MySqlLiveTests.csproj'
dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test $project -c $Configuration --no-build --filter 'FullyQualifiedName~MySqlLive'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host 'Run-MySqlLiveProof: MySQL live parser/linter tests passed.' -ForegroundColor Green
