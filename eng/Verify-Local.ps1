[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$FullCi
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    Write-Host '==> dotnet restore' -ForegroundColor Cyan
    dotnet restore .\JustyBase.NetezzaSql.sln
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    Write-Host '==> dotnet build' -ForegroundColor Cyan
    dotnet build .\JustyBase.NetezzaSql.sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    Write-Host '==> dotnet test' -ForegroundColor Cyan
    dotnet test .\JustyBase.NetezzaSql.sln -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }

    Write-Host '==> per-library coverage' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'Test-Coverage.ps1') -Configuration $Configuration

    if ($FullCi) {
        Write-Host '==> vulnerable packages' -ForegroundColor Cyan
        dotnet list .\JustyBase.NetezzaSql.sln package --vulnerable --include-transitive
        if ($LASTEXITCODE -ne 0) { throw 'vulnerable package check failed.' }

        $packageDir = Join-Path $repoRoot 'artifacts/packages'
        New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
        Write-Host '==> dotnet pack' -ForegroundColor Cyan
        dotnet pack .\JustyBase.NetezzaSqlParser.csproj --no-build -c $Configuration -o $packageDir
        dotnet pack .\JustyBase.NetezzaDdl\JustyBase.NetezzaDdl.csproj --no-build -c $Configuration -o $packageDir
        dotnet pack .\JustyBase.NetezzaCatalogSql\JustyBase.NetezzaCatalogSql.csproj --no-build -c $Configuration -o $packageDir
        dotnet pack .\JustyBase.Netezza\JustyBase.Netezza.csproj --no-build -c $Configuration -o $packageDir
        dotnet pack .\JustyBase.Core\JustyBase.Core.csproj --no-build -c $Configuration -o $packageDir
        dotnet pack .\JustyBase.ImportExport\JustyBase.ImportExport.csproj --no-build -c $Configuration -o $packageDir
        if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed.' }

        Write-Host '==> package consumer' -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot 'Test-PackageConsumer.ps1') -Configuration $Configuration
    }

    Write-Host '==> git diff --check' -ForegroundColor Cyan
    git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'git diff --check reported problems.' }

    Write-Host 'Verify-Local: all checks passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
