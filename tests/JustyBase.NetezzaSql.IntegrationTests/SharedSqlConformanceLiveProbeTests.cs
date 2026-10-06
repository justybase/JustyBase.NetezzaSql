using System.Text.Json;
using JustyBase.NetezzaDriver;
using Xunit;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// .NET parity driver for the tag-driven shared live probe suites
/// (read-only-probes, disposable-ddl, dml-fixture). Emits one
/// CONFORMANCE_LIVE_RESULT marker per suite. Run with:
/// dotnet test ... --filter Category=LiveConformance
/// </summary>
public sealed class SharedSqlConformanceLiveProbeTests
{
    [OptInLiveConformanceFact]
    [Trait("Category", "LiveConformance")]
    public void Shared_live_probe_suites_match_the_server()
    {
        var host = Required("NZ_DEV_HOST");
        var database = Required("NZ_DEV_DATABASE");
        var user = Required("NZ_DEV_USER");
        var password = Required("NZ_DEV_PASSWORD");
        var port = int.TryParse(Environment.GetEnvironmentVariable("NZ_DEV_PORT"), out var configuredPort)
            ? configuredPort
            : 5480;
        var schema = (Environment.GetEnvironmentVariable("NZ_DEV_SCHEMA") ?? "ADMIN").ToUpperInvariant();

        using var connection = new NzConnection(user, password, host, database, port);
        connection.Open();

        RunReadOnly(connection, database, schema);
        RunDisposableDdl(connection, database, schema);
        RunDmlFixture(connection, database, schema);
    }

    private static void RunReadOnly(NzConnection connection, string database, string schema)
    {
        var cases = LoadLiveCases("live-readonly");
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var table = $"{schema}.JB_CF_RO_A_{suffix}";
        var table2 = $"{schema}.JB_CF_RO_B_{suffix}";
        var table3 = $"{schema}.JB_CF_RO_C_{suffix}";
        var created = new List<string>();
        foreach (var name in new[] { table, table2, table3 })
        {
            NetezzaLiveTestHost.Execute(connection,
                $"CREATE TABLE {name} (ID INT4, NAME VARCHAR(40), ACCOUNTKEY INT4, CREATED_AT TIMESTAMP, EVENT_DATE DATE)");
            created.Add(name);
        }

        var passed = new List<string>();
        var failures = new List<string>();
        var observations = new List<object>();
        foreach (var item in cases)
        {
            var id = item.GetProperty("id").GetString()!;
            var expected = !item.GetProperty("expect").TryGetProperty("valid", out var valid) || valid.GetBoolean();
            var sql = Render(item.GetProperty("sql").GetString()!, table, table2, table3, database, schema);
            var observed = TryExecute(connection, sql);
            if (observed == expected)
            {
                passed.Add(id);
            }
            else
            {
                failures.Add(id);
                observations.Add(new { id, expected, observed });
            }
        }

        foreach (var name in created)
            TryDrop(connection, name);
        Emit("read-only-probes", passed, failures, observations);
        Assert.Empty(failures);
    }

    private static void RunDisposableDdl(NzConnection connection, string database, string schema)
    {
        RequireFixtureDdl();
        var cases = LoadLiveCases("live-ddl-");
        var passed = new List<string>();
        var failures = new List<string>();
        var observations = new List<object>();
        foreach (var item in cases)
        {
            var id = item.GetProperty("id").GetString()!;
            var tags = Tags(item);
            var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            var table = $"{schema}.JB_CF_DDL_A_{suffix}";
            var table2 = $"{schema}.JB_CF_DDL_B_{suffix}";
            var setupOk = true;
            var expect = item.GetProperty("expect");
            if (expect.TryGetProperty("setupSql", out var setupElement)
                && setupElement.ValueKind == JsonValueKind.String)
            {
                setupOk = TryExecute(connection,
                    Render(setupElement.GetString()!, table, table2, table, database, schema));
            }
            var expected = !expect.TryGetProperty("valid", out var valid) || valid.GetBoolean();
            var observed = TryExecute(connection,
                Render(item.GetProperty("sql").GetString()!, table, table2, table, database, schema));
            CleanupDisposableDdl(connection, tags, table, table2);
            if (setupOk && observed == expected)
            {
                passed.Add(id);
            }
            else
            {
                failures.Add(id);
                observations.Add(new { id, expected, observed, setupOk });
            }
        }

        Emit("disposable-ddl", passed, failures, observations);
        Assert.Empty(failures);
    }

