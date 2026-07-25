using JustyBase.Netezza.Ddl;
using JustyBase.Netezza.Models;

namespace JustyBase.Netezza.Tests;

public sealed class NetezzaColumnCatalogMapperTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData("t", true)]
    [InlineData("f", false)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("no", false)]
    [InlineData("", false)]
    public void NormalizeBooleanFlag_HandlesCatalogShapes(object value, bool expected)
        => Assert.Equal(expected, NetezzaColumnCatalogMapper.NormalizeBooleanFlag(value));

    [Fact]
    public void NormalizeBooleanFlag_TreatsNullAsFalse()
    {
        Assert.False(NetezzaColumnCatalogMapper.NormalizeBooleanFlag(null));
        Assert.False(NetezzaColumnCatalogMapper.NormalizeBooleanFlag(DBNull.Value));
    }

    [Fact]
    public void AttNotNullToNullable_InvertsFlag()
    {
        Assert.False(NetezzaColumnCatalogMapper.AttNotNullToNullable(true));
        Assert.True(NetezzaColumnCatalogMapper.AttNotNullToNullable(false));
        Assert.False(NetezzaColumnCatalogMapper.AttNotNullToNullable(1));
        Assert.True(NetezzaColumnCatalogMapper.AttNotNullToNullable(0));
    }

    [Fact]
    public void ToSchemaColumn_MapsAttNotNullAndStripsEmbeddedNotNull()
    {
        var column = NetezzaColumnCatalogMapper.ToSchemaColumn(
            "DATEKEY",
            "INTEGER NOT NULL",
            attNotNull: true,
            description: "pk",
            defaultValue: null);

        Assert.Equal("DATEKEY", column.Name);
        Assert.Equal("INTEGER", column.DataType);
        Assert.False(column.Nullable);
        Assert.Equal("pk", column.Description);
    }

    [Fact]
    public void ToColumnDdl_FromCatalog_SetsNotNullFromAttNotNull()
    {
        var pk = NetezzaColumnCatalogMapper.ToColumnDdl("DATEKEY", "INTEGER", true);
        var nullable = NetezzaColumnCatalogMapper.ToColumnDdl(
            "FULLDATEALTERNATEKEY",
            "TIMESTAMP NOT NULL",
            false);

        Assert.True(pk.NotNull);
        Assert.Equal("INTEGER", pk.FullTypeName);
        Assert.False(nullable.NotNull);
        Assert.Equal("TIMESTAMP", nullable.FullTypeName);
    }

    [Fact]
    public void ToColumnDdl_FromSchemaColumn_PreservesNullabilityAndStripsType()
    {
        var ddl = NetezzaColumnCatalogMapper.ToColumnDdl(
            new NetezzaSchemaColumn("NAME", "VARCHAR(100) NOT NULL", Nullable: true));

        Assert.Equal("VARCHAR(100)", ddl.FullTypeName);
        Assert.False(ddl.NotNull);
    }
}
