namespace JustyBase.Ai.Ports;

/// <summary>Object kinds the schema-browsing chat tools can search for.</summary>
public enum ChatObjectType
{
    Table,
    View,
    Procedure,
    Function,
    ExternalTable,
    Synonym,
    Fluid,
    Index,
    Partition,
    Other
}

public static class ChatObjectTypeExtensions
{
    public static string ToSlug(this ChatObjectType type) => type switch
    {
        ChatObjectType.Table => "table",
        ChatObjectType.View => "view",
        ChatObjectType.Procedure => "procedure",
        ChatObjectType.Function => "function",
        ChatObjectType.ExternalTable => "external",
        ChatObjectType.Synonym => "synonym",
        ChatObjectType.Fluid => "fluid",
        ChatObjectType.Index => "index",
        ChatObjectType.Partition => "partition",
        _ => "other"
    };

    public static ChatObjectType FromSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return ChatObjectType.Other;
        }

        return slug.ToLowerInvariant() switch
        {
            "table" or "tables" => ChatObjectType.Table,
            "view" or "views" => ChatObjectType.View,
            "procedure" or "procedures" or "proc" => ChatObjectType.Procedure,
            "function" or "functions" or "func" => ChatObjectType.Function,
            "external" or "external table" or "external tables" => ChatObjectType.ExternalTable,
            "synonym" or "synonyms" => ChatObjectType.Synonym,
            "fluid" => ChatObjectType.Fluid,
            "index" or "indexes" or "indices" => ChatObjectType.Index,
            "partition" or "partitions" => ChatObjectType.Partition,
            _ => ChatObjectType.Other
        };
    }
}

/// <summary>Database schema object as seen by the chat schema tools.</summary>
public sealed record ChatDatabaseObject(string Name, string? Description);

/// <summary>Database column metadata as seen by the chat schema tools.</summary>
public sealed record ChatDatabaseColumn(string Name, string FullTypeName);

/// <summary>
/// UI-agnostic database access surface used by the chat tool executor and state
/// provider. Hosts adapt their own database layer onto this port.
/// </summary>
public interface IChatDatabaseAccess
{
    /// <summary>Default database of the underlying connection.</summary>
    string Database { get; }

    IReadOnlyList<string> GetSchemas(string databaseName, string schemaPattern);

    IReadOnlyList<ChatDatabaseObject> GetDbObjects(
        string databaseName,
        string schemaName,
        string objectPattern,
        ChatObjectType type);

    IReadOnlyList<ChatDatabaseColumn> GetColumns(
        string databaseName,
        string schemaName,
        string objectName,
        string columnPattern);

    Task<string?> GetCreateTableTextAsync(string database, string schema, string table);

    Task<string?> GetCreateViewTextAsync(string database, string schema, string view);

    Task<string?> GetCreateProcedureTextAsync(string database, string schema, string procedure);

    Task<string?> GetCreateExternalTextAsync(string database, string schema, string externalTable);

    Task<string?> GetCreateSynonymTextAsync(string database, string schema, string synonym);

    Task<string?> GetCreateIndexTextAsync(string database, string schema, string index);

    Task<string?> GetCreatePartitionTextAsync(string database, string schema, string partition);

    string GetCheckDistributeText(string database, string schema, string table);

    /// <summary>
    /// Executes SQL that must not read or return result rows (used by the
    /// approval-gated execute_sql chat tool). Returns the affected-row count.
    /// </summary>
    Task<int> ExecuteNonQueryAsync(string sql, string databaseName, CancellationToken cancellationToken = default);

    /// <summary>Netezza-specific distribution columns; null when unavailable/unsupported.</summary>
    IReadOnlyList<string>? TryGetDistributionColumns(string database, string schema, string table);

    /// <summary>Netezza-specific organize columns; null when unavailable/unsupported.</summary>
    IReadOnlyList<string>? TryGetOrganizeColumns(string database, string schema, string table);
}

/// <summary>Resolves the database access surface for a named connection.</summary>
public interface IChatDatabaseAccessProvider
{
    IChatDatabaseAccess? GetDatabaseAccess(string connectionName);
}
