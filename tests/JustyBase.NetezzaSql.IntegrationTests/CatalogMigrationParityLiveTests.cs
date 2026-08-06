using JustyBase.NetezzaCatalogSql;
using JustyBase.NetezzaDriver;
using CatalogSql = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Live smoke for the modern shared catalog SQL used by the Legacy host after the legacy catalog
/// SQL file was retired (<c>NetezzaCatalogSql.Legacy.cs</c>). Gated by NZ_DEV_* (soft-skip).
/// </summary>
public sealed class CatalogMigrationParityLiveTests
{
    private static bool TryOpen(out NzConnection? connection)
    {
        if (!NetezzaLiveTestHost.TryCreateConnection(out var created))
        {
            connection = null;
            return false;
        }

        connection = created!;
        connection.Open();
        return true;
    }

    private static List<object?[]> Run(NzConnection conn, string sql, int fieldCount)
        => NetezzaLiveTestHost.ExecuteReaderRows(conn, sql, fieldCount);

    private static string Str(object? value) => value is null or DBNull ? string.Empty : value.ToString()!;

    [Fact]
    public void OwnerAndSchemaColumns_PresentInModernObjects()
    {
        if (!TryOpen(out var created))
        {
            return;
        }

        using var conn = created!;
        try
        {
            var rows = Run(conn, CatalogSql.GetSqlTablesAndOtherObjects(conn.Database), 7);
            Assert.NotEmpty(rows);

            int withSchema = rows.Count(r => !string.IsNullOrEmpty(Str(r[3])));
            int withOwner = rows.Count(r => !string.IsNullOrEmpty(Str(r[5])));

            Assert.True(withSchema >= rows.Count * 0.8, $"schema present in {withSchema}/{rows.Count}");
            Assert.True(withOwner >= rows.Count * 0.5, $"owner present in {withOwner}/{rows.Count}");
        }
        finally
        {
            conn.Close();
        }
    }

    [Fact]
    public void Columns_ModernAndDistributionQueries_AreConsistent()
    {
        if (!TryOpen(out var created))
        {
            return;
        }

        using var conn = created!;
        try
        {
            string database = conn.Database;

            var modern = Run(conn, CatalogSql.GetSqlOfColumns(database), 6);
            var distOrg = Run(conn, CatalogSql.GetLegacyDistributionColumnsSql(database), 4);

            Assert.NotEmpty(modern);
            Assert.NotEmpty(distOrg);

            Assert.All(distOrg, row => Assert.True(
                row[2] is null or DBNull or sbyte or byte or short or int,
                $"unexpected DISTSEQNO type: {row[2]?.GetType().Name}"));
        }
        finally
        {
            conn.Close();
        }
    }

    [Fact]
    public void Keys_ModernQuery_ReturnsRows()
    {
        if (!TryOpen(out var created))
        {
            return;
        }

        using var conn = created!;
        try
        {
            var rows = Run(conn, CatalogSql.GetLegacyKeysSql(conn.Database), 9);

            Assert.NotEmpty(rows);
            Assert.All(rows, row =>
            {
                Assert.True(Convert.ToInt32(row[0]) != 0);
                Assert.NotEmpty(Str(row[2]));
            });
        }
        finally
        {
            conn.Close();
        }
    }

    [Fact]
    public void Descriptions_SharedQuery_ReturnsRows()
    {
        if (!TryOpen(out var created))
        {
            return;
        }

        using var conn = created!;
        try
        {
            var rows = Run(conn, CatalogSql.GetDescSql(conn.Database), 2);

            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.NotEmpty(Str(row[1])));
        }
        finally
        {
            conn.Close();
        }
    }
}
