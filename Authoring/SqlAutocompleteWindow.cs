namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Slices large SQL documents down to the current statement (or a lookback window)
/// before calling the completion engine. Port of Legacy <c>NetezzaHybridAutocompleteSource.SliceSqlForEngine</c>.
/// </summary>
public static class SqlAutocompleteWindow
{
    /// <summary>
    /// True when the completion engine should run for this caret position on a (possibly large) document.
    /// Passive autocomplete on oversized trailing statements is skipped; forced / short statements run.
    /// </summary>
    public static bool ShouldRunEngine(
        string sql,
        int cursorOffset,
        int lineCount,
        bool forcedAutocomplete)
    {
        if (string.IsNullOrEmpty(sql))
            return false;

        bool largeDoc = SqlPerformancePolicy.ShouldSkipDeepAutocompleteScan(lineCount, sql.Length);
        if (!largeDoc || forcedAutocomplete)
            return true;

        int probe = Math.Min(cursorOffset, Math.Max(0, sql.Length - 1));
        (int stmtStart, _) = SqlStatementBounds.GetTopLevelStatementBounds(probe, sql);
        int stmtChars = stmtStart >= 0 ? cursorOffset - stmtStart : sql.Length;
        return stmtChars <= SqlPerformancePolicy.PassiveAutocompleteStatementCharLimit;
    }

    /// <summary>
    /// Returns the SQL fragment and adjusted caret offset that should be fed to <c>NzCompletionEngine</c>.
    /// On large docs, prefers the statement after the last top-level semicolon when it fits the limit;
    /// otherwise falls back to a 48k lookback window.
    /// </summary>
    public static (string Sql, int CursorOffset) SliceForEngine(
        string sql,
        int cursorOffset,
        int lineCount,
        bool forcedAutocomplete = true)
    {
        ArgumentNullException.ThrowIfNull(sql);

        cursorOffset = Math.Clamp(cursorOffset, 0, sql.Length);
        bool largeDoc = SqlPerformancePolicy.ShouldSkipDeepAutocompleteScan(lineCount, sql.Length);

        if (!largeDoc || sql.Length <= SqlPerformancePolicy.AutocompleteLookbackCharLimit)
            return (sql, cursorOffset);

        int probe = Math.Min(cursorOffset, Math.Max(0, sql.Length - 1));
        (int stmtStart, _) = SqlStatementBounds.GetTopLevelStatementBounds(probe, sql);
        if (stmtStart >= 0)
        {
            int stmtChars = cursorOffset - stmtStart;
            int stmtLimit = forcedAutocomplete
                ? SqlPerformancePolicy.AutocompleteLookbackCharLimit
                : SqlPerformancePolicy.PassiveAutocompleteStatementCharLimit;
            if (stmtChars > 0 && stmtChars <= stmtLimit)
            {
                string block = sql.Substring(stmtStart, stmtChars);
                return (block, block.Length);
            }
        }

        int windowStart = Math.Max(0, cursorOffset - SqlPerformancePolicy.AutocompleteLookbackCharLimit);
        int windowEnd = Math.Min(sql.Length, cursorOffset + 4_096);
        string window = sql.Substring(windowStart, windowEnd - windowStart);
        return (window, cursorOffset - windowStart);
    }
}
