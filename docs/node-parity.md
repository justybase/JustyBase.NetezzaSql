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
