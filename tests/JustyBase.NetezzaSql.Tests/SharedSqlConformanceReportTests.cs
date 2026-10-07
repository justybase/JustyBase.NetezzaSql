using System.Text.Json;
using JustyBase.NetezzaSqlParser.Dialects;
using Xunit;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Aggregates the existing shared xUnit assertions into the neutral report format.
/// All checks still call the production parser and authoring services through the
/// established shared conformance tests; this class only maps outcomes to JSON rows.
/// </summary>
[Trait("Category", "SqlConformance")]
public sealed class SharedSqlConformanceReportTests
{
    private const string EmitVariable = "JUSTYBASE_EMIT_CONFORMANCE_REPORT";
    private const string Implementation = "JustyBase.NetezzaSql";

    [Fact]
    public async Task Emit_report_when_requested()
    {
        if (Environment.GetEnvironmentVariable(EmitVariable) != "1")
            return;

        var outcomes = new Dictionary<(string Id, SqlDialect Dialect), List<Evaluation>>();
        var parserTests = new SharedSqlConformanceTests();
        foreach (var data in SharedSqlConformanceTests.SharedParserCases())
        {
            var id = (string)data[0];
            var dialect = (SqlDialect)data[1];
            try
            {
                parserTests.Parser_behavior_matches_the_shared_contract(
                    id, dialect, (string)data[2], (string)data[3], (string)data[4],
                    (string)data[5], (string)data[6], (string)data[7], (int)data[8]);
                Add(outcomes, id, dialect, Evaluation.Pass);
            }
            catch (Exception error)
            {
                Add(outcomes, id, dialect, new Evaluation(false, error.Message));
            }
        }

        var authoringTests = new SharedSqlConformanceAuthoringTests();
        foreach (var data in SharedSqlConformanceAuthoringTests.SharedCases())
        {
            var id = (string)data[0];
            var dialect = (SqlDialect)data[2];
            try
            {
                await authoringTests.Shared_authoring_behavior_matches_the_contract(
                    id, (string)data[1], dialect);
                Add(outcomes, id, dialect, Evaluation.Pass);
            }
            catch (Exception error)
            {
                Add(outcomes, id, dialect, new Evaluation(false, error.Message));
            }
        }

        var grouped = LoadCases()
            .GroupBy(testCase => testCase.Dialect, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ReportDocument
            {
                Implementation = Implementation,
                Dialect = group.Key,
                Results = group.Select(testCase => ToReportRow(testCase, outcomes)).ToArray()
            })
            .ToArray();

        Console.WriteLine(
            "CONFORMANCE_REPORTS_JSON=" +
            JsonSerializer.Serialize(
                grouped,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static ReportResult ToReportRow(
        CorpusCase testCase,
        Dictionary<(string Id, SqlDialect Dialect), List<Evaluation>> outcomes)
    {
        string status;
        string message;
        if (testCase.Dialect is not "netezza" and not "common"
            || testCase.Dialect == "common" && !testCase.AppliesTo.Contains("netezza", StringComparer.Ordinal))
        {
            status = "not-applicable";
            message = "The .NET adapter currently covers Netezza and common cases applicable to Netezza.";
        }
        else if (testCase.VerificationStatus == "needsVerification")
        {
            status = "needs-verification";
            message = "The corpus marks this case as needing verification.";
        }
        else if (Enum.TryParse<SqlDialect>("Netezza", ignoreCase: true, out var netezza)
            && outcomes.TryGetValue((testCase.Id, netezza), out var evaluations)
            && evaluations.Count > 0)
        {
            status = evaluations.All(evaluation => evaluation.Passed) ? "pass" : "fail";
            message = string.Join(" | ", evaluations
                .Where(evaluation => !evaluation.Passed)
                .Select(evaluation => evaluation.Message)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        else
        {
            status = "fail";
            message = "No shared production conformance evaluator covered this applicable case.";
        }

        return new ReportResult
        {
            Id = testCase.Id,
            Category = testCase.Category,
            Contract = testCase.Contract,
            Area = testCase.Area,
            Priority = testCase.Priority,
            Status = status,
            Message = message
        };
    }

    private static void Add(
        Dictionary<(string Id, SqlDialect Dialect), List<Evaluation>> outcomes,
        string id,
        SqlDialect dialect,
        Evaluation evaluation)
    {
        var key = (id, dialect);
        if (!outcomes.TryGetValue(key, out var values))
            outcomes[key] = values = [];
        values.Add(evaluation);
    }

    private static IEnumerable<CorpusCase> LoadCases()
    {
        var root = ConformanceRoot();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "dialects"), "*.jsonl", SearchOption.AllDirectories)
                 .Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                using var document = JsonDocument.Parse(line);
                var item = document.RootElement;
                var verification = item.TryGetProperty("verification", out var value)
                    ? GetString(value, "status", "source-backed")
                    : "source-backed";
                var appliesTo = item.TryGetProperty("appliesTo", out var applies)
                    ? applies.EnumerateArray().Select(entry => entry.GetString() ?? "").ToArray()
                    : Array.Empty<string>();
                yield return new CorpusCase(
                    GetString(item, "id"),
                    GetString(item, "category"),
                    GetString(item, "contract"),
                    GetString(item, "area"),
                    GetString(item, "priority"),
                    GetString(item, "dialect"),
                    appliesTo,
                    verification);
            }
        }
    }

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
            throw new DirectoryNotFoundException($"JUSTYBASE_SQL_CONFORMANCE_PATH has no dialects/: {full}");
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

        throw new DirectoryNotFoundException("Set JUSTYBASE_SQL_CONFORMANCE_PATH to the local JustyBase.SqlConformance repository.");
    }

    private sealed record CorpusCase(
        string Id,
        string Category,
        string Contract,
        string Area,
        string Priority,
        string Dialect,
        string[] AppliesTo,
        string VerificationStatus);

    private sealed record Evaluation(bool Passed, string Message)
    {
        public static Evaluation Pass { get; } = new(true, "");
    }

    private sealed class ReportDocument
    {
        public string Implementation { get; init; } = "";
        public string Dialect { get; init; } = "";
        public ReportResult[] Results { get; init; } = [];
    }

    private sealed class ReportResult
    {
        public string Id { get; init; } = "";
        public string Category { get; init; } = "";
        public string Contract { get; init; } = "";
        public string Area { get; init; } = "";
        public string Priority { get; init; } = "";
        public string Status { get; init; } = "";
        public string Message { get; init; } = "";
    }
}
