namespace JustyBase.Core.Git;

public enum GitChangeKind
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Ignored,
    Unmerged,
    Unknown
}

public sealed record GitCommandResult(
    bool Succeeded,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public string CombinedOutput =>
        string.IsNullOrWhiteSpace(StandardError)
            ? StandardOutput
            : string.IsNullOrWhiteSpace(StandardOutput)
                ? StandardError
                : StandardOutput + Environment.NewLine + StandardError;

    public static GitCommandResult Success(string stdout = "", string stderr = "") =>
        new(true, 0, stdout, stderr);

    public static GitCommandResult Failure(int exitCode, string stdout, string stderr) =>
        new(false, exitCode, stdout, stderr);
}

public sealed record GitFileStatus(
    string Path,
    string? OriginalPath,
    GitChangeKind Kind,
    bool IsStaged,
    bool IsUnstaged,
    string IndexStatus,
    string WorkTreeStatus)
{
    public string DisplayStatus
    {
        get
        {
            if (Kind == GitChangeKind.Untracked)
                return "?";
            if (IsStaged && !IsUnstaged)
                return IndexStatus.Trim();
            if (!IsStaged && IsUnstaged)
                return WorkTreeStatus.Trim();
            return $"{IndexStatus}{WorkTreeStatus}".Trim();
        }
    }
}

public sealed record GitCommitInfo(
    string Hash,
    string ShortHash,
    string Author,
    DateTimeOffset AuthorDate,
    string Subject);

public sealed record GitBranchInfo(
    string Name,
    bool IsCurrent);

public sealed record GitRepoStatus(
    string RepoPath,
    string BranchName,
    bool IsDetached,
    IReadOnlyList<GitFileStatus> Files);

public sealed record GitFileContents(
    string RelativePath,
    string Title,
    string OldText,
    string NewText);

public sealed record GitCommitFile(
    string Path,
    string? OriginalPath,
    string StatusCode);

public sealed record GitCommitTooltipInfo(
    string Body,
    int FilesChanged,
    int Insertions,
    int Deletions);

public sealed record GitUserIdentity(
    string? Name,
    string? Email,
    bool NameIsLocal,
    bool EmailIsLocal);
