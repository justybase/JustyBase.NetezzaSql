using System.Text.Json;
using System.Text.RegularExpressions;
using JustyBase.NetezzaDriver;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Explicit live oracle checks for shared Netezza P0 contracts whose source
/// fixtures disagree or were previously blocked by a client-side $1 binder.
/// Run with: dotnet test ... --filter Category=LiveConformance
/// </summary>
public sealed class SharedSqlConformanceLiveNetezzaTests
{
    [OptInLiveConformanceFact]
    [Trait("Category", "LiveConformance")]
    public void Shared_netezza_p0_oracle_cases_match_the_live_server()
    {
        var host = Required("NZ_DEV_HOST");
        var database = Required("NZ_DEV_DATABASE");
        var user = Required("NZ_DEV_USER");
        var password = Required("NZ_DEV_PASSWORD");
        var port = int.TryParse(Environment.GetEnvironmentVariable("NZ_DEV_PORT"), out var configuredPort)
            ? configuredPort
            : 5480;

        using var connection = new NzConnection(user, password, host, database, port);
        connection.Open();

        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var table = $"JB_CF_{suffix}";
        var createdTable = false;
        try
        {
            Execute(connection, $"CREATE TABLE {table} (ID INT4, NAME VARCHAR(20), CREATED_AT TIMESTAMP)");
            createdTable = true;

            Execute(connection,
                $"CREATE TABLE {table}_DIST (ID INT4, CREATED_AT TIMESTAMP) " +
                "DISTRIBUTE ON (ID) ORGANIZE ON (CREATED_AT)");
            try
            {
                AssertLiveQuery(connection,
                    $"SELECT * FROM {table} E LIMIT 10 OFFSET 5;", expectedValid: true);
                AssertLiveQuery(connection,
                    $"SELECT * FROM {table} E WHERE E.* = 1;", expectedValid: false);
            }
            finally
            {
                Execute(connection, $"DROP TABLE {table}_DIST");
            }

            var corpus = ReadNetezzaCases();
            var verifiedProcedureIds = new List<string>();
            foreach (var id in new[]
            {
                "netezza.nzplsql.args.args-null-passed",
                "netezza.nzplsql.args.args-numeric-ps",
                "netezza.nzplsql.args.args-types-multi",
                "netezza.nzplsql.args.args-types-single",
                "netezza.nzplsql.declare.decl-alias-for-dollar",
            })
            {
                VerifyProcedure(connection, corpus[id], suffix);
                verifiedProcedureIds.Add(id);
            }

            Console.WriteLine("CONFORMANCE_LIVE_RESULT=" + JsonSerializer.Serialize(new
            {
                dialect = "netezza",
                suite = "nzplsql-procedure",
                passedIds = verifiedProcedureIds,
                adaptedIds = Array.Empty<string>(),
                failures = Array.Empty<string>(),
                pendingDetails = Array.Empty<string>(),
                detailFailures = Array.Empty<string>(),
            }));
        }
        finally
        {
            if (createdTable)
                Execute(connection, $"DROP TABLE {table}");
        }
    }

    private static void VerifyProcedure(NzConnection connection, JsonElement testCase, string suffix)
    {
        var id = testCase.GetProperty("id").GetString()!;
        var procedure = $"JB_CF_{suffix}_{id.Split('.').Last().Replace('-', '_').ToUpperInvariant()}";
        var sql = testCase.GetProperty("sql").GetString()!
            .Replace("{{proc}}", procedure, StringComparison.Ordinal)
            .Replace("{{table}}", "JB_CF_UNUSED", StringComparison.Ordinal);
        var signature = ExtractProcedureSignature(sql);
        var created = false;
        try
        {
            Execute(connection, sql);
            created = true;
            var callArgs = testCase.GetProperty("oracleProbe").GetProperty("callArgs").GetString() ?? string.Empty;
            Execute(connection, $"CALL {procedure}({callArgs})");
        }
        finally
        {
            if (created)
                Execute(connection, $"DROP PROCEDURE {procedure}({signature})");
        }
    }

    private static string ExtractProcedureSignature(string sql)
    {
        var match = Regex.Match(sql, @"\bPROCEDURE\s+[A-Za-z_][\w$]*(?:\.[A-Za-z_][\w$]*)?\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        Assert.True(match.Success, "Could not read the procedure signature from the shared fixture.");

        var signatureStart = match.Index + match.Length;
        var depth = 1;
        for (var index = signatureStart; index < sql.Length; index++)
        {
            if (sql[index] == '(')
                depth++;
            else if (sql[index] == ')' && --depth == 0)
            {
                return sql[signatureStart..index];
            }
        }

        throw new InvalidOperationException("Unclosed procedure signature in shared fixture.");
    }

    private static void AssertLiveQuery(NzConnection connection, string sql, bool expectedValid)
    {
        var error = Record.Exception(() => Execute(connection, sql));
        Assert.Equal(expectedValid, error is null);
    }

    private static void Execute(NzConnection connection, string sql)
    {
        using var command = connection.CreateCommand(sql);
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, JsonElement> ReadNetezzaCases()
    {
        var root = ConformanceRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "dialects", "netezza"), "*.jsonl", SearchOption.AllDirectories)
            .SelectMany(File.ReadLines)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            })
            .ToDictionary(item => item.GetProperty("id").GetString()!);
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

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Required live Netezza variable {name} is missing.");
        return value!;
    }
}
[AttributeUsage(AttributeTargets.Method)]
public sealed class OptInLiveConformanceFactAttribute : FactAttribute
{
    public OptInLiveConformanceFactAttribute()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("JUSTYBASE_RUN_LIVE_CONFORMANCE"),
            "1",
            StringComparison.Ordinal))
        {
            Skip = "Set JUSTYBASE_RUN_LIVE_CONFORMANCE=1 to opt in to live Netezza DDL probes.";
        }
    }
}
