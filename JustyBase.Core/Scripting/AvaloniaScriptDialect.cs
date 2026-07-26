using System.Text.RegularExpressions;

namespace JustyBase.Core.Scripting;

public sealed record ScriptPreprocessRequest(
    string SqlText,
    IReadOnlyDictionary<string, string>? Variables = null,
    bool NormalizeLegacyDirectives = false);

public sealed record ScriptPreprocessResult(
    string ProcessedSql,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<TimeSpan> Delays,
    IReadOnlyList<string> Messages);

/// <summary>The canonical JustyBase scripting dialect used by Avalonia.</summary>
public sealed partial class AvaloniaScriptDialect
{
    public ScriptPreprocessResult Process(ScriptPreprocessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Variables is not null)
        {
            foreach (var pair in request.Variables)
                variables[pair.Key.TrimStart('&')] = pair.Value;
        }

        string text = request.NormalizeLegacyDirectives
            ? LegacyScriptDialectAdapter.Normalize(request.SqlText)
            : request.SqlText;
        var delays = new List<TimeSpan>();
        var messages = new List<string>();

        text = LetRegex().Replace(text, match =>
        {
            variables[match.Groups["name"].Value] = match.Groups["value"].Value.Trim();
            return string.Empty;
        });

        text = DeclareRegex().Replace(text, match =>
        {
            variables[match.Groups["name"].Value] = match.Groups["value"].Value.Trim();
            return string.Empty;
        });

        text = SleepRegex().Replace(text, match =>
        {
            if (int.TryParse(match.Groups["milliseconds"].Value, out int milliseconds) && milliseconds >= 0)
                delays.Add(TimeSpan.FromMilliseconds(milliseconds));
            else
                messages.Add($"Invalid sleep directive: {match.Value.Trim()}");
            return string.Empty;
        });

        text = ReplaceVariables(text, variables);

        return new ScriptPreprocessResult(
            text.Trim(),
            new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase),
            delays,
            messages);
    }

    private static string ReplaceVariables(string text, IReadOnlyDictionary<string, string> variables)
    {
        var result = new System.Text.StringBuilder(text.Length);
        bool singleQuoted = false;
        bool doubleQuoted = false;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (current == '\'' && !doubleQuoted)
            {
                if (singleQuoted && index + 1 < text.Length && text[index + 1] == '\'')
                {
                    result.Append("''");
                    index++;
                }
                else
                {
                    singleQuoted = !singleQuoted;
                    result.Append(current);
                }
                continue;
            }
            if (current == '"' && !singleQuoted)
            {
                doubleQuoted = !doubleQuoted;
                result.Append(current);
                continue;
            }
            if (current == '&' && index + 1 < text.Length)
            {
                int end = index + 1;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                    end++;
                string name = text[(index + 1)..end];
                if (variables.TryGetValue(name, out string? value))
                {
                    if (singleQuoted && value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
                        result.Append(value[1..^1]);
                    else
                        result.Append(value);
                    index = end - 1;
                    continue;
                }
            }
            result.Append(current);
        }
        return result.ToString();
    }

    [GeneratedRegex(@"^\s*%let\s+(?<name>[A-Za-z_]\w*)\s*=?\s*(?<value>[^;\r\n]*);?", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LetRegex();

    [GeneratedRegex(@"^\s*declare\s+&(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>[^;\r\n]*);?", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeclareRegex();

    [GeneratedRegex(@"^\s*@sleep\s*:\s*(?<milliseconds>\d+)\s*;?", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SleepRegex();
}

public static partial class LegacyScriptDialectAdapter
{
    public static string Normalize(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return sql;

        sql = LegacySleepRegex().Replace(sql, match =>
            IsInsideQuotedLiteral(sql, match.Index)
                ? match.Value
                : "@sleep:" + match.Groups[1].Value);
        sql = LegacySessionRegex().Replace(sql, "declare &$1=$2");
        sql = LegacyGlobalRegex().Replace(sql, "declare &$1=$2");
        return sql;
    }

    private static bool IsInsideQuotedLiteral(string text, int position)
    {
        if (position <= 0 || position >= text.Length)
            return false;

        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < position; i++)
        {
            char c = text[i];

            if (inLineComment)
            {
                if (c is '\n' or '\r')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && i + 1 < position && text[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (inSingleQuote)
            {
                if (c == '\'')
                {
                    if (i + 1 < position && text[i + 1] == '\'')
                        i++;
                    else
                        inSingleQuote = false;
                }
                continue;
            }

            if (inDoubleQuote)
            {
                if (c == '"')
                    inDoubleQuote = false;
                continue;
            }

            if (c == '-' && i + 1 < position && text[i + 1] == '-')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && i + 1 < position && text[i + 1] == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c == '\'')
                inSingleQuote = true;
            else if (c == '"')
                inDoubleQuote = true;
        }

        return inSingleQuote || inDoubleQuote || inLineComment || inBlockComment;
    }

    [GeneratedRegex(@"___sleep\s*[: ]\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacySleepRegex();

    [GeneratedRegex(@"__SessionVar__\s*\$?([A-Za-z_]\w*)\s*=\s*([^\r\n;]+);?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacySessionRegex();

    [GeneratedRegex(@"__GlobalVar__\s*\$?([A-Za-z_]\w*)\s*=\s*([^\r\n;]+);?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyGlobalRegex();
}
