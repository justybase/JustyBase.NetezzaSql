using JustyBase.Netezza.Abstractions;
using JustyBase.Netezza.Completion;
using JustyBase.Netezza.Ddl;
using JustyBase.Netezza.DependencyInjection;
using JustyBase.Netezza.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.Netezza.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddJustyBaseNetezza_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddJustyBaseNetezza();
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ISqlCompletionMergePolicy>());
        Assert.NotNull(provider.GetRequiredService<INetezzaSchemaProviderAdapter>());
        Assert.NotNull(provider.GetRequiredService<INetezzaDdlInputFactory>());
    }

    [Fact]
    public void DefaultInstances_AreSingletons()
    {
        var services = new ServiceCollection();
        services.AddJustyBaseNetezza();
        var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<ISqlCompletionMergePolicy>();
        var second = provider.GetRequiredService<ISqlCompletionMergePolicy>();

        Assert.Same(first, second);
    }

    [Fact]
    public void DefaultInstances_DelegateToStaticMethods()
    {
        var services = new ServiceCollection();
        services.AddJustyBaseNetezza();
        var provider = services.BuildServiceProvider();

        var mergePolicy = provider.GetRequiredService<ISqlCompletionMergePolicy>();
        var engineItems = new[]
        {
            new NetezzaSqlParser.Completion.CompletionItem("SELECT", NetezzaSqlParser.Completion.CompletionKind.Keyword, "keyword")
        };

        Assert.True(mergePolicy.ShouldRunLegacyPath(engineItems, "SELECT "));
    }

    [Fact]
    public void SqlCompletionMergePolicy_Default_IsConsistent()
    {
        Assert.Same(SqlCompletionMergePolicy.Default, SqlCompletionMergePolicy.Default);
    }

    [Fact]
    public void NetezzaSchemaProviderAdapter_Default_IsConsistent()
    {
        Assert.Same(NetezzaSchemaProviderAdapter.Default, NetezzaSchemaProviderAdapter.Default);
    }

    [Fact]
    public void NetezzaDdlInputFactory_Default_IsConsistent()
    {
        Assert.Same(NetezzaDdlInputFactory.Default, NetezzaDdlInputFactory.Default);
    }
}
