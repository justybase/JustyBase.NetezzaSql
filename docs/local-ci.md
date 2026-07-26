# Local validation (before push)

Run the same checks that most often fail on GitHub **CI** before you push to `master`:

```powershell
pwsh .\eng\Verify-Local.ps1
```

This script runs, in order:

1. `dotnet restore` and `dotnet build` (Release)
2. `dotnet test` on the full solution (Release)
3. `pwsh .\eng\Test-Coverage.ps1` — per-library line/branch gates (parser, DDL, catalog, LSP, **Core**, **ImportExport**, Netezza integration)
4. `git diff --check` — trailing whitespace and conflict markers

## Full CI parity (before a release tag)

Add `-FullCi` to also run vulnerable-package scan, `dotnet pack` for all six libraries, and `eng\Test-PackageConsumer.ps1`:

```powershell
pwsh .\eng\Verify-Local.ps1 -FullCi
```

See [release.md](release.md) for the complete manual release checklist.

## Optional Git hook

To run `Verify-Local.ps1` automatically before every push:

```powershell
git config core.hooksPath eng/githooks
```

Copy or rename [eng/githooks/pre-push.sample](../eng/githooks/pre-push.sample) to `pre-push` if your Git client requires an extensionless hook file.

## Common failures

| Symptom | What to do |
|--------|------------|
| `importexport coverage is below its release threshold` | Add or extend tests in `tests/JustyBase.NetezzaSql.Tests` (e.g. `ImportExportCoreTests`, `ImportExportExtendedTests`). |
| `git diff --check` | Remove trailing spaces; fix conflict markers. |
| Tests pass locally but CI fails on Ubuntu | Prefer unit tests over OS-specific assumptions; pipe tests use `NamedPipeClientStream` and run on Linux CI. |

Workflow status: [GitHub Actions](https://github.com/justybase/JustyBase.NetezzaSql/actions).
