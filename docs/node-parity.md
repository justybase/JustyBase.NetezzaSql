# Node.js parity contract

`JustyBaseLite-netezzaTMP_` is the behavioral reference for features shared
with this repository. The C# implementation deliberately has no dependency on
VS Code, a connection manager, or query execution.

| Capability | Reference area | C# contract | Release status |
| --- | --- | --- | --- |
| Lexing, parsing and diagnostics | `src/dialects/netezza/sql`, `src/sqlParser` | `NzLexer`, `NzSqlParser`, `SqlValidator` | supported |
| Formatting and SQL authoring | `src/sqlParser`, editor providers | formatter, hover, signature, symbols, rename, semantic tokens | supported |
| Completion | `src/server/completionEngine.ts` | `NzCompletionEngine` | supported |
| Metadata-backed validation | `metadataCacheAdapter.ts` | `ISchemaProvider` and `InMemorySchemaProvider` | supported, host-neutral |
| DDL and catalog SQL | Netezza command/provider code | DDL and catalog packages | supported |
| Oracle dialect lexer | `src/dialects/oracle/sql/lexer.ts` | `OracleLexer` (12 Oracle tokens registered before shared chain) | supported |
| Oracle dialect parser | `src/dialects/oracle/sql/parser.ts` | `OracleSqlParser` partials (`Select`/`Dml`/`Ddl`), anonymous blocks, program units, PAR001 rejections | supported |
| Oracle quality rules | `extensions/oracle/src/sql/qualityRules.ts` | `OracleLintRules` (ORA001-ORA004) via `QualityRuleRegistry.AddRules` | supported |
| Oracle SQL authoring | `extensions/oracle/src/sql/authoring.ts` | `OracleSqlCatalog` through `ISqlAuthoringCatalog` (completion, hover, signature help) | supported |
| Oracle formatter output | — (no TS formatter override) | token-range reconstruction for Oracle statements in `NzSqlFormatter` | supported |
| Db2 dialect lexer | `src/dialects/db2/sql/lexer.ts` | `Db2Lexer` (Db2* multi-word tokens before shared chain) | supported |
| Db2 dialect parser | `src/dialects/db2/sql/parser.ts` | `Db2SqlParser` partials; DGTT/ALIAS/NICKNAME/PROCEDURE; FINAL TABLE; PAR001 rejections | supported |
| Db2 quality rules | `extensions/db2/src/sql/qualityRules.ts` | `Db2LintRules` (DB2001–DB2008) via dialect-only `QualityRuleRegistry` | supported |
| Db2 SQL authoring | `extensions/db2/src/sql/authoring.ts` | `Db2SqlCatalog` through `ISqlAuthoringCatalog` | supported |
| MSSQL dialect lexer | `src/dialects/mssql/sql/lexer.ts` | `MssqlLexer` (Mssql* tokens and bracketed identifiers registered before shared chain) | supported |
| MSSQL dialect parser | `src/dialects/mssql/sql/parser.ts` | `MssqlSqlParser` partials; TOP / OUTPUT / CROSS\|OUTER APPLY / bracketed identifiers / @variables / GO batches; PAR001 rejections | supported |
| MSSQL quality rules | `extensions/mssql/src/sql/qualityRules.ts` | `MssqlLintRules` (MSS001–MSS008) via dialect-only `QualityRuleRegistry` | supported |
| MSSQL SQL authoring | `extensions/mssql/src/sql/authoring.ts` | `MssqlSqlCatalog` through `ISqlAuthoringCatalog` | supported |
| MySQL 8 dialect lexer | `src/dialects/mysql/sql/lexer.ts` | `MySqlLexer` with backtick identifiers and `#` comments | supported |
| MySQL 8 dialect parser | `src/dialects/mysql/sql/parser.ts` | `MySqlSqlParser`; two-part names, MySQL LIMIT forms, INSERT IGNORE, ON DUPLICATE KEY UPDATE and MySQL 8 DDL | supported |
| MySQL 8 SQL authoring | `extensions/mysql/src/sql/authoring.ts` | `MySqlSqlCatalog` through `ISqlAuthoringCatalog` | supported |
| MySQL quality rules | `extensions/mysql/src/sql/qualityRules.ts` | empty dialect-only rule registry | supported |
| PostgreSQL dialect lexer | `src/dialects/postgresql/sql/lexer.ts` | `PostgreSqlLexer` (JSON operators, LATERAL, RETURNING, conflict/array tokens and unsupported Netezza token) | supported |
| PostgreSQL dialect parser | `src/dialects/postgresql/sql/parser.ts` | `PostgreSqlSqlParser`; strict schema.table names, DISTINCT ON, LATERAL, arrays, JSON operators, casts, ON CONFLICT and RETURNING | supported |
| PostgreSQL SQL authoring and quality rules | `extensions/postgresql/src/postgresqlSqlAuthoring.ts` | `PostgreSqlSqlCatalog` and empty `PostgreSqlLintRules` | supported |
| ANSI authoring base and dialect overlays | `src/sql/authoring/baseProfiles.ts` plus dialect authoring profiles | `AnsiSqlCatalog` composed with Netezza, Oracle, Db2, MSSQL, MySQL and PostgreSQL overlays; signatures are merged case-insensitively | supported |
| Common MERGE grammar | shared SQL parser and dialect parser entry points | `MergeStatement` with matched update/delete and not-matched insert clauses in all supported dialects | supported |
| ANSI OFFSET/FETCH | Oracle and Db2 select parsers; Netezza probe/fixtures | `OffsetFetchClause` preserves OFFSET-only, FIRST/NEXT, PERCENT, ONLY and WITH TIES; legacy `LimitClause` remains compatible | supported |
| Dialect dispatch | — | `DialectRuntime` (`Tokenize`/`CreateParser`/`QualityRules`/`AuthoringCatalog`) | supported |
| Query flow and CTE refactoring | `queryStructureAnalyzer.ts`, `flowAnalyzer.ts` | no public C# API | intentionally deferred |
| Connections, execution and VS Code UI | extension host | no public C# API | out of scope |

