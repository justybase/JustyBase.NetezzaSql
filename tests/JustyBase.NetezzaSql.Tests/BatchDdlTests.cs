using JustyBase.NetezzaDdl;
using JustyBase.NetezzaDdl.Models;

namespace JustyBase.NetezzaSql.Tests;

public sealed class BatchDdlTests
{
    [Fact]
    public void Build_EmitsObjectsInStableDependencyFriendlyOrder()
    {
        var sql = new NetezzaBatchDdlBuilder().Build(new NetezzaBatchDdlInput(
            Tables:
            [new NetezzaTableDdlInput("DB", "S", "T", [new NetezzaColumnDdl("ID", "INTEGER")])],
            ExternalTables:
            [new NetezzaExternalDdlInput("DB", "S", "EXT", [new NetezzaColumnDdl("ID", "INTEGER")], new())],
            Views:
            [new NetezzaViewDdlInput("DB", "S", "V", "SELECT ID FROM T")],
            Procedures:
            [new NetezzaProcedureDdlInput("DB", "S", "P", "INTEGER", "RETURN 1;")],
            Synonyms:
            [new NetezzaSynonymDdlInput("DB", "S", "SYN", "DB.S.T")]));

        Assert.True(sql.IndexOf("-- TABLE DB.S.T", StringComparison.Ordinal) <
                    sql.IndexOf("-- EXTERNAL TABLE DB.S.EXT", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("-- EXTERNAL TABLE DB.S.EXT", StringComparison.Ordinal) <
                    sql.IndexOf("-- VIEW DB.S.V", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("-- VIEW DB.S.V", StringComparison.Ordinal) <
                    sql.IndexOf("-- PROCEDURE DB.S.P", StringComparison.Ordinal));
        Assert.Contains("CREATE SYNONYM DB.S.SYN FOR DB.S.T;", sql);
    }

    [Fact]
    public void BuildDetailed_ReportsObjectsWithMissingRequiredMetadata()
    {
        var result = new NetezzaBatchDdlBuilder().BuildDetailed(new NetezzaBatchDdlInput(
            Tables: [new NetezzaTableDdlInput("DB", "S", "EMPTY", [])],
            Views: [new NetezzaViewDdlInput("DB", "S", "NO_SQL", " ")]));

        Assert.Equal(2, result.SkippedObjects.Count);
        Assert.Contains("DB.S.EMPTY", result.Sql);
        Assert.Contains("DB.S.NO_SQL", result.Sql);
        Assert.DoesNotContain("CREATE TABLE", result.Sql);
    }

    [Fact]
    public void Build_RecreateTablesUsesExistingRecreateFlow()
    {
        var sql = new NetezzaBatchDdlBuilder().Build(new NetezzaBatchDdlInput(
            Tables: [new NetezzaTableDdlInput("DB", "S", "T", [new NetezzaColumnDdl("ID", "INTEGER")])],
            RecreateTables: true));

        Assert.Contains("INSERT INTO DB.S.TMP_", sql);
        Assert.Contains("GENERATE EXPRESS STATISTICS", sql);
    }
}
