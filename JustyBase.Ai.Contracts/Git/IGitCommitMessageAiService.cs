namespace JustyBase.Ai.Git;

/// <summary>Generates a commit message from a working-tree change summary via the embedded FIM model.</summary>
public interface IGitCommitMessageAiService
{
    bool IsAvailable { get; }

    /// <summary>
    /// Returns a suggested commit message, or null when the model is disabled / unavailable / empty.
    /// </summary>
    Task<string?> GenerateAsync(string changeContext, CancellationToken cancellationToken = default);
}
