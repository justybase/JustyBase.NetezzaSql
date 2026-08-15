using JustyBase.Sqlite.Abstractions;
using JustyBase.Sqlite.DependencyInjection;
using JustyBase.Sqlite.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.Sqlite.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddJustyBaseSqlite_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddJustyBaseSqlite();
        var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<ISqliteSchemaProviderAdapter>();
        Assert.Same(SqliteSchemaProviderAdapter.Default, adapter);

        var cache = provider.GetRequiredService<SqliteSchemaCache>();
        Assert.NotNull(cache);
        Assert.Same(cache, provider.GetRequiredService<SqliteSchemaCache>());
    }
}
