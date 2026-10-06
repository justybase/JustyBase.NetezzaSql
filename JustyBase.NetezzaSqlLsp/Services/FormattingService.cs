using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Formatter;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>
/// Formats a whole SQL document with <see cref="NzSqlFormatter"/>.
/// The formatter is only applied when every statement parses and the document
/// has no comments, so a formatting request can never drop source content.
/// </summary>
public static class FormattingService
{
    /// <summary>Returns the formatted document, or <c>null</c> when formatting is not safe.</summary>
    public static string? FormatDocument(string sql, SqlDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return null;

        // AST formatting does not carry comments; leave commented documents untouched.
        if (ContainsComments(sql))
            return null;

        var index = StatementIndexBuilder.BuildIndex(sql);
        if (index.Statements.Count == 0)
            return null;

        var formatted = new List<string>(index.Statements.Count);
        foreach (var boundary in index.Statements)
        {
            var statementText = boundary.Sql;
            if (string.IsNullOrWhiteSpace(statementText))
                continue;

            var tokens = DialectRuntime.Tokenize(statementText, dialect).ToArray();
            var parser = DialectRuntime.CreateParser(tokens, dialect);
            var statement = parser.Parse();
            if (statement is null || parser.Errors.Count > 0)
                return null;

            formatted.Add(NzSqlFormatter.Format(statement));
        }

        if (formatted.Count == 0)
            return null;

        return string.Join(";\n\n", formatted) + ";";
    }

    private static bool ContainsComments(string sql) =>
        sql.Contains("--", StringComparison.Ordinal) || sql.Contains("/*", StringComparison.Ordinal);
}
