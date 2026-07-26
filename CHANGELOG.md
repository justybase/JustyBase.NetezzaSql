# Changelog

All notable changes to this project will be documented here.

## Unreleased

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
