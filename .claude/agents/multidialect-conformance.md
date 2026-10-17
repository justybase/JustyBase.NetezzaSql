---
name: multidialect-conformance
description: Status and rules of the multidialect SQL conformance program for JustyBase.NetezzaSql (C# library), a local-only consumer of the JustyBase.SqlConformance corpus. Use when changing PostgreSQL or Netezza parser, linter or completion behaviour covered by the corpus or running the local conformance tests.
---

# Multidialect conformance: JustyBase.NetezzaSql

Status as of 2026-10-17. The canonical, dated status is
`../JustyBase.SqlConformance/docs/MULTIDIALECT_STATUS.md`. Read it first; this
file is a working summary and may lag behind it.

## Role

SQL conformance tests (`Category=SqlConformance`) are optional local developer
tests. This repository does not vendor the corpus and standard CI never uses
it. Conformance tests read the sibling checkout directly via
`JUSTYBASE_SQL_CONFORMANCE_PATH`:

```powershell
$env:JUSTYBASE_SQL_CONFORMANCE_PATH = (Resolve-Path ..\JustyBase.SqlConformance).Path
dotnet test .\JustyBase.NetezzaSql.sln --filter "Category=SqlConformance"
```

- Standard gates (`dotnet test --filter "Category!=Performance&Category!=SqlConformance"`,
  `eng/Verify-Local.ps1`, `eng/Test-Coverage.ps1`, CI workflows) work without
  the corpus.
- A missing `JUSTYBASE_SQL_CONFORMANCE_PATH` on an explicit conformance run is
  an `InvalidOperationException`, never a silent skip; a set-but-invalid path
  is a `DirectoryNotFoundException`.

## Current state

- No committed corpus snapshot. The former `feac362` snapshot under
  `tests/JustyBase.NetezzaSql.Tests/Fixtures/sql-conformance/` and its
  snapshot-manifest verification are removed.
- Commits on `origin/master`: `73d7852` (ISNULL signature), `e120774` (SDK pin),
  `9aee0ad` (snapshot pin and PostgreSQL shared-corpus tests), `7132f9c`
  (C# PostgreSQL parser alignment).
- Last unit run: `JustyBase.NetezzaSql.Tests`, 5847 of 5847 passed on .NET 10.0.402.

## SDK

- `global.json` pins `10.0.402` with `rollForward: latestFeature`.
- The system SDK is `10.0.112` and cannot build this repository. Use the
  user-level SDK in `~/.dotnet-10`; `~/.bashrc` puts it first on `PATH`.
- Restart the terminal or VS Code after changing `PATH`.

## Rules

- PostgreSQL live checks read `POSTGRES_LIVE_TEST_*` (alias of `PG_LIVE_TEST_*`,
  set in `~/.bashrc`). The dev host is `192.168.0.61`. Missing configuration fails
  the live test; it does not skip.
- Do not commit corpus cases or regenerated fixtures into this repository.
- Parser changes need a focused regression test for every newly accepted form,
  verified against the production engine first.

## Related projects

- `JustyBase.SqlConformance`: the corpus owner and source of truth.
- `justybase-vscode`: TypeScript consumer, local-only via
  `JUSTYBASE_SQL_CONFORMANCE_PATH` (`npm run test:conformance`).
- `justybase_rust_tauri`: Rust consumer, still pins a snapshot under
  `crates/app-core/tests/fixtures/sql-conformance/` (out of scope here).
- `justybase` (C# application): routes Oracle, PostgreSQL and MySQL to the
  Netezza profile; this library does not change that routing.
