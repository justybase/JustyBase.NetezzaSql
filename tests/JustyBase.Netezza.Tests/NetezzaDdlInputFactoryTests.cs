using JustyBase.Netezza.Ddl;
using JustyBase.Netezza.Models;

namespace JustyBase.Netezza.Tests;

public sealed class NetezzaDdlInputFactoryTests
{
    [Fact]
    public void BuildTable_PreservesMetadataAndOptions()
    {
        var table = new NetezzaSchemaTable(
            "ORDERS",
            "PUBLIC",
            "SALES",
            Columns: [
                new NetezzaSchemaColumn("ORDER_ID", "BIGINT", Nullable: false, DefaultValue: "1"),
                new NetezzaSchemaColumn("NOTE", "VARCHAR(200)", Description: "free text")],
            Description: "orders table");

        var input = NetezzaDdlInputFactory.BuildTable(
            table,
            distributeColumns: ["ORDER_ID"],
            organizeColumns: ["ORDER_ID"],
            overrideTableName: "ORDERS_COPY");

        Assert.Equal("SALES", input.Database);
        Assert.Equal("PUBLIC", input.Schema);
        Assert.Equal("ORDERS_COPY", input.OverrideTableName);
        Assert.Equal(["ORDER_ID"], input.DistributeColumns);
        Assert.Equal("BIGINT", input.Columns[0].FullTypeName);
        Assert.True(input.Columns[0].NotNull);
        Assert.Equal("free text", input.Columns[1].Description);
    }

    [Fact]
    public void BuildProcedure_SplitsCatalogSignature()
    {
        var input = NetezzaDdlInputFactory.BuildProcedureFromSignature(
            "SALES", "PUBLIC", "PUBLIC.CALCULATE_TOTAL(INTEGER)", "INTEGER", "RETURN $1;", true, "total");

        Assert.Equal("CALCULATE_TOTAL", input.ProcedureName);
        Assert.Equal("(INTEGER)", input.Arguments);
        Assert.True(input.ExecuteAsOwner);
        Assert.Equal("total", input.Description);
    }

    [Fact]
    public void BuildTable_UsesSafeFallbackTypeForIncompleteMetadata()
    {
        var input = NetezzaDdlInputFactory.BuildTable(
            new NetezzaSchemaTable("STAGING", Columns: [new NetezzaSchemaColumn("RAW_VALUE")]));

        var column = Assert.Single(input.Columns);
        Assert.Equal("VARCHAR(ANY)", column.FullTypeName);
        Assert.False(column.NotNull);
    }

    [Fact]
    public void BuildTable_StripsEmbeddedNotNullFromDataType()
    {
        var input = NetezzaDdlInputFactory.BuildTable(
            new NetezzaSchemaTable(
                "T",
                Columns:
                [
                    new NetezzaSchemaColumn("ID", "INTEGER NOT NULL", Nullable: false),
                    new NetezzaSchemaColumn("NAME", "VARCHAR(10) NOT NULL", Nullable: true)
                ]));

        Assert.Equal("INTEGER", input.Columns[0].FullTypeName);
        Assert.True(input.Columns[0].NotNull);
        Assert.Equal("VARCHAR(10)", input.Columns[1].FullTypeName);
        Assert.False(input.Columns[1].NotNull);
    }

    [Fact]
    public void BuildExternal_RejectsNullOptions()
    {
        var table = new NetezzaSchemaTable("EXT_DATA");

        Assert.Throws<ArgumentNullException>(() => NetezzaDdlInputFactory.BuildExternal(table, null!));
    }
}
