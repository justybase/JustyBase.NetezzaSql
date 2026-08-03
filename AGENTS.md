# Repository Guidelines

## Project Structure & Module Organization

This repository contains .NET 10 libraries for Netezza SQL tooling and shared JustyBase application services. `JustyBase.NetezzaSqlParser.csproj` is the main parser package; its code is grouped by concern in `Lexer/`, `Parser/`, `Ast/`, `Visitor/`, `Formatter/`, `Linter/`, `Completion/`, `Authoring/`, and `Caching/`.

Supporting libraries are `JustyBase.NetezzaDdl/` (DDL generation), `JustyBase.NetezzaCatalogSql/` (catalog SQL helpers), `JustyBase.Netezza/` (integration layer), `JustyBase.Core/` (shared contracts and services), and `JustyBase.ImportExport/` (tabular import/export). The NativeAOT language server is in `JustyBase.NetezzaSqlLsp/`. Tests are under `tests/`; live-driver proof projects are intentionally isolated from the normal test run.

## Build, Test, and Development Commands

Run commands from the repository root:

```powershell
dotnet build .\JustyBase.NetezzaSqlParser.csproj
dotnet build .\JustyBase.NetezzaDdl\JustyBase.NetezzaDdl.csproj
dotnet test
pwsh .\eng\Verify-Local.ps1
```

Build individual projects while developing. `dotnet test` discovers standard test projects. `Verify-Local.ps1` runs the local CI checks, including builds, tests, coverage gates, and `git diff --check`; run it before pushing to `master`.

## Coding Style & Naming Conventions

Follow the established C# style: four-space indentation, nullable reference types, and implicit usings. Use PascalCase for public types, members, and filenames; use camelCase for parameters and locals. Keep responsibilities separated between lexer, parser, AST, and visitor code. Put partial classes together and name feature files after their owner, for example `Parser/NzSqlParser.Expression.cs` or `Visitor/NzSqlVisitor.Select.cs`.

## Testing Guidelines

Add focused unit or conformance tests in `tests/JustyBase.NetezzaSql.Tests` and integration-layer tests in `tests/JustyBase.Netezza.Tests`. Name tests by behavior, such as `ParseSelect_WithWhereClause_ReturnsFilterNode`. Cover valid input, malformed input, and boundary cases. Do not add `bin/` or `obj/` output to source control.

## Commit & Pull Request Guidelines

Use short, imperative commit subjects, such as `Add external table option mapping`. Keep commits narrowly scoped. Pull requests should describe the behavior change, identify affected projects, link related issues, and report test results. Include before/after SQL when parser, formatter, completion, or DDL output changes.
