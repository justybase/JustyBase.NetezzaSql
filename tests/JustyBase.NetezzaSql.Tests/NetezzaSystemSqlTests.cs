using JustyBase.NetezzaCatalogSql;

namespace JustyBase.NetezzaSql.Tests;

public sealed class NetezzaSystemSqlTests
{
    [Fact]
    public void ProcedureAndExternalQueries_UseValidatedObjectIds()
    {
        Assert.Contains("OBJID = 42", NetezzaSystemSql.GetProcedureByObjectId(42));
        Assert.Contains("RELID = 42", NetezzaSystemSql.GetExternalOptions(42));
        Assert.Contains("OBJID = 42", NetezzaSystemSql.GetExternalObjectName(42));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetezzaSystemSql.GetProcedureByObjectId(0));
    }

    [Fact]
    public void DdlQueries_ValidateDatabaseAndEscapeLiterals()
    {
        string sql = NetezzaSystemSql.GetProceduresForDdl("mixed_db");

        Assert.Contains("MIXED_DB.._V_PROCEDURE", sql);
        Assert.Contains("DATABASE = 'MIXED_DB'", sql);
        Assert.Equal("SET CATALOG MIXED_DB;", NetezzaSystemSql.SetCatalog("mixed_db"));
        Assert.Throws<ArgumentException>(() => NetezzaSystemSql.SetCatalog("DB; DROP TABLE X"));
    }

    [Fact]
    public void DdlQueries_FilterQuotedDatabaseByUnquotedValue()
    {
        Assert.Contains("FROM \"Mixed Db\".._V_PROCEDURE", NetezzaSystemSql.GetProceduresForDdl("\"Mixed Db\""));
        Assert.Contains("DATABASE = 'Mixed Db'", NetezzaSystemSql.GetProceduresForDdl("\"Mixed Db\""));
        Assert.Contains("FROM \"Mixed Db\".._V_VIEW", NetezzaSystemSql.GetViewsForDdl("\"Mixed Db\""));
        Assert.Contains("DATABASE = 'Mixed Db'", NetezzaSystemSql.GetViewsForDdl("\"Mixed Db\""));
    }

    [Fact]
    public void LoadProgress_RequiresNonNegativeRowCount()
    {
        Assert.Contains("ROWSINSERTED/15", NetezzaSystemSql.GetLoadProgress(15));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetezzaSystemSql.GetLoadProgress(-1));
    }

    [Fact]
    public void DiagnosticQueries_EscapeValuesAndValidateIdentifiers()
    {
        Assert.Contains("FUNCTION = 'O''HARE'", NetezzaSystemSql.GetFunctionInfo("O'HARE"));
        Assert.Contains("FROM MY_TABLE s", NetezzaSystemSql.GetSequenceMetadata("my_table"));
        Assert.Contains("FROM DB1..ORDERS", NetezzaSystemSql.GetDistributionWithDeletedRecords("db1", "orders"));
        Assert.Throws<ArgumentException>(() => NetezzaSystemSql.GetDistributionWithDeletedRecords("db1", "orders; drop table x"));
    }

    [Fact]
    public void DiagnosticTemplates_TargetNetezzaCatalogs()
    {
        Assert.Contains("_V_PROCEDURE", NetezzaSystemSql.ProcedureSearchTemplate);
        Assert.Contains("_V_VIEW", NetezzaSystemSql.ViewSearchTemplate);
        Assert.Contains("_V_SYSTEM_INFO", NetezzaSystemSql.ServerInformation);
        Assert.Contains("_V_HWCOMP", NetezzaSystemSql.HardwareInformation);
        Assert.Contains("_V_TABLE_STORAGE_STAT", NetezzaSystemSql.GetTableSizesReport("db1"));
        Assert.Contains("s.pid = 10", NetezzaSystemSql.GetEstimatedQueryCost(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetezzaSystemSql.GetEstimatedQueryCost(-1));
    }
}
