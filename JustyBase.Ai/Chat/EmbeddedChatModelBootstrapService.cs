using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Ports;

namespace JustyBase.Ai.Chat;

/// <summary>
/// Prepares the model used by the Embedded chat backend without starting a chat server.
/// Hosts use this service for an explicit Preferences action; the backend uses it before
/// the first Connect/Send so a missing model is downloaded instead of reported as absent.
/// </summary>
public interface IEmbeddedChatModelBootstrapService
{
    ModelDescriptor CurrentModel { get; }
    string ModelsDirectory { get; }
    string LocalModelPath { get; }
    bool IsModelPresent { get; }
    string SelectedModelDiskStatus { get; }
    event Action<FimModelProgress>? ProgressChanged;
    Task EnsureModelAsync(string? modelId = null, IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>License acceptance is deliberately a host-visible error, not an implicit consent.</summary>
public sealed class EmbeddedChatModelLicenseRequiredException : InvalidOperationException
{
    public EmbeddedChatModelLicenseRequiredException(ModelDescriptor model)
        : base($"Accept {model.LicenseName ?? "the model license"} before downloading {model.DisplayName}.")
    {
        Model = model;
    }

    public ModelDescriptor Model { get; }
}

public sealed class EmbeddedChatModelBootstrapService : IEmbeddedChatModelBootstrapService
{
    private readonly IChatSettingsStore _settingsStore;
    private readonly EmbeddedChatModelCatalog _catalog;
    private readonly IModelStore _modelStore;

    public EmbeddedChatModelBootstrapService(
        IChatSettingsStore settingsStore,
        EmbeddedChatModelCatalog catalog,
        [Microsoft.Extensions.DependencyInjection.FromKeyedServices(EmbeddedChatBackend.ChatModelStoreKey)] IModelStore modelStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
    }

    public ModelDescriptor CurrentModel => _modelStore.CurrentModel;
    public string ModelsDirectory => _modelStore.ModelsDirectory;
    public string LocalModelPath => _modelStore.LocalModelPath;
    public bool IsModelPresent => _modelStore.IsModelPresent;

    public string SelectedModelDiskStatus
    {
        get
        {
            var model = CurrentModel;
            if (!IsModelPresent)
                return $"{model.DisplayName}: not downloaded. Expected size {GetSizeLabel(model)}.";

            var size = _modelStore.LocalModelSizeBytes / (1024d * 1024d * 1024d);
            return $"{model.DisplayName}: ready on disk ({size:0.##} GB). Location: {LocalModelPath}";
        }
    }

    public event Action<FimModelProgress>? ProgressChanged;

    public async Task EnsureModelAsync(
        string? modelId = null,
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var model = ResolveRequestedModel(modelId);
        if (!string.Equals(_settingsStore.Settings.EmbeddedChatModelId, model.Id, StringComparison.OrdinalIgnoreCase))
            _settingsStore.Update(settings => settings.EmbeddedChatModelId = model.Id);

        if (model.RequiresLicenseAcceptance
            && !_settingsStore.Settings.EmbeddedChatAcceptedLicenseModelIds.Contains(model.Id, StringComparer.OrdinalIgnoreCase))
        {
            throw new EmbeddedChatModelLicenseRequiredException(model);
        }

        var effectiveProgress = new Progress<FimModelProgress>(update =>
        {
            progress?.Report(update);
            ProgressChanged?.Invoke(update);
        });
        await _modelStore.EnsureModelAsync(effectiveProgress, cancellationToken).ConfigureAwait(false);
    }

    private ModelDescriptor ResolveRequestedModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return _modelStore.CurrentModel;

        var model = _catalog.Models.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, modelId.Trim(), StringComparison.OrdinalIgnoreCase));
        return model ?? throw new ArgumentException($"Unknown Embedded chat model '{modelId}'.", nameof(modelId));
    }

    private static string GetSizeLabel(ModelDescriptor model)
        => string.IsNullOrWhiteSpace(model.MlxSizeLabel)
            ? model.ApproxSizeLabel
            : $"{model.ApproxSizeLabel} GGUF / {model.MlxSizeLabel} MLX";
}
