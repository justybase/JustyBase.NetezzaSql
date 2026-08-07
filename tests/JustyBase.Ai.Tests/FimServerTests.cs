using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Server;
using System.Net;
using System.Text;

namespace JustyBase.Ai.Tests;

/// <summary>
/// FIM runtime: LlamaServerFimProvider request building and LlamaServerFimBootstrapService
/// download/delete/speed-test flow — against fake server instances (no processes, no network).
/// </summary>
public sealed class FimServerTests
{
    [Fact]
    public async Task FimProvider_PostsCompletionRequestAndReturnsSuggestion()
    {
        var manager = CreateManager();
        await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model.gguf", 0, 4096);

        var handler = new CapturingHandler("""{"content":" SELECT 1"}""");
        var provider = new LlamaServerFimProvider(
            manager,
            new FakeModelStore(),
            getGpuLayers: () => 0,
            getContextSize: () => 4096,
            httpClient: new HttpClient(handler));

        var suggestion = await provider.CompleteAsync(
            new CompletionRequest("SELECT ", " FROM t", MaxTokens: 50),
            CancellationToken.None);

        Assert.NotNull(suggestion);
        Assert.Contains("SELECT 1", suggestion!.Text);
        Assert.Contains("\"input_prefix\":\"SELECT \"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"input_suffix\":\" FROM t\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"n_predict\":50", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FimProvider_ServerNotRunning_ReturnsNull()
    {
        var manager = CreateManager(); // no server started
        var provider = new LlamaServerFimProvider(
            manager,
            new FakeModelStore(),
            () => 0,
            () => 4096);

        var suggestion = await provider.CompleteAsync(new CompletionRequest("a", "b"), CancellationToken.None);

        Assert.Null(suggestion);
    }

    [Fact]
    public async Task FimProvider_NonSuccessResponse_ReturnsNull()
    {
        var manager = CreateManager();
        await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model.gguf", 0, 4096);

        var handler = new CapturingHandler("""{"content":"x"}""", statusCode: HttpStatusCode.InternalServerError);
        var provider = new LlamaServerFimProvider(
            manager,
            new FakeModelStore(),
            () => 0,
            () => 4096,
            httpClient: new HttpClient(handler));

        var suggestion = await provider.CompleteAsync(new CompletionRequest("a", "b"), CancellationToken.None);

        Assert.Null(suggestion);
    }

    [Fact]
    public async Task Bootstrap_EnsureReady_StartsProviderAndNotifies()
    {
        var provider = new RecordingCompletionProvider();
        var store = new FakeModelStore { IsModelPresent = true };
        var bootstrap = new LlamaServerFimBootstrapService(
            provider,
            store,
            CreateManager(),
            notifyModelReady: () => _ = 0);
        var notified = false;
        var bootstrapWithNotify = new LlamaServerFimBootstrapService(provider, store, CreateManager(), notifyModelReady: () => notified = true);
        _ = bootstrap;

        await bootstrapWithNotify.EnsureReadyAsync();

        Assert.True(provider.EnsureReadyCalled);
        Assert.True(notified);
    }

    [Fact]
    public async Task Bootstrap_Delete_StopsServerAndDeletesModel()
    {
        var manager = CreateManager();
        await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model.gguf", 0, 4096);
        Assert.NotNull(manager.FimServer);

        var store = new FakeModelStore { IsModelPresent = true };
        var bootstrap = new LlamaServerFimBootstrapService(new RecordingCompletionProvider(), store, manager);

        await bootstrap.DeleteSelectedModelAsync();

        Assert.Null(manager.FimServer);
        Assert.True(store.TryDeleteCurrentModelCalled);
    }

    [Fact]
    public async Task Bootstrap_RunSpeedTest_ReportsThroughput()
    {
        var provider = new RecordingCompletionProvider { Suggestion = new CompletionSuggestion("SELECT * FROM DIMDATE D WHERE D.CAL") };
        var store = new FakeModelStore { IsModelPresent = true };
        var bootstrap = new LlamaServerFimBootstrapService(provider, store, CreateManager());

        var report = await bootstrap.RunSpeedTestAsync(maxTokens: 50, maxPromptTokens: 1536, prefixPercentage: 0.65, suffixPercentage: 0.35);

        Assert.True(report.Succeeded);
        Assert.True(report.TokensPerSecond > 0);
        Assert.True(report.ElapsedMs >= 0);
    }

    [Fact]
    public async Task Bootstrap_RunSpeedTest_NoCompletion_ReportsFailure()
    {
        var provider = new RecordingCompletionProvider { Suggestion = null };
        var store = new FakeModelStore { IsModelPresent = true };
        var bootstrap = new LlamaServerFimBootstrapService(provider, store, CreateManager());

        var report = await bootstrap.RunSpeedTestAsync(maxTokens: 50, maxPromptTokens: 1536, prefixPercentage: 0.65, suffixPercentage: 0.35);

        Assert.False(report.Succeeded);
    }

    private static LlamaServerManager CreateManager()
        => new(new FakeBinary(), (_, _, _, _) => new FakeInstance());

    private sealed class FakeBinary : ILlamaServerBinary
    {
        public string BinaryPath => @"C:\fake\llama-server.exe";
        public bool IsBinaryPresent => true;
        public string BinaryVariant => "avx2";
        public Task EnsureBinaryAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeInstance : ILlamaServerInstance
    {
        public int Port => 8080;
        public Uri Endpoint => new("http://127.0.0.1:8080");
        public bool IsRunning => true;
        public string? LastError => null;
        public string LogFilePath => string.Empty;
        public Task<bool> StartAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeModelStore : IModelStore
    {
        public bool IsModelPresent { get; set; } = true;
        public bool TryDeleteCurrentModelCalled { get; private set; }

        public ModelDescriptor CurrentModel { get; set; } = new(
            "qwen2.5-coder-3b",
            "Qwen2.5-Coder 3B",
            "Qwen2.5-Coder-3B.gguf",
            new Uri("https://example.com/m"),
            "~1.9 GB",
            new Uri("https://example.com"),
            "note",
            1_000_000L);

        public string ModelsDirectory => @"C:\fake\models";
        public string ModelFileName => CurrentModel.FileName;
        public string LocalModelPath => @"C:\fake\models\Qwen2.5-Coder-3B.gguf";
        public string EnsureModelsDirectory() => ModelsDirectory;
        public Task EnsureModelAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool TryDeleteCurrentModel()
        {
            TryDeleteCurrentModelCalled = true;
            return true;
        }

        public bool TryDeletePartialDownload() => false;
    }

    private sealed class RecordingCompletionProvider : ICompletionProvider
    {
        public string Id => "fake";
        public string DisplayName => "Fake";
        public bool IsAvailable => true;
        public bool IsReady => true;
        public bool EnsureReadyCalled { get; private set; }
        public CompletionSuggestion? Suggestion { get; set; } = new("ok");

        public Task EnsureReadyAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            EnsureReadyCalled = true;
            return Task.CompletedTask;
        }

        public Task<CompletionSuggestion?> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Suggestion);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _statusCode;
        public string? LastBody { get; private set; }

        public CapturingHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _body = body;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
