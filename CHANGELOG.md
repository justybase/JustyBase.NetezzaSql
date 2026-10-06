# Changelog

All notable changes to this project will be documented here.

## 0.8.9

- Mark `JustyBase.Ai` as Native AOT compatible (`IsAotCompatible = true`):
  `GitHub.Copilot.SDK` 1.0.x bridges the Rust runtime via AOT-safe FFI and
  source-generated JSON, so the package no longer advertises JIT-only metadata.
- Add per-package NuGet descriptions and tags for the parser, DDL, catalog,
  `JustyBase.Ai`, and `JustyBase.Ai.Embedded`.
- Centralize package versions via `Directory.Packages.props` and update test
  and live-proof dependencies (Test SDK 18.10.1, coverlet 10.1.0, SqlClient 7.1.1,
  NetezzaDriver 1.9.5, Microsoft.Data.Sqlite 10.0.12).
- Add NativeAOT publish smoke checks for the LSP and a `JustyBase.Ai` probe to CI.
- Add coverage gates for `JustyBase.Ai` (35% / 25%) and `JustyBase.Sqlite` (88% / 74%).
- Add LSP `textDocument/codeAction` with lint quick fixes and safe-fix-all,
  `textDocument/inlayHint` type hints, and whole-document `textDocument/formatting`.
- Add completion ranking tiers and fuzzy name matching, procedure/script
  variable completion (`&`, `$`, `{}`, `${}`), and foreign-key JOIN predicate
  suggestions (`IForeignKeyProvider`, `justy/syncSchema` FK metadata).
- Add a frozen reference parity corpus (`Fixtures/parity`) with tests against
  justybase/justybase-vscode behavior, and align the `NZ004`/`NZ010` quick
  fixes with the reference semantics.
- Port the reference NZPLSQL procedure matrix (101 accept/reject cases) and a
  parser/validator parity corpus (150 dialect cases); intentional differences
  are recorded with reasons instead of being skipped.
- Add parser performance gates: generated ~1 MB DDL/DML/Complex fixtures with
  hard budgets (`Category=Performance`, CI step) plus a BenchmarkDotNet project
  and `eng/Run-ParserBenchmarks.ps1` for local profiling.
- Align NZPLSQL grammar with the reference behavior: `ELSEIF` synonym,
  `%TYPE`/`%ROWTYPE` declarations, mandatory `AS` and `LANGUAGE NZPLSQL`,
  `RAISE` severity/message validation, non-empty `EXCEPTION` handlers, and
  removal of the SQL038 warning for `RETURNS` without `RETURN` (live-verified
  legal).
- Split monolithic validator/completion test files into feature-named partial
  classes (no behavior change).
- Emit `COMMENT ON COLUMN <view>.<column>` for view columns with descriptions
  (`NetezzaViewDdlInput.Columns`, `NetezzaDdlInputFactory.BuildView`), matching
  the reference view DDL contract.

## 0.8.8

- Extend the shared Git service with amend, undo last commit, reset, revert,
  and stash operations, and improve branch creation and commit retrieval.

## 0.8.7

- Complete Legacy Netezza catalog source search, schema refresh, column caching,
  quoted-identifier completion, and destructive SQL risk handling.
- Add `DROP` risk diagnostics and the `NZ025` random-distribution lint rule.

## 0.8.6

- Add support for legacy script variables in identifiers, `LIMIT`/`OFFSET` clauses,
  AST round-tripping, and schema-aware validation.
- Add regression coverage for script-variable parsing and formatting.

## 0.8.5

- Improve schema-aware completion for active databases, qualified relation paths,
  quoted identifiers, and wildcard column expansion.
- Preserve active-database context in linting and visitor table lookups.
- Add regression coverage for qualified completion and linter behavior.

## 0.8.3

- Add the GitHub Copilot SDK backend with device-flow sign-in, SQL-only tool
  permissions, resumable chat sessions, model/reasoning selection, and attachment
  context support.
- Add embedded chat model bootstrap and progress reporting, and preserve provider
  session bindings across application restarts without leaking them across logout.
- Harden Copilot runtime discovery, session configuration, and authentication cleanup.

## 0.8.2

- Preserve Access DML joins, quoted aliases/assignments, and index column
  direction metadata through parsing and formatting.
- Complete Access provider function/type authoring metadata and declared
  parameter validation; ignore Access identifiers in non-Access syntax linting.
- Publish the Access AST contract consumed by the UCanAccess provider package.

