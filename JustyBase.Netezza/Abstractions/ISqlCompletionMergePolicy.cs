using JustyBase.NetezzaSqlParser.Completion;

namespace JustyBase.Netezza.Abstractions;

/// <summary>
/// Decides whether the legacy completion path should run as a fallback
/// when the engine-based completion produced no schema-relevant results.
/// </summary>
public interface ISqlCompletionMergePolicy
{
    /// <summary>Returns <see langword="true"/> when the legacy fallback should be used.</summary>
    /// <param name="engineItems">Items produced by the engine-based completion.</param>
    /// <param name="sql">The SQL text being completed.</param>
    bool ShouldRunLegacyPath(IReadOnlyList<CompletionItem> engineItems, string sql);
}
