using JustyBase.Netezza.Models;
using JustyBase.Netezza.Schema;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Netezza.Tests;

public sealed class NetezzaSchemaProviderAdapterTests
{
    [Fact]
    public void Apply_LoadsTablesViewsAndColumns()
    {
        var provider = new InMemorySchemaProvider();
        var snapshot = new NetezzaSchemaSnapshot([
            new NetezzaSchemaTable(
                "CUSTOMERS",
                "PUBLIC",
                "SALES",
                IsView: false,
                Columns: [
                    new NetezzaSchemaColumn("ID", "INTEGER", Nullable: false),
                    new NetezzaSchemaColumn("NAME", "VARCHAR(100)")]),
            new NetezzaSchemaTable("ACTIVE_CUSTOMERS", "PUBLIC", "SALES", IsView: true)
        ], Version: 7);

        NetezzaSchemaProviderAdapter.Apply(provider, snapshot);

        var table = provider.GetTable("SALES", "PUBLIC", "CUSTOMERS");
        Assert.NotNull(table);
        Assert.False(table!.IsView);
        Assert.Equal(["ID", "NAME"], table.Columns!.Select(c => c.Name));
        Assert.Equal("INTEGER", table.Columns![0].DataType);
        Assert.True(provider.GetTable("SALES", "PUBLIC", "ACTIVE_CUSTOMERS")!.IsView);
    }

    [Fact]
    public void Apply_ClearRemovesStaleTables()
    {
        var provider = new InMemorySchemaProvider();
        provider.AddTable(new TableInfo("OLD_TABLE"));

        NetezzaSchemaProviderAdapter.Apply(
            provider,
            new NetezzaSchemaSnapshot([new NetezzaSchemaTable("NEW_TABLE")]));

        Assert.False(provider.TableExists(null, null, "OLD_TABLE"));
        Assert.True(provider.TableExists(null, null, "NEW_TABLE"));
    }

    [Fact]
    public void Apply_AdvancesMetadataEpochOnceAfterReplacingSnapshot()
    {
        var provider = new InMemorySchemaProvider();
        var initialEpoch = provider.MetadataEpoch;

        NetezzaSchemaProviderAdapter.Apply(
            provider,
            new NetezzaSchemaSnapshot([new NetezzaSchemaTable("ORDERS")]),
            clear: false);

        Assert.Equal(initialEpoch + 1, provider.MetadataEpoch);
    }

    [Fact]
    public void Apply_PreservesSchemaSnapshotWhenColumnsAreMissing()
    {
        var provider = new InMemorySchemaProvider();

        NetezzaSchemaProviderAdapter.Apply(
            provider,
            new NetezzaSchemaSnapshot([new NetezzaSchemaTable("ORDERS", Columns: null)]));

        var table = provider.GetTable(null, null, "ORDERS");
        Assert.NotNull(table);
        Assert.Empty(table!.Columns!);
    }
}
