# SQL Authoring Shared Core Status

This document tracks which **SQL authoring** surfaces live in the shared library
(`JustyBase.NetezzaSqlParser`) versus the two host applications, and the rules
for keeping that boundary clean.

Sibling document: [shared-core-status.md](shared-core-status.md) covers
`JustyBase.Core` / `JustyBase.ImportExport` (execution, scripting, risk, export).
This document covers the editor-intelligence engine only.

## The boundary (single sentence)

**All authoring *logic* lives in the shared library; hosts keep only thin,
UI-framework-specific adapters** (mapping shared results onto Avalonia
RoslynPad / WinForms FastColoredTextBox primitives, and host-owned schema/data
access).

## Layout

```
JustyBase.NetezzaSqlParser (assembly JustyBase.NetezzaSqlParser)   ← engine (SoT)
├─ Parser/    NzSqlParser + per-dialect partials (Netezza, Db2, Oracle, MSSQL, MySQL, Postgres)
├─ Lexer/     NzLexer, Db2Lexer, OracleLexer, …
├─ Linter/    LintEngine, NzLintRules, Db2LintRules, LintQueue, QualityRuleRegistry, NzLintCodeActions
├─ Formatter/ NzSqlFormatter
├─ Completion/NzCompletionEngine, CompletionAliasResolver, CompletionScopeProvider, SqlAutocompleteWindow
├─ Authoring/ NzSemanticTokenClassifier, NzRenameService, NzSymbolService, NzSignatureHelpService,
│             NzHoverService, NetezzaSqlCatalog, Db2SqlCatalog, SqlPerformancePolicy, SqlLintInvocation
├─ Caching/   DocumentParsingCoordinator, DocumentParseSession, StatementIndex
├─ Dialects/  SqlDialect, DialectRuntime (single per-dialect facade)
└─ Visitor/   ISchemaProvider, scope builders, structural scanner

JustyBase.Core (assembly JustyBase.Core)                           ← UI-agnostic utilities
└─ Diagnostics/SqlTypingPerfProbe                                  (shared typing UX probe)
```

## Shared surface status

| Surface | Status | Consumers |
| --- | --- | --- |
| Parser + AST (all dialects incl. DB2) | **Production** | Avalonia (lint/highlight/outline), Legacy (lint/highlight), LSP |
| Lexers (all dialects incl. DB2) | **Production** | Avalonia, Legacy, LSP, tests |
| Linter (`LintEngine`, `NzLintRules`, `Db2LintRules`) | **Production** | Avalonia `NzLinterService`, Legacy `LegacySqlAuthoringServices` |
| Formatter (`NzSqlFormatter`) | **Production** | Avalonia `NzSqlDocumentFormatter`, Legacy `LegacySqlAuthoringServices` (generic SQL fallback remains host-side) |
| Completion (`NzCompletionEngine`) | **Production** | Avalonia `SqlCompletionProvider`, Legacy `NetezzaHybridAutocompleteSource` / `NetezzaSqlAuthoringUseCase` |
| Completion merge policy (`SqlCompletionMergePolicy`) | **Production** | Both hosts (Avalonia `SqlCompletionProvider.ShouldRunLegacyPath`, Legacy `NetezzaHybridAutocompleteSource`) |
| DB word-list fallback contract (`ISqlDbWordListProvider`) | **Production (contract + headless seam)** | `JustyBase.Core.Database`; adapters in both hosts (`DbWordListProvider`, `LegacyDbWordListProvider`); headless `SqlWordListService` + `SqlWordListRequestExtractor`; parser-backed `EngineSqlWordListRequestBuilder` in the parser lib; LSP `CompletionService` merges word-list items via optional provider |
| Semantic tokens (`NzSemanticTokenClassifier`) | **Production** | Avalonia `SemanticLineColorizer`, Legacy `LegacySqlAuthoringServices` + `FctbSemanticStyleMapper` |
| Rename / symbols / references (`NzRenameService`, `NzSymbolService`) | **Production** | Avalonia `SqlDocumentViewModel`, Legacy FCTB key handlers |
| Signature help (`NzSignatureHelpService`) | **Production** | Avalonia `SqlCompletionProvider`, Legacy `LegacySqlAuthoringServices` + `NzSignatureHelpPopup` |
| Hover (`NzHoverService`) | **Production** | Legacy tooltips; Avalonia hover path |
| Parse/schema cache (`DocumentParsingCoordinator`, `InMemorySchemaProvider`) | **Production** | Both hosts |
| Performance policy (`SqlPerformancePolicy`) | **Production** | Both hosts |
| Typing UX probe (`SqlTypingPerfProbe`) | **Production** | Avalonia `SemanticLineColorizer`, FCTB control + Legacy host |
| LSP server (`JustyBase.NetezzaSqlLsp`) | **Production** | External editors via LSP |

\* `LegacyCompletionPolicy` was a delegating shim and has been removed; Legacy
calls `SqlCompletionMergePolicy` directly.

## Consumer adapter layers (host-owned by design)

**Avalonia (JustyBase):** `SemanticLineColorizer`, `SqlCompletionProvider` +
`CompletionDataSql` (shared `CompletionItem` → RoslynPad), `NzLinterService`,
`NzSqlDocumentFormatter`, `SqlOutlineBuilder`, `SqlDiagnosticsViewModel`.

