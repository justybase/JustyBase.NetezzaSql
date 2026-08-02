using System.Text.RegularExpressions;

namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>
/// Scanner switches used by the small, dialect-neutral quality rules.
/// Dialects can opt into their identifier and statement-boundary conventions
/// without copying the rule implementation.
/// </summary>
public sealed record SqlLintScannerOptions(
    bool AllowQuotedIdentifiers = true,
    bool AllowBracketedIdentifiers = true,
    bool HandleQQuotedStrings = false,
    Func<string, int, int>? StatementEnd = null);

/// <summary>
/// Creates the quality rules shared by ANSI-derived dialects. The factory
/// owns the rule mechanics; dialect wrappers supply only stable metadata and
/// scanner options.
/// </summary>
public static class SharedQualityRuleFactory
{
    public static LintRule CreateSelectStar(
        string id,
        string name,
        string description,
        LintSeverity defaultSeverity,
        SqlLintScannerOptions? options = null)
    {
        IEnumerable<LintIssue> Check(string sql)
        {
            foreach (Match match in Regex.Matches(sql, @"\bSELECT\s+(\*)", RegexOptions.IgnoreCase))
            {
                if (IsIgnored(sql, match.Index, options))
                    continue;

                var star = match.Groups[1];
                yield return new LintIssue(id, $"{id}: {description}", defaultSeverity,
                    star.Index, star.Index + star.Length);
            }
        }

        return Create(id, name, description, defaultSeverity, Check);
    }

    public static LintRule CreateDeleteWithoutWhere(
        string id,
        string name,
        string description,
        LintSeverity defaultSeverity,
        SqlLintScannerOptions? options = null)
    {
        options ??= new SqlLintScannerOptions();
        var identifier = IdentifierPattern(options);
        var pattern = $@"\bDELETE\s+FROM\s+{identifier}(?:\s*\.\s*{identifier}){{0,2}}";
        IEnumerable<LintIssue> Check(string sql)
        {
            foreach (Match match in Regex.Matches(sql, pattern, RegexOptions.IgnoreCase))
            {
                if (IsIgnored(sql, match.Index, options))
                    continue;

                var end = EndOfStatement(sql, match.Index, options);
                var tail = sql[(match.Index + match.Length)..end];
                if (!Regex.IsMatch(tail, @"\bWHERE\b", RegexOptions.IgnoreCase))
                    yield return new LintIssue(id, $"{id}: {description}", defaultSeverity,
                        match.Index, match.Index + 6);
            }
        }

        return Create(id, name, description, defaultSeverity, Check);
    }

    public static LintRule CreateUpdateWithoutWhere(
        string id,
        string name,
        string description,
        LintSeverity defaultSeverity,
        SqlLintScannerOptions? options = null)
    {
        options ??= new SqlLintScannerOptions();
        var identifier = IdentifierPattern(options);
        var pattern = $@"\bUPDATE\s+{identifier}(?:\s*\.\s*{identifier}){{0,2}}\s+SET\b";
        IEnumerable<LintIssue> Check(string sql)
        {
            foreach (Match match in Regex.Matches(sql, pattern, RegexOptions.IgnoreCase))
            {
                if (IsIgnored(sql, match.Index, options))
                    continue;

                var end = EndOfStatement(sql, match.Index, options);
                var tail = sql[(match.Index + match.Length)..end];
                if (!Regex.IsMatch(tail, @"\bWHERE\b", RegexOptions.IgnoreCase))
                    yield return new LintIssue(id, $"{id}: {description}", defaultSeverity,
                        match.Index, match.Index + 6);
            }
        }

        return Create(id, name, description, defaultSeverity, Check);
    }

    public static LintRule CreateDoubleDotTable(
        string id,
        string name,
        string description,
        LintSeverity defaultSeverity,
        SqlLintScannerOptions? options = null)
    {
        options ??= new SqlLintScannerOptions(AllowQuotedIdentifiers: false, AllowBracketedIdentifiers: false);
        var identifier = IdentifierPattern(options);
        var pattern = $@"{identifier}\s*\.\s*\.\s*{identifier}";
        IEnumerable<LintIssue> Check(string sql)
        {
            foreach (Match match in Regex.Matches(sql, pattern, RegexOptions.IgnoreCase))
            {
                if (IsIgnored(sql, match.Index, options))
                    continue;

                yield return new LintIssue(id, $"{id}: {description}", defaultSeverity,
                    match.Index, match.Index + match.Length);
            }
        }

        return Create(id, name, description, defaultSeverity, Check);
    }

