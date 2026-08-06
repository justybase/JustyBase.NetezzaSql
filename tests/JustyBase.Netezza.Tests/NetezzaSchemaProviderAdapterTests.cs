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
                    new NetezzaSchemaColumn("NAME", "VARCHAR(100)", Description: "Customer display name")]),
            new NetezzaSchemaTable("ACTIVE_CUSTOMERS", "PUBLIC", "SALES", IsView: true)
        ], Version: 7);

        NetezzaSchemaProviderAdapter.Apply(provider, snapshot);

        var table = provider.GetTable("SALES", "PUBLIC", "CUSTOMERS");
        Assert.NotNull(table);
        Assert.False(table!.IsView);
        Assert.Equal(["ID", "NAME"], table.Columns!.Select(c => c.Name));
        Assert.Equal("INTEGER", table.Columns![0].DataType);
        Assert.Equal("Customer display name", table.Columns![1].Description);
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

    [Fact]
    public void Apply_ProjectsOnlyTableLikeObjects()
    {
        var provider = new InMemorySchemaProvider();

        NetezzaSchemaProviderAdapter.Apply(
            provider,
            new NetezzaSchemaSnapshot(
            [
                new NetezzaSchemaTable("T1", "PUBLIC", "DB", Kind: NetezzaObjectKind.Table),
                new NetezzaSchemaTable("V1", "PUBLIC", "DB", Kind: NetezzaObjectKind.View),
                new NetezzaSchemaTable("EXT1", "PUBLIC", "DB", Kind: NetezzaObjectKind.ExternalTable),
                new NetezzaSchemaTable("SYN1", "PUBLIC", "DB", Kind: NetezzaObjectKind.Synonym),
                new NetezzaSchemaTable("P1", "PUBLIC", "DB", Kind: NetezzaObjectKind.Procedure),
                new NetezzaSchemaTable("F1", "PUBLIC", "DB", Kind: NetezzaObjectKind.Function),
                new NetezzaSchemaTable("SEQ1", "PUBLIC", "DB", Kind: NetezzaObjectKind.Sequence),
                new NetezzaSchemaTable("AGG1", "PUBLIC", "DB", Kind: NetezzaObjectKind.Aggregate),
            ]));

        Assert.True(provider.TableExists("DB", "PUBLIC", "T1"));
        Assert.True(provider.TableExists("DB", "PUBLIC", "V1"));
        Assert.True(provider.TableExists("DB", "PUBLIC", "EXT1"));
        Assert.True(provider.TableExists("DB", "PUBLIC", "SYN1"));
        Assert.False(provider.TableExists("DB", "PUBLIC", "P1"));
        Assert.False(provider.TableExists("DB", "PUBLIC", "F1"));
        Assert.False(provider.TableExists("DB", "PUBLIC", "SEQ1"));
        Assert.False(provider.TableExists("DB", "PUBLIC", "AGG1"));
    }

    [Fact]
    public void Apply_ExternalAndSynonymObjectsCarryKindFlags()
    {
        var provider = new InMemorySchemaProvider();

        NetezzaSchemaProviderAdapter.Apply(
            provider,
            new NetezzaSchemaSnapshot(
            [
                new NetezzaSchemaTable("EXT1", "PUBLIC", "DB", Kind: NetezzaObjectKind.ExternalTable),
                new NetezzaSchemaTable("SYN1", "PUBLIC", "DB", Kind: NetezzaObjectKind.Synonym),
                new NetezzaSchemaTable("V1", "PUBLIC", "DB", Kind: NetezzaObjectKind.View),
            ]));

        Assert.True(provider.GetTable("DB", "PUBLIC", "EXT1")!.IsExternal);
        Assert.False(provider.GetTable("DB", "PUBLIC", "EXT1")!.IsView);
        Assert.False(provider.GetTable("DB", "PUBLIC", "SYN1")!.IsExternal);
        Assert.True(provider.GetTable("DB", "PUBLIC", "V1")!.IsView);
    }
}
