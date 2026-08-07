using System.Text.RegularExpressions;

namespace JustyBase.Ai.Services;

public static class CopilotSqlAssistantAnalyzer
{
    private static readonly Regex SelectStarRegex = new(@"\bSELECT\s+\*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OrderByRegex = new(@"\bORDER\s+BY\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LimitRegex = new(@"\bLIMIT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DistinctRegex = new(@"\bDISTINCT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JoinRegex = new(@"\bJOIN\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JoinOnRegex = new(@"\bJOIN\b[\s\S]{0,120}?\bON\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LeadingWildcardLikeRegex = new(@"\bLIKE\s+'%[^']*'", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DmlWithoutWhereRegex = new(@"\b(UPDATE|DELETE)\b(?![\s\S]*\bWHERE\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CtasWithoutDistributeRegex = new(@"\bCREATE\s+TABLE\b(?![\s\S]*\bDISTRIBUTE\s+ON\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExplainRegex = new(@"\bEXPLAIN\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> BuildNetezzaOptimizationHints(string sqlText)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return ["Provide SQL text to get optimization hints."];
        }

        var hints = new List<string>();

        if (SelectStarRegex.IsMatch(sqlText))
        {
            hints.Add("Avoid SELECT * in production queries; project only required columns to reduce I/O.");
        }

        if (OrderByRegex.IsMatch(sqlText) && !LimitRegex.IsMatch(sqlText))
        {
            hints.Add("ORDER BY without LIMIT may cause expensive global sorts on Netezza.");
        }

        if (DistinctRegex.IsMatch(sqlText))
        {
            hints.Add("DISTINCT can be expensive; validate whether GROUP BY or pre-aggregation is more selective.");
        }

        if (JoinRegex.IsMatch(sqlText) && !JoinOnRegex.IsMatch(sqlText))
        {
            hints.Add("Detected JOIN usage without a nearby ON predicate; verify join conditions to avoid Cartesian products.");
        }

        if (LeadingWildcardLikeRegex.IsMatch(sqlText))
        {
            hints.Add("LIKE with a leading wildcard (%term) limits zone-map pruning and can force broad scans.");
        }

        if (DmlWithoutWhereRegex.IsMatch(sqlText))
        {
            hints.Add("UPDATE/DELETE without WHERE affects all rows; validate scope before execution.");
        }

        if (CtasWithoutDistributeRegex.IsMatch(sqlText))
        {
            hints.Add("CREATE TABLE should define DISTRIBUTE ON (...) or DISTRIBUTE ON RANDOM explicitly.");
        }

        if (!ExplainRegex.IsMatch(sqlText))
        {
            hints.Add("Use EXPLAIN to verify distribution, data movement, and join strategy before final execution.");
        }

        if (hints.Count == 0)
        {
            hints.Add("No obvious anti-patterns detected. Validate plan with EXPLAIN and confirm distribution/organize keys.");
        }

        return hints;
    }

    public static bool IsLikelyQuery(string sqlText)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return false;
        }

        var trimmed = sqlText.TrimStart();
        return trimmed.StartsWith("select", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("with", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("show", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("explain", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("values", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("table", StringComparison.OrdinalIgnoreCase);
    }

    public static (string? Database, string? Schema, string ObjectName) ParseQualifiedName(string rawName)
    {
        var input = rawName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return (null, null, string.Empty);
        }

        var parts = input.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            >= 3 => (parts[^3], parts[^2], parts[^1]),
            2 => (null, parts[0], parts[1]),
            _ => (null, null, parts[0]),
        };
    }

    public static string FormatCellValue(object? value, int maxLength = 120)
    {
        if (value is null || value == DBNull.Value)
        {
            return "NULL";
        }

        var text = value.ToString() ?? string.Empty;
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }
}
