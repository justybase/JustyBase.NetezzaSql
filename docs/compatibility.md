# Compatibility

## Target framework

The first public release targets `.NET 10` (`net10.0`). The repository is tested with the .NET 10 SDK.

## SQL surface

The parser includes Netezza-oriented support for:

- SELECT, INSERT, UPDATE, DELETE, MERGE, and common set operations;
- CREATE, ALTER, DROP, TRUNCATE, EXTERNAL TABLE, VIEW, SEQUENCE, and PROCEDURE statements;
- expressions, joins, CTEs, window clauses, casts, parameters, and function calls;
- NZPLSQL procedure blocks, variables, control flow, exceptions, and transaction statements;
- Netezza-specific commands such as GROOM, GENERATE STATISTICS, DISTRIBUTE, and ORGANIZE.

The exact accepted grammar is defined by the parser and regression corpus. Unsupported or partially supported syntax produces parser diagnostics rather than being silently treated as valid SQL.

## Formatter and authoring services

The formatter emits a stable canonical representation for supported AST statements. Completion, hover, rename, semantic tokens, and linting are intended for editor integrations and may require an `ISchemaProvider` for metadata-aware behavior.

## Catalog SQL

`JustyBase.NetezzaCatalogSql` contains query templates for Netezza catalog views. It does not execute SQL or manage connections. Callers must apply the public input contract and use an appropriate database driver.

`JustyBase.NetezzaDdl.NetezzaBatchDdlBuilder` combines catalog-derived table, external-table, view, procedure, and synonym models into a deterministic script. It skips incomplete objects and returns their qualified names in `NetezzaBatchDdlResult.SkippedObjects`.

Database-backed validation is optional; see [live-tests.md](live-tests.md).
