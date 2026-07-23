using JustyBase.NetezzaDdl;

namespace JustyBase.NetezzaSql.Tests;

public sealed class NetezzaImportSqlTests
{
    [Fact]
    public void ImportSql_BuildsRandomTableAndEscapesPipeName()
    {
        string create = NetezzaImportSql.CreateRandomDistributionTable("import table", ["ID INTEGER", "NAME VARCHAR(20)"]);
        string insert = NetezzaImportSql.InsertFromExternalPipe("import table", "a'b", ["ID INTEGER"]);

        Assert.Contains("CREATE TABLE \"import table\"", create);
        Assert.Contains("DISTRIBUTE ON RANDOM", create);
        Assert.Contains("\\\\.\\pipe\\a''b", insert);
    }
}
