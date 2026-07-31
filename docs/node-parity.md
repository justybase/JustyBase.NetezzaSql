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
  argument or the `justy/setDialect` request.
- q-quoted strings (`q'[...]'`) tokenize as `q` identifier + string literal in
  both the TS and C# lexers; embedded quote handling is preserved only in the
  linter's statement scanner (`OracleLintHelpers.StatementEnd`).
- Netezza-only constructs (LIMIT, `DB..TABLE`, DISTRIBUTE/ORGANIZE, EXTERNAL
  TABLE, GROOM, GENERATE STATISTICS) are rejected in Oracle mode with PAR001.
