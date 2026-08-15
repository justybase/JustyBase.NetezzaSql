using JustyBase.Sqlite.Abstractions;
using JustyBase.Sqlite.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.Sqlite.DependencyInjection;

/// <summary>
/// Extension methods for registering JustyBase.Sqlite services with the DI container.
/// <code>
/// services.AddJustyBaseSqlite();
/// </code>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers all JustyBase.Sqlite services as singletons.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddJustyBaseSqlite(this IServiceCollection services)
    {
        services.AddSingleton<ISqliteSchemaProviderAdapter>(_ => SqliteSchemaProviderAdapter.Default);
        services.AddSingleton(_ => new SqliteSchemaCache());
        return services;
    }
}
