# Changelog

All notable changes to this project will be documented here.

## Unreleased

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
