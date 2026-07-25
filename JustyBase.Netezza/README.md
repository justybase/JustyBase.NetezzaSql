# JustyBase.Netezza

[![NuGet](https://img.shields.io/nuget/v/JustyBase.Netezza)](https://www.nuget.org/packages/JustyBase.Netezza/)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](../LICENSE)

Integration layer between Netezza metadata, `JustyBase.NetezzaSqlParser` (SQL parsing, completion, linting), and `JustyBase.NetezzaDdl` (DDL generation).

This package lives in the [JustyBase.NetezzaSql](https://github.com/justybase/JustyBase.NetezzaSql) repository alongside the parser, DDL, and catalog libraries.

Use this package when you need to:

- Represent a Netezza database schema (tables, views, columns, procedures) as immutable C# models
- Feed that schema into the parser's completion engine via `InMemorySchemaProvider`
- Convert schema metadata into DDL input models for `CREATE TABLE / VIEW / PROCEDURE` generation
- Apply an engine-first fallback strategy for SQL completion

The package has no I/O, no UI dependency, and no database driver reference — it is pure data transformation.

## Dependencies

| Package | Purpose |
|---------|---------|
| `JustyBase.NetezzaSqlParser` | SQL lexer, parser, AST, completion engine, linter |
| `JustyBase.NetezzaDdl` | DDL text builder (`CREATE TABLE`, `CREATE VIEW`, `CREATE PROCEDURE`, etc.) |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | DI abstractions for service registration |

## Target framework

- .NET 10
- Native AOT compatible (`IsAotCompatible = true`)

## Public contract

| Namespace | Types | Purpose |
|-----------|-------|---------|
| `JustyBase.Netezza.Models` | `NetezzaSchemaSnapshot`, `NetezzaSchemaTable`, `NetezzaSchemaColumn`, `NetezzaProcedureDefinition` | Immutable, UI-free metadata DTOs |
| `JustyBase.Netezza.Schema` | `NetezzaSchemaProviderAdapter` | Loads a snapshot into the parser's `InMemorySchemaProvider` |
| `JustyBase.Netezza.Ddl` | `NetezzaDdlInputFactory` | Converts neutral models to DDL package input types |
| `JustyBase.Netezza.Completion` | `SqlCompletionMergePolicy` | Engine-first vs. legacy fallback decision |
| `JustyBase.Netezza.Abstractions` | `ISqlCompletionMergePolicy`, `INetezzaSchemaProviderAdapter`, `INetezzaDdlInputFactory` | DI-friendly interfaces for all services |
| `JustyBase.Netezza.DependencyInjection` | `ServiceCollectionExtensions` | `AddJustyBaseNetezza()` extension for `IServiceCollection` |
| `JustyBase.Netezza` | `NetezzaResult<T>` | Result object pattern for expected failure modes |

## End-to-end example

```csharp
using JustyBase.Netezza.DependencyInjection;
using JustyBase.Netezza.Ddl;
using JustyBase.Netezza.Models;
using JustyBase.Netezza.Schema;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.NetezzaDdl;

// 1. Build a schema snapshot (typically from a database query)
var snapshot = new NetezzaSchemaSnapshot([
    new NetezzaSchemaTable(
        "EMPLOYEES", Schema: "PUBLIC", Database: "HR",
        Columns: [
            new NetezzaSchemaColumn("ID", "INTEGER", Nullable: false),
            new NetezzaSchemaColumn("NAME", "VARCHAR(100)"),
            new NetezzaSchemaColumn("SALARY", "DECIMAL(10,2)")
        ],
        Description: "Employee master table")
]);

// 2. Load into parser schema provider (enables semantic completion/linting)
var provider = new InMemorySchemaProvider();
NetezzaSchemaProviderAdapter.Apply(provider, snapshot);

// 3. Build DDL input and generate CREATE TABLE script
var ddlInput = NetezzaDdlInputFactory.BuildTable(
    snapshot.Tables[0],
    distributeColumns: ["ID"]);
var ddlBuilder = new NetezzaDdlTextBuilder();
var sql = new StringBuilder();
ddlBuilder.AppendCreateTable(sql, ddlInput);
Console.WriteLine(sql.ToString());
// Output: CREATE TABLE "HR"."PUBLIC"."EMPLOYEES" ( ... ) DISTRIBUTE ON ("ID");
```

## DI registration

```csharp
services.AddJustyBaseNetezza();
// Registers: ISqlCompletionMergePolicy, INetezzaSchemaProviderAdapter, INetezzaDdlInputFactory
```

## Build and test

```powershell
dotnet restore .\JustyBase.NetezzaSql.sln
dotnet build .\JustyBase.NetezzaSql.sln -c Release
dotnet test .\tests\JustyBase.Netezza.Tests\JustyBase.Netezza.Tests.csproj -c Release
```

See the repository [release guide](../docs/release.md) for publishing.
