using System.Text.Json;
using JustyBase.NetezzaSqlParser.Dialects;
using Xunit;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Aggregates the existing shared xUnit assertions into the neutral report format
/// and publishes a per-case applicability/execution record.
///
/// Two independent dimensions are reported:
///   * evidence level (oracleVerified / editorContract / source-backed / needsVerification)
///     travels in the row `status` vocabulary, unchanged for backwards compatibility;
///   * execution result (matches / differs / unsupported / not-executed / error)
///     travels in the per-case <c>CONFORMANCE_APPLICABILITY_JSON</c> marker, because
///     the report row schema is fixed (`additionalProperties: false`).
///
/// `needsVerification` cases are executed through the same production paths as
/// verified cases. A differing result is an execution discrepancy to adjudicate,
/// not a correctness failure, so the row keeps the `needs-verification` status.
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
        foreach (var data in SharedSqlConformanceTests.SharedParserCasesIncludingNeedsVerification())
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
                Add(outcomes, id, dialect, EvaluationFor(error));
            }
        }

        var authoringTests = new SharedSqlConformanceAuthoringTests();
        foreach (var data in SharedSqlConformanceAuthoringTests.SharedCasesIncludingNeedsVerification())
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
                Add(outcomes, id, dialect, EvaluationFor(error));
            }
        }

        var cases = LoadCases().ToArray();
        var rows = cases.Select(testCase => BuildRow(testCase, outcomes)).ToArray();

        var grouped = cases
            .Select((testCase, index) => (testCase, row: rows[index].Row))
            .GroupBy(entry => entry.testCase.Dialect, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ReportDocument
            {
                Implementation = Implementation,
                Dialect = group.Key,
                Results = group.Select(entry => entry.row).ToArray()
            })
            .ToArray();

        Console.WriteLine(
            "CONFORMANCE_REPORTS_JSON=" +
            JsonSerializer.Serialize(
                grouped,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Console.WriteLine(
            "CONFORMANCE_APPLICABILITY_JSON=" +
            JsonSerializer.Serialize(
                rows.Select(row => row.Record).ToArray(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static (ReportResult Row, ApplicabilityRecord Record) BuildRow(
        CorpusCase testCase,
        Dictionary<(string Id, SqlDialect Dialect), List<Evaluation>> outcomes)
    {
        var classification = SharedSqlConformanceApplicability.Classify(ToDescriptor(testCase));
        var applicability = classification.Applicability;
        List<Evaluation>? evaluations = null;
        if (applicability == SharedSqlConformanceApplicability.Applicable
            && Enum.TryParse<SqlDialect>(classification.ExecutionDialect, ignoreCase: true, out var dialect)
            && outcomes.TryGetValue((testCase.Id, dialect), out var found))
        {
            evaluations = found;
        }

        string status;
        string message;
        bool? matches = null;
        var infrastructureError = false;
        if (applicability != SharedSqlConformanceApplicability.Applicable)
        {
            status = "not-applicable";
            message = classification.Reason;
        }
        else if (evaluations is { Count: > 0 })
        {
            matches = evaluations.All(evaluation => evaluation.Passed);
            infrastructureError = evaluations.Any(evaluation => evaluation.InfrastructureError);
            if (testCase.VerificationStatus == "needsVerification")
            {
                status = "needs-verification";
                message = matches.Value
                    ? "expectation still requires verification; observed behavior matches the recorded expectation"
                    : "expectation still requires verification; observed behavior differs"
                      + (testCase.DeclaredDivergence ? " (declared divergence)" : "")
                      + ": " + FailureText(evaluations);
            }
            else
            {
                status = matches.Value ? "pass" : "fail";
                message = matches.Value ? "" : FailureText(evaluations);
            }
        }
        else
        {
            status = "fail";
            message = "No shared production conformance evaluator covered this applicable case.";
        }

        var executionResult = SharedSqlConformanceApplicability.ExecutionResultFor(
            applicability, matches, infrastructureError);
        var reason = applicability == SharedSqlConformanceApplicability.Applicable
            ? (matches == false ? FailureText(evaluations!) : "")
            : classification.Reason;

        var row = new ReportResult
        {
            Id = testCase.Id,
            Category = testCase.Category,
            Contract = testCase.Contract,
            Area = testCase.Area,
            Priority = testCase.Priority,
            Status = status,
            Message = message
        };
        var record = new ApplicabilityRecord
        {
            Id = testCase.Id,
            Dialect = testCase.Dialect,
            Contract = testCase.Contract,
            Priority = testCase.Priority,
            Status = status,
            Applicability = applicability,
            ExecutionDialect = classification.ExecutionDialect,
            ExecutionResult = executionResult,
            EvidenceLevel = testCase.VerificationStatus,
            DeclaredDivergence = testCase.DeclaredDivergence,
            Reason = reason
        };
        return (row, record);
    }

    private static string FailureText(IEnumerable<Evaluation> evaluations) =>
        string.Join(" | ", evaluations
            .Where(evaluation => !evaluation.Passed)
            .Select(evaluation => evaluation.Message)
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>
    /// Assertion failures are behavioural differences; anything else is an
    /// infrastructure error and must not be reported as a plain expectation
    /// mismatch.
    /// </summary>
    private static Evaluation EvaluationFor(Exception error) =>
        error is Xunit.Sdk.XunitException || error.GetType().FullName?.StartsWith("Xunit.Sdk.", StringComparison.Ordinal) == true
            ? new Evaluation(false, error.Message)
            : new Evaluation(false, $"{error.GetType().Name}: {error.Message}", InfrastructureError: true);

    private static SharedSqlConformanceApplicability.CorpusCaseDescriptor ToDescriptor(CorpusCase testCase) =>
        new(testCase.Id, testCase.Category, testCase.Contract, testCase.Priority,
            testCase.Dialect, testCase.AppliesTo, testCase.VerificationStatus, testCase.DeclaredDivergence);

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
                var declaredDivergence = item.TryGetProperty("knownDivergence", out var divergences)
                    && divergences.EnumerateArray().Any(divergence =>
                        GetString(divergence, "repository") == Implementation);
                yield return new CorpusCase(
                    GetString(item, "id"),
                    GetString(item, "category"),
                    GetString(item, "contract"),
                    GetString(item, "area"),
                    GetString(item, "priority"),
                    GetString(item, "dialect"),
                    appliesTo,
                    verification,
                    declaredDivergence);
            }
        }
    }

    private static string GetString(JsonElement element, string property, string fallback = "") =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static string ConformanceRoot()
    {
        // The local JustyBase.SqlConformance checkout selected by
        // JUSTYBASE_SQL_CONFORMANCE_PATH is the only corpus source.
        // There is intentionally no fallback to vendored fixtures.
        var configured = Environment.GetEnvironmentVariable("JUSTYBASE_SQL_CONFORMANCE_PATH");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(SharedSqlConformanceTests.MissingRepoMessage);
        var full = Path.GetFullPath(configured);
        if (Directory.Exists(Path.Combine(full, "dialects")))
            return full;
        throw new DirectoryNotFoundException(
            $"JUSTYBASE_SQL_CONFORMANCE_PATH does not contain a SQL conformance corpus (missing dialects/): {full}");
    }

    private sealed record CorpusCase(
        string Id,
        string Category,
        string Contract,
        string Area,
        string Priority,
        string Dialect,
        string[] AppliesTo,
        string VerificationStatus,
        bool DeclaredDivergence);

    private sealed record Evaluation(bool Passed, string Message, bool InfrastructureError = false)
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

    private sealed class ApplicabilityRecord
    {
        public string Id { get; init; } = "";
        public string Dialect { get; init; } = "";
        public string Contract { get; init; } = "";
        public string Priority { get; init; } = "";
        public string Status { get; init; } = "";
        public string Applicability { get; init; } = "";
        public string ExecutionDialect { get; init; } = "";
        public string ExecutionResult { get; init; } = "";
        public string EvidenceLevel { get; init; } = "";
        public bool DeclaredDivergence { get; init; }
        public string Reason { get; init; } = "";
    }
}