    private static void RunDmlFixture(NzConnection connection, string database, string schema)
    {
        RequireFixtureDdl();
        var cases = LoadLiveCases("live-dml-fixture");
        var passed = new List<string>();
        var failures = new List<string>();
        var observations = new List<object>();
        foreach (var item in cases)
        {
            var id = item.GetProperty("id").GetString()!;
            var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            var table = $"{schema}.JB_CF_DML_A_{suffix}";
            var table2 = $"{schema}.JB_CF_DML_B_{suffix}";
            var setup = new[]
            {
                $"CREATE TABLE {table} (ID INT4, NAME VARCHAR(40), NOTE VARCHAR(40))",
                $"CREATE TABLE {table2} (ID INT4, NAME VARCHAR(40), NOTE VARCHAR(40))",
                $"INSERT INTO {table} VALUES (1, 'before', NULL)",
                $"INSERT INTO {table} VALUES (5, 'delete', 'before')",
                $"INSERT INTO {table2} VALUES (1, 'updated', NULL)",
                $"INSERT INTO {table2} VALUES (2, 'inserted', NULL)",
                $"INSERT INTO {table2} VALUES (3, 'conditional', NULL)",
                $"INSERT INTO {table2} VALUES (4, 'no column list', NULL)",
                $"INSERT INTO {table2} VALUES (5, 'delete', NULL)",
            };
            var setupOk = setup.All(statement => TryExecute(connection, statement));
            var expect = item.GetProperty("expect");
            var expected = !expect.TryGetProperty("valid", out var valid) || valid.GetBoolean();
            var observed = setupOk && TryExecute(connection,
                Render(item.GetProperty("sql").GetString()!, table, table2, table, database, schema));
            object? detailFailure = null;
            if (observed && expected
                && expect.TryGetProperty("afterSql", out var afterElement)
                && afterElement.ValueKind == JsonValueKind.String)
            {
                try
                {
                    var rows = NetezzaLiveTestHost.ExecuteReaderRows(connection,
                        Render(afterElement.GetString()!, table, table2, table, database, schema), 1);
                    var actual = rows.Select(row => new[] { Convert.ToString(row[0]) ?? string.Empty }).ToList();
                    if (expect.TryGetProperty("afterRows", out var expectedRows)
                        && !RowsMatch(expectedRows, actual))
                    {
                        observed = false;
                        detailFailure = new { expected = expectedRows, actual };
                    }
                }
                catch
                {
                    observed = false;
                }
            }

            TryDrop(connection, table);
            TryDrop(connection, table2);
            if (setupOk && observed == expected)
            {
                passed.Add(id);
            }
            else
            {
                failures.Add(id);
                observations.Add(new { id, expected, observed, setupOk, detailFailure });
            }
        }

        Emit("dml-fixture", passed, failures, observations);
        Assert.Empty(failures);
    }

