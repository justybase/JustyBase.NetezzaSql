using JustyBase.Core.Git;
using System.Diagnostics;
using System.Text;

namespace JustyBase.Core.Git;

/// <summary>Invokes system <c>git.exe</c> from PATH for source-control operations.</summary>
public sealed class SystemGitService : IGitService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
    private readonly string _gitExecutable;

    public SystemGitService(string? gitExecutable = null)
    {
        _gitExecutable = string.IsNullOrWhiteSpace(gitExecutable) ? "git" : gitExecutable;
    }

    public async Task<bool> IsGitAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            GitCommandResult result = await RunAsync(workingDirectory: null, ["--version"], cancellationToken).ConfigureAwait(false);
            return result.Succeeded;
        }
        catch
        {
            return false;
        }
    }

    public string? DiscoverRepo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string? current;
        try
        {
            current = Directory.Exists(path)
                ? Path.GetFullPath(path)
                : Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch
        {
            return null;
        }

        while (!string.IsNullOrEmpty(current))
        {
            string gitMarker = Path.Combine(current, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker))
                return current;

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
                break;
            current = parent.FullName;
        }

        return null;
    }

    public async Task<GitRepoStatus> GetStatusAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        string branch = string.Empty;
        bool detached = false;

        GitCommandResult branchResult = await RunAsync(repoPath, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (branchResult.Succeeded)
        {
            branch = branchResult.StandardOutput.Trim();
            if (string.Equals(branch, "HEAD", StringComparison.Ordinal))
            {
                detached = true;
                GitCommandResult shortHash = await RunAsync(repoPath, ["rev-parse", "--short", "HEAD"], cancellationToken).ConfigureAwait(false);
                branch = shortHash.Succeeded ? shortHash.StandardOutput.Trim() : "HEAD";
            }
        }

        GitCommandResult statusResult = await RunAsync(repoPath, ["status", "--porcelain=v1", "-u"], cancellationToken).ConfigureAwait(false);
        IReadOnlyList<GitFileStatus> files = statusResult.Succeeded
            ? GitOutputParser.ParsePorcelainStatus(statusResult.StandardOutput)
            : [];

        return new GitRepoStatus(repoPath, branch, detached, files);
    }

    public Task<GitCommandResult> StageAsync(string repoPath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
            return Task.FromResult(GitCommandResult.Success());

        // -A includes deletions for the given pathspecs (plain "add -- path" fails when the file is gone).
        var args = new List<string> { "add", "-A", "--" };
        args.AddRange(paths);
        return RunAsync(repoPath, args, cancellationToken);
    }

    public Task<GitCommandResult> StageAllAsync(string repoPath, CancellationToken cancellationToken = default) =>
        RunAsync(repoPath, ["add", "-A"], cancellationToken);

    public Task<GitCommandResult> UnstageAsync(string repoPath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
            return Task.FromResult(GitCommandResult.Success());

        var args = new List<string> { "restore", "--staged", "--" };
        args.AddRange(paths);
        return RunAsync(repoPath, args, cancellationToken);
    }

    public Task<GitCommandResult> DiscardAsync(string repoPath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
            return Task.FromResult(GitCommandResult.Success());

        var args = new List<string> { "restore", "--" };
        args.AddRange(paths);
        return RunAsync(repoPath, args, cancellationToken);
    }

    public Task<GitCommandResult> DeleteUntrackedAsync(string repoPath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
            return Task.FromResult(GitCommandResult.Success());

        var args = new List<string> { "clean", "-fd", "--" };
        args.AddRange(paths);
        return RunAsync(repoPath, args, cancellationToken);
    }

    public Task<GitCommandResult> CommitAsync(string repoPath, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Task.FromResult(GitCommandResult.Failure(1, string.Empty, "Commit message is required."));

        return RunAsync(repoPath, ["commit", "-m", message], cancellationToken);
    }

    public Task<GitCommandResult> PullAsync(string repoPath, CancellationToken cancellationToken = default) =>
        RunAsync(repoPath, ["pull"], cancellationToken);

    public Task<GitCommandResult> PushAsync(string repoPath, CancellationToken cancellationToken = default) =>
        RunAsync(repoPath, ["push"], cancellationToken);

    public async Task<IReadOnlyList<GitBranchInfo>> GetBranchesAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        GitCommandResult result = await RunAsync(repoPath, ["branch", "--list"], cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? GitOutputParser.ParseBranches(result.StandardOutput)
            : [];
    }

    public Task<GitCommandResult> CreateBranchAsync(string repoPath, string branchName, bool checkout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            return Task.FromResult(GitCommandResult.Failure(1, string.Empty, "Branch name is required."));

        return checkout
            ? RunAsync(repoPath, ["checkout", "-b", branchName.Trim()], cancellationToken)
            : RunAsync(repoPath, ["branch", branchName.Trim()], cancellationToken);
    }

    public Task<GitCommandResult> CheckoutAsync(string repoPath, string branchName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            return Task.FromResult(GitCommandResult.Failure(1, string.Empty, "Branch name is required."));

        return RunAsync(repoPath, ["checkout", branchName.Trim()], cancellationToken);
    }

    public Task<GitCommandResult> MergeAsync(string repoPath, string branchName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            return Task.FromResult(GitCommandResult.Failure(1, string.Empty, "Branch name is required."));

        return RunAsync(repoPath, ["merge", "--no-edit", branchName.Trim()], cancellationToken);
    }

    public async Task<IReadOnlyList<GitCommitInfo>> GetCommitsAsync(string repoPath, int maxCount = 50, CancellationToken cancellationToken = default)
    {
        int count = Math.Clamp(maxCount, 1, 500);
        GitCommandResult result = await RunAsync(
            repoPath,
            ["log", $"-n{count}", $"--pretty=format:{GitOutputParser.LogFormat}"],
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? GitOutputParser.ParseLog(result.StandardOutput)
            : [];
    }

    public async Task<IReadOnlyList<GitCommitFile>> GetCommitFilesAsync(
        string repoPath,
        string commitHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commitHash))
            return [];

        GitCommandResult result = await RunAsync(
            repoPath,
            ["show", "--name-status", "--pretty=format:", "--no-renames", commitHash.Trim()],
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? GitOutputParser.ParseNameStatus(result.StandardOutput)
            : [];
    }

    public async Task<GitCommitTooltipInfo> GetCommitTooltipAsync(
        string repoPath,
        string commitHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commitHash))
            return new GitCommitTooltipInfo(string.Empty, 0, 0, 0);

        string hash = commitHash.Trim();
        GitCommandResult bodyResult = await RunAsync(
            repoPath,
            ["show", "-s", "--format=%b", hash],
            cancellationToken).ConfigureAwait(false);
        GitCommandResult statResult = await RunAsync(
            repoPath,
            ["show", "--shortstat", "--format=", hash],
            cancellationToken).ConfigureAwait(false);

        return GitOutputParser.ParseCommitTooltip(
            bodyResult.Succeeded ? bodyResult.StandardOutput : string.Empty,
            statResult.Succeeded ? statResult.StandardOutput : string.Empty);
    }

    public async Task<string?> GetUpstreamBranchAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        GitCommandResult result = await RunAsync(
            repoPath,
            ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"],
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            return null;
        string name = result.StandardOutput.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public async Task<GitFileContents> GetCommitFileContentsAsync(
        string repoPath,
        string commitHash,
        GitCommitFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrWhiteSpace(commitHash))
            return new GitFileContents(file.Path, Path.GetFileName(file.Path), string.Empty, string.Empty);

        string hash = commitHash.Trim();
        string relative = file.Path.Replace('\\', '/');
        string oldPath = (file.OriginalPath ?? relative).Replace('\\', '/');
        char status = file.StatusCode.Length > 0 ? file.StatusCode[0] : 'M';
        string shortHash = hash.Length >= 7 ? hash[..7] : hash;
        string title = $"{Path.GetFileName(relative)} @ {shortHash}";

        string oldText = string.Empty;
        string newText = string.Empty;

        switch (status)
        {
            case 'A':
                newText = await ShowGitObjectAsync(repoPath, $"{hash}:{relative}", cancellationToken).ConfigureAwait(false);
                break;
            case 'D':
                oldText = await ShowGitObjectAsync(repoPath, $"{hash}^:{oldPath}", cancellationToken).ConfigureAwait(false);
                break;
            case 'R':
            case 'C':
                oldText = await ShowGitObjectAsync(repoPath, $"{hash}^:{oldPath}", cancellationToken).ConfigureAwait(false);
                newText = await ShowGitObjectAsync(repoPath, $"{hash}:{relative}", cancellationToken).ConfigureAwait(false);
                break;
            default:
                oldText = await ShowGitObjectAsync(repoPath, $"{hash}^:{oldPath}", cancellationToken).ConfigureAwait(false);
                newText = await ShowGitObjectAsync(repoPath, $"{hash}:{relative}", cancellationToken).ConfigureAwait(false);
                break;
        }

        return new GitFileContents(relative, title, oldText ?? string.Empty, newText ?? string.Empty);
    }

    public async Task<IReadOnlyList<GitCommitInfo>> GetFileHistoryAsync(string repoPath, string filePath, int maxCount = 30, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return [];

        int count = Math.Clamp(maxCount, 1, 500);
        string relative = MakeRelative(repoPath, filePath);
        GitCommandResult result = await RunAsync(
            repoPath,
            ["log", $"-n{count}", "--follow", $"--pretty=format:{GitOutputParser.LogFormat}", "--", relative],
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? GitOutputParser.ParseLog(result.StandardOutput)
            : [];
    }

    public async Task<GitFileContents> GetFileContentsAsync(
        string repoPath,
        GitFileStatus file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        string relative = file.Path.Replace('\\', '/');
        string title = Path.GetFileName(relative);
        string oldPath = (file.OriginalPath ?? relative).Replace('\\', '/');

        string oldText = string.Empty;
        string newText = string.Empty;

        if (file.Kind is GitChangeKind.Untracked or GitChangeKind.Added)
        {
            oldText = string.Empty;
            newText = await ReadWorktreeFileAsync(repoPath, relative, cancellationToken).ConfigureAwait(false);
        }
        else if (file.Kind == GitChangeKind.Deleted
                 || string.Equals(file.WorkTreeStatus, "D", StringComparison.Ordinal)
                 || string.Equals(file.IndexStatus, "D", StringComparison.Ordinal) && !file.IsUnstaged)
        {
            oldText = await ShowGitObjectAsync(repoPath, $"HEAD:{oldPath}", cancellationToken).ConfigureAwait(false);
            newText = string.Empty;
        }
        else if (file.Kind == GitChangeKind.Renamed)
        {
            oldText = await ShowGitObjectAsync(repoPath, $"HEAD:{oldPath}", cancellationToken).ConfigureAwait(false);
            newText = await ReadWorktreeFileAsync(repoPath, relative, cancellationToken).ConfigureAwait(false);
        }
        else if (file.IsStaged && file.IsUnstaged)
        {
            // MM: show unstaged delta (index vs worktree), not HEAD vs worktree.
            oldText = await ShowGitObjectAsync(repoPath, $":{relative}", cancellationToken).ConfigureAwait(false);
            newText = await ReadWorktreeFileAsync(repoPath, relative, cancellationToken).ConfigureAwait(false);
            title = $"{title} (unstaged)";
        }
        else if (file.IsStaged && !file.IsUnstaged)
        {
            oldText = await ShowGitObjectAsync(repoPath, $"HEAD:{relative}", cancellationToken).ConfigureAwait(false);
            newText = await ShowGitObjectAsync(repoPath, $":{relative}", cancellationToken).ConfigureAwait(false);
            title = $"{title} (staged)";
        }
        else
        {
            oldText = await ShowGitObjectAsync(repoPath, $"HEAD:{relative}", cancellationToken).ConfigureAwait(false);
            newText = await ReadWorktreeFileAsync(repoPath, relative, cancellationToken).ConfigureAwait(false);
        }

        return new GitFileContents(relative, title, oldText ?? string.Empty, newText ?? string.Empty);
    }

    public async Task<GitCommandResult> AddToGitIgnoreAsync(
        string repoPath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return GitCommandResult.Failure(1, string.Empty, "Path is required.");

        string normalized = relativePath.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        string ignorePath = Path.Combine(repoPath, ".gitignore");
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var existing = new HashSet<string>(StringComparer.Ordinal);
                if (File.Exists(ignorePath))
                {
                    foreach (string line in File.ReadAllLines(ignorePath))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
                            existing.Add(trimmed);
                    }
                }

                if (existing.Contains(normalized))
                    return;

                string prefix = File.Exists(ignorePath) && new FileInfo(ignorePath).Length > 0
                    ? Environment.NewLine
                    : string.Empty;
                File.AppendAllText(ignorePath, prefix + normalized + Environment.NewLine);
            }, cancellationToken).ConfigureAwait(false);

            return GitCommandResult.Success();
        }
        catch (Exception ex)
        {
            return GitCommandResult.Failure(-1, string.Empty, ex.Message);
        }
    }

    public async Task<GitUserIdentity> GetUserIdentityAsync(string? repoPath, CancellationToken cancellationToken = default)
    {
        string? name = await GetConfigValueAsync(repoPath, "user.name", cancellationToken).ConfigureAwait(false);
        string? email = await GetConfigValueAsync(repoPath, "user.email", cancellationToken).ConfigureAwait(false);
        bool nameIsLocal = false;
        bool emailIsLocal = false;

        if (!string.IsNullOrWhiteSpace(repoPath))
        {
            nameIsLocal = !string.IsNullOrEmpty(
                await GetConfigValueAsync(repoPath, "user.name", cancellationToken, localOnly: true).ConfigureAwait(false));
            emailIsLocal = !string.IsNullOrEmpty(
                await GetConfigValueAsync(repoPath, "user.email", cancellationToken, localOnly: true).ConfigureAwait(false));
        }

        return new GitUserIdentity(name, email, nameIsLocal, emailIsLocal);
    }

    public async Task<GitCommandResult> SetLocalUserIdentityAsync(
        string repoPath,
        string? name,
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
            return GitCommandResult.Failure(1, string.Empty, "Repository path is required.");

        if (!string.IsNullOrWhiteSpace(name))
        {
            GitCommandResult nameResult = await RunAsync(
                repoPath,
                ["config", "--local", "user.name", name.Trim()],
                cancellationToken).ConfigureAwait(false);
            if (!nameResult.Succeeded)
                return nameResult;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            GitCommandResult emailResult = await RunAsync(
                repoPath,
                ["config", "--local", "user.email", email.Trim()],
                cancellationToken).ConfigureAwait(false);
            if (!emailResult.Succeeded)
                return emailResult;
        }

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email))
            return GitCommandResult.Failure(1, string.Empty, "Name or email is required.");

        return GitCommandResult.Success();
    }

    public async Task<string> GetWorkingTreeChangeSummaryAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
            return string.Empty;

        const int maxChars = 6_000;
        GitCommandResult cachedStat = await RunAsync(repoPath, ["diff", "--cached", "--stat"], cancellationToken).ConfigureAwait(false);
        GitCommandResult cachedDiff = await RunAsync(repoPath, ["diff", "--cached"], cancellationToken).ConfigureAwait(false);
        bool hasStaged = cachedDiff.Succeeded && !string.IsNullOrWhiteSpace(cachedDiff.StandardOutput);

        string stat;
        string diff;
        string label;
        if (hasStaged)
        {
            label = "Staged changes";
            stat = cachedStat.Succeeded ? cachedStat.StandardOutput : string.Empty;
            diff = cachedDiff.StandardOutput;
        }
        else
        {
            label = "Unstaged changes";
            GitCommandResult workStat = await RunAsync(repoPath, ["diff", "--stat"], cancellationToken).ConfigureAwait(false);
            GitCommandResult workDiff = await RunAsync(repoPath, ["diff"], cancellationToken).ConfigureAwait(false);
            stat = workStat.Succeeded ? workStat.StandardOutput : string.Empty;
            diff = workDiff.Succeeded ? workDiff.StandardOutput : string.Empty;
        }

        GitCommandResult status = await RunAsync(repoPath, ["status", "--porcelain=v1", "-u"], cancellationToken).ConfigureAwait(false);
        string statusText = status.Succeeded ? status.StandardOutput.Trim() : string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(label);
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            sb.AppendLine("Status:");
            sb.AppendLine(TruncateForAi(statusText, 1_500));
        }

        if (!string.IsNullOrWhiteSpace(stat))
        {
            sb.AppendLine("Stat:");
            sb.AppendLine(TruncateForAi(stat.Trim(), 1_000));
        }

        if (!string.IsNullOrWhiteSpace(diff))
        {
            sb.AppendLine("Diff:");
            sb.AppendLine(TruncateForAi(diff.Trim(), maxChars));
        }

        return sb.ToString().Trim();
    }

    private static string TruncateForAi(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;
        return text[..maxChars] + Environment.NewLine + "…(truncated)";
    }

    private async Task<string?> GetConfigValueAsync(
        string? repoPath,
        string key,
        CancellationToken cancellationToken,
        bool localOnly = false)
    {
        var args = new List<string> { "config" };
        if (localOnly)
            args.Add("--local");
        args.Add("--get");
        args.Add(key);

        GitCommandResult result = await RunAsync(repoPath, args, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            return null;

        string value = result.StandardOutput.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private async Task<string> ShowGitObjectAsync(string repoPath, string objectSpec, CancellationToken cancellationToken)
    {
        GitCommandResult result = await RunAsync(repoPath, ["show", "--textconv", objectSpec], cancellationToken, trimOutput: false).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput : string.Empty;
    }

    private static async Task<string> ReadWorktreeFileAsync(string repoPath, string relativePath, CancellationToken cancellationToken)
    {
        string fullPath = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return string.Empty;

        try
        {
            return await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string MakeRelative(string repoPath, string filePath)
    {
        try
        {
            string fullRepo = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
            string fullFile = Path.GetFullPath(filePath);
            if (fullFile.StartsWith(fullRepo, StringComparison.OrdinalIgnoreCase))
                return fullFile[fullRepo.Length..].Replace('\\', '/');
        }
        catch
        {
        }

        return filePath.Replace('\\', '/');
    }

    private async Task<GitCommandResult> RunAsync(
        string? workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool trimOutput = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _gitExecutable,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.CurrentDirectory
                : workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdoutClosedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrClosedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) stdout.AppendLine(e.Data);
            else stdoutClosedTcs.TrySetResult();
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderr.AppendLine(e.Data);
            else stderrClosedTcs.TrySetResult();
        };
        process.Exited += (_, _) =>
        {
            try { exitTcs.TrySetResult(process.ExitCode); }
            catch (Exception ex) { exitTcs.TrySetException(ex); }
        };

        try
        {
            if (!process.Start())
                return GitCommandResult.Failure(-1, string.Empty, "Failed to start git process.");
        }
        catch (Exception ex)
        {
            return GitCommandResult.Failure(-1, string.Empty, ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeout);

        try
        {
            await using (timeoutCts.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                exitTcs.TrySetCanceled(timeoutCts.Token);
            }))
            {
                int exitCode = await exitTcs.Task.ConfigureAwait(false);
                await Task.WhenAll(stdoutClosedTcs.Task, stderrClosedTcs.Task).ConfigureAwait(false);
                string outText = stdout.ToString();
                string errText = stderr.ToString();
                if (trimOutput)
                {
                    outText = outText.TrimEnd();
                    errText = errText.TrimEnd();
                }
                else
                {
                    // OutputDataReceived + AppendLine always adds a final newline after the last line.
                    if (outText.EndsWith('\n'))
                        outText = outText.TrimEnd('\r', '\n') + (outText.Contains('\r') ? "\r\n" : "\n");
                }

                return exitCode == 0
                    ? GitCommandResult.Success(outText, errText.TrimEnd())
                    : GitCommandResult.Failure(exitCode, outText, errText.TrimEnd());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return GitCommandResult.Failure(-1, stdout.ToString(), "Git command timed out.");
        }
    }
}
