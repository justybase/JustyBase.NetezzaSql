using JustyBase.Ai.Chat;
using JustyBase.Ai.Ports;
using JustyBase.Ai.Services;
using Microsoft.Extensions.AI;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace JustyBase.Ai.Tests;

/// <summary>
/// Proves that switching the active backend actually swaps the exposed model list and
/// reasoning-effort options (headless, no processes spawned — local backends only).
/// </summary>
public sealed class BackendModelListSwitchingTests
{
    [Fact]
    public async Task SwitchBackend_OpenAiToEmbedded_SwitchesModelListAndReasoningEfforts()
    {
        using var service = CreateService(
            new FakeBackend("openai-compatible", "OpenAI Compatible", "llama-3.1-8b", "deepseek-v3"),
            new FakeBackend("embedded", "Embedded (local)", "qwen3.5-4b"));

        Assert.True(await service.SwitchBackendAsync("openai-compatible"));
        Assert.Equal(["llama-3.1-8b", "deepseek-v3"], await service.GetAvailableModelsAsync());
        Assert.Empty(await service.GetAvailableReasoningEffortsAsync());

        Assert.True(await service.SwitchBackendAsync("embedded"));
        Assert.Equal(["qwen3.5-4b"], await service.GetAvailableModelsAsync());
        Assert.Equal(["low", "medium", "high"], await service.GetAvailableReasoningEffortsAsync());

        Assert.Equal("embedded", service.ActiveBackendId);
        Assert.True(service.IsConnected);
    }

    [Fact]
    public async Task SwitchBackend_EmbeddedToOpenAi_ResetsReasoningEfforts()
    {
        using var service = CreateService(
            new FakeBackend("openai-compatible", "OpenAI Compatible", "m1"),
            new FakeBackend("embedded", "Embedded (local)", "m2"));

        await service.SwitchBackendAsync("embedded");
        Assert.Equal(["low", "medium", "high"], await service.GetAvailableReasoningEffortsAsync());

        await service.SwitchBackendAsync("openai-compatible");
        Assert.Empty(await service.GetAvailableReasoningEffortsAsync());
        Assert.Equal(["m1"], await service.GetAvailableModelsAsync());
    }

    [Fact]
    public async Task SwitchBackend_UnknownBackend_FailsAndPreservesPreviousBackend()
    {
        using var service = CreateService(new FakeBackend("embedded", "Embedded (local)", "m1"));

        Assert.True(await service.SwitchBackendAsync("embedded"));
        Assert.False(await service.SwitchBackendAsync("does-not-exist"));
        // A failed switch keeps the previously-connected backend active.
        Assert.Equal("embedded", service.ActiveBackendId);
        Assert.True(service.IsConnected);
        Assert.Equal(["m1"], await service.GetAvailableModelsAsync());
    }

    private static LocalChatService CreateService(params ILocalChatBackend[] backends)
    {
        var factory = new LocalChatClientFactory(backends);
        var settings = new ChatSettings();
        var codex = new CodexAppServerClient(new FakeEnvironment(), EmptySimpleLogger.Instance);
        return new LocalChatService(
            EmptySimpleLogger.Instance,
            new FakeChatSettingsStore(settings),
            new FakeDatabaseAccessProvider(),
            EmptySqlDiagnosticsProvider.Instance,
            factory,
            new FakeStateProvider(),
            new LocalModelConfigurationService(factory),
            codex,
            new SqlExecutionErrorStore(),
            new FakeDispatcher());
    }

    private sealed class FakeBackend : ILocalChatBackend
    {
        private readonly List<string> _models;

        public FakeBackend(string id, string displayName, params string[] models)
        {
            Id = id;
            DisplayName = displayName;
            _models = models.ToList();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public Uri Endpoint { get; set; } = new("http://127.0.0.1:0");

        public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<List<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult(_models.ToList());
        public IChatClient CreateChatClient(string modelId, bool enableFunctionInvocation = true)
            => throw new NotImplementedException();
    }

    private sealed class FakeChatSettingsStore : IChatSettingsStore
    {
        public FakeChatSettingsStore(ChatSettings settings) => Settings = settings;
        public ChatSettings Settings { get; }
        public void Update(Action<ChatSettings> mutate) => mutate(Settings);
    }

    private sealed class FakeDatabaseAccessProvider : IChatDatabaseAccessProvider
    {
        public IChatDatabaseAccess? GetDatabaseAccess(string connectionName) => null;
    }

    private sealed class FakeStateProvider : ILocalStateProvider
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

    private sealed class FakeDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEnvironment : IChatEnvironment
    {
        public string ConfigDirectory => Path.GetTempPath();
    }
}

/// <summary>Proves the OpenAI-compatible client serializes reasoning_effort for the embedded backend only.</summary>
public sealed class OpenAiReasoningEffortTests
{
    [Fact]
    public async Task EmbeddedClient_SendsReasoningEffort_WhenProvided()
    {
        var handler = new CapturingHandler();
        var client = new OpenAiCompatibleChatClient(
            new Uri("http://localhost:1234/v1"),
            "qwen3.5-4b",
            httpClient: new HttpClient(handler),
            sendReasoningEffort: true);

        var options = new ChatOptions { AdditionalProperties = new() { ["reasoning_effort"] = "high" } };
        await CollectAsync(client, options);

        Assert.Contains("\"reasoning_effort\":\"high\"", handler.LastBody, StringComparison.Ordinal);
        // think:false would disable thinking — it must be omitted when a budget is requested.
        Assert.DoesNotContain("\"think\":false", handler.LastBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbeddedClient_OmitsReasoningEffort_WhenNotProvided()
    {
        var handler = new CapturingHandler();
        var client = new OpenAiCompatibleChatClient(
            new Uri("http://localhost:1234/v1"),
            "qwen3.5-4b",
            httpClient: new HttpClient(handler),
            sendReasoningEffort: true);

        await CollectAsync(client, options: null);

        Assert.DoesNotContain("reasoning_effort", handler.LastBody, StringComparison.OrdinalIgnoreCase);
        // Without a budget, the default think suppression stays in place.
        Assert.Contains("\"think\":false", handler.LastBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAiCompatibleClient_NeverSendsReasoningEffort()
    {
        var handler = new CapturingHandler();
        var client = new OpenAiCompatibleChatClient(
            new Uri("http://localhost:1234/v1"),
            "llama-3.1-8b",
            httpClient: new HttpClient(handler),
            sendReasoningEffort: false);

        var options = new ChatOptions { AdditionalProperties = new() { ["reasoning_effort"] = "high" } };
        await CollectAsync(client, options);

        Assert.DoesNotContain("reasoning_effort", handler.LastBody, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CollectAsync(OpenAiCompatibleChatClient client, ChatOptions? options)
    {
        var messages = new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, "hi") };
        await foreach (var _ in client.GetStreamingResponseAsync(messages, options))
        {
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: [DONE]\n", Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
