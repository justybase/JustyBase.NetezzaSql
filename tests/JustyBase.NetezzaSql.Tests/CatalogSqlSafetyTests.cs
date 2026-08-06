using CatalogSql = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql;

namespace JustyBase.NetezzaSql.Tests;

public sealed class CatalogSqlSafetyTests
{
    [Theory]
    [InlineData("DB; DROP TABLE USERS")]
    [InlineData("DB' OR 1=1 --")]
    [InlineData("\"DB\"; DROP TABLE USERS")]
    public void CatalogMethods_RejectUnsafeDatabaseIdentifiers(string database)
    {
        Assert.Throws<ArgumentException>(() => CatalogSql.GetSqlOfColumns(database));
    }

    [Fact]
    public void CatalogMethods_PreserveQuotedDatabaseIdentifiers()
    {
        string sql = CatalogSql.GetSqlOfColumns("\"Mixed.Db\"");

        Assert.Contains("\"Mixed.Db\".._V_RELATION_COLUMN", sql);
        Assert.Contains("DATABASE = '\"Mixed.Db\"'", sql);
    }

    [Fact]
    public void CatalogMethods_EscapeProcedureFilterLiteral()
    {
        string sql = CatalogSql.GetProceduresSql("DB", "PROC'OOPS");

        Assert.Contains("PROCEDURESIGNATURE = 'PROC''OOPS'", sql);
    }

    [Theory]
    [InlineData("DB; DROP TABLE USERS")]
    [InlineData("DB' OR 1=1 --")]
    [InlineData("\"DB\"; DROP TABLE USERS")]
    public void LegacyHostQueries_RejectUnsafeDatabaseIdentifiers(string database)
    {
        Assert.Throws<ArgumentException>(() => CatalogSql.GetLegacyKeysSql(database));
        Assert.Throws<ArgumentException>(() => CatalogSql.GetLegacyDistributionColumnsSql(database));
    }

    [Fact]
    public void LegacyHostQueries_PreserveQuotedDatabaseIdentifiers()
    {
        string keys = CatalogSql.GetLegacyKeysSql("\"Mixed.Db\"");
        string dist = CatalogSql.GetLegacyDistributionColumnsSql("\"Mixed.Db\"");

        Assert.Contains("\"Mixed.Db\".._V_RELATION_KEYDATA", keys, StringComparison.Ordinal);
        Assert.Contains("\"Mixed.Db\".._V_RELATION_COLUMN", dist, StringComparison.Ordinal);
    }
}