**Legacy WinForms (JustyBase.Legacy):**
- `NetezzaSqlCompletionServices` — per-tab `NzCompletionEngine` + shared
  `DocumentParsingCoordinator` + `InMemorySchemaProvider`; `EnsureDb2Schema`
  projects host DB2 metadata into the shared provider so alias resolution stays
  in the engine.
- `LegacySqlAuthoringServices` — lint/format/hover/signature/rename/symbols all
  delegate to the engine; only FCTB marker rendering is host code.
- `NetezzaSqlAuthoringUseCase` + `NetezzaSqlAuthoringUseCaseAdapter` — map the
  engine onto the neutral `ISqlAuthoringUseCase` application contract.
- `NetezzaHybridAutocompleteSource`, `FctbCompletionMapper`,
  `FctbSemanticStyleMapper`, `CompletionItemAppearance` — FCTB UI adapters.
- `LegacyDbCompletionFallback`, `LegacySnippetsProvider`,
  `LegacySchemaSync` — host data/schema access only.

## DB2

The DB2 **engine** (lexer, parser, lint rules, `Db2SqlCatalog`) is fully shared.
In the desktop hosts:

- **Legacy** exercises DB2 authoring end-to-end (`SqlDialect.Db2`): DB2 catalog
  metadata loaded by `App.Data.DB2` is projected into the shared
  `InMemorySchemaProvider` (`EnsureDb2Schema`), and lint/format/completion run
  through `DialectRuntime`/`SqlDialect.Db2`.
- **Avalonia** does not ship a DB2 connection plugin today; DB2 authoring is
  covered there by unit tests (`Db2AuthoringCatalogTests`,
  `Db2DialectLspTests`) and the LSP server. This asymmetry is intentional and
  should be re-examined when a DB2 plugin is added.

## Definition of done for a shared surface

1. Logic lives in the `JustyBase.NetezzaSql` packages.
2. Both hosts call it on a production path (or one host + documented deprecation).
3. Local duplicate removed or reduced to a UI adapter.
4. Unit tests cover the contract; dialect-specific live tests exist
   (`*LiveTests`) when a database is available.
5. Package is included in CI `dotnet pack`.

## Known residuals (tracked)

| Item | Status | Notes |
| --- | --- | --- |
| Legacy "live word-list" completion path | **Phase B (contract) + Phase C (headless seam) — done** | Shared `ISqlDbWordListProvider` + `SqlWordListItem`/`SqlWordListRequest` live in `JustyBase.Core.Database`. Both hosts implement it as adapters over their existing engines (`DbWordListProvider` over `AutocompleteService`/`IDatabaseService`; `LegacyDbWordListProvider` over `LegacyDbCompletionFallback`) and register it in DI. Hot completion paths are unchanged (Avalonia stays `IAsyncEnumerable`, FCTB stays synchronous). Headless seam: `SqlWordListService` (Core orchestrator: text + caret → request → provider) with injected `SqlWordListRequestExtractor`; the default `SqlWordListRequest.FromText` computes only the dotted fragment (no hints), while the parser-backed `EngineSqlWordListRequestBuilder` (parser lib) runs `NzCompletionEngine` + `GetScopeHints` for alias/CTE/temp-table hints. The LSP `CompletionService.GetCompletions` (now async) takes an optional `ISqlDbWordListProvider` and merges word-list items after engine items (deduped by label); the LSP executable registers no provider today, so behavior is unchanged until a DB-backed provider exists. Note: the parser lib now references `JustyBase.Core` (packed as a `JustyBase.Core` package dependency — package-graph change). Contract notes: (1) `Label` is opaque **insert text** — it may be qualified (`SCHEMA.OBJECT` on the DB2 path) or unqualified depending on dialect and typed fragment; (2) the interface is `IAsyncEnumerable` while both underlying engines are synchronous — the adapters are async ceremony today, intended for future async DB access; (3) the hint dictionaries (`AliasDbTable`/`Subquery`/`With`/`TempTable`) are consumed only by the Avalonia engine and the parser-backed builder; the Legacy adapter currently ignores them (behavioral parity comes later). |
| Generic SQL formatter fallback | Host | Legacy falls back to `Hogimn.Sql.Formatter` when the shared parser cannot parse. Keep until parity audit. |
| `SqlTypingPerfProbe` | **Resolved** | Two divergent copies (parser lib + FCTB) merged into one canonical class in `JustyBase.Core.Diagnostics`; both hosts and the FCTB control now reference Core. Note: the merged NDJSON format uses ISO timestamps, a `doc` key, and per-op slow budgets (the FCTB copy previously used unix-ms, `documentKey`, and a flat 16 ms threshold) — telemetry-only, no consumer contract. `SqlTypingPerfLocal` re-syncs from the probe inside the FCTB control itself, preserving activation on non-host forms. |
| `LegacyCompletionPolicy` | **Resolved** | Removed; both hosts use `SqlCompletionMergePolicy` directly. |

## Verification gates

- `dotnet build` + unit tests for both solutions (`JustyBase.NetezzaSql` and
  `JustyBase.Legacy`) after any engine change.
- Adapter parity tests: `JustyBase.NetezzaSql.Tests/NzCompletionParityGateTests`,
  `AppBase.Tests/Sql/*`, `JustData.UiTests/Phase8*`,
  `JustyBase.Tests/NetezzaSqlParser/*`.
