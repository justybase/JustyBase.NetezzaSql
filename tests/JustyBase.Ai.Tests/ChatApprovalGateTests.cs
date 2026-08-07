using JustyBase.Ai.Chat;
using JustyBase.Ai.Ports;
using JustyBase.Ai.Services;
using Microsoft.Extensions.AI;

namespace JustyBase.Ai.Tests;

/// <summary>
/// Approval gates for write tools (ExecuteSql / ApplySqlFix): a denial must produce the
/// "[... denied ...]" message WITHOUT touching the database port; approval must run it.
/// Exercised through the real backend wiring (OpenAiCompatibleChatBackend.ToolExecutor).
/// </summary>
public sealed class ChatApprovalGateTests
{
    [Fact]
    public async Task ExecuteSql_Denied_DoesNotTouchDatabasePort()
    {
        var (dbAccess, openAi, service) = CreateService();
        service.SetToolConfirmationHandler((_, _) => Task.FromResult(false));

        var result = await openAi.ToolExecutor!("execute_sql", "{\"sql\":\"DELETE FROM t\"}");

        Assert.Equal("[SQL execution denied by the user.]", result);
        Assert.Null(dbAccess.LastExecutedSql);
    }

    [Fact]
    public async Task ExecuteSql_Approved_ExecutesSql()
    {
        var (dbAccess, openAi, service) = CreateService();
        service.SetToolConfirmationHandler((_, _) => Task.FromResult(true));

        var result = await openAi.ToolExecutor!("execute_sql", "{\"sql\":\"DELETE FROM t\"}");

        Assert.Contains("Affected rows: 3", result);
        Assert.Equal("DELETE FROM t", dbAccess.LastExecutedSql);
    }

    [Fact]
    public async Task ApplySqlFix_Denied_DoesNotUpdateEditor()
    {
        var (dbAccess, openAi, service) = CreateService();
        service.SetCurrentSqlProvider(() => "SELECT 1");
        var updated = false;
        service.SetSqlEditorBufferUpdater(_ => updated = true);
        service.SetToolConfirmationHandler((_, _) => Task.FromResult(false));

        var result = await openAi.ToolExecutor!("apply_sql_document_change", "{\"proposedSql\":\"SELECT 2\"}");

        Assert.Equal("[SQL change denied by the user.]", result);
        Assert.False(updated);
    }

    [Fact]
    public async Task ApplySqlFix_Approved_UpdatesEditorBuffer()
    {
        var (dbAccess, openAi, service) = CreateService();
        service.SetCurrentSqlProvider(() => "SELECT 1");
        string? applied = null;
        service.SetSqlEditorBufferUpdater(sql => { applied = sql; return true; });
        service.SetToolConfirmationHandler((_, _) => Task.FromResult(true));

        var result = await openAi.ToolExecutor!("apply_sql_document_change", "{\"proposedSql\":\"SELECT 2\"}");

        Assert.Contains("Patch applied", result);
        Assert.Equal("SELECT 2", applied);
    }

    [Fact]
    public async Task ReadTool_ExecutesWithoutApprovalGate()
    {
        var (dbAccess, openAi, service) = CreateService();
        service.SetActiveSqlContextProvider(() => ("conn1", "db1"));
        service.SetToolConfirmationHandler((_, _) => throw new InvalidOperationException("read tools must not ask for approval"));

        var result = await openAi.ToolExecutor!("list_schemas", "{}");

        Assert.Contains("db1.ADMIN", result);
        Assert.Contains("db1.PUBLIC", result);
    }

    private static (FakeDbAccess DbAccess, OpenAiCompatibleChatBackend OpenAi, LocalChatService Service) CreateService()
    {
        var dbAccess = new FakeDbAccess();
        var settings = new ChatSettings { AiChatOpenAiCompatibleEndpoint = "http://localhost:1234/v1" };
        var openAi = new OpenAiCompatibleChatBackend(new TestChatSettingsStore(settings));
        var factory = new LocalChatClientFactory(new ILocalChatBackend[] { openAi, new FakeLocalBackend("embedded", "Embedded") });
        var codex = new CodexAppServerClient(new TestEnvironment(), EmptySimpleLogger.Instance);
        var service = new LocalChatService(
            EmptySimpleLogger.Instance,
            new TestChatSettingsStore(settings),
            new TestDatabaseAccessProvider(dbAccess),
            EmptySqlDiagnosticsProvider.Instance,
            factory,
            new TestStateProvider(),
            new LocalModelConfigurationService(factory),
            codex,
            new SqlExecutionErrorStore(),
            new TestDispatcher());
        service.SetActiveSqlContextProvider(() => ("conn1", "db1"));
        return (dbAccess, openAi, service);
    }

