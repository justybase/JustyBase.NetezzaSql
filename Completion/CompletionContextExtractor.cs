using JustyBase.NetezzaSqlParser.Caching;

namespace JustyBase.NetezzaSqlParser.Completion;

/// <summary>
/// Extracts statement boundaries and persistent scope text for completion.
/// Port of completionContextExtractor.ts statement-boundary logic.
/// </summary>
public static class CompletionContextExtractor
{
    public static (int StatementStart, string PersistentScopeText) GetStatementContext(string sql, int cursorPosition)
    {
        if (string.IsNullOrEmpty(sql) || cursorPosition <= 0)
            return (0, string.Empty);

        cursorPosition = Math.Min(cursorPosition, sql.Length);
        var index = StatementIndexBuilder.BuildIndex(sql[..cursorPosition]);
        var boundary = index.Statements.LastOrDefault();
        var start = boundary.StartOffset;

        // BuildIndex only emits completed statements. When the cursor is just
        // after a top-level semicolon (possibly followed by whitespace), the
        // next statement starts after that semicolon rather than at the prior
        // statement's start.
        if (index.Statements.Count > 0
            && boundary.EndOffset < cursorPosition
            && sql[boundary.EndOffset] == ';')
        {
            start = boundary.EndOffset + 1;
        }

        var persistent = start > 0 ? sql[..start] : string.Empty;
        return (start, persistent);
    }

    public static string GetStatementLocalPrefix(string sql, int cursorPosition)
    {
        var (start, _) = GetStatementContext(sql, cursorPosition);
        return sql[start..Math.Min(cursorPosition, sql.Length)];
    }
}
