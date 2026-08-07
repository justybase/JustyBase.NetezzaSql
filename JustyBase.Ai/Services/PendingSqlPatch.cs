namespace JustyBase.Ai.Services;

public sealed class PendingSqlPatch
{
    public required string OriginalText { get; init; }
    public required string ProposedText { get; init; }
    public required string UnifiedDiff { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
