using System.Text.Json;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using Xunit;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class SharedSqlConformanceTests
{
    private static readonly HashSet<string> ParserCategories = new(StringComparer.Ordinal)
    {
        "parser", "syntax", "ddl", "dml", "functions", "nzplsql", "recovery", "statement"
    };

    public static IEnumerable<object[]> SharedParserCases()
    {
        foreach (var (caseJson, dialect) in LoadCases())
        {
            using var document = JsonDocument.Parse(caseJson);
            var root = document.RootElement;
            var category = GetString(root, "category");
            var verification = GetString(root.GetProperty("verification"), "status", "source-backed");
            if (!ParserCategories.Contains(category) || verification == "needsVerification")
                continue;

            var expected = root.GetProperty("expect");
            bool? expectedValid = expected.TryGetProperty("valid", out var valid)
                ? valid.GetBoolean()
                : expected.TryGetProperty("parserValid", out var parserValid)
                    ? parserValid.GetBoolean()
                    : null;
            int? statementCount = expected.TryGetProperty("statementCount", out var count)
                ? count.GetInt32()
                : null;

            yield return
            [
                GetString(root, "id"), dialect, category,
                GetString(root, "priority"), verification,
                GetString(root, "sql"), GetString(root, "cursorMarker"),
                expectedValid?.ToString() ?? string.Empty, statementCount ?? -1
            ];
        }
    }

    // Unresolved Netezza template cases from this implementation remain local smoke baselines.
    // Their SQL lives only in the shared corpus; these assertions preserve the
    // current .NET parser behavior without treating it as cross-implementation truth.
    public static IEnumerable<object[]> DotnetUnverifiedTemplateBaselineCases()
    {
        foreach (var (caseJson, dialect) in LoadCases())
        {
            using var document = JsonDocument.Parse(caseJson);
            var root = document.RootElement;
            if (!ParserCategories.Contains(GetString(root, "category"))
                || GetString(root.GetProperty("verification"), "status", "source-backed") != "needsVerification"
                || !root.TryGetProperty("sources", out var sources)
                || !sources.EnumerateArray().Any(source =>
                    GetString(source, "repository") == "JustyBase.NetezzaSql"
                    && (GetString(source, "path").EndsWith("/ReferenceTemplateParserCorpusTests.cs", StringComparison.Ordinal)
                        || GetString(source, "path").EndsWith("/ReferenceInvalidTemplateParserCorpusTests.cs", StringComparison.Ordinal))))
            {
                continue;
            }

            var expected = root.GetProperty("expect");
            bool? expectedValid = expected.TryGetProperty("valid", out var valid)
                ? valid.GetBoolean()
                : expected.TryGetProperty("parserValid", out var parserValid)
                    ? parserValid.GetBoolean()
                    : null;
            if (expectedValid is null)
                continue;

            yield return
            [
                GetString(root, "id"), dialect, GetString(root, "category"),
                expectedValid.Value, GetString(root, "sql"), GetString(root, "cursorMarker")
            ];
        }
    }

    [Theory]
    [MemberData(nameof(DotnetUnverifiedTemplateBaselineCases))]
    public void Dotnet_unverified_template_cases_remain_local_smoke_baselines(
        string id,
        SqlDialect dialect,
        string category,
        bool expectedValid,
        string sourceSql,
        string cursorMarker)
    {
        var sql = RenderSql(sourceSql);
        if (cursorMarker.Length > 0)
        {
            var cursorIndex = sql.IndexOf(cursorMarker, StringComparison.Ordinal);
            Assert.True(cursorIndex >= 0, $"[{id}] cursor marker '{cursorMarker}' was not found in SQL.");
            sql = sql.Remove(cursorIndex, cursorMarker.Length);
        }

        using var runtime = new ParsingRuntime(dialect);
        var result = runtime.Parse(sql);
        Assert.True(
            result.Valid == expectedValid,
            $"[{id}][local .NET baseline/{category}] expected valid={expectedValid}, observed valid={result.Valid}; " +
            string.Join(" | ", result.Errors.Select(error => $"{error.Code}: {error.Message}")));
    }

    [Theory]
    [MemberData(nameof(SharedParserCases))]
    public void Parser_behavior_matches_the_shared_contract(
        string id,
        SqlDialect dialect,
        string category,
        string priority,
        string verification,
        string sourceSql,
        string cursorMarker,
        string expectedValidText,
        int expectedStatementCount)
    {
        var sql = RenderSql(sourceSql);
        if (cursorMarker.Length > 0)
        {
            var cursorIndex = sql.IndexOf(cursorMarker, StringComparison.Ordinal);
            Assert.True(cursorIndex >= 0, $"[{id}] cursor marker '{cursorMarker}' was not found in SQL.");
            sql = sql.Remove(cursorIndex, cursorMarker.Length);
        }

        using var runtime = new ParsingRuntime(dialect);
        var result = runtime.Parse(sql);
        bool? expectedValid = bool.TryParse(expectedValidText, out var parsedExpected) ? parsedExpected : null;

        if (expectedValid is { } valid)
        {
            Assert.True(
                result.Valid == valid,
                $"[{id}][{priority}/{verification}] {dialect} expected valid={valid}, observed valid={result.Valid}; " +
                string.Join(" | ", result.Errors.Select(error => $"{error.Code}: {error.Message}")));
        }

        if (expectedStatementCount >= 0)
            Assert.True(result.Statements.Count == expectedStatementCount, $"[{id}] expected {expectedStatementCount} statements, observed {result.Statements.Count}.");

        if (category == "recovery")
        {
            using var document = JsonDocument.Parse(FindCase(id));
            if (document.RootElement.GetProperty("expect").TryGetProperty("statementDetected", out var detected) && detected.GetBoolean())
                Assert.NotEmpty(result.Statements);
        }
    }

    [Fact]
    public void Shared_parser_contracts_have_no_unresolved_P0_cases()
    {
        var unresolved = LoadCases()
            .Where(item =>
            {
                using var document = JsonDocument.Parse(item.caseJson);
                var root = document.RootElement;
                var dialect = GetString(root, "dialect");
                var appliesToNetezza = dialect == "netezza"
                    || dialect == "common"
                    && root.TryGetProperty("appliesTo", out var appliesTo)
                    && appliesTo.EnumerateArray().Any(item => item.GetString() == "netezza");
                return appliesToNetezza
                    && ParserCategories.Contains(GetString(root, "category"))
                    && GetString(root, "priority") == "P0"
                    && GetString(root.GetProperty("verification"), "status", "source-backed") == "needsVerification";
            })
            .Select(item =>
            {
                using var document = JsonDocument.Parse(item.caseJson);
                return GetString(document.RootElement, "id");
            })
            .ToArray();

        Assert.Empty(unresolved);
    }

    private static IEnumerable<(string caseJson, SqlDialect dialect)> LoadCases()
    {
        var root = ConformanceRoot();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "dialects"), "*.jsonl", SearchOption.AllDirectories).Order())
        {
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                using var document = JsonDocument.Parse(line);
                var caseRoot = document.RootElement;
                var dialectName = GetString(caseRoot, "dialect");
                if (dialectName == "common")
                {
                    if (!caseRoot.TryGetProperty("appliesTo", out var appliesTo))
                        continue;
                    foreach (var item in appliesTo.EnumerateArray())
                    {
                        if (TryDialect(item.GetString(), out var dialect))
                            yield return (line, dialect);
                    }
                }
                else if (TryDialect(dialectName, out var dialect))
                {
                    yield return (line, dialect);
                }
            }
        }
    }

    private static string FindCase(string id)
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(ConformanceRoot(), "dialects"), "*.jsonl", SearchOption.AllDirectories))
        foreach (var line in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var document = JsonDocument.Parse(line);
            if (GetString(document.RootElement, "id") == id)
                return line;
        }
        throw new InvalidOperationException($"Shared SQL conformance case {id} was not found.");
    }

    private static bool TryDialect(string? name, out SqlDialect dialect)
    {
        if (Enum.TryParse(name, ignoreCase: true, out dialect) && Enum.IsDefined(dialect))
            return true;
        dialect = SqlDialect.Netezza;
        return false;
    }

    private static string RenderSql(string sql) => sql
        .Replace("{{proc}}", "JB_CF_PROCEDURE", StringComparison.Ordinal)
        .Replace("{{table}}", "JUST_DATA.ADMIN.JB_CF_TABLE", StringComparison.Ordinal)
        .Replace("{{table2}}", "JUST_DATA.ADMIN.JB_CF_TABLE2", StringComparison.Ordinal)
        .Replace("{{table3}}", "JUST_DATA.ADMIN.JB_CF_TABLE3", StringComparison.Ordinal);

    private static string GetString(JsonElement element, string property, string fallback = "") =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

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