The following reference-host behaviors are deliberately not represented as
skipped xUnit tests: Chevrotain parser performance/runtime internals,
connection-manager metadata-cache behavior, and query-flow/structure analysis.
They require Node.js or VS Code host services and have no corresponding public
C# contract. Their scope is documented here rather than hidden behind skipped
tests; a future C# API must add observable contract tests before it is marked
supported.

Every supported row needs a focused C# test plus a conformance fixture when a
Node test defines externally observable behavior. Tests must assert observable
results (AST shape, diagnostics, edits, or completion items), not implementation
details of Chevrotain or the VS Code host.

Quoted metadata names are normalized at the schema-provider boundary; callers
may use either catalog names or SQL-quoted names. Source text and diagnostic
offsets are preserved by the lexer and parser.

## Oracle dialect mapping notes

- The TS `src/dialects/oracle` project contains only the lexer, parser and a
  stub dialect (`index.ts`); the visitor has no Oracle counterpart, so the C#
  `NzSqlVisitor` dispatches Oracle statements (anonymous blocks, program units)
  as opaque token ranges.
- Oracle quality rules and authoring live in `extensions/oracle/src/sql/`
  (not in `src/dialects/oracle`); they are composed per document in the C# LSP
  through `SqlDialect` (`Dialects/SqlDialect.cs`) with `--dialect` startup
  argument or the `justy/setDialect` request (`netezza` | `oracle` | `db2` |
  `mssql` | `mysql` | `postgresql`).
- q-quoted strings (`q'[...]'`) tokenize as `q` identifier + string literal in
  both the TS and C# lexers; embedded quote handling is preserved only in the
  linter's statement scanner (`OracleLintHelpers.StatementEnd`).
- Netezza-only constructs (LIMIT, `DB..TABLE`, DISTRIBUTE/ORGANIZE, EXTERNAL
  TABLE, GROOM, GENERATE STATISTICS) are rejected in Oracle mode with PAR001.
- The capability matrix is explicit in `SqlDialectCapabilitiesCatalog`: LIMIT
  is enabled only for Netezza, while MERGE and ANSI OFFSET/FETCH are enabled
  for Netezza, Oracle and Db2. The Netezza parser keeps both LIMIT and the
  ANSI form available so a live appliance probe can refine the release
  contract without changing the AST shape.

## Shared ANSI grammar and authoring notes

