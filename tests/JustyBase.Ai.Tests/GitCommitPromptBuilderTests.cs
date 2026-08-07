using JustyBase.Ai.Git;
using JustyBase.Ai.Models;

namespace JustyBase.Ai.Tests;

public sealed class GitCommitPromptBuilderTests
{
    [Fact]
    public void Build_ContainsThreeFewShotExamplesAndChangeContext()
    {
        var prompt = GitCommitPromptBuilder.Build(" M src/A.cs");

        Assert.Contains("### Example 1", prompt);
        Assert.Contains("### Example 2", prompt);
        Assert.Contains("### Example 3", prompt);
        Assert.Contains("src/A.cs", prompt);
        Assert.Contains("Commit message:", prompt);
    }

    [Fact]
    public void CleanMessage_StripsFencesAndPrefixes()
    {
        var cleaned = GitCommitPromptBuilder.CleanMessage("```\nCommit message: Fix the bug\n```");

        Assert.Equal("Fix the bug", cleaned);
    }

    [Fact]
    public void CleanMessage_StopsAtNextExampleSection()
    {
        var cleaned = GitCommitPromptBuilder.CleanMessage("Add tests\n### Example 2\nChanges:...");

        Assert.Equal("Add tests", cleaned);
    }

    [Fact]
    public void CleanMessage_RejectsInstructionEcho()
    {
        Assert.Equal(string.Empty, GitCommitPromptBuilder.CleanMessage("Write a concise git commit message:"));
        Assert.Equal(string.Empty, GitCommitPromptBuilder.CleanMessage("- imperative subject\n- optional short body"));
    }

    [Fact]
    public void AntiPrompts_ContainFimTokensAndSections()
    {
        Assert.Contains("<|fim_middle|>", GitCommitPromptBuilder.AntiPrompts);
        Assert.Contains("\nChanges:", GitCommitPromptBuilder.AntiPrompts);
    }
}

public sealed class ChatPresetsTests
{
    [Fact]
    public void All_ContainsBalancedPreciseCreativeCustom()
    {
        Assert.Equal(["balanced", "precise", "creative", "custom"], ChatPresets.All.Select(p => p.Id));
    }

    [Fact]
    public void AllModes_ContainsExpertSqlFixSimple()
    {
        Assert.Equal(["expert", "sqlfix", "simple"], ChatPresets.AllModes.Select(m => m.Id));
    }
}
