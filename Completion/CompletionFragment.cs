namespace JustyBase.NetezzaSqlParser.Completion;

/// <summary>Shared text-only extraction of the fragment immediately before a caret.</summary>
public static class CompletionFragment
{
    private const int MaxFragmentLength = 128;

    /// <summary>
    /// Returns the word or dotted identifier before <paramref name="position"/>.
    /// Whitespace, parentheses, commas and semicolons are boundaries; a dot is
    /// deliberately retained so qualified fragments such as <c>A.COL</c> work.
    /// </summary>
    public static string? GetLastWordFromText(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0 || position <= 0)
            return string.Empty;

        position = Math.Min(position, text.Length);
        int start = position;
        while (start > 0)
        {
            char c = text[start - 1];
            if (char.IsWhiteSpace(c) || c is '(' or ',' or ';')
                break;

            start--;
            if (position - start > MaxFragmentLength)
                return null;
        }

        return text[start..position];
    }
}
