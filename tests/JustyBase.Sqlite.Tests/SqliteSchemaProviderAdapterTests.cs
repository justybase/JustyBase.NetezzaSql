using JustyBase.Sqlite.Models;
using JustyBase.Sqlite.Schema;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Sqlite.Tests;

public sealed class SqliteSchemaProviderAdapterTests
{
    private static SqliteSchemaSnapshot Snapshot() => new(
    [
        new SqliteSchemaTable("users", "main", Columns:
        [
            new SqliteSchemaColumn("id", "INTEGER", IsPrimaryKey: true),
            new SqliteSchemaColumn("name", "TEXT", NotNull: true),
        ]),
        new SqliteSchemaTable("active_users", "main", SqliteObjectKind.View, IsView: true),
    ]);

    [Fact]
    public void Apply_PopulatesProvider()
    {
        var provider = new InMemorySchemaProvider();
        SqliteSchemaProviderAdapter.Apply(provider, Snapshot());

        Assert.True(provider.HasTables());
        Assert.True(provider.TableExists(null, "main", "users"));
        var table = provider.GetTable(null, "main", "users");
        Assert.NotNull(table);
        Assert.Equal(2, table!.Columns!.Count);
        Assert.Equal("INTEGER", table.Columns[0].DataType);
        Assert.True(table.Columns[1].DataType == "TEXT");
    }

    [Fact]
    public void Apply_MapsViews()
    {
        var provider = new InMemorySchemaProvider();
        SqliteSchemaProviderAdapter.Apply(provider, Snapshot());

        var view = provider.GetTable(null, "main", "active_users");
        Assert.NotNull(view);
        Assert.True(view!.IsView);
    }

    [Fact]
    public void Apply_Clear_RemovesPreviousTables()
    {
        var provider = new InMemorySchemaProvider();
        SqliteSchemaProviderAdapter.Apply(provider, Snapshot());
        SqliteSchemaProviderAdapter.Apply(provider, new SqliteSchemaSnapshot([], Version: 2));

        Assert.False(provider.HasTables());
        Assert.True(provider.MetadataEpoch >= 2);
    }

    [Fact]
    public void Apply_WithoutClear_Merges()
    {
        var provider = new InMemorySchemaProvider();
        SqliteSchemaProviderAdapter.Apply(provider, Snapshot());
        SqliteSchemaProviderAdapter.Apply(provider, new SqliteSchemaSnapshot(
            [new SqliteSchemaTable("extra", "main")], Version: 2), clear: false);

        Assert.True(provider.TableExists(null, "main", "users"));
        Assert.True(provider.TableExists(null, "main", "extra"));
    }
}
