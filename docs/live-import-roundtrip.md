# Live import round-trip proof (type inference)

This suite proves the shared host import path against a **real Netezza** database:

1. Parse CSV (`FastCsvImportEngine.ReadAsync`)
2. Infer column types (`DatabaseTypeChooser.Infer`)
3. `CREATE TABLE … DISTRIBUTE ON RANDOM`
4. Named-pipe `INSERT … FROM EXTERNAL` with `REMOTESOURCE 'dotnet'`
5. `SELECT` ordered rows and compare locally to expected values

## Requirements

- Local only: set `NZ_DEV_HOST`, `NZ_DEV_DATABASE`, `NZ_DEV_USER`, `NZ_DEV_PASSWORD` (optional `NZ_DEV_PORT`).
- **Do not** run this on GitHub Actions (secrets are local-only).
- Pipe topology: soft-skips on named-pipe / relative-path errors unless `NZ_REQUIRE_PIPE=1`.

## Run

```powershell
pwsh .\eng\Run-LiveImportProof.ps1
# Strict pipe (fail if topology rejects \\.\pipe\...):
pwsh .\eng\Run-LiveImportProof.ps1 -RequirePipe
```

Or:

```powershell
dotnet test .\tests\JustyBase.NetezzaSql.IntegrationTests\ --filter "Category=Live&FullyQualifiedName~RoundTrip"
```

This is **not** part of `eng\Verify-Local.ps1` (offline CI parity). Use Verify-Local before every push; use this script when validating import/inference against Netezza.

## Case matrix

| Name | What it proves |
|------|----------------|
| `simple_types` | INTEGER / BOOLEAN / NUMERIC / DATETIME / VARCHAR infer + round-trip |
| `nullable_empty` | Empty fields → null; nullable VARCHAR |
| `escaping` | Tab, newline, backslash in text after pipe escape |
| `hard_quoted_csv` | Quoted commas, `""`, multiline CSV fields |
| `adversarial` | SQL-like / HTML / Unicode payloads stored as data only |
| `mixed_falls_back_varchar` | Mixed int+text column → VARCHAR (not INTEGER) |

## Adding a case

1. Add a `LiveImportCase` factory in `NetezzaLiveImportRoundTripTests`.
2. Include it in `Cases()`.
3. Set `ExpectedInferredTypes` when the case exists to prove inference.
4. Keep `ExpectedRows` ordered by the first column (runner uses `ORDER BY` that column).

## Soft-skip vs fail

| Situation | Result |
|-----------|--------|
| Missing `NZ_DEV_*` | Soft-skip (test returns); `Run-LiveImportProof.ps1` exits 2 if vars missing |
| Pipe topology error, `NZ_REQUIRE_PIPE` unset | Soft-skip with console message |
| Pipe topology error, `NZ_REQUIRE_PIPE=1` | Fail |
| Data / type mismatch after successful insert | Fail |

## Live DDL note

Production `DatabaseTypeChooser` emits `VARCHAR(...)`. The round-trip runner maps `VARCHAR` → `NVARCHAR` for CREATE/EXTERNAL only so Unicode payloads round-trip safely without changing the Infer API. BOOLEAN imports use `BoolStyle = TRUE_FALSE`.

Pipe lines are encoded with `NetezzaPipeImportExecutor.Sanitize` (escape + real tab/newline bytes), matching the typed-pipe SoT — not `DelimitedRowEncoder`'s `\\n` text form.
