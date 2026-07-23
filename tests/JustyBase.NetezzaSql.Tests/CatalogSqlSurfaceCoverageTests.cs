using JustyBase.NetezzaCatalogSql;
using CatalogSql = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql;

namespace JustyBase.NetezzaSql.Tests;

public sealed class CatalogSqlSurfaceCoverageTests
{
    [Fact]
    public void CatalogQueries_GenerateQualifiedSqlForEveryPublicQuery()
    {
        const string database = "sample_db";

        var queries = new[]
        {
            CatalogSql.GetSqlTablesAndOtherObjects(database),
            CatalogSql.GetSqlOfColumns(database),
            CatalogSql.GetSchemasSql(database),
            CatalogSql.GetObjectTypesSql(database),
            CatalogSql.GetTableStorageStatsSql(database),
            CatalogSql.GetTableStorageStatsSql(database, "admin", "orders"),
            CatalogSql.GetObjectDescriptionsSql(database),
            CatalogSql.GetObjectDescriptionsSql(database, "admin"),
            CatalogSql.GetProceduresSql(database, string.Empty),
            CatalogSql.GetProceduresSql(database, "proc'name"),
            CatalogSql.GetSynonymSql(database),
            CatalogSql.GetViewsSql(database, string.Empty),
            CatalogSql.GetViewsSql(database, "view'name"),
            CatalogSql.GetExternalTableSql(database),
            CatalogSql.GetKeysSql(database),
            CatalogSql.GetDistributeSql(database),
            CatalogSql.GetOrganizeSql(database),
            CatalogSql.GetDescSql(database),
            CatalogSql.GetLegacyProcSql(database),
            CatalogSql.GetLegacySynonymSql(database),
            CatalogSql.GetLegacyViewSql(database),
            CatalogSql.GetLegacyExternalSql(database)
        };

        Assert.All(queries, sql =>
        {
            Assert.NotEmpty(sql);
            Assert.Contains("SAMPLE_DB", sql, StringComparison.Ordinal);
        });
        Assert.Contains("proc''name", queries[9], StringComparison.Ordinal);
        Assert.Contains("view''name", queries[12], StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogObjectQueries_HandleSystemAndQuotedDatabases()
    {
        var systemObjects = CatalogSql.GetSqlTablesAndOtherObjects("SYSTEM");
        var legacySystem = CatalogSql.GetLegacyBazyTabeleSql("SYSTEM");
        var legacyUnion = CatalogSql.GetLegacyBazyTabeleSql("sample", noDescMode: true);
        var quoted = CatalogSql.GetExternalTableSql("\"Mixed Db\"");

        Assert.Contains("FROM SYSTEM.._V_OBJECT_DATA", systemObjects, StringComparison.Ordinal);
        Assert.Contains("FROM SYSTEM.._V_OBJECT_DATA", legacySystem, StringComparison.Ordinal);
        Assert.Contains("UNION ALL", legacyUnion, StringComparison.Ordinal);
        Assert.Contains("\"Mixed Db\".._V_EXTERNAL", quoted, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyCatalogQueries_CoverOwnerSchemaAndEscapingModes()
    {
        var owner = CatalogSql.GetLegacyOneTableSqlOwner("order'name");
        var schema = CatalogSql.GetLegacyOneTableSqlSchema("order'name", schemaOn: true);
        var ownerFallback = CatalogSql.GetLegacyOneTableSqlSchema("orders", schemaOn: false);
        var columns = CatalogSql.GetLegacyObjectColumnsSql("sample");
        var search = CatalogSql.GetLegacySearchInSchemaSql("sample", "needle'name");
        var functions = CatalogSql.GetLegacyFulidesSql("sample", 17);

        Assert.Contains("ORDER''NAME", owner, StringComparison.Ordinal);
        Assert.Contains("D1.SCHEMA", schema, StringComparison.Ordinal);
        Assert.Contains("D1.OWNER", ownerFallback, StringComparison.Ordinal);
        Assert.Contains("_V_TABLE_DIST_MAP", columns, StringComparison.Ordinal);
        Assert.Contains("%NEEDLE''NAME%", search, StringComparison.Ordinal);
        Assert.Contains("DATABASEID = 17", functions, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1DATABASE")]
    [InlineData("DATABASE-NAME")]
    [InlineData("\"unterminated")]
    public void CatalogDatabaseQueries_RejectInvalidIdentifiers(string database)
    {
        Assert.Throws<ArgumentException>(() => CatalogSql.GetViewsSql(database, string.Empty));
    }

    [Fact]
    public void SystemQueries_GenerateAllPublicDiagnosticStatements()
    {
        var queries = new[]
        {
            NetezzaSystemSql.GetViewsForDdl("db"),
            NetezzaSystemSql.GetViewDefinitionLength(1),
            NetezzaSystemSql.GetViewDefinitionByObjectId(1),
            NetezzaSystemSql.GetExternalOptions(1),
            NetezzaSystemSql.GetExternalObjectName(1),
            NetezzaSystemSql.GetTableSizesReport("db"),
            NetezzaSystemSql.GetQueryHistory("db"),
            NetezzaSystemSql.GetUserSessions("db"),
            NetezzaSystemSql.GetGroomTableCandidates("db"),
            NetezzaSystemSql.GetDatabaseSize("db"),
            NetezzaSystemSql.GetTableStorageStatistics("orders"),
            NetezzaSystemSql.GetAggregateInfo("sum'name"),
            NetezzaSystemSql.GetEstimatedQueryCost(9),
            NetezzaSystemSql.GetEstimatedQueryCost(9, 10)
        };

        Assert.All(queries, Assert.NotEmpty);
        Assert.Contains("AGGREGATE = 'sum''name'", queries[11], StringComparison.Ordinal);
        Assert.Contains("QS_SESSIONID = 9", queries[12], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SystemQueries_RejectInvalidObjectIds(int objectId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NetezzaSystemSql.GetViewDefinitionLength(objectId));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetezzaSystemSql.GetViewDefinitionByObjectId(objectId));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetezzaSystemSql.GetExternalOptions(objectId));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetezzaSystemSql.GetExternalObjectName(objectId));
    }

    [Theory]
    [InlineData("CHARACTER VARYING", "CHARACTER VARYING(ANY)")]
    [InlineData("NATIONAL CHARACTER VARYING", "NATIONAL CHARACTER VARYING(ANY)")]
    [InlineData("NATIONAL CHARACTER", "NATIONAL CHARACTER(ANY)")]
    [InlineData("CHARACTER", "CHARACTER(ANY)")]
    [InlineData("INTEGER", "INTEGER")]
    public void ProcedureReturnTypes_UseExpectedNetezzaForms(string input, string expected)
    {
        Assert.Equal(expected, NetezzaProcTypes.FixProcedureReturnType(input));
    }
}
