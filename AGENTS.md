# Repository Guidelines

## Project Structure & Module Organization

This repository contains .NET 10 class libraries for Netezza SQL tooling:

- `JustyBase.NetezzaSqlParser.csproj` is the main parser package. Its source is organized by responsibility: `Lexer/`, `Parser/`, `Ast/`, `Visitor/`, `Formatter/`, `Linter/`, `Completion/`, `Authoring/`, and `Caching/`.
- `JustyBase.NetezzaDdl/` contains the DDL-generation library and its `Models/` input types.
- `JustyBase.NetezzaCatalogSql/` provides catalog SQL and procedure-type helpers.
- `JustyBase.NetezzaSqlLsp/` contains the standalone NativeAOT LSP executable built on the parser package.

Keep related partial-class files together (for example, `Parser/NzSqlParser.*.cs`). Do not place build output from `bin/` or `obj/` under source control.

## Build, Test, and Development Commands

Run commands from the repository root:

```powershell
dotnet build .\JustyBase.NetezzaSqlParser.csproj
dotnet build .\JustyBase.NetezzaDdl\JustyBase.NetezzaDdl.csproj
dotnet build .\JustyBase.NetezzaCatalogSql\JustyBase.NetezzaCatalogSql.csproj
dotnet test
```

The first three commands compile each library; `dotnet test` discovers and runs tests when a test project or solution is present. The projects target `net10.0`; use a compatible .NET SDK.

## Coding Style & Naming Conventions

Follow the existing C# style: four-space indentation, file-scoped namespaces where already used, nullable reference types enabled, and implicit usings enabled. Use PascalCase for public types, members, and filenames; use camelCase for parameters and local variables. Name feature-specific files after their owning type, such as `NzSqlVisitor.Select.cs` or `NzSqlParser.Expression.cs`. Keep lexer, parser, AST, and visitor responsibilities separated.

## Testing Guidelines

Unit and conformance tests are in `tests/JustyBase.NetezzaSql.Tests`; driver-backed live checks are isolated in `tests/JustyBase.NetezzaSql.IntegrationTests`. Add focused tests named by behavior, for example `ParseSelect_WithWhereClause_ReturnsFilterNode`. Cover valid SQL, malformed input, and edge cases for parser, formatter, linter, catalog SQL, or DDL changes. Run `dotnet test` before submitting changes.

## Commit & Pull Request Guidelines

Git history is not available in this checkout, so no repository-specific commit convention can be derived. Use short, imperative commit subjects, such as `Add external table option mapping`. Keep commits focused. Pull requests should explain the behavior change, identify affected library projects, link relevant issues, and include test results. Include before/after SQL examples when parser, formatter, completion, or DDL output changes.
