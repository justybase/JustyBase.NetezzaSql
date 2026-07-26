namespace JustyBase.Core.Risk;

public enum SqlRiskKind
{
    UnsafeUpdateDelete,
    MissingDistribute,
    SelectInto
}

/// <summary>A warning that must be acknowledged before the statement is run.</summary>
public sealed record SqlRisk(
    SqlRiskKind Kind,
    string Message,
    bool IsBlocking = true);

/// <summary>
/// The single risk policy used by both UI hosts. Detection is deliberately
/// lexical so quoted values and comments cannot suppress or create a warning.
/// </summary>
public sealed partial class SqlRiskAnalysisService
{
    public IReadOnlyList<SqlRisk> Analyze(string? sql, string? driverName = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return [];

        string executable = SqlTextMasker.MaskLiteralsAndComments(sql);
        var risks = new List<SqlRisk>();

        foreach (string statement in executable.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = statement.TrimStart();
            if ((trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                 || trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
                && !WordRegex("WHERE").IsMatch(trimmed))
            {
                risks.Add(new SqlRisk(
                    SqlRiskKind.UnsafeUpdateDelete,
                    "UPDATE/DELETE without a WHERE clause may affect all rows."));
            }

            if (SelectIntoRegex().IsMatch(trimmed))
            {
                risks.Add(new SqlRisk(
                    SqlRiskKind.SelectInto,
                    "SELECT INTO may cause table distribution problems."));
            }

            if (IsNetezza(driverName)
                && CreateTableRegex().IsMatch(trimmed)
                && !WordRegex("DISTRIBUTE").IsMatch(trimmed))
            {
                risks.Add(new SqlRisk(
                    SqlRiskKind.MissingDistribute,
                    "CREATE TABLE without a DISTRIBUTE option."));
            }
        }

        return risks;
    }

    private static bool IsNetezza(string? driverName)
        => string.Equals(driverName, "NetezzaSQL", StringComparison.OrdinalIgnoreCase)
           || string.Equals(driverName, "Netezza", StringComparison.OrdinalIgnoreCase)
           || string.Equals(driverName, "JustyBase.Netezza", StringComparison.OrdinalIgnoreCase);

    private static System.Text.RegularExpressions.Regex WordRegex(string word)
        => new($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*CREATE\s+(?:TEMP(?:ORARY)?\s+)?TABLE\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex CreateTableRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\bSELECT\b[\s\S]*?\bINTO\b\s+[A-Za-z_""\[]", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SelectIntoRegex();
}

internal static class SqlTextMasker
{
    public static string MaskLiteralsAndComments(string text)
    {
        var chars = text.ToCharArray();
        bool single = false;
        bool doubleQuoted = false;
        bool lineComment = false;
        bool blockComment = false;

        for (int i = 0; i < chars.Length; i++)
        {
            char current = chars[i];
            char next = i + 1 < chars.Length ? chars[i + 1] : '\0';

            if (lineComment)
            {
                if (current is '\r' or '\n')
                    lineComment = false;
                else
                    chars[i] = ' ';
                continue;
            }

            if (blockComment)
            {
                if (current == '*' && next == '/')
                {
                    chars[i] = ' ';
                    chars[++i] = ' ';
                    blockComment = false;
                }
                else if (current is not '\r' and not '\n')
                    chars[i] = ' ';
                continue;
            }

            if (!single && !doubleQuoted && current == '-' && next == '-')
            {
                chars[i] = ' ';
                chars[++i] = ' ';
                lineComment = true;
                continue;
            }

            if (!single && !doubleQuoted && current == '/' && next == '*')
            {
                chars[i] = ' ';
                chars[++i] = ' ';
                blockComment = true;
                continue;
            }

            if (single)
            {
                chars[i] = current is '\r' or '\n' ? current : ' ';
                if (current == '\'' && next == '\'')
                {
                    chars[++i] = ' ';
                }
                else if (current == '\'')
                {
                    single = false;
                }
                continue;
            }

            if (doubleQuoted)
            {
                chars[i] = current is '\r' or '\n' ? current : ' ';
                if (current == '"')
                    doubleQuoted = false;
                continue;
            }

            if (current == '\'')
            {
                chars[i] = ' ';
                single = true;
            }
            else if (current == '"')
            {
                chars[i] = ' ';
                doubleQuoted = true;
            }
        }

        return new string(chars);
    }
}

public interface IRiskConfirmService
{
    Task<bool> ConfirmAsync(IReadOnlyList<SqlRisk> risks, CancellationToken cancellationToken = default);
}

public sealed class SqlRiskGate(SqlRiskAnalysisService analyzer)
{
    public async Task<bool> ConfirmAsync(
        string sql,
        string? driverName,
        IRiskConfirmService confirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        var risks = analyzer.Analyze(sql, driverName);
        return risks.Count == 0 || await confirmation.ConfirmAsync(risks, cancellationToken).ConfigureAwait(false);
    }
}
