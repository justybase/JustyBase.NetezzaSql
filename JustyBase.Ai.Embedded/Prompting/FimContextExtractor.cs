namespace JustyBase.Ai.Embedded.Prompting;

/// <summary>Extracts prefix/suffix windows around the caret for FIM prompts.</summary>
public static class FimContextExtractor
{
    public const int DefaultPrefixLimit = 4096;
    public const int DefaultSuffixLimit = 1024;

    public const int MinMaxTokens = 20;
    public const int MaxMaxTokens = 200;
    public const int DefaultMaxTokens = 50;

    public static int ClampMaxTokens(int maxTokens)
    {
        if (maxTokens <= 0)
        {
            return DefaultMaxTokens;
        }

        var clamped = Math.Clamp(maxTokens, MinMaxTokens, MaxMaxTokens);
        var snapped = (int)(Math.Round(clamped / 10.0) * 10);
        return Math.Clamp(snapped, MinMaxTokens, MaxMaxTokens);
    }

    public static int ClampMaxPromptTokens(int maxPromptTokens) =>
        Math.Clamp(maxPromptTokens <= 0 ? 1536 : maxPromptTokens, 128, 8192);

    public static (string Prefix, string Suffix) Extract(
        string documentText,
        int caretOffset,
        int prefixLimit = DefaultPrefixLimit,
        int suffixLimit = DefaultSuffixLimit)
    {
        ArgumentNullException.ThrowIfNull(documentText);
        if (caretOffset < 0)
        {
            caretOffset = 0;
        }

        if (caretOffset > documentText.Length)
        {
            caretOffset = documentText.Length;
        }

        var prefixStart = Math.Max(0, caretOffset - prefixLimit);
        var prefix = documentText[prefixStart..caretOffset];

        var suffixEnd = Math.Min(documentText.Length, caretOffset + suffixLimit);
        var suffix = documentText[caretOffset..suffixEnd];

        return (prefix, suffix);
    }

    public static (string Prefix, string Suffix) Extract(
        string documentText,
        int caretOffset,
        int maxPromptTokens,
        double prefixPercentage,
        double suffixPercentage)
    {
        var (prefixLimit, suffixLimit) = FimPresets.ResolveCharBudgets(
            maxPromptTokens,
            prefixPercentage,
            suffixPercentage);
        return Extract(documentText, caretOffset, prefixLimit, suffixLimit);
    }
}
