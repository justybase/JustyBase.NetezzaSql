[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$PackageDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/packages')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$packages = Get-ChildItem -Path $PackageDirectory -Filter '*.nupkg' | Where-Object { $_.Name -notlike '*.symbols.nupkg' }
$required = 'JustyBase.NetezzaSqlParser', 'JustyBase.NetezzaDdl', 'JustyBase.NetezzaCatalogSql', 'JustyBase.Netezza', 'JustyBase.Core', 'JustyBase.ImportExport'
$versions = @{}
foreach ($id in $required) {
    $package = $packages | Where-Object { $_.Name -match "^$([regex]::Escape($id))\.(.+)\.nupkg$" } | Select-Object -First 1
    if ($null -eq $package) { throw "Package $id was not found in $PackageDirectory." }
    $versions[$id] = [regex]::Match($package.Name, "^$([regex]::Escape($id))\.(.+)\.nupkg$").Groups[1].Value
}

$consumerRoot = Join-Path $repoRoot 'artifacts/package-consumer'
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $consumerRoot
New-Item -ItemType Directory -Force -Path $consumerRoot | Out-Null
$escapedSource = [Security.SecurityElement]::Escape((Resolve-Path $PackageDirectory).Path)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RestoreSources>$escapedSource;`$(RestoreSources)</RestoreSources></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="JustyBase.NetezzaSqlParser" Version="$($versions['JustyBase.NetezzaSqlParser'])" />
    <PackageReference Include="JustyBase.NetezzaDdl" Version="$($versions['JustyBase.NetezzaDdl'])" />
    <PackageReference Include="JustyBase.NetezzaCatalogSql" Version="$($versions['JustyBase.NetezzaCatalogSql'])" />
    <PackageReference Include="JustyBase.Netezza" Version="$($versions['JustyBase.Netezza'])" />
    <PackageReference Include="JustyBase.Core" Version="$($versions['JustyBase.Core'])" />
    <PackageReference Include="JustyBase.ImportExport" Version="$($versions['JustyBase.ImportExport'])" />
  </ItemGroup>
</Project>
"@ | Set-Content -NoNewline (Join-Path $consumerRoot 'PackageConsumer.csproj')
@"
using JustyBase.Core.Risk;
using JustyBase.ImportExport.Export;
using JustyBase.Netezza;
using JustyBase.NetezzaCatalogSql;
using JustyBase.NetezzaDdl;
using JustyBase.NetezzaSqlParser.Lexer;

Console.WriteLine(NzLexer.Tokenize("SELECT 1").Count());
Console.WriteLine(NetezzaNameHelper.QuoteNameIfNeeded("sample"));
Console.WriteLine(NetezzaCatalogSql.GetSchemasSql("SAMPLE"));
Console.WriteLine(NetezzaResult<int>.Ok(42).Value);
Console.WriteLine(new SqlRiskAnalysisService().Analyze("UPDATE t SET a=1").Count);
Console.WriteLine(typeof(CsvExportWriter).FullName);
"@ | Set-Content -NoNewline (Join-Path $consumerRoot 'Program.cs')

dotnet build (Join-Path $consumerRoot 'PackageConsumer.csproj') --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Package-only consumer build failed.' }
