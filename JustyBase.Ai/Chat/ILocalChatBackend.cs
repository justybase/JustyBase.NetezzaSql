using Microsoft.Extensions.AI;

namespace JustyBase.Ai.Chat;

public interface ILocalChatBackend
{
    string Id { get; }
    string DisplayName { get; }
    Uri Endpoint { get; set; }
    Task<bool> PingAsync(CancellationToken ct = default);
    Task<List<string>> ListModelsAsync(CancellationToken ct = default);
    IChatClient CreateChatClient(string modelId, bool enableFunctionInvocation = true);
}
