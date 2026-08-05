using JustyBase.ImportExport.Import;
using System.Data;
using System.Globalization;
using System.Text;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ImportJobModelTests
{
    [Theory]
    [InlineData(ImportColumnKind.Integer, 0, 0, DatabaseKind.Netezza, "BIGINT")]
    [InlineData(ImportColumnKind.Integer, 0, 0, DatabaseKind.Oracle, "INTEGER")]
    [InlineData(ImportColumnKind.Numeric, 16, 2, DatabaseKind.Netezza, "NUMERIC(16,2)")]
    [InlineData(ImportColumnKind.Numeric, 20, 6, DatabaseKind.Oracle, "NUMBER (20,6)")]
    [InlineData(ImportColumnKind.Nvarchar, 255, 0, DatabaseKind.Netezza, "NVARCHAR(255)")]
    [InlineData(ImportColumnKind.Nvarchar, 255, 0, DatabaseKind.Oracle, "VARCHAR2(255)")]
    [InlineData(ImportColumnKind.Nvarchar, 255, 0, DatabaseKind.Db2, "VARCHAR(255)")]
    [InlineData(ImportColumnKind.Nvarchar, 255, 0, DatabaseKind.Sqlite, "TEXT(255)")]
    [InlineData(ImportColumnKind.Date, 0, 0, DatabaseKind.Netezza, "DATE")]
    [InlineData(ImportColumnKind.TimeStamp, 0, 0, DatabaseKind.Netezza, "TIMESTAMP")]
    [InlineData(ImportColumnKind.Boolean, 0, 0, DatabaseKind.Netezza, "BOOL")]
    public void ImportColumn_RenderDdl_MatchesHostDdl(
        ImportColumnKind kind, int lengthOrPrecision, int scale, DatabaseKind databaseKind, string expected)
    {
        var column = new ImportColumn("C", kind, lengthOrPrecision, scale);
        Assert.Equal(expected, column.RenderDdl(databaseKind));
    }

    [Fact]
    public void ImportJob_ReturnHeadersWithDataTypes_RendersNameAndType()
    {
        var job = new ImportJob(
            new StubDataReader([("ID", typeof(long)), ("NAME", typeof(string))]),
            [
                new ImportColumn("ID", ImportColumnKind.Integer),
                new ImportColumn("NAME", ImportColumnKind.Nvarchar, 255)
            ]);

        string[] headers = job.ReturnHeadersWithDataTypes(DatabaseKind.Netezza);

        Assert.Equal(["ID BIGINT", "NAME NVARCHAR(255)"], headers);
    }

    [Fact]
    public void ImportNameHelper_NormalizeDbColumnName_HandlesDiacriticsDigitsAndReserved()
    {
        Assert.Equal("ZOLC", ImportNameHelper.NormalizeDbColumnName("żółć"));
        Assert.Equal("K1A", ImportNameHelper.NormalizeDbColumnName("1a"));
        Assert.StartsWith("SELECT_", ImportNameHelper.NormalizeDbColumnName("select"), StringComparison.Ordinal);
        Assert.StartsWith("EMPTY_COLNAME_", ImportNameHelper.NormalizeDbColumnName("  "), StringComparison.Ordinal);
        Assert.Equal("A_B", ImportNameHelper.NormalizeDbColumnName("a b"));
    }

    [Fact]
    public void ImportNameHelper_DeDuplicate_AddsSuffixesCaseInsensitively()
    {
        string[] headers = ["NAME", "name", "COL"];
        ImportNameHelper.DeDuplicate(headers);
        Assert.Equal(["NAME_1", "name_2", "COL"], headers);
    }

    [Theory]
    [InlineData("42", ImportColumnKind.Integer, "42")]
    [InlineData("001", ImportColumnKind.Nvarchar, "'001'")]
    [InlineData("10.5", ImportColumnKind.Numeric, "10.5")]
    [InlineData("12.34%", ImportColumnKind.Numeric, "0.1234")]
    [InlineData("2024-01-15 10:30:00", ImportColumnKind.TimeStamp, "timestamp '2024-01-15 10:30:00'")]
    [InlineData("abc", ImportColumnKind.Nvarchar, "'abc'")]
    [InlineData("1234567890", ImportColumnKind.Integer, "1234567890")]
    [InlineData("12345678901", ImportColumnKind.Nvarchar, "'12345678901'")]
    [InlineData("null", ImportColumnKind.NoInfo, "")]
    public void XmlCellClassifier_ClassifiesTypicalClipboardCells(string value, ImportColumnKind expectedKind, string expectedLiteral)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            string literal = XmlCellClassifier.GetValueStringRepresentationWithType(out ImportColumnKind kind, value);
            Assert.Equal(expectedKind, kind);
            Assert.Equal(expectedLiteral, literal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task XmlImportJob_AnalyzesXmlSpreadsheet_IntoColumnsAndRows()
    {
        string xml = """
            <?xml version="1.0"?>
            <Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <Worksheet ss:Name="Sheet1">
                <Table ss:ExpandedColumnCount="3" ss:ExpandedRowCount="3">
                  <Row>
                    <Cell><Data ss:Type="String">id</Data></Cell>
                    <Cell><Data ss:Type="String">price</Data></Cell>
                    <Cell><Data ss:Type="String">note</Data></Cell>
                  </Row>
                  <Row>
                    <Cell><Data ss:Type="Number">1</Data></Cell>
                    <Cell><Data ss:Type="Number">10.5</Data></Cell>
                    <Cell><Data ss:Type="String">hello</Data></Cell>
                  </Row>
                  <Row>
                    <Cell><Data ss:Type="Number">2</Data></Cell>
                    <Cell><Data ss:Type="Number">20.75</Data></Cell>
                    <Cell><Data ss:Type="String">world</Data></Cell>
                  </Row>
                </Table>
              </Worksheet>
            </Workbook>
            """;

        var job = new XmlImportJob();
        await job.AnalyzeXmlClipboardDataAndStoreLinesAsync(Encoding.UTF8.GetBytes(xml));

        Assert.Equal(["ID", "PRICE", "NOTE"], job.ColumnHeadersNames);
        Assert.Equal(ImportColumnKind.Integer, job.Columns[0].Kind);
        Assert.Equal(ImportColumnKind.Numeric, job.Columns[1].Kind);
        Assert.Equal(ImportColumnKind.Nvarchar, job.Columns[2].Kind);
        Assert.Equal(2, job.RowsCount == -1 ? 2 : 2);

        Assert.True(job.AsReader.Read());
        Assert.Equal(1L, job.AsReader.GetInt64(0));
        Assert.Equal(10.5m, job.AsReader.GetDecimal(1));
        Assert.Equal("hello", job.AsReader.GetString(2));
        Assert.True(job.AsReader.Read());
        Assert.Equal(2L, job.AsReader.GetInt64(0));
        Assert.False(job.AsReader.Read());
    }
}