## 0.8.1

- Update `Microsoft.Extensions.AI` to 10.9.0 and the .NET 10 dependency-abstractions packages to 10.0.11.
- Update `Parquet.Net` to 6.1.0 for the shared import/export libraries.
- Fix Access `NOT LIKE` / `NOT ILIKE` AST mapping so negated comparisons keep their operator.

## 0.8.0

- Add the shared AI libraries: `JustyBase.Ai` (chat service, tool executor,
  OpenAI-compatible and Codex backends, prompt building, chat model contracts) and
  `JustyBase.Ai.Embedded` (embedded llama-server / GGUF model management for FIM inline
  completion and AI chat). Both are packed and published under the same 0.8.0 version.
- Add MLX backend for Apple Silicon and harden llama-server startup: CPU fallback when GPU
  start fails, GPU offload defaulting to auto (model layer count read), KV cache kept in
  system RAM, and the FIM prompt field fixed for recent llama.cpp.
- Add a shared completion orchestrator, fragment and gate; route the LSP completion through
  it. Gate completion after whitespace and add FROM-continuation and WHERE-continuation
  contexts.
- Complete the Microsoft Access / Jet / ACE SQL dialect: Access lexer tokens,
  TOP/DISTINCTROW, TRANSFORM/PIVOT, PARAMETERS, Access index DDL, formatter,
  lint rules, completion/hover/signature metadata, semantic tokens and LSP/runtime dispatch.
- Add a shared Netezza schema loader with typed schema cache (and unit/live tests), plus
  modern Legacy-host catalog queries replacing the retired legacy SQL file.
- Harden the import pipe, schema cache, and scan-preview handling; improve Netezza import
  date handling and logging.
- CI: cross-platform attachment paths and AI package READMEs; `JustyBase.Ai` and
  `JustyBase.Ai.Embedded` are now packed in `ci.yml` alongside the six existing libraries.

## 0.5.2

- Live test host: treat the driver's `Error opening file` XferTable message as a pipe-topology
  error and retry the EXTERNAL insert handshake once before soft-skipping, so intermittent
  server-to-client pipe connectivity no longer fails `Verify-Local` runs.

## 0.5.1

- Fix typed named-pipe import of numeric columns: values are written without a forced
  six-decimal fraction, so they no longer exceed the scale of NUMERIC(p,s) columns
  inferred by the vscode type chooser (e.g. `NUMERIC(16,2)` rejected `10.500000`).
- Fix typed named-pipe import of date-overridden columns: midnight `DateTime` values are
  emitted in date-only form (`YYYY-MM-DD`), which Netezza `DATE` columns accept.
- Add unit coverage for the pipe numeric/date formats.

## 0.5.0

- Import/export consolidation (phases 1.2–2.2): the shared import/export engines now live in
  `JustyBase.ImportExport`; both hosts (Avalonia, WinForms) keep only UI adapters.
- Add `JustyBase.ImportExport.Export.ExportEncodingResolver` (unified encoding/newline
  resolution; superset of the previously duplicated per-host helpers) and
  `JsonExportWriter` (row = JSON string array; source-generated context).
- Add `CsvRowReader` (Sylvan-backed, Brotli/Gzip/Zstd) with pure `CsvCellTypeResolver`
  (`CsvCell`/`CsvCellKind`), including `TreatAllColumnsAsText` and Pesel/Regon-as-text.
- Add the streaming `ImportTypeAnalyzer` with `ImportColumnKind`/`DetectedImportColumnType`,
  `ImportHeaderNormalizer`, and `ImportTypeInferenceUtils` (vscode `importHeaderUtils.ts` /
  `importTypeInferenceUtils.ts` ports).
- Add the vscode type-inference contracts: `DatabaseColumnTypeChooser`,
  `DatabaseImportDataType`, `DatabaseImportTypeMapper`, `NetezzaColumnTypeChooser`, and
  retarget the batch `DatabaseTypeChooser.Infer` onto the chooser algorithm — this is a
  **behavioral change**: integers infer as `BIGINT`, decimals as `NUMERIC(p,s)` sized from
  the data, and booleans only with `inferBoolean: true`.
- Document the consolidation boundary and phase status in `docs/import-export-consolidation.md`.
- Tooling: `Verify-Local.ps1` now clears `artifacts/packages` before packing, and
  `Test-PackageConsumer.ps1` prefers the most recently packed nupkg per package id.

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
