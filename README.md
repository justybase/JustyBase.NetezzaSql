# JustyBase Netezza SQL

Open-source .NET libraries for working with Netezza SQL without requiring a live database connection.

The solution contains three libraries:

| Library | Purpose |
| --- | --- |
| `JustyBase.NetezzaSqlParser` | Lexer, recursive-descent parser, AST, formatter, linter, completion, and editor-authoring services. |
| `JustyBase.NetezzaDdl` | Netezza DDL text builders, identifier/literal helpers, and external-table option mapping. |
| `JustyBase.NetezzaCatalogSql` | Reusable SQL statements for reading Netezza catalog metadata. |

## Status

This project is in active development and currently targets `net10.0`. The parser is designed for Netezza SQL and NZPLSQL grammar used by JustyBase tooling. It is not a database driver and does not open connections or execute SQL.

The public API and supported grammar may evolve before the first stable `1.0.0` release. Use tagged GitHub releases when consuming the source.

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
```

The test suite covers parser and linter conformance, malformed SQL, runtime behavior, DDL helpers, and regression cases.

Optional database-backed smoke tests are documented in [docs/live-tests.md](docs/live-tests.md).

## Create NuGet packages

The three libraries can be packed independently:

```powershell
dotnet pack .\JustyBase.NetezzaSqlParser.csproj -c Release
dotnet pack .\JustyBase.NetezzaDdl\JustyBase.NetezzaDdl.csproj -c Release
dotnet pack .\JustyBase.NetezzaCatalogSql\JustyBase.NetezzaCatalogSql.csproj -c Release
```

Each package includes this README and XML documentation. The CI workflow builds, tests, packs, and uploads all three packages as one artifact.

## Runnable examples

The repository includes a small console application demonstrating parsing, formatting, schema-aware linting, completion, DDL generation, and catalog SQL:

```powershell
dotnet run --project .\samples\JustyBase.NetezzaSql.Sample\JustyBase.NetezzaSql.Sample.csproj
```

The sample uses in-memory metadata and does not connect to a Netezza database.

## Compatibility and limitations

- Netezza-specific SQL and NZPLSQL syntax is the primary compatibility target.
- Parser support is intentionally broader than the formatter's canonical output for some command-tail statements.
- Catalog SQL is generated as text; callers remain responsible for connection management, permissions, and execution.
- The libraries do not validate that generated SQL is accepted by a particular Netezza appliance version.
- Treat database, schema, object, and search values passed to catalog SQL helpers as untrusted input and use the documented escaping/validation contract.

See [docs/compatibility.md](docs/compatibility.md) for the supported surface and [CONTRIBUTING.md](CONTRIBUTING.md) for development guidance.

The Node.js-to-C# behavioral boundary is maintained in
[docs/node-parity.md](docs/node-parity.md). The project does not publish from
CI; see [docs/release.md](docs/release.md) for the manual GitHub and NuGet
handoff.

## License

Licensed under the [Apache License 2.0](LICENSE).

Netezza and IBM are trademarks of their respective owners. This project is not affiliated with or endorsed by IBM.
