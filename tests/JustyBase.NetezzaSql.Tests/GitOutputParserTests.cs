using JustyBase.Core.Git;

namespace JustyBase.NetezzaSql.Tests;

public sealed class GitOutputParserTests
{
    [Fact]
    public void ParsePorcelainStatus_parses_modified_staged_untracked_and_renames()
    {
        const string output =
            """
            M  src/A.cs
             M src/B.cs
            MM src/C.cs
            ?? new.txt
            R  old.txt -> new-name.txt
            """;

        IReadOnlyList<GitFileStatus> files = GitOutputParser.ParsePorcelainStatus(output);

        Assert.Equal(5, files.Count);

        Assert.Equal("src/A.cs", files[0].Path);
        Assert.True(files[0].IsStaged);
        Assert.False(files[0].IsUnstaged);
        Assert.Equal(GitChangeKind.Modified, files[0].Kind);

        Assert.Equal("src/B.cs", files[1].Path);
        Assert.False(files[1].IsStaged);
        Assert.True(files[1].IsUnstaged);

        Assert.Equal("src/C.cs", files[2].Path);
        Assert.True(files[2].IsStaged);
        Assert.True(files[2].IsUnstaged);

        Assert.Equal("new.txt", files[3].Path);
        Assert.Equal(GitChangeKind.Untracked, files[3].Kind);
        Assert.Equal("?", files[3].DisplayStatus);

        Assert.Equal("new-name.txt", files[4].Path);
        Assert.Equal("old.txt", files[4].OriginalPath);
        Assert.Equal(GitChangeKind.Renamed, files[4].Kind);
    }

    [Fact]
    public void ParseLog_parses_custom_tab_format()
    {
        const string output =
            "abcdef1234567890\tabcdef1\tAlice\t2024-01-15T10:00:00+01:00\tfeat: add panel\n" +
            "1111222233334444\t1111222\tBob\t2024-02-01T12:30:00Z\tfix: tip\twith tab";

        IReadOnlyList<GitCommitInfo> commits = GitOutputParser.ParseLog(output);

        Assert.Equal(2, commits.Count);
        Assert.Equal("abcdef1234567890", commits[0].Hash);
        Assert.Equal("abcdef1", commits[0].ShortHash);
        Assert.Equal("Alice", commits[0].Author);
        Assert.Equal("feat: add panel", commits[0].Subject);
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.FromHours(1)), commits[0].AuthorDate);

        Assert.Equal("fix: tip\twith tab", commits[1].Subject);
    }

    [Fact]
    public void ParseBranches_marks_current_branch()
    {
        const string output =
            """
              develop
            * main
              feature/x
            """;

        IReadOnlyList<GitBranchInfo> branches = GitOutputParser.ParseBranches(output);

        Assert.Equal(3, branches.Count);
        Assert.Equal("develop", branches[0].Name);
        Assert.False(branches[0].IsCurrent);
        Assert.Equal("main", branches[1].Name);
        Assert.True(branches[1].IsCurrent);
        Assert.Equal("feature/x", branches[2].Name);
    }

    [Theory]
    [InlineData(0.5, "now")]
    [InlineData(5, "5m")]
    [InlineData(120, "2h")]
    [InlineData(60 * 24 * 3, "3d")]
    public void FormatRelativeDate_uses_compact_units(double minutesAgo, string expected)
    {
        var now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset date = now.AddMinutes(-minutesAgo);

        Assert.Equal(expected, GitOutputParser.FormatRelativeDate(date, now));
    }

    [Fact]
    public void ParseNameStatus_parses_added_modified_deleted_and_renames()
    {
        const string output =
            """
            A	src/New.cs
            M	src/Changed.cs
            D	src/Gone.cs
            R100	old.txt	new.txt
            """;

        IReadOnlyList<GitCommitFile> files = GitOutputParser.ParseNameStatus(output);

        Assert.Equal(4, files.Count);
        Assert.Equal("src/New.cs", files[0].Path);
        Assert.Equal("A", files[0].StatusCode);
        Assert.Equal("src/Changed.cs", files[1].Path);
        Assert.Equal("M", files[1].StatusCode);
        Assert.Equal("src/Gone.cs", files[2].Path);
        Assert.Equal("D", files[2].StatusCode);
        Assert.Equal("new.txt", files[3].Path);
        Assert.Equal("old.txt", files[3].OriginalPath);
        Assert.Equal("R100", files[3].StatusCode);
    }

    [Fact]
    public void ParseCommitTooltip_parses_body_and_shortstat()
    {
        const string body = "Implement a new key combination (Ctrl + P).";
        const string shortstat = " 6 files changed, 1424 insertions(+), 12 deletions(-)";

        GitCommitTooltipInfo info = GitOutputParser.ParseCommitTooltip(body, shortstat);

        Assert.Equal(body, info.Body);
        Assert.Equal(6, info.FilesChanged);
        Assert.Equal(1424, info.Insertions);
        Assert.Equal(12, info.Deletions);
    }

    [Fact]
    public void FormatRelativeDateLong_formats_hours()
    {
        DateTimeOffset now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset then = now.AddHours(-13);

        Assert.Equal("13 hours ago", GitOutputParser.FormatRelativeDateLong(then, now));
    }
}