    private static LintRule Create(
        string id,
        string name,
        string description,
        LintSeverity defaultSeverity,
        Func<string, IEnumerable<LintIssue>> check)
        => new DelegateLintRule(id, name, description, defaultSeverity, check);

    private static string IdentifierPattern(SqlLintScannerOptions options)
    {
        var alternatives = new List<string> { @"[A-Za-z_][\w$#]*" };
        if (options.AllowQuotedIdentifiers)
            alternatives.Add(@"""[^""\r\n]+""");
        if (options.AllowBracketedIdentifiers)
            alternatives.Add(@"\[[^\]\r\n]+\]");
        return $"(?:{string.Join("|", alternatives)})";
    }

    private static bool IsIgnored(string sql, int index, SqlLintScannerOptions? options)
        => LintHelpers.IsInsideStringOrComment(sql, index);

    private static int EndOfStatement(string sql, int start, SqlLintScannerOptions options)
        => options.StatementEnd?.Invoke(sql, start)
            ?? DefaultStatementEnd(sql, start, options.HandleQQuotedStrings);

    private static int DefaultStatementEnd(string sql, int start, bool handleQQuotes)
    {
        char? quote = null;
        char? qDelimiter = null;
        var lineComment = false;
        var blockComment = false;

        static char ClosingDelimiter(char opening) => opening switch
        {
            '[' => ']',
            '{' => '}',
            '(' => ')',
            '<' => '>',
            _ => opening,
        };

        for (var index = start; index < sql.Length; index++)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (lineComment)
            {
                if (current is '\n' or '\r') lineComment = false;
                continue;
            }
            if (blockComment)
            {
                if (current == '*' && next == '/') { blockComment = false; index++; }
                continue;
            }
            if (qDelimiter is not null)
            {
                if (current == ClosingDelimiter(qDelimiter.Value) && next == '\'')
                {
                    qDelimiter = null;
                    index++;
                }
                continue;
            }
            if (quote is not null)
            {
                if (current == quote && next == quote) index++;
                else if (current == quote) quote = null;
                continue;
            }
            if (current == '-' && next == '-') { lineComment = true; index++; continue; }
            if (current == '/' && next == '*') { blockComment = true; index++; continue; }
            if (handleQQuotes && current is ('q' or 'Q') && next == '\'' && index + 2 < sql.Length)
            {
                qDelimiter = sql[index + 2];
                index += 2;
                continue;
            }
            if (current is '\'' or '"') { quote = current; continue; }
            if (current == ';') return index;
        }

        return sql.Length;
    }

    private sealed class DelegateLintRule(
        string id,
        string name,
        string description,
        LintSeverity defaultSeverity,
        Func<string, IEnumerable<LintIssue>> check) : LintRule
    {
        public override string Id => id;
        public override string Name => name;
        public override string Description => description;
        public override LintSeverity DefaultSeverity => defaultSeverity;
        public override RuleCost Cost => RuleCost.Cheap;
        public override IEnumerable<LintIssue> Check(string sql) => check(sql);
    }
}

/// <summary>Small compatibility wrapper for public dialect rule types.</summary>
public abstract class DelegatingLintRule : LintRule
{
    private readonly LintRule _inner;

    protected DelegatingLintRule(LintRule inner)
        => _inner = inner;

    public sealed override string Id => _inner.Id;
    public sealed override string Name => _inner.Name;
    public sealed override string Description => _inner.Description;
    public sealed override LintSeverity DefaultSeverity => _inner.DefaultSeverity;
    public sealed override RuleCost Cost => _inner.Cost;
    public sealed override bool OnDemandOnly => _inner.OnDemandOnly;
    public sealed override int Priority => _inner.Priority;
    public sealed override IEnumerable<LintIssue> Check(string sql) => _inner.Check(sql);
    public sealed override IEnumerable<LintIssue> CheckStatement(Ast.Statement stmt) => _inner.CheckStatement(stmt);
}
