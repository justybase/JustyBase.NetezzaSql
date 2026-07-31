using System.Data;
using System.Text.RegularExpressions;

namespace JustyBase.Core.Scripting;

/// <summary>
/// Host-supplied evaluator for <c>SQL_RESULT[...]</c> / <c>SQL_RECORDS_AFFECTED[...]</c>
/// (and any other I/O-backed session var RHS). Pure expression eval stays in Core.
/// </summary>
public interface ISessionVarEvaluator
{
    ValueTask<object?> EvaluateSqlAsync(string sql, CancellationToken cancellationToken = default);
}

public sealed record LegacyDirectiveProcessResult(
    string Sql,
    IReadOnlyDictionary<string, string> KnownParameters);

public sealed record SessionVarDefinitionResult(
    string SqlWithoutDefinition,
    string VariableName,
    string EvaluatedValue,
    bool IsSession);

/// <summary>
/// Pure Legacy preprocessing helpers shared by WinForms hosts.
/// Prompting, export directives, and SpecialCommand FS I/O remain host-local.
/// </summary>
public static partial class LegacySqlDirectiveProcessor
{
    private static readonly DataTable ExpressionTable = new();
    private static readonly char[] NewLines = ['\n', '\r'];

    public static string NormalizeSleepMarkers(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return sql;

        return LegacySleepOnlyRegex().Replace(sql, match =>
            LegacyScriptDialectAdapter.IsInsideQuotedLiteral(sql, match.Index)
                ? match.Value
                : "@sleep:" + match.Groups[1].Value);
    }

    public static LegacyDirectiveProcessResult ProcessLetDirectives(
        string sql,
        IDictionary<string, string> knownParameters)
    {
        ArgumentNullException.ThrowIfNull(knownParameters);
        string trimmedSql = sql.TrimStart();
        if (!trimmedSql.StartsWith("__Let ", StringComparison.OrdinalIgnoreCase)
            && !trimmedSql.StartsWith("__LetFor ", StringComparison.OrdinalIgnoreCase))
            return new LegacyDirectiveProcessResult(sql, knownParameters.ToDictionary(static p => p.Key, static p => p.Value, StringComparer.OrdinalIgnoreCase));

        sql = trimmedSql;

        if (sql.StartsWith("__Let ", StringComparison.OrdinalIgnoreCase))
        {
            int newlineIndex = sql.IndexOfAny(NewLines);
            string directive = newlineIndex > 0
                ? sql["__Let ".Length..newlineIndex]
                : sql["__Let ".Length..];
            string[] variables = directive.Split('|');
            sql = newlineIndex > 0 ? sql[newlineIndex..] : string.Empty;

            foreach (string variable in variables)
            {
                int equalsIndex = variable.IndexOf('=');
                if (equalsIndex > 0)
                {
                    string varName = variable[..equalsIndex].Trim();
                    string varValue = variable[(equalsIndex + 1)..].Trim();
                    if (!varName.StartsWith('$'))
                        varName = '$' + varName;
                    knownParameters[varName.ToUpperInvariant()] = varValue;
                }
            }
        }
        else if (sql.Trim().StartsWith("__LetFor ", StringComparison.OrdinalIgnoreCase))
        {
            sql = sql.Trim();
            int newlineIndex = sql.IndexOfAny(NewLines);
            if (newlineIndex > 0)
            {
                string[] variables = sql["__LetFor ".Length..newlineIndex].Split('|');
                sql = sql[newlineIndex..];

                if (variables.Length >= 2)
                {
                    string varName = variables[0];
                    var sb = new System.Text.StringBuilder();
                    for (int i = 1; i < variables.Length; i++)
                    {
                        sb.Append(sql.Replace(varName, variables[i]));
                        sb.Append(';');
                    }
                    sql = sb.ToString();
                }
            }
        }

        return new LegacyDirectiveProcessResult(
            sql,
            knownParameters.ToDictionary(static p => p.Key, static p => p.Value, StringComparer.OrdinalIgnoreCase));
    }

