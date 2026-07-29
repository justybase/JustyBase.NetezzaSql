namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Shared size/debounce policy for SQL editor intelligence.
/// Port of validationConfig.ts from justybase-vscode-private.
/// Legacy UI should consume these helpers instead of hardcoding thresholds.
/// </summary>
public static class SqlPerformancePolicy
{
    /// <summary>Line count above which a script is treated as heavy.</summary>
    public const int LargeScriptLineThreshold = 500;

    /// <summary>Character count above which CST/parser-heavy features should degrade.</summary>
    public const int LargeScriptCharThreshold = 150_000;

    /// <summary>Above this line count, full diagnostics are skipped entirely.</summary>
    public const int HugeScriptLineThreshold = 3_000;

    /// <summary>Above this line count, lint uses the large-script debounce.</summary>
    public const int LargeDiagnosticsLineThreshold = 1_500;

    public const int DefaultLintDebounceMs = 400;
    public const int LargeScriptLintDebounceMs = 2_000;

    public const int DefaultSemanticDebounceMs = 150;
    public const int LargeScriptSemanticDebounceMs = 800;

    public const int DefaultTypingDelayedMs = 100;
    public const int LargeScriptTypingDelayedMs = 400;

    /// <summary>
    /// Cap for autocomplete statement lookback on large scripts (chars before caret).
    /// Avoids O(document) walks when typing at the end of huge single-statement files.
    /// </summary>
    public const int AutocompleteLookbackCharLimit = 48_000;

    /// <summary>Idle delay before a full comment/string clean-SQL rebuild on large scripts.</summary>
    public const int LargeScriptFullCommentScanDebounceMs = 2_000;

    /// <summary>Hard ceiling for semantic CST classification (legacy large-doc guard).</summary>
    public const int SemanticFullParseCharLimit = 150_000;

    /// <summary>Absolute ceiling beyond which only cheap lint rules run.</summary>
    public const int CheapLintOnlyCharLimit = 500_000;

    public static bool IsLargeScript(int textLength) =>
        textLength > LargeScriptCharThreshold;

    public static bool IsLargeScriptDocument(int lineCount, int textLength) =>
        lineCount > LargeScriptLineThreshold || textLength > LargeScriptCharThreshold;

    public static bool IsHugeScript(int lineCount, int textLength) =>
        lineCount > HugeScriptLineThreshold || textLength > CheapLintOnlyCharLimit;

    public static bool ShouldSkipFullParse(int lineCount, int textLength) =>
        IsLargeScriptDocument(lineCount, textLength);

    public static bool ShouldSkipSemanticClassification(int lineCount, int textLength) =>
        textLength > SemanticFullParseCharLimit || lineCount > HugeScriptLineThreshold;

    public static bool ShouldRunCheapLintOnly(int lineCount, int textLength) =>
        IsHugeScript(lineCount, textLength) || textLength > CheapLintOnlyCharLimit;

    /// <summary>
    /// Live typing on huge scripts publishes empty diagnostics (VS Code LSP parity).
    /// </summary>
    public static bool ShouldSkipLiveLint(int lineCount, int textLength) =>
        IsHugeScript(lineCount, textLength);

    /// <summary>
    /// Alias for <see cref="ShouldSkipLiveLint"/> — clear empty-diagnostics intent at call sites.
    /// </summary>
    public static bool ShouldPublishEmptyDiagnosticsWhileTyping(int lineCount, int textLength) =>
        ShouldSkipLiveLint(lineCount, textLength);

    /// <summary>
    /// Skip lint engine work for live typing on huge scripts; save/manual always run.
    /// </summary>
    public static bool ShouldSkipLint(SqlLintInvocation invocation, int lineCount, int textLength) =>
        invocation == SqlLintInvocation.Live && ShouldSkipLiveLint(lineCount, textLength);

    /// <summary>
    /// Resolves line count for skip/cheap gates. Prefers O(1) <paramref name="knownLineCount"/>;
    /// falls back to an early-exit probe against the huge-script line threshold.
    /// </summary>
    public static int ResolveLineCountForLintGate(string? sql, int knownLineCount = -1)
    {
        if (knownLineCount >= 0)
            return knownLineCount;

        int length = sql?.Length ?? 0;
        if (length > CheapLintOnlyCharLimit)
            return HugeScriptLineThreshold + 1;

        if (ExceedsLineThreshold(sql, HugeScriptLineThreshold))
            return HugeScriptLineThreshold + 1;

        return CountLines(sql);
    }

    public static bool ShouldSkipOutline(int lineCount, int textLength) =>
        IsLargeScriptDocument(lineCount, textLength);

    public static bool ShouldUseCheapTypingPath(int lineCount, int textLength) =>
        IsLargeScriptDocument(lineCount, textLength);

    public static int GetLintDebounceMs(int lineCount, int textLength)
    {
        if (lineCount > LargeDiagnosticsLineThreshold || IsLargeScript(textLength))
            return LargeScriptLintDebounceMs;
        return DefaultLintDebounceMs;
    }

    /// <summary>
    /// Length-first debounce. Avoids scanning the full string for newlines on every keystroke.
    /// When <paramref name="knownLineCount"/> is available (e.g. editor.LinesCount), it is used.
    /// </summary>
    public static int GetLintDebounceMs(string? sql, int knownLineCount = -1)
    {
        int length = sql?.Length ?? 0;
        if (IsLargeScript(length))
            return LargeScriptLintDebounceMs;

        if (knownLineCount >= 0)
            return GetLintDebounceMs(knownLineCount, length);

        // Early-exit line probe — stops once the diagnostics threshold is exceeded.
        if (ExceedsLineThreshold(sql, LargeDiagnosticsLineThreshold))
            return LargeScriptLintDebounceMs;

        return DefaultLintDebounceMs;
    }

    public static int GetSemanticDebounceMs(int lineCount, int textLength) =>
        IsLargeScriptDocument(lineCount, textLength)
            ? LargeScriptSemanticDebounceMs
            : DefaultSemanticDebounceMs;

    public static int GetTypingDelayedMs(int lineCount, int textLength) =>
        IsLargeScriptDocument(lineCount, textLength)
            ? LargeScriptTypingDelayedMs
            : DefaultTypingDelayedMs;

    public static bool ShouldSkipDeepAutocompleteScan(int lineCount, int textLength) =>
        IsHugeScript(lineCount, textLength) || textLength > LargeScriptCharThreshold;

    public static bool ShouldSkipFullCommentScanOnTyping(int lineCount, int textLength) =>
        IsLargeScriptDocument(lineCount, textLength);

    /// <summary>
    /// Incremental validation is worthwhile only when a minority of statements changed.
    /// </summary>
    public static bool ShouldUseIncrementalValidation(int statementCount, int dirtyCount)
    {
        if (statementCount <= 0 || dirtyCount <= 0)
            return false;
        int maxDirty = Math.Max(1, statementCount / 2);
        return dirtyCount <= maxDirty;
    }

    /// <summary>
    /// Returns true as soon as more than <paramref name="threshold"/> lines are found.
    /// Stops early so huge scripts do not pay a full O(n) pass just to compare thresholds.
    /// </summary>
    public static bool ExceedsLineThreshold(string? text, int threshold)
    {
        if (string.IsNullOrEmpty(text) || threshold < 0)
            return false;

        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                lines++;
                if (lines > threshold)
                    return true;
            }
            else if (c == '\r')
            {
                lines++;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                if (lines > threshold)
                    return true;
            }
        }

        return false;
    }

    public static int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
                lines++;
            else if (c == '\r')
            {
                lines++;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
            }
        }

        return lines;
    }
}
