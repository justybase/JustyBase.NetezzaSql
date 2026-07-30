namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Quote-aware top-level statement bounds (semicolon-delimited).
/// Port of Legacy <c>SqlTextCursorParser.GetStatementBounds</c>.
/// </summary>
public static class SqlStatementBounds
{
    /// <summary>
    /// Top-level statement bounds around <paramref name="position"/> (semicolon-delimited, quote-aware).
    /// Returns <c>(-1, -1)</c> when the position or text is empty.
    /// </summary>
    public static (int Start, int End) GetTopLevelStatementBounds(int position, string sqlText)
    {
        ArgumentNullException.ThrowIfNull(sqlText);

        int length = sqlText.Length;
        if (position == -1 || length == 0)
            return (-1, -1);

        if (position >= length)
            position = length - 1;

        bool quoteBalance = true;
        bool doubleQuoteBalance = true;
        int start = position > 0 ? position - 1 : position;

        while (start > 0 && start < length)
        {
            char c = sqlText[start];
            if (c == ';' && quoteBalance && doubleQuoteBalance)
            {
                start++;
                break;
            }

            if (c == '\'')
                quoteBalance = !quoteBalance;
            else if (c == '"')
                doubleQuoteBalance = !doubleQuoteBalance;
            start--;
        }

        quoteBalance = true;
        doubleQuoteBalance = true;
        int end = position;
        while (end < length)
        {
            char c = sqlText[end];
            if (c == ';' && quoteBalance && doubleQuoteBalance)
                break;

            if (c == '\'')
                quoteBalance = !quoteBalance;
            else if (c == '"')
                doubleQuoteBalance = !doubleQuoteBalance;
            end++;
        }

        if (end > length || end < start)
            return (start, length);

        return (start, end);
    }
}
