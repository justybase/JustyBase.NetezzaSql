# JustyBase Netezza SQL

Open-source .NET libraries for working with SQL without requiring a live database connection.

The solution ships six NuGet libraries plus a standalone LSP executable:

| Project | Purpose |
| --- | --- |
| `JustyBase.NetezzaSqlParser` | Lexer, recursive-descent parser, AST, formatter, linter, completion, and editor-authoring services. Primary target is Netezza SQL / NZPLSQL; Oracle, Db2, MSSQL, MySQL 8 and PostgreSQL dialects are also supported. |
| `JustyBase.NetezzaDdl` | Netezza DDL text builders, identifier/literal helpers, import/maintenance SQL, and external-table option mapping. |
| `JustyBase.NetezzaCatalogSql` | Reusable SQL statements for reading Netezza catalog metadata. |
| `JustyBase.Netezza` | UI-agnostic metadata models, schema adapter for the parser, and DDL input mapping. |
| `JustyBase.Core` | Shared host-agnostic app core: risk analysis, scripting dialect, execution contracts, schema cache/catalog ports. |
| `JustyBase.ImportExport` | Shared Netezza import engines and tabular export writers used by Avalonia and Legacy hosts. |
| `JustyBase.NetezzaSqlLsp` | NativeAOT Language Server Protocol executable built on the parser package (not published to NuGet). |

## Status

This project is in active development and currently targets `net10.0`. Netezza SQL and NZPLSQL remain the primary grammar and tooling focus. Oracle, Db2, MSSQL, MySQL 8 and PostgreSQL dialects share the same lexer/parser/formatter/lint/completion/hover surfaces via `SqlDialect` (see [docs/node-parity.md](docs/node-parity.md)). The libraries are not a database driver and do not open connections or execute SQL by themselves. Shared app-core packages (`JustyBase.Core`, `JustyBase.ImportExport`) hold host-agnostic risk/import/export/scripting surfaces; see [docs/shared-core-status.md](docs/shared-core-status.md) for production vs scaffold.

The public API and supported grammar may evolve before the first stable `1.0.0` release. Prefer tagged GitHub releases when consuming packages.

## Parse and format SQL

```csharp
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

const string sql = "SELECT account_id, amount FROM sales WHERE amount > 0";

var tokens = NzLexer.Tokenize(sql).ToArray();
var parser = new NzSqlParser(tokens);
var statement = parser.Parse();

if (statement is not null && parser.Errors.Count == 0)
{
    string formattedSql = NzSqlFormatter.Format(statement);
    Console.WriteLine(formattedSql);
}
else
{
    foreach (var error in parser.Errors)
        Console.Error.WriteLine($"{error.Code}: {error.Message}");
}
```

The resulting AST is made of immutable record types in `JustyBase.NetezzaSqlParser.Ast` and can be traversed with `NzSqlVisitor` or inspected directly.

## Netezza authoring and DDL

`NetezzaSqlCatalog` provides the shared function, signature, data-type, and Netezza keyword catalog used by completion, hover, and signature help:

```csharp
using JustyBase.NetezzaSqlParser.Authoring;

if (NetezzaSqlCatalog.TryGetFunction("HASH", out var hash))
    Console.WriteLine(hash.Signatures[0].Label);
```

`JustyBase.NetezzaDdl` also supports one deployment script for catalog-derived objects:

```csharp
using JustyBase.NetezzaDdl;
using JustyBase.NetezzaDdl.Models;

var sql = new NetezzaBatchDdlBuilder().Build(new NetezzaBatchDdlInput(
    Tables: tables,
    Views: views,
    Procedures: procedures));
```

The batch builder preserves object order, reuses the single-object builders, and reports objects skipped because required metadata is missing. Catalog query helpers for schemas, object types, storage statistics, and descriptions are available from `NetezzaCatalogSql`.

## Lint SQL

```csharp
using JustyBase.NetezzaSqlParser.Linter;

using var validator = new SqlValidator();
var result = validator.Validate("SELECT * FROM sales");

foreach (var issue in result.Issues)
    Console.WriteLine($"{issue.StartLine}:{issue.StartColumn} {issue.RuleId}: {issue.Message}");
```

