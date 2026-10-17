using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>Version warnings for the supported PostgreSQL 16–18 grammar union.</summary>
public static class PostgreSqlVersionDiagnostics
{
    public static IReadOnlyList<LintIssue> Analyze(string sql, int? serverVersionNum)
    {
        if (serverVersionNum is null or <= 0) return [];
        Superpower.Model.Token<NzToken>[] tokens;
        try { tokens = PostgreSqlLexer.Tokenize(sql).ToArray(); }
        catch (Superpower.ParseException) { return []; }
        var result = new List<LintIssue>();
        var merge = false;
        var generated = false;
        var major = serverVersionNum.Value / 10000;
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var word = token.ToStringValue().ToUpperInvariant();
            var next = i + 1 < tokens.Length ? tokens[i + 1].ToStringValue().ToUpperInvariant() : "";
            if (token.Kind == NzToken.Semicolon) { merge = false; generated = false; }
            if (token.Kind == NzToken.Merge) merge = true;
            if (word == "GENERATED") generated = true;
            var required = word == "JSON_TABLE" ? 17
                : merge && token.Kind == NzToken.PostgreSqlReturning ? 17
                : merge && word == "BY" && next is "SOURCE" or "TARGET" ? 17
                : token.Kind == NzToken.PostgreSqlReturning && next == "WITH" ? 18
                : generated && word == "VIRTUAL" ? 18 : 0;
            if (required == 0 || major >= required) continue;
            result.Add(new LintIssue("PG001", $"{word} requires PostgreSQL {required} or newer (connected to {major}).",
                LintSeverity.Warning, token.Position.Absolute, token.Position.Absolute + token.Span.Length,
                token.Position.Line, token.Position.Column));
        }
        return result;
    }
}
