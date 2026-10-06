[CmdletBinding()]
param(
    [string]$Filter = '*',
    [switch]$Short
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tests/JustyBase.NetezzaSql.Benchmarks/JustyBase.NetezzaSql.Benchmarks.csproj'

$arguments = @('run', '--project', $project, '-c', 'Release', '--', '--filter', $Filter)
if ($Short) {
    $arguments += @('--job', 'short')
}

Write-Host "==> parser benchmarks (filter: $Filter$(if ($Short) { ', short job' }))" -ForegroundColor Cyan
dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Benchmark run failed.' }
