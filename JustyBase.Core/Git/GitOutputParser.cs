using System.Globalization;

namespace JustyBase.Core.Git;

/// <summary>Parses porcelain status and custom log formats from system git output.</summary>
public static class GitOutputParser
{
    public const string LogFormat = "%H%x09%h%x09%an%x09%aI%x09%s";

    public static IReadOnlyList<GitFileStatus> ParsePorcelainStatus(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        var results = new List<GitFileStatus>();
        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length < 3)
                continue;

            char x = line[0];
            char y = line[1];
            string remainder = line.Length > 3 ? line[3..] : string.Empty;

            if (x == '?' && y == '?')
            {
                results.Add(new GitFileStatus(
                    Path: NormalizePath(remainder),
                    OriginalPath: null,
                    Kind: GitChangeKind.Untracked,
                    IsStaged: false,
                    IsUnstaged: true,
                    IndexStatus: "?",
                    WorkTreeStatus: "?"));
                continue;
            }

            if (x == '!' && y == '!')
            {
                results.Add(new GitFileStatus(
                    Path: NormalizePath(remainder),
                    OriginalPath: null,
                    Kind: GitChangeKind.Ignored,
                    IsStaged: false,
                    IsUnstaged: false,
                    IndexStatus: "!",
                    WorkTreeStatus: "!"));
                continue;
            }

