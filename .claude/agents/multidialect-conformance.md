---
name: multidialect-conformance
description: Status and rules of the multidialect SQL conformance program for JustyBase.NetezzaSql (C# library), a consumer of the pinned JustyBase.SqlConformance snapshot. Use when changing PostgreSQL or Netezza parser, linter or completion behaviour covered by the corpus, regenerating its fixtures, or touching the .NET SDK pin.
---

# Multidialect conformance: JustyBase.NetezzaSql

Status as of 2026-10-17. The canonical, dated status is
`../JustyBase.SqlConformance/docs/MULTIDIALECT_STATUS.md`. Read it first; this
file is a working summary and may lag behind it.

## Role

The library pins a snapshot of the shared corpus under
`tests/JustyBase.NetezzaSql.Tests/Fixtures/sql-conformance/` and runs the shared
conformance tests against it, including the PostgreSQL shared-corpus tests.

- Check the pin: `python3 ../JustyBase.SqlConformance/scripts/snapshot.py --corpus-root ../JustyBase.SqlConformance --output tests/JustyBase.NetezzaSql.Tests/Fixtures/sql-conformance --dialect common --dialect db2 --dialect mssql --dialect mysql --dialect netezza --dialect oracle --dialect postgresql --dialect sqlite --check`
- Never edit the pinned fixtures by hand.

## Current state

- Pin: `feac362`, `corpusDirty: false`, 2464 cases for this dialect set.
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
- Do not commit fixtures that were not regenerated from a clean corpus commit.
- Parser changes need a focused regression test for every newly accepted form,
  verified against the production engine first.

## Related projects

- `JustyBase.SqlConformance`: the corpus owner and source of truth.
- `justybase-vscode`: TypeScript consumer, pinned snapshot under `src/__tests__/sqlParser/fixtures/sqlConformance/`.
- `justybase_rust_tauri`: Rust consumer, pinned snapshot under `crates/app-core/tests/fixtures/sql-conformance/`.
- `justybase` (C# application): routes Oracle, PostgreSQL and MySQL to the Netezza profile; this library does not change that routing.
