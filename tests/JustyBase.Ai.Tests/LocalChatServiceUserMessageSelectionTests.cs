using System.Reflection;
using JustyBase.Ai.Models;
using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class LocalChatServiceUserMessageSelectionTests
{
    [Fact]
    public void FindLastUserMessage_ShouldMatchRoleCaseInsensitively()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "assistant", Content = "A1" },
            new() { Role = "USER", Content = "U1" }
        };

        var result = InvokeFindLastUserMessage(messages);

        Assert.NotNull(result);
        Assert.Equal("U1", result.Content);
    }

    [Fact]
    public void FindLastUserMessage_ShouldReturnLastUserMessage()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "U1" },
            new() { Role = "assistant", Content = "A1" },
            new() { Role = "User", Content = "U2" }
        };

        var result = InvokeFindLastUserMessage(messages);

        Assert.NotNull(result);
        Assert.Equal("U2", result.Content);
    }

    [Fact]
    public void FindLastUserMessage_ShouldReturnNull_WhenNoUserMessageExists()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "assistant", Content = "A1" }
        };

        var result = InvokeFindLastUserMessage(messages);

        Assert.Null(result);
    }

    private static ChatMessage? InvokeFindLastUserMessage(List<ChatMessage> messages)
    {
        var method = typeof(LocalChatService).GetMethod("FindLastUserMessage", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("FindLastUserMessage method not found.");
        return method.Invoke(null, [messages]) as ChatMessage;
    }
}
