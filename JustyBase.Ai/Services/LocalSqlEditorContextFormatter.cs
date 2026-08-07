namespace JustyBase.Ai.Services;

public static class LocalSqlEditorContextFormatter
{
    public const string NoActiveSqlDocumentMessage = "No active SQL document. Please open and select an SQL document to get its content.";
    private const string ErrorGettingCurrentSqlPrefix = "Error getting current SQL";

    public static bool IsUnavailableSqlMessage(string sql)
    {
        return sql.StartsWith(NoActiveSqlDocumentMessage, StringComparison.OrdinalIgnoreCase) ||
               sql.StartsWith(ErrorGettingCurrentSqlPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasValidSelection((string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset) context)
    {
        return context.SelectionLength > 0 &&
               context.SelectionStart >= 0 &&
               context.SelectionStart + context.SelectionLength <= context.FullText.Length;
    }

    public static string GetSelectedText((string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset) context)
    {
        if (!HasValidSelection(context))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(context.SelectedText)
            ? context.FullText.Substring(context.SelectionStart, context.SelectionLength)
            : context.SelectedText;
    }

    public static string MarkSelectedSqlRegion((string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset) context)
    {
        if (!HasValidSelection(context))
        {
            return context.FullText;
        }

        var start = context.SelectionStart;
        var end = context.SelectionStart + context.SelectionLength;
        var before = context.FullText[..start];
        var selected = context.FullText.Substring(start, context.SelectionLength);
        var after = context.FullText[end..];
        return $"{before}/*<SELECTION_START>*/{selected}/*<SELECTION_END>*/{after}";
    }
}