    internal sealed class FakeDbAccess : IChatDatabaseAccess
    {
        public string Database => "db1";
        public string? LastExecutedSql { get; private set; }

        public IReadOnlyList<string> GetSchemas(string databaseName, string schemaPattern) => ["ADMIN", "PUBLIC"];
        public IReadOnlyList<ChatDatabaseObject> GetDbObjects(string databaseName, string schemaName, string objectPattern, ChatObjectType type)
            => schemaName == "PUBLIC" ? [new ChatDatabaseObject("MY_TABLE", "sample")] : [];
        public IReadOnlyList<ChatDatabaseColumn> GetColumns(string databaseName, string schemaName, string objectName, string columnPattern)
            => [new ChatDatabaseColumn("ID", "INTEGER")];
        public Task<string?> GetCreateTableTextAsync(string database, string schema, string table) => Task.FromResult<string?>("CREATE TABLE T (ID INTEGER) DISTRIBUTE ON (ID);");
        public Task<string?> GetCreateViewTextAsync(string database, string schema, string view) => Task.FromResult<string?>(null);
        public Task<string?> GetCreateProcedureTextAsync(string database, string schema, string procedure) => Task.FromResult<string?>(null);
        public Task<string?> GetCreateExternalTextAsync(string database, string schema, string externalTable) => Task.FromResult<string?>(null);
        public Task<string?> GetCreateSynonymTextAsync(string database, string schema, string synonym) => Task.FromResult<string?>(null);
        public Task<string?> GetCreateIndexTextAsync(string database, string schema, string index) => Task.FromResult<string?>(null);
        public Task<string?> GetCreatePartitionTextAsync(string database, string schema, string partition) => Task.FromResult<string?>(null);
        public string GetCheckDistributeText(string database, string schema, string table) => string.Empty;
        public Task<int> ExecuteNonQueryAsync(string sql, string databaseName, CancellationToken cancellationToken = default)
        {
            LastExecutedSql = sql;
            return Task.FromResult(3);
        }

        public IReadOnlyList<string>? TryGetDistributionColumns(string database, string schema, string table) => ["ID"];
        public IReadOnlyList<string>? TryGetOrganizeColumns(string database, string schema, string table) => null;
    }

    internal sealed class TestChatSettingsStore : IChatSettingsStore
    {
        public TestChatSettingsStore(ChatSettings settings) => Settings = settings;
        public ChatSettings Settings { get; }
        public void Update(Action<ChatSettings> mutate) => mutate(Settings);
    }

    internal sealed class TestDatabaseAccessProvider : IChatDatabaseAccessProvider
    {
        private readonly IChatDatabaseAccess _access;
        public TestDatabaseAccessProvider(IChatDatabaseAccess access) => _access = access;
        public IChatDatabaseAccess? GetDatabaseAccess(string connectionName) => _access;
    }

    internal sealed class TestStateProvider : ILocalStateProvider
    {
        public void SetActiveSqlContextProvider(Func<(string ConnectionName, string DatabaseName)?> provider) { }
        public void SetSqlEditorContextProvider(Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?> provider) { }
        public (string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)? GetSqlEditorContextSnapshot() => null;
        public string BuildDatabaseContextSection() => string.Empty;
        public bool TryGetActiveDatabaseAccess(out IChatDatabaseAccess? access, out string connectionName, out string databaseName, out string errorMessage)
        {
            access = null;
            connectionName = string.Empty;
            databaseName = string.Empty;
            errorMessage = string.Empty;
            return false;
        }

        public string BuildAttachmentMetadataSection(List<Models.ChatAttachment>? attachments) => string.Empty;
    }

    internal sealed class TestDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    internal sealed class TestEnvironment : IChatEnvironment
    {
        public string ConfigDirectory => Path.GetTempPath();
    }

    internal sealed class FakeLocalBackend : ILocalChatBackend
    {
        public FakeLocalBackend(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public Uri Endpoint { get; set; } = new("http://127.0.0.1:0");
        public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<List<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult(new List<string> { "m1" });
        public IChatClient CreateChatClient(string modelId, bool enableFunctionInvocation = true) => throw new NotImplementedException();
    }
}
