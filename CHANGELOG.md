# Changelog

All notable changes to this project will be documented here.

## 0.2.0-preview.6

- Fix false SQL005 when a table is in schema cache but columns are deferred/empty (lazy hydrate ≥500 objects). Qualified column refs no longer report "table not found in schema cache".
- Completion treats empty column lists as a miss so hosts can lazy-hydrate alias-qualified paths (`A.` → `db.schema.table`).
- Expose `CompletionAliasResolver` for host-side alias → table path hydration.

## Unreleased

- Initial public development release.
- Netezza SQL lexer, parser, AST, formatter, linter, completion, and authoring services.
- Netezza DDL builders and catalog SQL helpers.
