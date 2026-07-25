[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$PrintCurrent
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$baselinePath = Join-Path $PSScriptRoot 'api-baseline.json'
$assemblies = @(
    @{ Name = 'JustyBase.NetezzaSqlParser'; Path = 'bin/{0}/net10.0/JustyBase.NetezzaSqlParser.dll' -f $Configuration },
    @{ Name = 'JustyBase.NetezzaDdl'; Path = 'JustyBase.NetezzaDdl/bin/{0}/net10.0/JustyBase.NetezzaDdl.dll' -f $Configuration },
    @{ Name = 'JustyBase.NetezzaCatalogSql'; Path = 'JustyBase.NetezzaCatalogSql/bin/{0}/net10.0/JustyBase.NetezzaCatalogSql.dll' -f $Configuration },
    @{ Name = 'JustyBase.Netezza'; Path = 'JustyBase.Netezza/bin/{0}/net10.0/JustyBase.Netezza.dll' -f $Configuration },
    @{ Name = 'JustyBase.NetezzaSqlLsp'; Path = 'JustyBase.NetezzaSqlLsp/bin/{0}/net10.0/JustyBase.NetezzaSqlLsp.dll' -f $Configuration }
)

$assemblyResolver = [ResolveEventHandler]{
    param($sender, $eventArgs)
    $name = [Reflection.AssemblyName]$eventArgs.Name
    $simpleName = $name.Name
    if ($simpleName -in @('Superpower', 'Microsoft.Extensions.DependencyInjection.Abstractions')) {
        # Derive search paths from the assembly output directories
        $searchPaths = $assemblies | ForEach-Object {
            Join-Path $repoRoot (Split-Path $_.Path -Parent)
        } | Sort-Object -Unique
        foreach ($dir in $searchPaths) {
            $path = Join-Path $dir "$simpleName.dll"
            if (Test-Path $path) { return [Reflection.Assembly]::LoadFrom($path) }
        }
        # Fallback: NuGet global cache (cross-platform)
        if ($simpleName -eq 'Superpower') {
            $fallback = Join-Path $HOME '.nuget/packages/superpower/3.2.1/lib/netstandard2.0/Superpower.dll'
            if (Test-Path $fallback) { return [Reflection.Assembly]::LoadFrom($fallback) }
        }
        if ($simpleName -eq 'Microsoft.Extensions.DependencyInjection.Abstractions') {
            $pkgRoot = Join-Path $HOME '.nuget/packages/microsoft.extensions.dependencyinjection.abstractions'
            if (Test-Path $pkgRoot) {
                $fallback = Get-ChildItem -Path $pkgRoot -Recurse -Filter 'Microsoft.Extensions.DependencyInjection.Abstractions.dll' |
                    Where-Object { $_.FullName -match '[\\/]lib[\\/]net(10\.0|9\.0|8\.0|standard2\.0)[\\/]' } |
                    Sort-Object FullName -Descending |
                    Select-Object -First 1
                if ($null -ne $fallback) { return [Reflection.Assembly]::LoadFrom($fallback.FullName) }
            }
        }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($assemblyResolver)

function Get-PublicApiHash([string]$assemblyPath) {
    $assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
    $lines = foreach ($type in $assembly.GetExportedTypes() | Sort-Object FullName) {
        "type $($type.FullName)"
        foreach ($member in $type.GetMembers([Reflection.BindingFlags]'DeclaredOnly,Public,Instance,Static') |
            Where-Object { $_.MemberType -in 'Constructor', 'Method', 'Property', 'Field', 'Event', 'NestedType' } |
            Sort-Object MemberType, Name, ToString) {
            "$($member.MemberType) $($member.ToString())"
        }
    }
    $text = $lines -join "`n"
    $bytes = [Text.Encoding]::UTF8.GetBytes($text)
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    [pscustomobject]@{ Hash = $hash; Surface = $text }
}

$current = @{}
foreach ($assembly in $assemblies) {
    $path = Join-Path $repoRoot $assembly.Path
    if (-not (Test-Path $path)) { throw "Build output is missing: $path" }
    $current[$assembly.Name] = Get-PublicApiHash $path
}

if ($PrintCurrent) {
    $current.GetEnumerator() | Sort-Object Name | ForEach-Object { "{0} {1}" -f $_.Key, $_.Value.Hash }
    exit 0
}

if (-not (Test-Path $baselinePath)) { throw "Public API baseline is missing: $baselinePath" }
$expected = Get-Content -Raw $baselinePath | ConvertFrom-Json -AsHashtable
foreach ($name in $current.Keys) {
    if (-not $expected.ContainsKey($name)) { throw "Public API baseline has no entry for $name." }
    if ($current[$name].Hash -ne $expected[$name]) {
        throw "Public API changed for $name. Intentional changes require an explicit baseline update and compatibility review."
    }
}
Write-Host 'Public API baselines match.'
[AppDomain]::CurrentDomain.remove_AssemblyResolve($assemblyResolver)
