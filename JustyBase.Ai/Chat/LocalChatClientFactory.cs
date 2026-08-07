namespace JustyBase.Ai.Chat;

public sealed class LocalChatClientFactory
{
    private readonly List<ILocalChatBackend> _backends;

    public LocalChatClientFactory(IEnumerable<ILocalChatBackend> backends)
    {
        _backends = backends.ToList();
    }

    public IReadOnlyList<ILocalChatBackend> Backends => _backends;

    public ILocalChatBackend? GetBackend(string id)
        => _backends.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public ILocalChatBackend GetDefaultBackend()
        => _backends.FirstOrDefault() ?? throw new InvalidOperationException("No chat backends registered");
}
