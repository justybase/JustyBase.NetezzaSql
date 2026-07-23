using CatalogSql = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql;

namespace JustyBase.NetezzaSql.Tests;

public sealed class CatalogMetadataQueryTests
{
    [Fact]
    public void MetadataQueries_UseDatabaseQualifiedViewsAndFilters()
    {
        var schemas = CatalogSql.GetSchemasSql("sample_db");
        var types = CatalogSql.GetObjectTypesSql("sample_db");
        var storage = CatalogSql.GetTableStorageStatsSql("sample_db", "admin", "orders");
        var descriptions = CatalogSql.GetObjectDescriptionsSql("sample_db", "admin");

        Assert.Contains("SAMPLE_DB.._V_SCHEMA", schemas);
        Assert.Contains("DATABASE = 'SAMPLE_DB'", schemas);
        Assert.Contains("SAMPLE_DB.._V_OBJECT_DATA", types);
        Assert.Contains("DBNAME = 'SAMPLE_DB'", types);
        Assert.Contains("SAMPLE_DB.._V_TABLE_STORAGE_STAT", storage);
        Assert.Contains("SCHEMA = 'admin'", storage);
        Assert.Contains("TABLENAME = 'orders'", storage);
        Assert.Contains("DESCRIPTION IS NOT NULL", descriptions);
    }

    [Theory]
    [InlineData("DB' OR '1'='1")]
    [InlineData("SCHEMA'; DROP TABLE T;--")]
    public void MetadataQueries_EscapeLiteralFilters(string value)
    {
        var sql = CatalogSql.GetTableStorageStatsSql("DB", value, value);

        Assert.Contains("''", sql);
        Assert.DoesNotContain($"'{value}'", sql);
    }
}
