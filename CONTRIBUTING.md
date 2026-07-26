# Contributing

## Development setup

Install the .NET 10 SDK, clone the repository, and run:

```powershell
dotnet restore .\JustyBase.NetezzaSql.sln
dotnet test .\JustyBase.NetezzaSql.sln
```

The project does not require a live Netezza database for its unit and conformance tests.

## Before you push

From the repository root, run:

```powershell
pwsh .\eng\Verify-Local.ps1
```

That matches the main **CI** gates (build, test, per-library coverage, whitespace). Use `-FullCi` before creating a release tag; see [docs/local-ci.md](docs/local-ci.md).

## Pull requests

- Keep changes focused and explain the behavior change.
- Add or update tests for parser, linter, formatter, DDL, catalog SQL, or public API changes.
- Update README or compatibility documentation when user-visible behavior changes.
- Run `pwsh .\eng\Verify-Local.ps1` before pushing to `master` (or at minimum `dotnet build -c Release`, `dotnet test -c Release`, and `pwsh .\eng\Test-Coverage.ps1`).
- Run `git diff --check` before opening a pull request.
- Do not commit credentials, connection strings, database exports, IDE state, or generated build output.

Use short imperative commit subjects, for example `Add external table option mapping`.
