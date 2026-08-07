using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class LocalToolStatusFormatterTests
{
    [Fact]
    public void FormatAutoApproved_ShouldFallbackToUnknown_WhenToolNameMissing()
    {
        var result = LocalToolStatusFormatter.FormatAutoApproved(null);

        Assert.Equal("Tool: unknown", result);
    }

    [Fact]
    public void FormatToolStart_ShouldIncludeToolName()
    {
        var result = LocalToolStatusFormatter.FormatToolStart("read_file");

        Assert.Equal("Tool start: read_file", result);
    }

    [Fact]
    public void FormatToolStart_ShouldFallbackToUnknown_WhenToolNameMissing()
    {
        var result = LocalToolStatusFormatter.FormatToolStart(" ");

        Assert.Equal("Tool start: unknown", result);
    }

    [Fact]
    public void FormatToolProgress_ShouldReturnFallback_WhenMessageEmpty()
    {
        var result = LocalToolStatusFormatter.FormatToolProgress(" ");

        Assert.Equal("Tool progress update", result);
    }

    [Fact]
    public void FormatToolProgress_ShouldReturnMessage_WhenMessageProvided()
    {
        const string progressMessage = "Running step 2/4";
        var result = LocalToolStatusFormatter.FormatToolProgress(progressMessage);

        Assert.Equal(progressMessage, result);
    }

    [Theory]
    [InlineData(true, "Tool completed")]
    [InlineData(false, "Tool failed")]
    public void FormatToolCompletion_ShouldMapBooleanToMessage(bool success, string expected)
    {
        var result = LocalToolStatusFormatter.FormatToolCompletion(success);

        Assert.Equal(expected, result);
    }
}
