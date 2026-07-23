[CmdletBinding()]
param(
    [string]$LegacyRoot = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'JustyBase.Legacy'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $LegacyRoot 'App.Data.Netezza/App.Data.Netezza.csproj'
if (-not (Test-Path $project)) { throw "JustyBase.Legacy consumer project was not found: $project" }

# Legacy remains unmodified; it consumes the current source-compatible libraries.
dotnet build $project --configuration $Configuration -p:UseLocalJustyBaseLibraries=true
if ($LASTEXITCODE -ne 0) { throw 'JustyBase.Legacy downstream smoke build failed.' }