- `AnsiSqlCatalog` is the base profile. Dialect catalogs add functions, types,
  completion phrases and formatter phrases through
  `SqlAuthoringCatalogComposer`; multi-word phrases such as `GROUP BY`,
  `ORDER BY` and `PARTITION BY` are atomic values.
- `MergeStatement` is visited by the existing semantic visitor. MERGE source
  and target aliases therefore participate in the same scope rules as before,
  while `OffsetFetchClause` is a scalar-tail no-op and does not change CTE,
  temporary-table or subquery scope traversal.
- `LimitClause` is still populated for compatibility. New consumers should
  inspect `SelectStatement.OffsetFetch` when the original OFFSET/FETCH syntax
  (direction, percentage or ties behavior) matters.

## Db2 dialect mapping notes

- The TS `src/dialects/db2` project contains the lexer and parser; quality rules
  and authoring live in `extensions/db2/src/sql/`. C# mirrors that split via
  `Db2Lexer` / `Db2SqlParser` / `Db2LintRules` / `Db2SqlCatalog`, composed through
  `DialectRuntime` and `SqlDialect.Db2`.
- Isolation phrases (`WITH UR|CS|RS|RR`) and `FOR READ ONLY` are registered
  before shared `WITH` / `FOR` so they win in the lexer (same order as TS).
- SQL PL procedure bodies are opaque token ranges (`Db2ProcedureUnitStatement`);
  deep SQL PL visitor coverage is intentionally deferred.
- Live proof: `eng/Run-Db2LiveProof.ps1` against `DB2_LIVE_TEST_*` (soft-skip
  without env/driver; fail-fast when `DB2_LIVE_TEST_REQUIRED=true`). Hosted in
  `tests/JustyBase.NetezzaSql.Db2LiveTests` (not part of solution `dotnet test`)
  so missing `db2app64`/clidriver cannot crash the shared IntegrationTests host.

## MSSQL dialect mapping notes

- The TS `src/dialects/mssql` project contains the lexer and parser; quality
  rules and authoring live in `extensions/mssql/src/sql/`. C# mirrors that split
  via `MssqlLexer` / `MssqlSqlParser` / `MssqlLintRules` / `MssqlSqlCatalog`,
  composed through `DialectRuntime` and `SqlDialect.Mssql`.
- T-SQL-only lexical forms (`TOP`, `OUTPUT`, `CROSS|OUTER APPLY`, `GO`,
  `TRY`/`CATCH`, `PROC`, bracketed identifiers, `@`/`@@` variables) are
  registered before the shared Netezza chain so they win over `Identifier` /
  `@SET` / `LBracket-RBracket`. `N'...'` lexes as `Identifier` + `StringLiteral`,
  matching the reference; `#temp`/`##temp` tables are intentionally not lexed.
- `TOP` / `OUTPUT` / procedure bodies are opaque token ranges
  (`TopTokens`, `OutputTokens`, `MssqlProcedureUnitStatement`), so deep T-SQL
  (TRY-CATCH nesting, CLR, Service Broker) stays out of scope. Procedure bodies
  keep `BEGIN TRY`/`END TRY`/`BEGIN CATCH`/`END CATCH` balanced so a unit is not
  truncated at the first `END TRY`.
- UPDATE/DELETE `OUTPUT ... FROM ... WHERE` joins are parsed structurally: the
  `OUTPUT` range stops at `FROM`, and the join source is captured in
  `UpdateStatement.From` / `DeleteStatement.From` (matching the reference).
- T-SQL table hints (`WITH (NOLOCK)`, `WITH (INDEX (...))`) after a table source
  are consumed as an opaque parenthesis range so the leftover `WITH` cannot
  cascade into CTE parsing errors.
- Netezza-only surfaces (LIMIT, `DB..TABLE`, GROOM, GENERATE, DISTRIBUTE ON)
  are rejected with PAR001 and flagged by MSS004/MSS005/MSS007/MSS008.
- Live proof: `eng/Run-MssqlLiveProof.ps1` against `MSSQL_LIVE_TEST_*`
  (soft-skip without env; fail-fast when `MSSQL_LIVE_TEST_REQUIRED=true`).
  Hosted in `tests/JustyBase.NetezzaSql.MssqlLiveTests` (not part of solution
  `dotnet test`) using `Microsoft.Data.SqlClient`.
