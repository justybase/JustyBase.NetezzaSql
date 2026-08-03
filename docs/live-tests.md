# Optional Netezza live tests

The normal test suite is offline and does not require a database. The live smoke test uses the `JustyBase.NetezzaDriver` package and can be enabled with the development connection variables.

PowerShell example:

```powershell
$env:NZ_DEV_HOST = "host"
$env:NZ_DEV_PORT = "5480" # optional; defaults to 5480
$env:NZ_DEV_DATABASE = "DB"
$env:NZ_DEV_USER = "user"
$env:NZ_DEV_PASSWORD = "secret"

dotnet test .\tests\JustyBase.NetezzaSql.IntegrationTests\JustyBase.NetezzaSql.IntegrationTests.csproj --filter Category=Live
```

The live suite executes `SELECT 1`, catalog queries, a typed named-pipe CREATE/INSERT round-trip (including delimiter/newline escaping), SAMEAS insert into an existing table, and a Fast raw-line pipe import with filter. All import `USING` clauses emit `REMOTESOURCE 'dotnet'` so the driver opens DATAOBJECT/pipe paths on the client. When any required variable is missing, it exits without opening a connection so the offline suite remains green.

### Type inference round-trips

Additional cases prove CSV → `DatabaseTypeChooser.Infer` → CREATE → pipe INSERT → SELECT equality (simple types, escaping, quoted multiline CSV, adversarial payloads, mixed-type NVARCHAR fallback). See [live-import-roundtrip.md](live-import-roundtrip.md).

```powershell
pwsh .\eng\Run-LiveImportProof.ps1
```

Pipe round-trips still need a working Windows named-pipe server on the client. If the driver/topology rejects the pipe (for example a named-pipe error), the test soft-skips unless `NZ_REQUIRE_PIPE=1` is set.

## Live MySQL 8 parser proof (optional, local only)

```powershell
pwsh .\eng\Run-MySqlLiveProof.ps1
```

The isolated `tests/JustyBase.NetezzaSql.MySqlLiveTests` project uses
`MYSQL_LIVE_TEST_HOST`, `MYSQL_LIVE_TEST_DATABASE`, `MYSQL_LIVE_TEST_USER` and
`MYSQL_LIVE_TEST_PASSWORD`; optional variables are `MYSQL_LIVE_TEST_PORT` and
`MYSQL_LIVE_TEST_CONNECT_STRING`. Set `MYSQL_LIVE_TEST_REQUIRED=true` to make
missing configuration or connection failures hard failures.
