using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Server;
using System.Net;
using System.Text;
using System.Text.Json;

namespace JustyBase.Ai.Tests;

/// <summary>
/// MLX (Apple Silicon) runtime: mlx_lm.server command line, the MLX FIM provider posting FIM tokens
/// to /v1/completions, and MLX variant coverage in the model catalogs. Fake instances only.
/// </summary>
public sealed class MlxServerTests
{
    [Fact]
    public void MlxInstance_BuildArguments_UsesUvToolRunWithModelAndPort()
    {
        var args = MlxServerInstance.BuildArguments(@"/Users/me/models/qwen3.5-9b", port: 48213);

        Assert.Equal("tool", args[0]);
        Assert.Equal("run", args[1]);
        Assert.Contains("--from", args);
        Assert.Contains("mlx-lm", args);
        Assert.Contains("mlx_lm.server", args);

        var modelIndex = Array.IndexOf(args.ToArray(), "--model");
        Assert.True(modelIndex >= 0);
        Assert.Equal("/Users/me/models/qwen3.5-9b", args[modelIndex + 1]);

        var portIndex = Array.IndexOf(args.ToArray(), "--port");
        Assert.True(portIndex >= 0);
        Assert.Equal("48213", args[portIndex + 1]);

        var hostIndex = Array.IndexOf(args.ToArray(), "--host");
        Assert.True(hostIndex >= 0);
        Assert.Equal("127.0.0.1", args[hostIndex + 1]);

        // MLX runs on the unified GPU only — llama.cpp CPU/KV-offload knobs must not appear.
        Assert.DoesNotContain("--n-gpu-layers", args);
        Assert.DoesNotContain("--no-kv-offload", args);
    }

    [Fact]
    public void MlXInstance_BuildArguments_DefaultsTempToZeroAndFixedContext()
    {
        var args = MlxServerInstance.BuildArguments("/models/x", 48123);

        var tempIndex = Array.IndexOf(args.ToArray(), "--temp");
        Assert.Equal("0", args[tempIndex + 1]);
        Assert.Contains("--max-tokens", args);
        Assert.Contains("512", args);
        Assert.Contains("--log-level", args);
        Assert.Equal("warning", args[Array.IndexOf(args.ToArray(), "--log-level") + 1]);
    }

    [Fact]
    public async Task MlxFimProvider_PostsV1CompletionsWithFimTokensAndReadsChoiceText()
    {
        var manager = CreateManager();
        await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "models/qwen2.5-coder-3b", 0, 4096);

        var handler = new CapturingHandler("""{"choices":[{"text":" SELECT 1","finish_reason":"stop"}]}""");
        var provider = new MlxFimProvider(
            manager,
            new FakeModelStore(),
            completions: new MlxCompletionClient(new HttpClient(handler)));

        var suggestion = await provider.CompleteAsync(
            new CompletionRequest("SELECT ", " FROM t", MaxTokens: 50),
            CancellationToken.None);

        Assert.NotNull(suggestion);
        Assert.Contains("SELECT 1", suggestion!.Text);
        Assert.EndsWith("/v1/completions", handler.LastUri, StringComparison.Ordinal);

        using (var doc = JsonDocument.Parse(handler.LastBody!))
        {
            Assert.Equal(
                "<|fim_prefix|>SELECT <|fim_suffix|> FROM t<|fim_middle|>",
                doc.RootElement.GetProperty("prompt").GetString());
            Assert.Equal(50, doc.RootElement.GetProperty("max_tokens").GetInt32());
            Assert.Equal(0.15f, doc.RootElement.GetProperty("temperature").GetSingle());
        }
    }

    [Fact]
    public async Task MlxFimProvider_ServerNotRunning_ReturnsNull()
    {
        var manager = CreateManager(); // no server started
        var provider = new MlxFimProvider(manager, new FakeModelStore());

        var suggestion = await provider.CompleteAsync(new CompletionRequest("a", "b"), CancellationToken.None);

        Assert.Null(suggestion);
    }

    [Fact]
    public async Task MlxFimProvider_NonSuccessResponse_ReturnsNull()
    {
        var manager = CreateManager();
        await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "models/model", 0, 4096);

        var handler = new CapturingHandler("""{"error":"no"}""", HttpStatusCode.InternalServerError);
        var provider = new MlxFimProvider(
            manager,
            new FakeModelStore(),
            completions: new MlxCompletionClient(new HttpClient(handler)));

        var suggestion = await provider.CompleteAsync(new CompletionRequest("a", "b"), CancellationToken.None);

        Assert.Null(suggestion);
    }

    [Fact]
    public void ChatCatalog_AllModels_HaveMlxVariant()
    {
        var catalog = new EmbeddedChatModelCatalog();

        Assert.NotEmpty(catalog.Models);
        foreach (var model in catalog.Models)
        {
            Assert.False(string.IsNullOrWhiteSpace(model.MlxRepoId), $"{model.Id} is missing an MLX variant.");
            Assert.True(model.MlxApproxBytes > 0, $"{model.Id} has no MLX size.");
        }
    }

    [Fact]
    public void FimCatalog_AllModels_HaveMlxVariant()
    {
        var catalog = new FimModelCatalog();

        Assert.NotEmpty(catalog.Models);
        foreach (var model in catalog.Models)
        {
            Assert.False(string.IsNullOrWhiteSpace(model.MlxRepoId), $"{model.Id} is missing an MLX variant.");
            Assert.True(model.MlxApproxBytes > 0, $"{model.Id} has no MLX size.");
        }
    }

    private static LlamaServerManager CreateManager()
        => new(new FakeBinary(), (_, _, _, _) => new FakeInstance());

    private sealed class FakeBinary : ILlamaServerBinary
    {
        public string BinaryPath => "/Users/me/.local/bin/uv";
        public bool IsBinaryPresent => true;
        public string BinaryVariant => "mlx";
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
        public string Family { get; set; } = "Qwen (recommended)";
        public bool IsModelPresent => true;
        public ModelDescriptor CurrentModel => new(
            "qwen2.5-coder-3b",
            "Qwen2.5-Coder 3B",
            "Qwen2.5-Coder-3B.gguf",
            new Uri("https://example.com/m"),
            "~1.9 GB",
            new Uri("https://example.com"),
            "note",
            1_000_000L,
            Family);
        public string ModelsDirectory => @"C:\fake\models";
        public string ModelFileName => CurrentModel.FileName;
        public string LocalModelPath => @"C:\fake\models\Qwen2.5-Coder-3B.gguf";
        public string EnsureModelsDirectory() => ModelsDirectory;
        public Task EnsureModelAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool TryDeleteCurrentModel() => false;
        public bool TryDeletePartialDownload() => false;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _statusCode;
        public string LastUri { get; private set; } = string.Empty;
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
            LastUri = request.RequestUri?.ToString() ?? string.Empty;
            LastBody = request.Content is null ? string.Empty : await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}