# Architecture: Access SQL tooling and provider boundary

## Status

Accepted.

## Context

`JustyBase.NetezzaSql` provides host-agnostic SQL language tooling. It now
contains several SQL dialects, including Microsoft Access / Jet / ACE, and is
consumed by `JustyBase.UCanAccessCs` as a NuGet package.

`JustyBase.UCanAccessCs` is an execution provider. It reads and writes MDB /
ACCDB files, builds a SQLite mirror, translates Access SQL, implements ADO.NET
semantics, and owns transaction and file-mutation behavior.

The two repositories must share Access lexical and syntactic knowledge without
coupling the language tooling to a physical database implementation.

## Decision

The dependency direction is one-way:

```text
JustyBase.NetezzaSqlParser
        ^
        |
JustyBase.UCanAccessCs
        ^
        |
    host application
```

The component responsibilities are:

| Component | Owns | Must not own |
|---|---|---|
| `JustyBase.NetezzaSqlParser` | lexer, tokens, AST, dialect parser, formatter, authoring, completion, linting and LSP | MDB/ACCDB I/O, SQLite mirror, provider transactions or Access file mutation |
| `JustyBase.UCanAccess.File` | Jet/ACE file format, pages, tables, indexes, relationships and file metadata | SQL parser, SQLite execution or ADO.NET provider behavior |
| `JustyBase.UCanAccessCs` | ADO.NET provider, SQLite mirror, Access-to-SQLite translation, Access functions, DML/DDL and transactions | editor/LSP-specific behavior and file-format primitives |
| `JustyBase.UCanAccess.AccessCrypto` | optional encrypted-page codec | parser and general provider policy |

The Access lexer and parser are the shared syntax contract. UCanAccess may
adapt parser tokens to its internal translator token model, but it must not
introduce a second Access lexer.

The parser AST is used for language tooling and contract tests first. UCanAccess
also has a deliberately small, parity-tested AST normalization bridge for
simple SELECT/TOP/DISTINCTROW/date/parameter forms and explicit crosstabs. It
formats those nodes back to canonical Access SQL and delegates the result to
the existing provider translator. The translator therefore remains the
execution authority; unsupported or uncertain AST shapes fall back to the
legacy path. A parser limitation must not silently reject an execution feature
that the provider already supports.

The existing package name `JustyBase.NetezzaSqlParser` remains for
compatibility even though the package contains multiple dialects. A neutral
package name can be considered in a later, explicitly versioned migration.

## Consequences

- `UCanAccess.File` stays usable without the parser package.
- Access syntax fixes are made in the parser lexer/AST/parser and consumed by
  UCanAccess through its NuGet dependency.
- SQLite rewrites, Access NULL behavior, VBA functions, exact decimal rules,
  mirror refreshes and atomic file operations stay in UCanAccess.
- Parser support, translator support and provider/file support are documented
  and tested as separate capabilities.
- The two repositories coordinate parser package versions, but do not use
  cross-repository project references.

## Release contract

The Access dialect contract for this integration is parser package `0.8.2`.
UCanAccess consumes that package version explicitly and publishes its
corresponding provider patch release after the parser package is available.
