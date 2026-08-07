namespace JustyBase.Ai.Models;

public enum ChatMode
{
    Expert,
    SqlFix,
    Simple
}

public static class ChatModeExtensions
{
    public static string ToSlug(this ChatMode mode) => mode switch
    {
        ChatMode.Expert => "expert",
        ChatMode.SqlFix => "sqlfix",
        ChatMode.Simple => "simple",
        _ => "expert"
    };

    public static string ToDisplayName(this ChatMode mode) => mode switch
    {
        ChatMode.Expert => "Expert",
        ChatMode.SqlFix => "SQL Fix",
        ChatMode.Simple => "Simple",
        _ => "Expert"
    };

    public static ChatMode FromSlug(string slug) => slug?.ToLowerInvariant() switch
    {
        "expert" or "sql" or "sql-expert" => ChatMode.Expert,
        "sqlfix" or "fix" or "sql-fix" => ChatMode.SqlFix,
        "simple" or "plain" => ChatMode.Simple,
        _ => ChatMode.Expert
    };
}

public sealed class ChatModeConfig
{
    public ChatMode Mode { get; init; }
    public required string Slug { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string RoleDefinition { get; init; }
    public ToolGroup AllowedTools { get; init; } = ToolGroup.All;
    public string? WhenToUse { get; init; }

    public static readonly ChatModeConfig Expert = new()
    {
        Mode = ChatMode.Expert,
        Slug = "expert",
        DisplayName = "Expert",
        Description = "Full-featured SQL development assistant with schema exploration and SQL editing tools.",
        RoleDefinition = "You are an expert Netezza SQL assistant. Use tools to explore schema and fix SQL. Always use DATABASE.SCHEMA.OBJECT naming.",
        AllowedTools = ToolGroup.Read | ToolGroup.Schema | ToolGroup.Edit,
        WhenToUse = "Use for general SQL development, query optimization, schema exploration, and data analysis."
    };

    public static readonly ChatModeConfig SqlFix = new()
    {
        Mode = ChatMode.SqlFix,
        Slug = "sqlfix",
        DisplayName = "SQL Fix",
        Description = "Automated SQL diagnostics fixer — reads diagnostics, applies fixes, rechecks.",
        RoleDefinition = "You are an SQL diagnostics fixer. Read heuristic diagnostics as advisory evidence, verify them against SQL/schema, fix SQL issues, recheck. Output corrected SQL only.",
        AllowedTools = ToolGroup.Read | ToolGroup.Edit,
        WhenToUse = "Use for automatically fixing SQL errors and warnings from the diagnostics panel."
    };

    public static readonly ChatModeConfig Simple = new()
    {
        Mode = ChatMode.Simple,
        Slug = "simple",
        DisplayName = "Simple",
        Description = "Plain chat — no tools, no schema. Current SQL is provided in context.",
        RoleDefinition = "",
        AllowedTools = ToolGroup.None,
        WhenToUse = "Use for plain conversation, quick questions, or when you don't need SQL tools."
    };

    public static readonly IReadOnlyList<ChatModeConfig> AllModes = [
        Expert,
        SqlFix,
        Simple
    ];

    public static ChatModeConfig GetByMode(ChatMode mode) => mode switch
    {
        ChatMode.Expert => Expert,
        ChatMode.SqlFix => SqlFix,
        ChatMode.Simple => Simple,
        _ => Expert
    };

    public static ChatModeConfig GetBySlug(string slug) =>
        AllModes.FirstOrDefault(m => m.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)) ?? Expert;
}

[Flags]
public enum ToolGroup
{
    None = 0,
    Read = 1,
    Edit = 2,
    Schema = 4,
    All = Read | Edit | Schema
}
