using Microsoft.Extensions.AI;

namespace JustyBase.Ai.Services;

public interface ILocalToolExecutor
{
    Task<string> GetCurrentSql();
    string GetActiveDatabaseContext();
    string ListSchemas(string? databaseName = null, int limit = 50);
    string BrowseSchemaObjects(string schemaName, string objectType = "all", int limit = 100);
    string SearchSchemaObjects(string pattern, string? objectType = null, string? schemaName = null, int limit = 50);
    string GetObjectColumns(string objectName, string? schemaName = null, string? databaseName = null, int limit = 200);
    Task<string> GetObjectDefinition(string objectName, string? objectType = null, string? schemaName = null, string? databaseName = null, int maxChars = 20000);
    Task<string> GetTableMetadata(string tableName, string? schemaName = null, string? databaseName = null, bool includeStatsPreview = false, int rowLimit = 20);
    Task<string> GetCurrentSqlEditorContext(int maxChars = 20000);
    string GetNetezzaReference(string topic = "all");
    Task<string> GetDiagnostics(string? severityFilter = null, int limit = 50);
    Task<string> GetLastExecutionError();
    Task<string> ExportSchema(string? schemaName = null, string? objectType = null, int maxChars = 30000);
    Task<string> ExecuteSql(string sql);
    Task<string> ApplySqlFix(string proposedSql);
    List<AIFunction> BuildToolList();
    Task<string> ExecuteToolAsync(string toolName, string argumentsJson);

    void SetCurrentSqlProvider(Func<string?> provider);
    void SetActiveSqlContextProvider(Func<(string ConnectionName, string DatabaseName)?> provider);
    void SetSqlEditorContextProvider(Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?> provider);
    void SetSqlEditorBufferUpdater(Func<string, bool> updater);
}
