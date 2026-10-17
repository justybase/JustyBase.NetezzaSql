using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Product/adapter capability model and per-case applicability classification for
/// the shared conformance corpus.
///
/// The vocabulary mirrors the Rust conformance adapter so all three consumers
/// report the same semantics:
///   applicable, explicit-not-applicable, unsupported-dialect,
///   unsupported-contract, missing-capability, adapter-gap, unknown.
///
/// Classification order follows the Rust model: resolve the `common` dialect
/// context from `appliesTo` first, then reject unknown dialects, then unsupported
/// contracts, then applicable. `adapter-gap` is reserved for a contract the
/// product implements but the conformance adapter cannot execute; this suite has
/// an evaluator for every category it classifies, so it stays at zero unless a
/// future category lands without one.
/// </summary>
internal static class SharedSqlConformanceApplicability
{
    internal const string Applicable = "applicable";
    internal const string ExplicitNotApplicable = "explicit-not-applicable";
    internal const string UnsupportedDialect = "unsupported-dialect";
    internal const string UnsupportedContract = "unsupported-contract";
    internal const string MissingCapability = "missing-capability";
    internal const string AdapterGap = "adapter-gap";
    internal const string Unknown = "unknown";

    /// <summary>Categories executed through <c>ParsingRuntime</c>.</summary>
    private static readonly HashSet<string> ParserCategories = new(StringComparer.Ordinal)
    {
        "parser", "syntax", "ddl", "dml", "functions", "nzplsql", "recovery", "statement"
    };

    /// <summary>Categories executed through the authoring services.</summary>
    private static readonly HashSet<string> AuthoringCategories = new(StringComparer.Ordinal)
    {
        "completion", "diagnostics", "semantic", "quick-fixes", "signature",
        "formatting", "hover", "recovery"
    };

    /// <summary>
    /// Dialects the .NET library implements, in `common` resolution preference
    /// order: Netezza is the primary implementation and MSSQL is the preferred
    /// fallback for the T-SQL flavoured `common` cases (`appliesTo=[db2,mssql]`).
    /// </summary>
    private static readonly string[] ProductDialects =
    {
        "netezza", "mssql", "postgresql", "sqlite", "oracle", "db2", "mysql", "access"
    };

    internal static IReadOnlyList<string> ProductDialectNames => ProductDialects;

    internal static bool IsProductDialect(string dialect) =>
        ProductDialects.Contains(dialect, StringComparer.Ordinal);

    /// <summary>Contracts executed by this suite, keyed by corpus category.</summary>
    internal static bool SupportsCategory(string category) =>
        ParserCategories.Contains(category) || AuthoringCategories.Contains(category);

    /// <summary>
    /// Language capabilities this conformance adapter declares per dialect, used
    /// to honour `requiresCapabilities`. Only capabilities whose absence the
    /// corpus exercises today are listed; anything unknown is treated as not
    /// declared so the gap stays visible as missing-capability debt instead of a
    /// silently skipped or failed case. Netezza is the deep dialect and declares
    /// every capability.
    /// </summary>
    internal static bool DeclaresCapability(string dialect, string capability)
    {
        if (string.Equals(dialect, "netezza", StringComparison.Ordinal))
            return true;
        if (string.Equals(dialect, "sqlite", StringComparison.Ordinal))
        {
            // The .NET SQLite surface does not implement UPDATE/DELETE
            // ORDER BY ... LIMIT (SQLITE_ENABLE_UPDATE_DELETE_LIMIT syntax).
            return capability switch
            {
                "triggerBodies" => true,
                _ => false,
            };
        }
        return false;
    }

    /// <summary>
    /// Required capabilities the resolved execution dialect does not declare.
    /// Cases with missing capabilities are not executed: their gap is recorded
    /// as missing-capability parity debt instead of an assertion failure.
    /// </summary>
    internal static IReadOnlyList<string> MissingCapabilities(
        string dialect, IReadOnlyList<string> requiresCapabilities, IReadOnlyList<string> appliesTo)
    {
        if (requiresCapabilities.Count == 0)
            return Array.Empty<string>();
        var executionDialect = ResolveExecutionDialect(dialect, appliesTo);
        if (executionDialect.Length == 0)
            return Array.Empty<string>();
        return requiresCapabilities
            .Where(capability => !DeclaresCapability(executionDialect, capability))
            .ToArray();
    }

