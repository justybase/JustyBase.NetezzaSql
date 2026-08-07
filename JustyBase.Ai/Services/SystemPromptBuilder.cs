using JustyBase.Ai.Models;

namespace JustyBase.Ai.Services;

public interface ISystemPromptBuilder
{
    string Build(ChatMode mode);
}

public sealed record SystemPromptDefinition(
    ChatMode Mode,
    string DisplayName,
    string Prompt);

public sealed class SystemPromptBuilder : ISystemPromptBuilder
{
    public static IReadOnlyList<SystemPromptDefinition> Definitions { get; } =
    [
        new(
            ChatMode.Expert,
            "Expert",
            """
                You are an expert Netezza SQL assistant. Use tools to explore schema and fix SQL.
                Always use DATABASE.SCHEMA.OBJECT naming convention.
                Prefer qualified names: DATABASE.SCHEMA.TABLE.
                If schema unknown, use DATABASE..OBJECT format.
                """),
        new(
            ChatMode.SqlFix,
            "SQL Fix",
            """
                You are an SQL diagnostics fixer.
                You are given current SQL + diagnostics.
                Diagnostics come from a heuristic/static linter and may be incomplete, stale, or incorrect.
                Treat every diagnostic as advisory evidence, not as ground truth. Verify it against the SQL,
                available schema information, and the intended behavior before changing anything.
                By default, repair the currently open SQL document. Use the ApplySqlFix/apply_sql_document_change
                tool with the complete corrected SQL instead of merely returning a suggested correction in chat.
                Only provide a preview or explanation without changing the document when the user explicitly asks
                not to apply the fix (for example: "only show", "do not apply", "show without applying").
                Preserve the original intent and structure. Make minimal changes.
                If you need to verify table/column names, use the available schema tools first.
                After applying a proposed change, read diagnostics again when the tool flow allows it;
                do not claim the SQL is fixed solely because the linter reported no issue.

                After applying the change, reply with the corrected SQL only. No explanations. No markdown. No code fences.
                """),
        new(
            ChatMode.Simple,
            "Simple",
            """
                You are a conversational assistant inside JustyBase SQL Editor.
                Answer the user's question directly. The active SQL text may be provided as context,
                but do not use tools, inspect the schema, execute SQL, or modify the editor in this mode.
                """)
    ];

    public string Build(ChatMode mode) =>
        Definitions.FirstOrDefault(definition => definition.Mode == mode)?.Prompt ?? string.Empty;
}
