using JustyBase.Ai.Models;

namespace JustyBase.Ai.Models;

[Flags]
public enum ToolSafetyLevel
{
    None = 0,
    ReadOnly = 1,
    SchemaIntrospection = 2,
    CodePreview = 4,
    CodeModification = 8,
    DdlExecution = 16,
    DmlExecution = 32,
    Destructive = 64,
    ExternalAccess = 128,
    
    Safe = ReadOnly | SchemaIntrospection | CodePreview,
    Moderate = Safe | CodeModification,
    Dangerous = Moderate | DdlExecution | DmlExecution,
    Critical = Dangerous | Destructive | ExternalAccess,
    All = Critical
}

public sealed class AutoApproveRule
{
    public string ToolName { get; init; } = string.Empty;
    public ToolSafetyLevel SafetyLevel { get; init; } = ToolSafetyLevel.ReadOnly;
    public string? Description { get; init; }
    public bool AutoApprove { get; init; } = true;
    public bool RequirePatternMatch { get; init; } = false;
    public string? ArgsPattern { get; init; }
    public string[]? ModeRestrictions { get; init; }
    public int? MaxAutoApprovalsPerSession { get; init; }
    
    private static readonly Dictionary<string, AutoApproveRule> DefaultRules = new()
    {
        ["GetActiveDatabaseContext"] = new()
        {
            ToolName = "GetActiveDatabaseContext",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "Get active database connection info and schemas",
            AutoApprove = true
        },
        ["ListConnections"] = new()
        {
            ToolName = "ListConnections",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "List all database connections",
            AutoApprove = true
        },
        ["ListDatabases"] = new()
        {
            ToolName = "ListDatabases",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "List databases in connection",
            AutoApprove = true
        },
        ["ListSchemas"] = new()
        {
            ToolName = "ListSchemas",
            SafetyLevel = ToolSafetyLevel.SchemaIntrospection,
            Description = "List schemas in database",
            AutoApprove = true
        },
        ["BrowseSchemaObjects"] = new()
        {
            ToolName = "BrowseSchemaObjects",
            SafetyLevel = ToolSafetyLevel.SchemaIntrospection,
            Description = "Browse objects in schema",
            AutoApprove = true
        },
        ["GetCurrentSql"] = new()
        {
            ToolName = "GetCurrentSql",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "Read current SQL from editor",
            AutoApprove = true
        },
        ["GetCurrentSqlSelection"] = new()
        {
            ToolName = "GetCurrentSqlSelection",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "Read selected SQL text",
            AutoApprove = true
        },
        ["GetCurrentSqlEditorContext"] = new()
        {
            ToolName = "GetCurrentSqlEditorContext",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "Get full editor context",
            AutoApprove = true
        },
        ["SearchInCurrentSql"] = new()
        {
            ToolName = "SearchInCurrentSql",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "Search text in editor",
            AutoApprove = true
        },
        ["SearchSchemaObjects"] = new()
        {
            ToolName = "SearchSchemaObjects",
            SafetyLevel = ToolSafetyLevel.SchemaIntrospection,
            Description = "Search database schema objects",
            AutoApprove = true
        },
        ["GetObjectDefinition"] = new()
        {
            ToolName = "GetObjectDefinition",
            SafetyLevel = ToolSafetyLevel.SchemaIntrospection,
            Description = "Get DDL for database object",
            AutoApprove = true
        },
        ["GetObjectColumns"] = new()
        {
            ToolName = "GetObjectColumns",
            SafetyLevel = ToolSafetyLevel.SchemaIntrospection,
            Description = "Get column metadata",
            AutoApprove = true
        },
        ["GetObjectDependencies"] = new()
        {
            ToolName = "GetObjectDependencies",
            SafetyLevel = ToolSafetyLevel.SchemaIntrospection,
            Description = "Parse object dependencies",
            AutoApprove = true
        },
        ["GetTableMetadata"] = new()
        {
            ToolName = "GetTableMetadata",
            SafetyLevel = ToolSafetyLevel.SchemaIntrospection,
            Description = "Get distribution/organize info",
            AutoApprove = true
        },
        ["SearchSqlHistory"] = new()
        {
            ToolName = "SearchSqlHistory",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "Search execution history",
            AutoApprove = true
        },
        ["SearchExecutionLogs"] = new()
        {
            ToolName = "SearchExecutionLogs",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "Search error/warning logs",
            AutoApprove = true
        },
        ["SearchSqlRepository"] = new()
        {
            ToolName = "SearchSqlRepository",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "Search saved SQL files",
            AutoApprove = true
        },
        ["GetResultGridPreview"] = new()
        {
            ToolName = "GetResultGridPreview",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "Preview query results",
            AutoApprove = true
        },
        ["PreviewSqlEditorPatch"] = new()
        {
            ToolName = "PreviewSqlEditorPatch",
            SafetyLevel = ToolSafetyLevel.CodePreview,
            Description = "Preview code changes as diff",
            AutoApprove = true
        },
        ["ApplyPreviewedSqlEditorPatch"] = new()
        {
            ToolName = "ApplyPreviewedSqlEditorPatch",
            SafetyLevel = ToolSafetyLevel.CodeModification,
            Description = "Apply previewed changes to editor",
            AutoApprove = true
        },
        ["ExecuteSql"] = new()
        {
            ToolName = "ExecuteSql",
            SafetyLevel = ToolSafetyLevel.DmlExecution | ToolSafetyLevel.DdlExecution,
            Description = "Execute SQL on database",
            AutoApprove = false
        },
        ["CompileProcedure"] = new()
        {
            ToolName = "CompileProcedure",
            SafetyLevel = ToolSafetyLevel.DdlExecution,
            Description = "Compile stored procedure",
            AutoApprove = false
        },
        ["CompileView"] = new()
        {
            ToolName = "CompileView",
            SafetyLevel = ToolSafetyLevel.DdlExecution,
            Description = "Compile view",
            AutoApprove = false
        },
        ["UpdateTodoList"] = new()
        {
            ToolName = "UpdateTodoList",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "Update task list",
            AutoApprove = true
        },
        ["AttemptCompletion"] = new()
        {
            ToolName = "AttemptCompletion",
            SafetyLevel = ToolSafetyLevel.ReadOnly,
            Description = "Report task completion",
            AutoApprove = true
        }
    };

    public static IReadOnlyDictionary<string, AutoApproveRule> AllRules => DefaultRules;

    public static AutoApproveRule? GetRule(string toolName)
    {
        return DefaultRules.TryGetValue(toolName, out var rule) ? rule : null;
    }

    public static bool ShouldAutoApprove(string toolName, ChatMode currentMode, ToolSafetyLevel maxAllowedLevel)
    {
        var rule = GetRule(toolName);
        if (rule is null)
            return false;

        if (!rule.AutoApprove)
            return false;

        if ((rule.SafetyLevel & maxAllowedLevel) != rule.SafetyLevel)
            return false;

        if (rule.ModeRestrictions is not null && rule.ModeRestrictions.Length > 0)
        {
            var modeSlug = currentMode.ToSlug();
            if (Array.IndexOf(rule.ModeRestrictions, modeSlug) < 0)
                return false;
        }

        return true;
    }

    public static ToolSafetyLevel GetMaxAllowedLevel(ChatMode mode) => mode switch
    {
        ChatMode.Expert => ToolSafetyLevel.All,
        ChatMode.SqlFix => ToolSafetyLevel.CodePreview | ToolSafetyLevel.ReadOnly,
        ChatMode.Simple => ToolSafetyLevel.Safe,
        _ => ToolSafetyLevel.Safe
    };
}
