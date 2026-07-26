namespace JustyBase.Core.Credentials;

public sealed record CredentialProfile(string Name, string UserName, string? Secret, string? Host = null);

public interface ICredentialStore
{
    ValueTask<CredentialProfile?> ReadAsync(string name, CancellationToken cancellationToken = default);
    ValueTask WriteAsync(CredentialProfile profile, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, CredentialProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask<CredentialProfile?> ReadAsync(string name, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_profiles.GetValueOrDefault(name));

    public ValueTask WriteAsync(CredentialProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profiles[profile.Name] = profile;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Reads the new store first and transparently falls back to Legacy.
/// Scaffold only — does not implement JBAG/JBCG file formats.</summary>
public sealed class DualCredentialStore(ICredentialStore primary, ICredentialStore legacy) : ICredentialStore
{
    public async ValueTask<CredentialProfile?> ReadAsync(string name, CancellationToken cancellationToken = default)
        => await primary.ReadAsync(name, cancellationToken).ConfigureAwait(false)
           ?? await legacy.ReadAsync(name, cancellationToken).ConfigureAwait(false);

    public ValueTask WriteAsync(CredentialProfile profile, CancellationToken cancellationToken = default)
        => primary.WriteAsync(profile, cancellationToken);

    public async ValueTask<bool> MigrateAsync(string name, CancellationToken cancellationToken = default)
    {
        var profile = await primary.ReadAsync(name, cancellationToken).ConfigureAwait(false);
        if (profile is not null)
            return false;
        profile = await legacy.ReadAsync(name, cancellationToken).ConfigureAwait(false);
        if (profile is null)
            return false;
        await primary.WriteAsync(profile, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
