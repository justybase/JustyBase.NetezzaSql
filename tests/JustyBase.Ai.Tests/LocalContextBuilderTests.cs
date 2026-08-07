using JustyBase.Ai.Models;
using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public class LocalContextBuilderTests
{
    [Fact]
    public void BuildPromptWithActiveEditorContext_NullContext_ReturnsOriginalPrompt()
    {
        var result = LocalContextBuilder.BuildPromptWithActiveEditorContext("my prompt", null);
        Assert.Equal("my prompt", result);
    }

    [Fact]
    public void BuildPromptWithActiveEditorContext_NoSelection_ReturnsOriginalPrompt()
    {
        var context = ("SELECT 1", "", 0, 0, 3);
        var result = LocalContextBuilder.BuildPromptWithActiveEditorContext("my prompt", context);
        Assert.Equal("my prompt", result);
    }

    [Fact]
    public void BuildPromptWithActiveEditorContext_WithSelection_ContainsMarkers()
    {
        var context = ("SELECT 1 FROM t", "SELECT 1", 0, 8, 8);
        var result = LocalContextBuilder.BuildPromptWithActiveEditorContext("my prompt", context);
        Assert.Contains("[ACTIVE_SQL_EDITOR_CONTEXT]", result);
        Assert.Contains("[SELECTED_SQL]", result);
        Assert.Contains("SELECT 1", result);
    }

    [Fact]
    public void BuildPromptWithConversationContext_SingleMessage_ReturnsOriginalPrompt()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "hello", Timestamp = DateTime.Now }
        };

        var result = LocalContextBuilder.BuildPromptWithConversationContext(messages, "prompt");
        Assert.Equal("prompt", result);
    }

    [Fact]
    public void BuildPromptWithConversationContext_MultipleMessages_AddsContextSection()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "first question", Timestamp = DateTime.Now },
            new() { Role = "assistant", Content = "first answer", Timestamp = DateTime.Now },
            new() { Role = "user", Content = "second question", Timestamp = DateTime.Now }
        };

        var result = LocalContextBuilder.BuildPromptWithConversationContext(messages, "prompt");
        Assert.Contains("[RECENT_CHAT_CONTEXT]", result);
        Assert.Contains("USER: first question", result);
        Assert.Contains("ASSISTANT: first answer", result);
    }

    [Fact]
    public void BuildPromptWithConversationContext_TruncatesLongMessages()
    {
        var longContent = new string('x', 5000);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = longContent, Timestamp = DateTime.Now },
            new() { Role = "assistant", Content = "ok", Timestamp = DateTime.Now }
        };

        var result = LocalContextBuilder.BuildPromptWithConversationContext(messages, "prompt", maxCharsPerMessage: 100);
        Assert.Contains("...[truncated]", result);
    }

    [Fact]
    public void CompactContextIfNeeded_BelowThreshold_ReturnsSameList()
    {
        var messages = Enumerable.Range(0, 10)
            .Select(i => new ChatMessage { Role = "user", Content = $"msg {i}", Timestamp = DateTime.Now })
            .ToList();

        var result = LocalContextBuilder.CompactContextIfNeeded(messages, maxMessages: 15);
        Assert.Same(messages, result);
    }

    [Fact]
    public void CompactContextIfNeeded_AboveThreshold_CompactsOlderMessages()
    {
        var messages = Enumerable.Range(0, 20)
            .Select(i => new ChatMessage
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"message {i}",
                Timestamp = DateTime.Now
            })
            .ToList();

        var result = LocalContextBuilder.CompactContextIfNeeded(messages, maxMessages: 10, keepRecent: 5);

        // Should have 2 summary messages + 5 kept recent = 7
        Assert.Equal(7, result.Count);
        Assert.Contains("[CONVERSATION_SUMMARY]", result[0].Content);
        Assert.Equal("message 19", result[^1].Content);
    }

    [Fact]
    public void CompactContextIfNeeded_SummaryCounts_AreCorrect()
    {
        var messages = new List<ChatMessage>();
        for (int i = 0; i < 20; i++)
        {
            messages.Add(new ChatMessage
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"message {i}",
                Timestamp = DateTime.Now
            });
        }

        var result = LocalContextBuilder.CompactContextIfNeeded(messages, maxMessages: 10, keepRecent: 3);

        var summary = result[0].Content;
        Assert.Contains("User asked", summary);
        Assert.Contains("Assistant provided", summary);
    }
}
