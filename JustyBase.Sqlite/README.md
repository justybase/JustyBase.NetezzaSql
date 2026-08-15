# JustyBase.Sqlite

UI-agnostic SQLite metadata, parser schema, and DDL integration components for
the JustyBase SQL tooling stack.

## Components

- `Schema/SqliteSchemaLoader` — reads `PRAGMA database_list`, `sqlite_schema`
  and `PRAGMA table_info` over any ADO.NET `DbConnection` (Microsoft.Data.Sqlite,
  System.Data.SQLite, ODBC, test doubles) and produces host-neutral
  `SqliteSchemaSnapshot` values.
- `Schema/SqliteSchemaCache` — per-connection/per-database snapshot cache with
  TTL freshness and a monotonic generation counter.
- `Schema/SqliteSchemaProviderAdapter` — loads snapshots into the parser's
  `InMemorySchemaProvider` so completion and semantic validation can resolve
  `main.table`-style names.
- `Ddl/SqliteDdlGenerator` — canonical `CREATE TABLE` generation from a snapshot
  (inline single-column `PRIMARY KEY`, table-level composite keys, `NOT NULL`
  and `DEFAULT` clauses).
- `Diagnostics/SqliteErrorLocator` — maps SQLite error messages
  (`near "X": syntax error`, `no such table: t`, ...) to the token to highlight.
- `DependencyInjection/ServiceCollectionExtensions` — `AddJustyBaseSqlite()`.

## Usage

```csharp
using JustyBase.Sqlite.Schema;
using Microsoft.Data.Sqlite;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var snapshot = await SqliteSchemaLoader.LoadCatalogAsync(connection, "main");
var ddl = SqliteDdlGenerator.CreateTable(snapshot.Tables[0]);
```
