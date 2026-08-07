using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class LocalStreamResponseFormatterTests
{
    [Fact]
    public void ClientNotInitializedMessage_ShouldMatchExpectedText()
    {
        Assert.Equal("[Error: Copilot client not initialized. Please restart the application.]", LocalStreamResponseFormatter.ClientNotInitializedMessage);
    }

    [Fact]
    public void NoResponseMessage_ShouldMatchExpectedText()
    {
        Assert.Equal("[No response received. Model may be unavailable or there may be an authentication issue.]", LocalStreamResponseFormatter.NoResponseMessage);
    }

    [Fact]
    public void FormatError_ShouldWrapMessageWithPrefix()
    {
        var result = LocalStreamResponseFormatter.FormatError("boom");

        Assert.Contains("[Error:", result, StringComparison.Ordinal);
        Assert.Contains("boom", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatTimeout_ShouldIncludeTimeoutSeconds()
    {
        var result = LocalStreamResponseFormatter.FormatTimeout(120);

        Assert.Equal("\n[Response timeout - no response within 120 seconds]", result);
    }
}