Pass an implementation of `ISchemaProvider` to `SqlValidator` when semantic analysis needs database metadata.

## Build and test from source

Requires the .NET 10 SDK.

```powershell
dotnet restore .\JustyBase.NetezzaSql.sln
dotnet build .\JustyBase.NetezzaSql.sln -c Release
dotnet test .\JustyBase.NetezzaSql.sln -c Release
pwsh .\eng\Verify-Local.ps1
```

Before pushing to `master`, prefer `Verify-Local.ps1` (build, tests, coverage gates, whitespace) — see [docs/local-ci.md](docs/local-ci.md).

The test suite covers parser and linter conformance, malformed SQL, runtime behavior, DDL helpers, and regression cases.

Optional database-backed smoke tests:

- Netezza: [docs/live-tests.md](docs/live-tests.md) and `pwsh .\eng\Run-LiveImportProof.ps1`
- Oracle / Db2 / MSSQL / MySQL / PostgreSQL parser proof (local only) — see [docs/local-ci.md](docs/local-ci.md)

## Create NuGet packages

Pack all six libraries under the same `PackageVersion` (default from `Directory.Build.props`):

```powershell
dotnet pack .\JustyBase.NetezzaSql.sln -c Release -o .\artifacts
```

Or pack individually:

```powershell
dotnet pack .\JustyBase.NetezzaSqlParser.csproj -c Release
dotnet pack .\JustyBase.NetezzaDdl\JustyBase.NetezzaDdl.csproj -c Release
dotnet pack .\JustyBase.NetezzaCatalogSql\JustyBase.NetezzaCatalogSql.csproj -c Release
dotnet pack .\JustyBase.Netezza\JustyBase.Netezza.csproj -c Release
dotnet pack .\JustyBase.Core\JustyBase.Core.csproj -c Release
dotnet pack .\JustyBase.ImportExport\JustyBase.ImportExport.csproj -c Release
```

Each package includes README and XML documentation. On push/PR, CI builds, tests, packs, and uploads all packages as one artifact. Publishing a GitHub Release (tag like `v0.3.0`) runs the same workflow’s **publish** job and pushes `.nupkg` / `.snupkg` to NuGet.org via OIDC. See [docs/release.md](docs/release.md).

## Runnable examples

The repository includes a small console application demonstrating parsing, formatting, schema-aware linting, completion, DDL generation, and catalog SQL:

```powershell
dotnet run --project .\samples\JustyBase.NetezzaSql.Sample\JustyBase.NetezzaSql.Sample.csproj
```

The sample uses in-memory metadata and does not connect to a Netezza database.

## Compatibility and limitations

- Netezza-specific SQL and NZPLSQL syntax is the primary compatibility target; Oracle, Db2, MSSQL, MySQL 8 and PostgreSQL dialects are supported on the shared authoring stack. PostgreSQL accepts strict `schema.table` names, JSON operators, arrays, `LATERAL`, `ON CONFLICT` and `RETURNING`, while rejecting Netezza storage clauses.
- Parser support is intentionally broader than the formatter's canonical output for some command-tail statements.
- Catalog SQL is generated as text; callers remain responsible for connection management, permissions, and execution.
- The libraries do not validate that generated SQL is accepted by a particular Netezza appliance version.
- Treat database, schema, object, and search values passed to catalog SQL helpers as untrusted input and use the documented escaping/validation contract.

See [docs/compatibility.md](docs/compatibility.md) for the supported surface and [CONTRIBUTING.md](CONTRIBUTING.md) for development guidance.

The Node.js-to-C# behavioral boundary (including Oracle/Db2/MSSQL/MySQL/PostgreSQL dialect mapping) is maintained in [docs/node-parity.md](docs/node-parity.md).

## License

Licensed under the [Apache License 2.0](LICENSE).

Netezza and IBM are trademarks of their respective owners. This project is not affiliated with or endorsed by IBM.
