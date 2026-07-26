[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$RequirePipe
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $required = @('NZ_DEV_HOST', 'NZ_DEV_DATABASE', 'NZ_DEV_USER', 'NZ_DEV_PASSWORD')
    $missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
    if ($missing.Count -gt 0) {
        Write-Error ("Live import proof requires environment variables (values not printed): {0}" -f ($missing -join ', '))
        exit 2
    }

    Write-Host 'NZ_DEV_* variables are set (values not printed).' -ForegroundColor Cyan
    if ($RequirePipe) {
        $env:NZ_REQUIRE_PIPE = '1'
        Write-Host 'NZ_REQUIRE_PIPE=1 (pipe topology failures will fail the run).' -ForegroundColor Cyan
    }

    Write-Host '==> build IntegrationTests' -ForegroundColor Cyan
    dotnet build .\tests\JustyBase.NetezzaSql.IntegrationTests\JustyBase.NetezzaSql.IntegrationTests.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    Write-Host '==> live round-trip proof (Category=Live & RoundTrip)' -ForegroundColor Cyan
    dotnet test .\tests\JustyBase.NetezzaSql.IntegrationTests\JustyBase.NetezzaSql.IntegrationTests.csproj `
        -c $Configuration --no-build `
        --filter 'Category=Live&FullyQualifiedName~RoundTrip' `
        --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Live import round-trip proof failed.' }

    Write-Host 'Run-LiveImportProof: all selected live round-trip tests passed (or soft-skipped only when NZ_REQUIRE_PIPE is unset).' -ForegroundColor Green
}
finally {
    Pop-Location
}
