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

The live test executes `SELECT 1` and the catalog queries for schemas and object types. When any required variable is missing, it exits without opening a connection so the offline suite remains green.
