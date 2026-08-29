using JustyBase.Ai.Chat;
using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Ports;

namespace JustyBase.Ai.Tests;

public sealed class EmbeddedChatModelBootstrapTests
{
    [Fact]
    public async Task EnsureModelAsync_downloads_selected_model_and_forwards_progress()
    {
        var settings = new ChatSettings
        {
            EnableEmbeddedChatAi = true,
            EmbeddedChatModelId = EmbeddedChatModelIds.Qwen35_4B
        };
        var store = new FakeSettingsStore(settings);
        var modelStore = new FakeModelStore(new EmbeddedChatModelCatalog(), () => settings.EmbeddedChatModelId);
        var bootstrap = new EmbeddedChatModelBootstrapService(store, new EmbeddedChatModelCatalog(), modelStore);
        var progress = new RecordingProgress();

        await bootstrap.EnsureModelAsync(progress: progress);

        Assert.Equal(1, modelStore.EnsureCalls);
        Assert.True(modelStore.IsModelPresent);
        Assert.Contains(progress.Messages, message => message.Contains("download", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ready on disk", bootstrap.SelectedModelDiskStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureModelAsync_requires_license_before_downloading_restricted_model()
    {
        var settings = new ChatSettings
        {
            EnableEmbeddedChatAi = true,
            EmbeddedChatModelId = EmbeddedChatModelIds.Gemma4_12B
        };
        var store = new FakeSettingsStore(settings);
        var catalog = new EmbeddedChatModelCatalog();
        var modelStore = new FakeModelStore(catalog, () => settings.EmbeddedChatModelId);
        var bootstrap = new EmbeddedChatModelBootstrapService(store, catalog, modelStore);

        await Assert.ThrowsAsync<EmbeddedChatModelLicenseRequiredException>(
            () => bootstrap.EnsureModelAsync());

        Assert.Equal(0, modelStore.EnsureCalls);
        settings.EmbeddedChatAcceptedLicenseModelIds.Add(EmbeddedChatModelIds.Gemma4_12B);
        await bootstrap.EnsureModelAsync();
        Assert.Equal(1, modelStore.EnsureCalls);
    }

    [Fact]
    public async Task EnsureModelAsync_switches_the_selected_model_before_preparing_it()
    {
        var settings = new ChatSettings { EmbeddedChatModelId = EmbeddedChatModelIds.Qwen35_4B };
        var store = new FakeSettingsStore(settings);
        var catalog = new EmbeddedChatModelCatalog();
        var modelStore = new FakeModelStore(catalog, () => settings.EmbeddedChatModelId);
        var bootstrap = new EmbeddedChatModelBootstrapService(store, catalog, modelStore);

        await bootstrap.EnsureModelAsync(EmbeddedChatModelIds.Qwen35_9B);

        Assert.Equal(EmbeddedChatModelIds.Qwen35_9B, settings.EmbeddedChatModelId);
        Assert.Equal(EmbeddedChatModelIds.Qwen35_9B, modelStore.CurrentModel.Id);
    }

    private sealed class FakeSettingsStore(ChatSettings settings) : IChatSettingsStore
    {
        public ChatSettings Settings { get; } = settings;
        public void Update(Action<ChatSettings> mutate) => mutate(Settings);
    }

    private sealed class FakeModelStore : IModelStore
    {
        private readonly IModelCatalog _catalog;
        private readonly Func<string?> _selectedModel;

        public FakeModelStore(IModelCatalog catalog, Func<string?> selectedModel)
        {
            _catalog = catalog;
            _selectedModel = selectedModel;
            ModelsDirectory = Path.Combine(Path.GetTempPath(), "JustyBase-Ai-ModelTests", Guid.NewGuid().ToString("N"));
        }

        public ModelDescriptor CurrentModel => _catalog.Resolve(_selectedModel());
        public string ModelsDirectory { get; }
        public string ModelFileName => CurrentModel.FileName;
        public string LocalModelPath => Path.Combine(ModelsDirectory, CurrentModel.FileName);
        public bool IsModelPresent { get; private set; }
        public int EnsureCalls { get; private set; }
        public long LocalModelSizeBytes => IsModelPresent ? 2_700_000_000 : 0;

        public string EnsureModelsDirectory() => ModelsDirectory;

        public Task EnsureModelAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCalls++;
            IsModelPresent = true;
            progress?.Report(new FimModelProgress(0.5, $"Downloading {CurrentModel.Id}…"));
            progress?.Report(new FimModelProgress(1.0, $"{CurrentModel.DisplayName} download complete."));
            return Task.CompletedTask;
        }

        public bool TryDeleteCurrentModel() => false;
        public bool TryDeletePartialDownload() => false;
    }

    private sealed class RecordingProgress : IProgress<FimModelProgress>
    {
        public List<string> Messages { get; } = [];
        public void Report(FimModelProgress value) => Messages.Add(value.Message);
    }
}
