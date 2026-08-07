namespace JustyBase.Ai.Git;

/// <summary>Placeholder when embedded FIM is not available or disabled.</summary>
public sealed class UnavailableGitCommitMessageAiService : IGitCommitMessageAiService
{
    public bool IsAvailable => false;

    public Task<string?> GenerateAsync(string changeContext, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