            string? originalPath = null;
            string path = remainder;
            int arrow = remainder.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                originalPath = NormalizePath(remainder[..arrow]);
                path = NormalizePath(remainder[(arrow + 4)..]);
            }
            else
            {
                path = NormalizePath(path);
            }

            bool isStaged = x is not ' ' and not '?';
            bool isUnstaged = y is not ' ' and not '?';
            GitChangeKind kind = MapKind(x, y);

            results.Add(new GitFileStatus(
                Path: path,
                OriginalPath: originalPath,
                Kind: kind,
                IsStaged: isStaged,
                IsUnstaged: isUnstaged || (x == ' ' && y != ' '),
                IndexStatus: x == ' ' ? string.Empty : x.ToString(),
                WorkTreeStatus: y == ' ' ? string.Empty : y.ToString()));
        }

        return results;
    }

    public static IReadOnlyList<GitCommitFile> ParseNameStatus(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        var results = new List<GitCommitFile>();
        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] parts = line.Split('\t');
            if (parts.Length < 2)
                continue;

            string status = parts[0].Trim();
            if (status.Length == 0)
                continue;

            if ((status[0] is 'R' or 'C') && parts.Length >= 3)
            {
                results.Add(new GitCommitFile(
                    Path: NormalizePath(parts[2]),
                    OriginalPath: NormalizePath(parts[1]),
                    StatusCode: status));
            }
            else
            {
                results.Add(new GitCommitFile(
                    Path: NormalizePath(parts[1]),
                    OriginalPath: null,
                    StatusCode: status.Length > 0 ? status[..1] : status));
            }
        }

        return results;
    }

    public static IReadOnlyList<GitCommitInfo> ParseLog(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        var results = new List<GitCommitInfo>();
        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] parts = line.Split('\t');
            if (parts.Length < 5)
                continue;

            if (!DateTimeOffset.TryParse(parts[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset date))
                date = DateTimeOffset.MinValue;

            string subject = parts.Length == 5
                ? parts[4]
                : string.Join('\t', parts.Skip(4));

            results.Add(new GitCommitInfo(
                Hash: parts[0],
                ShortHash: parts[1],
                Author: parts[2],
                AuthorDate: date,
                Subject: subject));
        }

        return results;
    }

    public static IReadOnlyList<GitBranchInfo> ParseBranches(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        var results = new List<GitBranchInfo>();
        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            bool isCurrent = trimmed.StartsWith('*');
            string name = isCurrent ? trimmed[1..].Trim() : trimmed;
            if (name.StartsWith("(HEAD detached", StringComparison.Ordinal))
                name = "HEAD";

            results.Add(new GitBranchInfo(name, isCurrent));
        }

        return results;
    }

    public static string FormatRelativeDate(DateTimeOffset date, DateTimeOffset? now = null)
    {
        if (date == DateTimeOffset.MinValue)
            return string.Empty;

        DateTimeOffset reference = now ?? DateTimeOffset.Now;
        TimeSpan delta = reference - date;
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;

        if (delta.TotalMinutes < 1)
            return "now";
        if (delta.TotalMinutes < 60)
            return $"{(int)delta.TotalMinutes}m";
        if (delta.TotalHours < 24)
            return $"{(int)delta.TotalHours}h";
        if (delta.TotalDays < 14)
            return $"{(int)delta.TotalDays}d";
        if (delta.TotalDays < 60)
            return $"{(int)(delta.TotalDays / 7)} wks";
        if (delta.TotalDays < 365)
            return $"{(int)(delta.TotalDays / 30)} mo";
        return $"{(int)(delta.TotalDays / 365)} yr";
    }

    /// <summary>Human-readable relative time for tooltips, e.g. "13 hours ago".</summary>
    public static string FormatRelativeDateLong(DateTimeOffset date, DateTimeOffset? now = null)
    {
        if (date == DateTimeOffset.MinValue)
            return string.Empty;

        DateTimeOffset reference = now ?? DateTimeOffset.Now;
        TimeSpan delta = reference - date;
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;

        if (delta.TotalSeconds < 45)
            return "just now";
        if (delta.TotalMinutes < 1)
            return "1 minute ago";
        if (delta.TotalMinutes < 60)
        {
            int m = (int)delta.TotalMinutes;
            return m == 1 ? "1 minute ago" : $"{m} minutes ago";
        }

        if (delta.TotalHours < 24)
        {
            int h = (int)delta.TotalHours;
            return h == 1 ? "1 hour ago" : $"{h} hours ago";
        }

        if (delta.TotalDays < 30)
        {
            int d = (int)delta.TotalDays;
            return d == 1 ? "1 day ago" : $"{d} days ago";
        }

        if (delta.TotalDays < 365)
        {
            int mo = Math.Max(1, (int)(delta.TotalDays / 30));
            return mo == 1 ? "1 month ago" : $"{mo} months ago";
        }

        int y = Math.Max(1, (int)(delta.TotalDays / 365));
        return y == 1 ? "1 year ago" : $"{y} years ago";
    }

    public static GitCommitTooltipInfo ParseCommitTooltip(string bodyOutput, string shortstatOutput)
    {
        string body = (bodyOutput ?? string.Empty).Trim();
        int files = 0, insertions = 0, deletions = 0;

        if (!string.IsNullOrWhiteSpace(shortstatOutput))
        {
            foreach (string raw in shortstatOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!raw.Contains("changed", StringComparison.OrdinalIgnoreCase))
                    continue;

                files = MatchInt(raw, @"(\d+)\s+files?\s+changed");
                insertions = MatchInt(raw, @"(\d+)\s+insertions?\(\+\)");
                deletions = MatchInt(raw, @"(\d+)\s+deletions?\(-\)");
                break;
            }
        }

        return new GitCommitTooltipInfo(body, files, insertions, deletions);
    }

    private static int MatchInt(string text, string pattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : 0;
    }

    private static GitChangeKind MapKind(char x, char y)
    {
        char code = y != ' ' ? y : x;
        return code switch
        {
            'M' => GitChangeKind.Modified,
            'A' => GitChangeKind.Added,
            'D' => GitChangeKind.Deleted,
            'R' => GitChangeKind.Renamed,
            'C' => GitChangeKind.Copied,
            'U' => GitChangeKind.Unmerged,
            _ => GitChangeKind.Unknown
        };
    }

    private static string NormalizePath(string path) =>
        path.Trim().Trim('"').Replace('\\', '/');
}
