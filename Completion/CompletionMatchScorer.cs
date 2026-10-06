namespace JustyBase.NetezzaSqlParser.Completion;

/// <summary>
/// Identifier match scoring for completion, mirroring the reference host's
/// name matching order: direct prefix, compact spelling, snake/camel word
/// starts, delimited initials, then multi-character fragments.
/// </summary>
internal static class CompletionMatchScorer
{
    public const int Exact = 1000;
    public const int Prefix = 900;
    public const int CompactPrefix = 800;
    public const int WordStart = 700;
    public const int Initials = 650;
    public const int Fragment = 600;

    /// <summary>Returns a match score, or 0 when the label does not match.</summary>
    public static int Compute(string label, string partial)
    {
        if (string.IsNullOrEmpty(partial))
            return 1;

        if (string.Equals(label, partial, StringComparison.OrdinalIgnoreCase))
            return Exact;

        if (label.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            return Prefix;

        var compactLabel = label.Replace("_", "");
        var compactPartial = partial.Replace("_", "");
        if (compactPartial.Length > 0
            && compactLabel.StartsWith(compactPartial, StringComparison.OrdinalIgnoreCase))
        {
            return CompactPrefix;
        }

        var words = SplitWords(label);
        if (words.Any(word => word.StartsWith(partial, StringComparison.OrdinalIgnoreCase)))
            return WordStart;

        if (compactPartial.Length > 1)
        {
            var initials = string.Concat(words.Select(word => word[0]));
            if (initials.StartsWith(compactPartial, StringComparison.OrdinalIgnoreCase))
                return Initials;

            if (IsSubsequence(compactLabel, compactPartial))
                return Fragment;
        }

        return 0;
    }

    private static IReadOnlyList<string> SplitWords(string label)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var ch in label)
        {
            if (ch == '_' || ch == '-' || ch == '.')
            {
                Flush();
                continue;
            }

            if (char.IsUpper(ch) && current.Length > 0 && char.IsLower(current[^1]))
                Flush();

            current.Append(ch);
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length == 0)
                return;
            words.Add(current.ToString());
            current.Clear();
        }
    }

    private static bool IsSubsequence(string haystack, string needle)
    {
        var index = 0;
        foreach (var ch in haystack)
        {
            if (index < needle.Length
                && char.ToUpperInvariant(ch) == char.ToUpperInvariant(needle[index]))
            {
                index++;
            }

            if (index == needle.Length)
                return true;
        }

        return needle.Length == 0;
    }
}
