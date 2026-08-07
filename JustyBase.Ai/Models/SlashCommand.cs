namespace JustyBase.Ai.Models;

public sealed class SlashCommand
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ChatMode? TargetMode { get; init; }
    public string? AutoContext { get; init; }
    public string? Action { get; init; }
    public string[]? Aliases { get; init; }
    public bool IsBuiltIn { get; init; } = true;

    public static readonly IReadOnlyList<SlashCommand> BuiltInCommands = [
        new SlashCommand
        {
            Name = "expert",
            Description = "Switch to Expert mode (full tools)",
            TargetMode = ChatMode.Expert,
            Aliases = ["sql", "sql-expert"]
        },
        new SlashCommand
        {
            Name = "sqlfix",
            Description = "Switch to SQL Fix mode — auto-fix diagnostics",
            TargetMode = ChatMode.SqlFix,
            Aliases = ["fix", "sql-fix"]
        },
        new SlashCommand
        {
            Name = "simple",
            Description = "Switch to Simple mode — plain chat, no tools",
            TargetMode = ChatMode.Simple,
            Aliases = ["plain"]
        },
        new SlashCommand
        {
            Name = "schema",
            Description = "Search schema objects in current database",
            AutoContext = "schema"
        },
        new SlashCommand
        {
            Name = "newtask",
            Description = "Start a new conversation",
            Action = "clear"
        },
    ];

    public static SlashCommand? Match(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.TrimStart('/');
        if (string.IsNullOrWhiteSpace(trimmed)) return null;

        var commandName = trimmed.Split(' ')[0].ToLowerInvariant();

        return BuiltInCommands.FirstOrDefault(cmd =>
            cmd.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase) ||
            (cmd.Aliases?.Any(a => a.Equals(commandName, StringComparison.OrdinalIgnoreCase)) ?? false));
    }

    public static IReadOnlyList<SlashCommand> GetMatchingCommands(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('/')) return [];

        var query = input[1..].ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(query)) return BuiltInCommands;

        return BuiltInCommands
            .Where(cmd =>
                cmd.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                cmd.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (cmd.Aliases?.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false))
            .ToList();
    }

    public static bool IsSlashCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var trimmed = input.TrimStart();
        return trimmed.StartsWith('/');
    }
}

public sealed class MentionContext
{
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Schema { get; init; }
    public string? Database { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }

    public string DisplayText => Type.ToLowerInvariant() switch
    {
        "schema" or "table" or "view" or "procedure" or "function" =>
            FormatQualifiedName(),
        "connection" => $"Connection: {Value}",
        "file" => $"File: {Value}",
        "sql" => "Current SQL",
        "results" => "Query Results",
        "history" => "History",
        _ => Value
    };

    private string FormatQualifiedName()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Database)) parts.Add(Database);
        if (!string.IsNullOrWhiteSpace(Schema)) parts.Add(Schema);
        if (!string.IsNullOrWhiteSpace(Value)) parts.Add(Value);
        return string.Join(".", parts);
    }

    public static MentionContext? Parse(string mention)
    {
        if (string.IsNullOrWhiteSpace(mention)) return null;

        var trimmed = mention.TrimStart('@');
        if (string.IsNullOrWhiteSpace(trimmed)) return null;

        if (trimmed.StartsWith("schema:", StringComparison.OrdinalIgnoreCase))
            return ParseQualifiedName(trimmed[7..], "schema");

        if (trimmed.StartsWith("table:", StringComparison.OrdinalIgnoreCase))
            return ParseQualifiedName(trimmed[6..], "table");

        if (trimmed.StartsWith("view:", StringComparison.OrdinalIgnoreCase))
            return ParseQualifiedName(trimmed[5..], "view");

        if (trimmed.StartsWith("connection:", StringComparison.OrdinalIgnoreCase))
            return new MentionContext { Type = "connection", Value = trimmed[11..] };

        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return new MentionContext { Type = "file", Value = trimmed[5..] };

        if (trimmed.Equals("sql", StringComparison.OrdinalIgnoreCase))
            return new MentionContext { Type = "sql", Value = "current" };

        if (trimmed.Equals("results", StringComparison.OrdinalIgnoreCase))
            return new MentionContext { Type = "results", Value = "current" };

        if (trimmed.Contains('.'))
            return ParseQualifiedName(trimmed, "schema");

        return new MentionContext { Type = "unknown", Value = trimmed };
    }

    private static MentionContext ParseQualifiedName(string qualifiedName, string defaultType)
    {
        var parts = qualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => new MentionContext { Type = defaultType, Value = parts[0] },
            2 => new MentionContext { Type = defaultType, Schema = parts[0], Value = parts[1] },
            3 => new MentionContext { Type = defaultType, Database = parts[0], Schema = parts[1], Value = parts[2] },
            _ => new MentionContext { Type = defaultType, Value = qualifiedName }
        };
    }
}