    public static string ApplyKnownParameters(string sql, IReadOnlyDictionary<string, string> knownParameters)
    {
        if (knownParameters.Count == 0)
            return sql;

        foreach (var kvp in knownParameters.OrderByDescending(static k => k.Key.Length))
            sql = sql.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
        return sql;
    }

    public static string ReplaceDollarVariables(
        string sql,
        IReadOnlyDictionary<string, string> knownParameters,
        IReadOnlyDictionary<string, string>? extraParameters = null)
    {
        if (string.IsNullOrEmpty(sql))
            return sql;

        var parameters = new Dictionary<string, string>(knownParameters, StringComparer.OrdinalIgnoreCase);
        if (extraParameters is not null)
        {
            foreach (var kvp in extraParameters)
                parameters[kvp.Key.StartsWith('$') ? kvp.Key : '$' + kvp.Key] = kvp.Value;
        }

        if (parameters.Count == 0)
            return sql;

        foreach (var kvp in parameters.OrderByDescending(static k => k.Key.Length))
        {
            if (sql.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                sql = sql.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
        }

        return sql;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Legacy session-var RHS uses DataTable.Compute for arithmetic expressions; hosts do not trim this path.")]
    public static async ValueTask<SessionVarDefinitionResult?> TryEvaluateSessionOrGlobalDefinitionAsync(
        string sql,
        IReadOnlyDictionary<string, string> knownParameters,
        IReadOnlyDictionary<string, string> extraParameters,
        ISessionVarEvaluator? sqlEvaluator,
        CancellationToken cancellationToken = default)
    {
        Match m = SessionVarDefineRegex().Match(sql);
        Match m2 = GlobalVarDefineRegex().Match(sql);
        if (!m.Success && !m2.Success)
            return null;

        Match activeMatch = m.Success ? m : m2;
        bool isSession = m.Success;

        string variableValue = activeMatch.Groups["sessionValue"].Value;
        string name = activeMatch.Groups["sessionVar"].Value;
        string val = ReplaceDollarVariables(variableValue, knownParameters, extraParameters);

        object? evaluated = val;
        try
        {
            if (!val.StartsWith("SQL_", StringComparison.Ordinal))
            {
                evaluated = ExpressionTable.Compute(val, "");
            }
            else if (sqlEvaluator is not null)
            {
                if (val.StartsWith("SQL_RESULT[", StringComparison.Ordinal) && val.EndsWith(']'))
                {
                    string innerSql = val["SQL_RESULT[".Length..^1];
                    evaluated = await sqlEvaluator.EvaluateSqlAsync(innerSql, cancellationToken).ConfigureAwait(false);
                }
                else if (val.StartsWith("SQL_RECORDS_AFFECTED[", StringComparison.Ordinal) && val.EndsWith(']'))
                {
                    string innerSql = val["SQL_RECORDS_AFFECTED[".Length..^1];
                    evaluated = await sqlEvaluator.EvaluateSqlAsync(innerSql, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            evaluated = val;
        }

        string without = SessionVarDefineRegex().Replace(GlobalVarDefineRegex().Replace(sql, ""), "");
        return new SessionVarDefinitionResult(without, name, evaluated?.ToString() ?? string.Empty, isSession);
    }

    [GeneratedRegex(@"___sleep\s*[: ]\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacySleepOnlyRegex();

    [GeneratedRegex(@"^\s*__SessionVar__(?<sessionVar>\$\w+)\s*=\s*(?<sessionValue>.+)$", RegexOptions.Multiline)]
    private static partial Regex SessionVarDefineRegex();

    [GeneratedRegex(@"^\s*__GlobalVar__(?<sessionVar>\$\w+)\s*=\s*(?<sessionValue>.+)$", RegexOptions.Multiline)]
    private static partial Regex GlobalVarDefineRegex();
}
