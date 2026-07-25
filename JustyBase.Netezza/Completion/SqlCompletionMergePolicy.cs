using JustyBase.Netezza.Abstractions;
using JustyBase.NetezzaSqlParser.Completion;

namespace JustyBase.Netezza.Completion;

/// <summary>
/// Shared engine-first completion merge policy for Legacy WinForms and Avalonia SQL editors.
/// </summary>
public static class SqlCompletionMergePolicy
{
    /// <summary>Maximum SQL length before the legacy path is always skipped.</summary>
    public const int MaxSqlLengthForLegacyPath = 100_000;

    /// <summary>Default singleton instance that implements <see cref="ISqlCompletionMergePolicy"/>.</summary>
    public static ISqlCompletionMergePolicy Default { get; } = new DefaultSqlCompletionMergePolicy();

    /// <summary>Returns <see langword="true"/> when the legacy fallback should be used.</summary>
    public static bool ShouldRunLegacyPath(IReadOnlyList<CompletionItem> engineItems, string sql)
        => Default.ShouldRunLegacyPath(engineItems, sql);
}

internal sealed class DefaultSqlCompletionMergePolicy : ISqlCompletionMergePolicy
{
    public bool ShouldRunLegacyPath(IReadOnlyList<CompletionItem> engineItems, string sql)
    {
        if (sql.Length >= SqlCompletionMergePolicy.MaxSqlLengthForLegacyPath)
            return false;

        if (engineItems.Any(i => i.Kind is CompletionKind.Table or CompletionKind.View or CompletionKind.Column
                or CompletionKind.Schema or CompletionKind.Database or CompletionKind.Cte))
            return false;

        return true;
    }
}