    /// <summary>
    /// Resolves the dialect a case executes in. Non-common cases keep their own
    /// dialect; `common` cases resolve to the first preferred product dialect
    /// named by `appliesTo`.
    /// </summary>
    internal static string ResolveExecutionDialect(string dialect, IReadOnlyList<string> appliesTo)
    {
        if (!string.Equals(dialect, "common", StringComparison.Ordinal))
            return dialect;
        foreach (var candidate in ProductDialects)
        {
            if (appliesTo.Contains(candidate, StringComparer.Ordinal))
                return candidate;
        }
        foreach (var candidate in appliesTo)
        {
            if (IsProductDialect(candidate))
                return candidate;
        }
        return string.Empty;
    }

    internal static CaseClassification Classify(CorpusCaseDescriptor testCase)
    {
        var executionDialect = ResolveExecutionDialect(testCase.Dialect, testCase.AppliesTo);

        if (string.Equals(testCase.Dialect, "common", StringComparison.Ordinal)
            && executionDialect.Length == 0)
        {
            var described = testCase.AppliesTo.Count == 0
                ? "no appliesTo dialects"
                : $"appliesTo [{string.Join(", ", testCase.AppliesTo)}]";
            if (testCase.AppliesTo.Count > 0
                && testCase.AppliesTo.All(candidate => !IsProductDialect(candidate)))
            {
                return new CaseClassification(
                    UnsupportedDialect, testCase.Dialect,
                    $"common case requires a dialect the .NET library does not implement ({described})");
            }
            return new CaseClassification(
                ExplicitNotApplicable, testCase.Dialect,
                $"common case {described} matches no .NET dialect");
        }

        if (executionDialect.Length == 0 || !IsProductDialect(executionDialect))
        {
            return new CaseClassification(
                UnsupportedDialect, executionDialect.Length == 0 ? testCase.Dialect : executionDialect,
                $"the .NET library has no support for dialect `{executionDialect}`");
        }

        var missing = testCase.RequiresCapabilities
            .Where(capability => !DeclaresCapability(executionDialect, capability))
            .ToArray();
        if (missing.Length > 0)
        {
            return new CaseClassification(
                MissingCapability, executionDialect,
                $"dialect `{executionDialect}` does not declare [{string.Join(", ", missing)}]");
        }

        if (!SupportsCategory(testCase.Category))
        {
            return new CaseClassification(
                UnsupportedContract, executionDialect,
                $"the .NET library does not implement `{testCase.Contract}` for dialect `{executionDialect}`");
        }

        return new CaseClassification(Applicable, executionDialect, string.Empty);
    }

    /// <summary>
    /// Execution outcome vocabulary shared by the marker records:
    /// matches, differs, unsupported, not-executed, error.
    /// </summary>
    internal static string ExecutionResultFor(string applicability, bool? matches, bool infrastructureError)
    {
        switch (applicability)
        {
            case UnsupportedContract:
            case UnsupportedDialect:
            case MissingCapability:
            case AdapterGap:
            case Unknown:
                return "unsupported";
            case ExplicitNotApplicable:
                return "not-executed";
            default:
                if (matches is null)
                    return "not-executed";
                if (matches.Value)
                    return "matches";
                return infrastructureError ? "error" : "differs";
        }
    }

    internal sealed record CaseClassification(
        string Applicability,
        string ExecutionDialect,
        string Reason);

    /// <summary>Minimal case shape needed for classification.</summary>
    internal sealed record CorpusCaseDescriptor(
        string Id,
        string Category,
        string Contract,
        string Priority,
        string Dialect,
        IReadOnlyList<string> AppliesTo,
        IReadOnlyList<string> RequiresCapabilities,
        string VerificationStatus,
        bool DeclaredDivergence);
}
