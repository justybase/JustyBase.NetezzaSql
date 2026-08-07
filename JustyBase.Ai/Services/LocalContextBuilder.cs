using JustyBase.Ai.Models;
using JustyBase.Ai.Ports;
using System.Text;

namespace JustyBase.Ai.Services;

/// <summary>
/// Builds prompt context sections for Copilot messages.
/// Extracted from CopilotChatService to keep context-assembly logic testable and separate from streaming/tool orchestration.
/// </summary>
public static class LocalContextBuilder
{
    public static string BuildPromptWithActiveEditorContext(
        string prompt,
        (string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)? context)
    {
        if (context is null || !LocalSqlEditorContextFormatter.HasValidSelection(context.Value))
        {
            return prompt;
        }

        var selectedText = LocalSqlEditorContextFormatter.GetSelectedText(context.Value);
        var marked = LocalSqlEditorContextFormatter.MarkSelectedSqlRegion(context.Value);

        return $"""
{prompt}

[ACTIVE_SQL_EDITOR_CONTEXT]
Source-of-truth: current in-memory SQL editor buffer.
SelectionStart={context.Value.SelectionStart}, SelectionLength={context.Value.SelectionLength}, CaretOffset={context.Value.CaretOffset}

[SELECTED_SQL]
{selectedText}
[/SELECTED_SQL]

[FULL_SQL_WITH_SELECTION_MARKERS]
{marked}
[/FULL_SQL_WITH_SELECTION_MARKERS]
[/ACTIVE_SQL_EDITOR_CONTEXT]
""";
    }

    public static string BuildPromptWithConversationContext(
        List<ChatMessage> messages,
        string currentPrompt,
        int maxMessages = 12,
        int maxCharsPerMessage = 4000)
    {
        var relevant = messages
            .Where(x => x is not null &&
                        (x.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                         x.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)) &&
                        !string.IsNullOrWhiteSpace(x.Content))
            .TakeLast(Math.Clamp(maxMessages, 2, 30))
            .ToList();

        if (relevant.Count <= 1)
        {
            return currentPrompt;
        }

        var sb = new StringBuilder();
        sb.AppendLine(currentPrompt);
        sb.AppendLine();
        sb.AppendLine("[RECENT_CHAT_CONTEXT]");
        foreach (var message in relevant)
        {
            var role = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "ASSISTANT" : "USER";
            var content = message.Content.Length <= maxCharsPerMessage
                ? message.Content
                : message.Content[..maxCharsPerMessage] + " ...[truncated]";
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{role}: {content}");
        }
        sb.AppendLine("[/RECENT_CHAT_CONTEXT]");
        return sb.ToString().TrimEnd();
    }

    public static List<ChatMessage> CompactContextIfNeeded(
        List<ChatMessage> messages,
        int maxMessages = 15,
        int keepRecent = 5)
    {
        if (messages.Count <= maxMessages)
        {
            return messages;
        }

        var toCompact = messages.Take(messages.Count - keepRecent).ToList();
        var toKeep = messages.TakeLast(keepRecent).ToList();

        var summaryBuilder = new StringBuilder();
        summaryBuilder.AppendLine("[CONVERSATION_SUMMARY]");
        summaryBuilder.AppendLine("Previous conversation (compactified):");

        var userQueries = toCompact
            .Where(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Content[..Math.Min(200, m.Content.Length)])
            .ToList();

        var assistantResponses = toCompact
            .Where(m => m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            .Count();

        if (userQueries.Count > 0)
        {
            summaryBuilder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"User asked {userQueries.Count} questions including:");
            foreach (var query in userQueries.Take(5))
            {
                summaryBuilder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  - {query}");
            }
        }

        summaryBuilder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Assistant provided {assistantResponses} responses.");
        summaryBuilder.AppendLine("[/CONVERSATION_SUMMARY]");

        var compactedMessages = new List<ChatMessage>
        {
            new()
            {
                Role = "user",
                Content = summaryBuilder.ToString(),
                Timestamp = DateTime.Now
            },
            new()
            {
                Role = "assistant",
                Content = "I understand the previous conversation context from the summary above.",
                Timestamp = DateTime.Now
            }
        };

        compactedMessages.AddRange(toKeep);
        return compactedMessages;
    }
}
