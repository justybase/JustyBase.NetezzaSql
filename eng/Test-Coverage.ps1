[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot 'tests/JustyBase.NetezzaSql.Tests/JustyBase.NetezzaSql.Tests.csproj'
$coverageRoot = Join-Path $repoRoot 'artifacts/coverage'
New-Item -ItemType Directory -Force -Path $coverageRoot | Out-Null

$targets = @(
    @{ Name = 'parser'; Line = 90; Branch = 80 },
    @{ Name = 'ddl'; Line = 90; Branch = 80 },
    @{ Name = 'catalog'; Line = 90; Branch = 80 },
    # The executable entry point is excluded; this gate measures protocol and handlers only.
    @{ Name = 'lsp'; Line = 70; Branch = 60 }
)

foreach ($target in $targets) {
    $resultsDirectory = Join-Path $coverageRoot "$($target.Name)-results"
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $resultsDirectory
    dotnet test $testProject --no-build --configuration $Configuration --settings (Join-Path $PSScriptRoot "coverage/$($target.Name).runsettings") --collect:'XPlat Code Coverage' --results-directory $resultsDirectory
    if ($LASTEXITCODE -ne 0) { throw "Tests failed while collecting $($target.Name) coverage." }

    $report = Get-ChildItem -Path $resultsDirectory -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
    if ($null -eq $report) { throw "Coverage collector did not produce a report for $($target.Name)." }

    [xml]$coverage = Get-Content -Raw $report.FullName
    $line = [decimal]::Parse($coverage.coverage.'line-rate', [Globalization.CultureInfo]::InvariantCulture) * 100
    $branch = [decimal]::Parse($coverage.coverage.'branch-rate', [Globalization.CultureInfo]::InvariantCulture) * 100
    Copy-Item $report.FullName (Join-Path $coverageRoot "$($target.Name).cobertura.xml") -Force
    Write-Host ("{0}: line {1:N2}% (minimum {2}%), branch {3:N2}% (minimum {4}%)" -f $target.Name, $line, $target.Line, $branch, $target.Branch)
    if ($line -lt $target.Line -or $branch -lt $target.Branch) {
        throw "$($target.Name) coverage is below its release threshold."
    }
}