    private static bool RowsMatch(JsonElement expected, List<string[]> actual)
    {
        if (expected.ValueKind != JsonValueKind.Array)
            return true;
        var expectedRows = expected.EnumerateArray().ToArray();
        if (expectedRows.Length != actual.Count)
            return false;
        for (var rowIndex = 0; rowIndex < expectedRows.Length; rowIndex++)
        {
            var expectedCells = expectedRows[rowIndex].EnumerateArray()
                .Select(cell => cell.ValueKind == JsonValueKind.Null ? string.Empty : cell.GetString() ?? string.Empty)
                .ToArray();
            if (expectedCells.Length != actual[rowIndex].Length)
                return false;
            for (var cellIndex = 0; cellIndex < expectedCells.Length; cellIndex++)
            {
                if (!string.Equals(expectedCells[cellIndex], actual[rowIndex][cellIndex], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        return true;
    }

    private static void CleanupDisposableDdl(NzConnection connection, List<string> tags, string table, string table2)
    {
        if (tags.Contains("live-ddl-drop"))
            return;
        if (tags.Contains("live-ddl-table"))
        {
            TryDrop(connection, table);
            TryDrop(connection, table2);
        }
        if (tags.Contains("live-ddl-view") || tags.Contains("live-ddl-matview"))
        {
            TryDropView(connection, table);
            TryDropView(connection, table2);
        }
        if (tags.Contains("live-ddl-sequence"))
            TryDropSequence(connection, table);
        if (tags.Contains("live-ddl-synonym"))
            TryDropSynonym(connection, table);
    }

    private static void TryDrop(NzConnection connection, string table)
    {
        try { NetezzaLiveTestHost.Execute(connection, $"DROP TABLE {table}"); }
        catch { }
    }

    private static void TryDropView(NzConnection connection, string view)
    {
        try { NetezzaLiveTestHost.Execute(connection, $"DROP VIEW {view}"); }
        catch { }
    }

    private static void TryDropSequence(NzConnection connection, string sequence)
    {
        try { NetezzaLiveTestHost.Execute(connection, $"DROP SEQUENCE {sequence}"); }
        catch { }
    }

    private static void TryDropSynonym(NzConnection connection, string synonym)
    {
        try { NetezzaLiveTestHost.Execute(connection, $"DROP SYNONYM {synonym}"); }
        catch { }
    }

    private static bool TryExecute(NzConnection connection, string sql)
    {
        try
        {
            NetezzaLiveTestHost.Execute(connection, sql);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Render(string sql, string table, string table2, string table3, string database, string schema) =>
        sql.Replace("{{table}}", table, StringComparison.Ordinal)
            .Replace("{{table2}}", table2, StringComparison.Ordinal)
            .Replace("{{table3}}", table3, StringComparison.Ordinal)
            .Replace("{{bareTable}}", Bare(table), StringComparison.Ordinal)
            .Replace("{{bareTable2}}", Bare(table2), StringComparison.Ordinal)
            .Replace("{{bareTable3}}", Bare(table3), StringComparison.Ordinal)
            .Replace("{{database}}", database, StringComparison.Ordinal)
            .Replace("{{schema}}", schema, StringComparison.Ordinal);

    private static string Bare(string name)
    {
        var index = name.LastIndexOf('.');
        return index < 0 ? name : name[(index + 1)..];
    }

    private static List<string> Tags(JsonElement item)
    {
        if (!item.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return [];
        return tags.EnumerateArray()
            .Where(tag => tag.ValueKind == JsonValueKind.String)
            .Select(tag => tag.GetString()!)
            .ToList();
    }

    private static List<JsonElement> LoadLiveCases(string tagPrefix)
    {
        var root = ConformanceRoot();
        var directory = Path.Combine(root, "dialects", "netezza", "live");
        var cases = new List<JsonElement>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl").Order())
        {
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                using var document = JsonDocument.Parse(line);
                var item = document.RootElement.Clone();
                if (Tags(item).Any(tag => tag == tagPrefix || tag.StartsWith(tagPrefix, StringComparison.Ordinal)))
                    cases.Add(item);
            }
        }
        Assert.NotEmpty(cases);
        return cases;
    }

    private static void Emit(string suite, List<string> passed, List<string> failures, List<object> observations)
    {
        Console.WriteLine("CONFORMANCE_LIVE_RESULT=" + JsonSerializer.Serialize(new
        {
            dialect = "netezza",
            suite,
            passedIds = passed,
            failures,
            observations,
        }));
    }

    private static void RequireFixtureDdl()
    {
        Assert.True(
            string.Equals(Environment.GetEnvironmentVariable("NZ_DEV_ALLOW_FIXTURE_DDL"), "1", StringComparison.Ordinal),
            "NZ_DEV_ALLOW_FIXTURE_DDL=1 is required for the live DDL/DML suites.");
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Required live Netezza variable {name} is missing.");
        return value!;
    }

    private static string ConformanceRoot()
    {
        var configured = Environment.GetEnvironmentVariable("JUSTYBASE_SQL_CONFORMANCE_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = Path.GetFullPath(configured);
            if (Directory.Exists(Path.Combine(full, "dialects")))
                return full;
            throw new DirectoryNotFoundException($"JUSTYBASE_SQL_CONFORMANCE_PATH does not contain dialects/: {full}");
        }

        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            var candidates = new[]
            {
                Path.Combine(current.FullName, "JustyBase.SqlConformance"),
                current.Parent is null ? string.Empty : Path.Combine(current.Parent.FullName, "JustyBase.SqlConformance")
            };
            var found = candidates.FirstOrDefault(candidate =>
                candidate.Length > 0 && Directory.Exists(Path.Combine(candidate, "dialects")));
            if (found is not null)
                return found;
        }

        throw new DirectoryNotFoundException(
            "Shared SQL conformance repository was not found. Set JUSTYBASE_SQL_CONFORMANCE_PATH.");
    }
}
