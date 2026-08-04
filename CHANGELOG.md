# Changelog

All notable changes to this project will be documented here.

## 0.4.1

- Move `SqlTypingPerfProbe` into the new `JustyBase.Core.Diagnostics` namespace so UI hosts
  (Avalonia editor, Legacy FCTB) share one typing-performance probe.
- Add the shared SQL word-list contract to `JustyBase.Core`: `ISqlDbWordListProvider`,
  `SqlWordListRequest` (with `FromText` fragment scan), `SqlWordListItem`, and
  `SqlWordListService` — the headless seam for host DB-backed completion fallback.
- Parser library: add `EngineSqlWordListRequestBuilder`, which slices the caret context via
  `SqlAutocompleteWindow` and runs `NzCompletionEngine` scope hints (aliases/CTE/temp tables)
  to build a `SqlWordListRequest`; `JustyBase.NetezzaSqlParser` now depends on the
  `JustyBase.Core` package.
- LSP: `CompletionService.GetCompletions` is now `async` and accepts an optional
  `ISqlDbWordListProvider` whose items are merged (dedupe by label) with engine items;
  `Program.cs` awaits the new API.
- Document the shared authoring core status and word-list seam in `docs/authoring-shared-core.md`.

## 0.4.0

- Add SQL authoring support for Microsoft SQL Server, MySQL 8, and PostgreSQL.
- Extend dialect-aware lexing, parsing, formatting, linting, completion, and catalog support.

## 0.3.0

- First production release of the shared JustyBase Netezza SQL libraries.
- Add a shared ANSI authoring catalog with explicit Netezza, Oracle, and Db2 overlays.
- Add common structured `MERGE` parsing and `OFFSET`/`FETCH` AST and formatter support,
  preserving the existing `LimitClause` contract.
- Add shared lint-rule factories and dialect capability coverage.
- Add live Netezza and Db2 syntax probes and conformance tests.

## 0.3.0-preview.9

- Improve parser runtime conformance and structural scanning.
- Add lint coordination and expand LSP linting, symbol, reference, rename, and document handling.
- Add parser performance coverage.

## 0.3.0-preview.8

- Propagate column descriptions through NetezzaSchemaProviderAdapter for tooltip/completion metadata.

## 0.3.0-preview.7

- Multi-dialect SQL authoring: Oracle and Db2 alongside Netezza (tokenizer, parser, formatter, lint, completion, hover).
- Add Oracle program-unit AST/structures and Db2 statement support; dialect-aware service wiring.
- Document Db2 live proof via `eng/Run-Db2LiveProof.ps1`; bump dependent package versions.

## 0.3.0-preview.5

- Large-document SQL authoring: progressive lex→full semantic coloring; lex-only above 150k chars (no empty spans for huge line counts alone).
- Add `SqlTypingPerfProbe` budgets and typing responsiveness tests with `Fixtures/BIG.SQL`.
- Add `SqlStatementBounds` / `SqlAutocompleteWindow` to slice completion to the statement after the last top-level `;` (Legacy parity) so `D.|` works at the end of huge scripts.
- Expand `SqlPerformancePolicy` semantic classification modes and autocomplete passive/forced statement limits.

## 0.3.0-preview.2

- `DatabaseTypeChooser.Infer`: prefer `NVARCHAR` over `VARCHAR` for text columns.
- Size text length as ceil(maxSampleLen × 1.2), then round up to the next 10 (e.g. 12 → 20);
  empty columns apply the same sizing to the `varcharLength` hint.
- Keep codes with significant leading zeros as text (not `INTEGER`/`NUMERIC`).
- Extend unit coverage and live CSV→infer→CREATE→pipe→SELECT round-trips for the new sizing.

## 0.3.0-preview.1

- Add `JustyBase.Core` and `JustyBase.ImportExport` NuGet packages (shared risk, import/export,
  grid stats, and host ports documented in `docs/shared-core-status.md`).
- Extend Netezza pipe/CSV import: `NetezzaImportUsingOptions`, `NetezzaPipeImportExecutor`
  cancellation/timeouts, and live integration coverage when `NZ_DEV_*` is set.
- Pack Core and ImportExport in CI; expand package-consumer and coverage gates for the new libraries.
- Remove the obsolete public API baseline check (`eng/Assert-PublicApi.ps1`).

## 0.2.0-preview.8

- Extract shared host SQL/DDL helpers into packages: `NetezzaErrorLocator`,
  `NetezzaMaintenanceSql`, session/skew SQL on `NetezzaSystemSql`,
  `NetezzaDdlTextBuilder.BuildCreateSequence`, and import CREATE/INSERT prefixes
  via `NetezzaImportSql`.
- Fix `NetezzaErrorLocator`: prefer at-char slice offsets over the crude
  `^ found` path; honor `UseRegexWordSearch` in `LocateInSql` (skip qualified
  `alias.col` for ambiguous columns).

## 0.2.0-preview.7

- Merge `JustyBase.Netezza` into this repository as a fourth library package
  (`JustyBase.Netezza/`). One restore/build/test/pack covers parser, DDL,
  catalog, and the integration layer under a shared `PackageVersion`.
- Local consumers (`JustyBase`, `JustyBase.Legacy`) need only the
  `JustyBase.NetezzaSql` sibling checkout for ProjectReferences.

## 0.2.0-preview.6

- Fix false SQL005 when a table is in schema cache but columns are deferred/empty (lazy hydrate ≥500 objects). Qualified column refs no longer report "table not found in schema cache".
- Completion treats empty column lists as a miss so hosts can lazy-hydrate alias-qualified paths (`A.` → `db.schema.table`).
- Expose `CompletionAliasResolver` for host-side alias → table path hydration.

## Earlier

- Initial public development release.
- Netezza SQL lexer, parser, AST, formatter, linter, completion, and authoring services.
- Netezza DDL builders and catalog SQL helpers.
