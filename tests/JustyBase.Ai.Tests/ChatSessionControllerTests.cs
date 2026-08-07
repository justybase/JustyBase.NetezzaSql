using JustyBase.Ai.Chat;
using JustyBase.Ai.Models;
using JustyBase.Ai.Ports;
using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

/// <summary>
/// ChatSessionController orchestration: message flow, streaming, cancel, session persistence
/// and the tool-confirmation TCS bridge — all against a scripted fake chat service.
/// </summary>
public sealed class ChatSessionControllerTests
{
    [Fact]
    public async Task Send_StreamsIntoAssistantAndPersistsSession()
    {
        var fake = new FakeChatService(["a", "b", "c"]);
        var store = new Store();
        var controller = new ChatSessionController(fake, store, EmptySimpleLogger.Instance);

        var ok = await controller.SendMessageAsync("SELECT 1", modelId: "m1");

        Assert.True(ok);
        Assert.Collection(
            controller.Messages,
            user => Assert.Equal("user", user.Role),
            assistant => Assert.Equal("assistant", assistant.Role));
        Assert.Equal("SELECT 1", controller.Messages[0].Content);
        Assert.Equal("abc", controller.Messages[1].Content);
        Assert.False(controller.Messages[1].IsStreaming);
        Assert.True(controller.Messages[1].GenerationTimeMs >= 0);
        Assert.False(controller.IsStreaming);
        Assert.Equal("m1", fake.LastModelId);
        // Session persisted with both messages.
        Assert.Single(store.Settings.ChatSessions);
        Assert.Equal(2, store.Settings.ChatSessions[0].Messages.Count);
    }

    [Fact]
    public async Task Send_UserCancel_RemovesAssistantPlaceholder()
    {
        var fake = new FakeChatService(new ScriptedStream(
            ct => Task.FromResult("x"),
            ct => Task.FromException<string>(new OperationCanceledException())));
        var store = new Store();
        var controller = new ChatSessionController(fake, store, EmptySimpleLogger.Instance);

        var ok = await controller.SendMessageAsync("do it");

        Assert.False(ok);
        // The assistant placeholder was removed; the user message stays.
        Assert.Single(controller.Messages);
        Assert.Equal("user", controller.Messages[0].Role);
        Assert.Single(store.Settings.ChatSessions[0].Messages);
    }

    [Fact]
    public async Task ToolConfirmation_Approve_CompletesHandlerWithTrue()
    {
        var fake = new FakeChatService(["ok"]);
        var controller = new ChatSessionController(fake, new Store(), EmptySimpleLogger.Instance);

        var pendingTask = fake.CapturedToolHandler!("ExecuteSql", "{\"sql\":\"DELETE FROM t\"}");
        var confirmation = controller.Messages.Last();
        Assert.True(confirmation.IsToolConfirmation);
        Assert.True(confirmation.ConfirmationPending);

        controller.ConfirmTool(true);

        Assert.True(await pendingTask);
        Assert.False(confirmation.ConfirmationPending);
        Assert.Contains("approved", confirmation.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToolConfirmation_Deny_CompletesHandlerWithFalse()
    {
        var fake = new FakeChatService(["ok"]);
        var controller = new ChatSessionController(fake, new Store(), EmptySimpleLogger.Instance);

        var pendingTask = fake.CapturedToolHandler!("ApplySqlFix", "{\"proposedSql\":\"SELECT 2\"}");
        controller.ConfirmTool(false);

        Assert.False(await pendingTask);
        Assert.Contains("denied", controller.Messages.Last().Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewSession_And_OpenSession_SwitchMessages()
    {
        var fake = new FakeChatService(["ok"]);
        var store = new Store();
        var controller = new ChatSessionController(fake, store, EmptySimpleLogger.Instance);

        var first = controller.CurrentSession;
        controller.NewSession();
        Assert.NotSame(first, controller.CurrentSession);
        Assert.Empty(controller.Messages);

        var saved = new ChatSession { Title = "saved" };
        saved.Messages.Add(new ChatMessage("hi", "user"));
        store.Settings.ChatSessions.Add(saved);
        controller.OpenSession(saved);
        Assert.Same(saved, controller.CurrentSession);
        Assert.Single(controller.Messages);
        Assert.Equal("hi", controller.Messages[0].Content);
    }

    private sealed class Store : IChatSettingsStore
    {
        public ChatSettings Settings { get; } = new();
        public void Update(Action<ChatSettings> mutate) => mutate(Settings);
    }

    private sealed class FakeChatService : ICopilotChatService
    {
        private readonly IAsyncEnumerable<string> _script;
        public string? LastModelId { get; private set; }
        public Func<string, string, Task<bool>>? CapturedToolHandler { get; private set; }

        public FakeChatService(string[] chunks) : this(new ScriptedStream(chunks.Select(c => (Func<CancellationToken, Task<string>>)(_ => Task.FromResult(c))).ToArray())) { }

        public FakeChatService(IAsyncEnumerable<string> script) => _script = script;

        public bool IsConnected => true;
        public string? ConnectionError => null;
        public IReadOnlyList<(string Id, string DisplayName)> AvailableBackends => [("codex", "Codex")];
        public string? ActiveBackendId => "codex";
        public bool IsCodexAuthenticated => false;
        public CodexAccountInfo? CodexAccount => null;
        public string? _threadId;

        public Task<bool> InitializeAsync() => Task.FromResult(true);
        public async IAsyncEnumerable<string> SendMessageAsync(List<ChatMessage> messages, string? modelId = null, string? reasoningEffort = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastModelId = modelId;
            await foreach (var chunk in _script.WithCancellation(cancellationToken))
            {
                yield return chunk;
            }
        }

        public Task<List<string>> GetAvailableModelsAsync() => Task.FromResult(new List<string>());
        public Task<List<string>> GetAvailableReasoningEffortsAsync(string? modelId = null) => Task.FromResult(new List<string>());
        public Task<bool> SwitchBackendAsync(string backendId) => Task.FromResult(true);
        public void SetCurrentSqlProvider(Func<string?> provider) { }
        public void SetActiveSqlContextProvider(Func<(string ConnectionName, string DatabaseName)?> provider) { }
        public void SetSqlEditorContextProvider(Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?> provider) { }
        public void SetSqlEditorBufferUpdater(Func<string, bool> updater) { }
        public void SetToolConfirmationHandler(Func<string, string, Task<bool>> handler) => CapturedToolHandler = handler;
        public void SetMode(ChatMode mode) { }
        public ChatMode GetCurrentMode() => ChatMode.Expert;
        public Task<CodexAccountInfo?> ReadCodexAccountAsync(CancellationToken cancellationToken = default) => Task.FromResult<CodexAccountInfo?>(null);
        public Task<bool> StartCodexLoginAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> LogoutCodexAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task CancelCurrentRequestAsync() => Task.CompletedTask;
        public void SetCodexThreadId(string? threadId) => _threadId = threadId;
        public string? GetCodexThreadId() => _threadId;
    }

    private sealed class ScriptedStream : IAsyncEnumerable<string>
    {
        private readonly IReadOnlyList<Func<CancellationToken, Task<string>>> _steps;

        public ScriptedStream(params Func<CancellationToken, Task<string>>[] steps) => _steps = steps;

        public async IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            foreach (var step in _steps)
            {
                yield return await step(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
