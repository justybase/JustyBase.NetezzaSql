using JustyBase.Ai.Chat;
using JustyBase.Ai.Models;
using JustyBase.Ai.Ports;
using JustyBase.Ai.Services;
using System.Reflection;

namespace JustyBase.Ai.Tests;

public sealed class CopilotChatIntegrationTests
{
    [Fact]
    public void CopilotAccountInfo_UsesNonSecretAccountFieldsOnly()
    {
        var account = new CopilotAccountInfo(IsAuthenticated: true, Login: "octocat", AuthType: "gh-cli", StatusMessage: "via gh");

        var secrets = typeof(CopilotAccountInfo)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("key", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(account.IsAuthenticated);
        Assert.Equal("octocat", account.Login);
        Assert.Equal("gh-cli", account.AuthType);
        Assert.Empty(secrets);
    }

    [Fact]
    public void CopilotClient_SessionIdSwitchWorksWithoutClientStart()
    {
        using var client = new CopilotClient(new TestEnvironment(), EmptySimpleLogger.Instance);

        Assert.False(client.IsRunning);
        Assert.Null(client.CopilotSessionId);

        client.SetCopilotSessionId("session-1");
        Assert.Equal("session-1", client.CopilotSessionId);

        client.SetCopilotSessionId("session-2");
        Assert.Equal("session-2", client.CopilotSessionId);

        client.SetCopilotSessionId(null);
        Assert.Null(client.CopilotSessionId);
    }

    [Fact]
    public void CopilotSessionId_StickyResumeValueSurvivesRepeatedSet()
    {
        using var client = new CopilotClient(new TestEnvironment(), EmptySimpleLogger.Instance);

        client.SetCopilotSessionId("session-a");
        client.SetCopilotSessionId("session-a");

        Assert.Equal("session-a", client.CopilotSessionId);
    }

    [Fact]
    public void CopilotTools_RespectChatModeBoundaries()
    {
        var service = CreateService();
        var simple = service.BuildCopilotTools(ChatMode.Simple).Select(t => t.Name).ToHashSet();
        var sqlFix = service.BuildCopilotTools(ChatMode.SqlFix).Select(t => t.Name).ToHashSet();
        var expert = service.BuildCopilotTools(ChatMode.Expert).Select(t => t.Name).ToHashSet();

        Assert.Empty(simple);
        Assert.Contains("GetDiagnostics", sqlFix);
        Assert.Contains("ApplySqlFix", sqlFix);
        Assert.DoesNotContain("ExecuteSql", sqlFix);
        Assert.Contains("ExecuteSql", expert);
        Assert.Contains("GetObjectDefinition", expert);
        Assert.Equal(15, expert.Count);
    }

    [Fact]
    public void CopilotToolNames_ArePascalCaseForSdkConversion()
    {
        var service = CreateService();
        var names = service.BuildCopilotTools(ChatMode.Expert).Select(t => t.Name).ToArray();

        foreach (var name in names)
        {
            Assert.True(char.IsUpper(name[0]), $"Tool '{name}' must start with an uppercase letter for CLI snake_case conversion.");
            Assert.DoesNotContain('_', name);
        }
    }

    [Fact]
    public void CopilotDeviceFlow_AlphanumericCodeIsDetectedAndPrefilled()
    {
        using var client = new CopilotClient(new TestEnvironment(), EmptySimpleLogger.Instance);
        var line = "To authenticate, visit https://github.com/login/device and enter code 529D-2183";

        var url = client.TryBuildVerificationUrl(line);

        Assert.Equal("https://github.com/login/device?user_code=529D-2183", url);
        Assert.Equal("529D-2183", client.LoginVerificationCode);
        Assert.Equal("https://github.com/login/device", client.LoginVerificationUrl);
    }

    [Fact]
    public void CopilotDeviceFlow_CodeOnSeparateLineIsRemembered()
    {
        using var client = new CopilotClient(new TestEnvironment(), EmptySimpleLogger.Instance);

        Assert.Null(client.TryBuildVerificationUrl("Enter code 7GH1-2AB9"));
        Assert.Equal("7GH1-2AB9", client.LoginVerificationCode);

        var url = client.TryBuildVerificationUrl("To authenticate, visit https://github.com/login/device");

        Assert.Equal("https://github.com/login/device?user_code=7GH1-2AB9", url);
    }

    private static LocalChatService CreateService()
    {
        var settings = new ChatSettings();
        var factory = new LocalChatClientFactory([]);
        var environment = new TestEnvironment();
        return new LocalChatService(
            EmptySimpleLogger.Instance,
            new TestChatSettingsStore(settings),
            new TestDatabaseAccessProvider(),
            EmptySqlDiagnosticsProvider.Instance,
            factory,
            new TestStateProvider(),
            new LocalModelConfigurationService(factory),
            new CodexAppServerClient(environment, EmptySimpleLogger.Instance),
            new CopilotClient(environment, EmptySimpleLogger.Instance),
            new SqlExecutionErrorStore(),
            new TestDispatcher());
    }

    private sealed class TestChatSettingsStore : IChatSettingsStore
    {
        public TestChatSettingsStore(ChatSettings settings) => Settings = settings;
        public ChatSettings Settings { get; }
        public void Update(Action<ChatSettings> mutate) => mutate(Settings);
    }

    private sealed class TestDatabaseAccessProvider : IChatDatabaseAccessProvider
    {
        public IChatDatabaseAccess? GetDatabaseAccess(string connectionName) => null;
    }

    private sealed class TestStateProvider : ILocalStateProvider
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

    private sealed class TestDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class TestEnvironment : IChatEnvironment
    {
        public string ConfigDirectory => Path.GetTempPath();
    }
}