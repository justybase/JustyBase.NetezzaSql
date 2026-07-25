using JustyBase.Netezza.Abstractions;
using JustyBase.Netezza.Completion;
using JustyBase.Netezza.Ddl;
using JustyBase.Netezza.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.Netezza.DependencyInjection;

/// <summary>
/// Extension methods for registering JustyBase.Netezza services with the DI container.
/// <code>
/// services.AddJustyBaseNetezza();
/// </code>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers all JustyBase.Netezza services as singletons.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddJustyBaseNetezza(this IServiceCollection services)
    {
        services.AddSingleton<ISqlCompletionMergePolicy>(_ => SqlCompletionMergePolicy.Default);
        services.AddSingleton<INetezzaSchemaProviderAdapter>(_ => NetezzaSchemaProviderAdapter.Default);
        services.AddSingleton<INetezzaDdlInputFactory>(_ => NetezzaDdlInputFactory.Default);
        return services;
    }
}
