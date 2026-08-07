using JustyBase.Ai.Chat;

namespace JustyBase.Ai.Services;

public sealed class LocalModelConfigurationService : ILocalModelConfigurationService
{
    private readonly LocalChatClientFactory _factory;

    public LocalModelConfigurationService(LocalChatClientFactory factory)
    {
        _factory = factory;
    }

    public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        var models = new List<string>();
        foreach (var backend in _factory.Backends)
        {
            try
            {
                var backendModels = await backend.ListModelsAsync(ct);
                models.AddRange(backendModels);
            }
            catch
            {
                // skip unreachable backends
            }
        }
        return models;
    }

    public async Task<List<string>> GetAvailableModelsAsync(string? backendId, CancellationToken ct = default)
    {
        if (backendId is null)
        {
            return await GetAvailableModelsAsync(ct);
        }

        var backend = _factory.GetBackend(backendId);
        if (backend is null)
        {
            return [];
        }

        try
        {
            return await backend.ListModelsAsync(ct);
        }
        catch
        {
            return [];
        }
    }
}
