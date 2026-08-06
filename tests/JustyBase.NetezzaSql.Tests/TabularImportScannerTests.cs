using JustyBase.ImportExport.Import;
using System.Data;
using System.Globalization;
using System.Text;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>Shared <see cref="IImportSource"/> over <see cref="CsvRowReader"/> for scanner tests (mirrors the host CSV adapter).</summary>
internal sealed class TestCsvImportSource : IImportSource
{
    private readonly CsvRowReader _inner;

    public TestCsvImportSource(string filePath)
    {
        FilePath = filePath;
        _inner = new CsvRowReader();
        _inner.Open(filePath);
    }

    public string? FilePath { get; }
    public bool IsCsvSource => true;
    public bool IsExclusiveOpen => false;
    public string? ActualSheetName { get; set; }
    public bool TreatAllColumnsAsText { get; set; }
    public int FieldCount => _inner.FieldCount;
    public IReadOnlyList<string> GetSheetNames() => [Path.GetFileName(FilePath!).Replace('.', '_')];
    public string? GetName(int column) => _inner.GetName(column);
    public bool Read() => _inner.Read();
    public string? GetCellText(int column) => _inner.GetFieldString(column);
    public int GetRawLength(int column) => _inner.GetFieldLength(column);
    public double ReadProgress => _inner.Position;

    public IDataReader CreateTypedReader(IReadOnlyList<ImportColumnKind> kinds, IReadOnlyList<string> normalizedHeaders)
        => new TestTypedReader(_inner, kinds, normalizedHeaders);

    public void Dispose() => _inner.Dispose();
}

internal sealed class TestTypedReader : IDataReader
{
    private readonly CsvRowReader _source;
    private readonly IReadOnlyList<ImportColumnKind> _kinds;
    private readonly IReadOnlyList<string> _headers;

    public TestTypedReader(CsvRowReader source, IReadOnlyList<ImportColumnKind> kinds, IReadOnlyList<string> headers)
    {
        _source = source;
        _kinds = kinds;
        _headers = headers;
    }

    public int FieldCount => _kinds.Count;

    public bool IsDBNull(int i) => _source.GetFieldLength(i) == 0;

    private object ConvertValue(int i)
    {
        string raw = _source.GetFieldString(i);
        return _kinds[i] switch
        {
            ImportColumnKind.Integer => long.Parse(raw),
            ImportColumnKind.Numeric => decimal.Parse(raw, CultureInfo.InvariantCulture),
            ImportColumnKind.Date or ImportColumnKind.TimeStamp => DateTime.Parse(raw),
            ImportColumnKind.Boolean => bool.Parse(raw),
            _ => raw
        };
    }

    public object GetValue(int i) => ConvertValue(i);
    public string GetName(int i) => _headers[i];
    public string GetString(int i) => _source.GetFieldString(i);
    public bool Read() => _source.Read();
    public object this[int i] => _source.GetFieldString(i);
    public object this[string name] => throw new NotImplementedException();
    public int Depth => 0;
    public bool IsClosed => false;
    public int RecordsAffected => throw new NotImplementedException();
    public void Close() { }
    public void Dispose() { }
    public bool GetBoolean(int i) => throw new NotImplementedException();
    public byte GetByte(int i) => throw new NotImplementedException();
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public char GetChar(int i) => throw new NotImplementedException();
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public IDataReader GetData(int i) => throw new NotImplementedException();
    public string GetDataTypeName(int i) => throw new NotImplementedException();
    public DateTime GetDateTime(int i) => throw new NotImplementedException();
    public decimal GetDecimal(int i) => throw new NotImplementedException();
    public double GetDouble(int i) => throw new NotImplementedException();
    public Type GetFieldType(int i) => throw new NotImplementedException();
    public float GetFloat(int i) => throw new NotImplementedException();
    public Guid GetGuid(int i) => throw new NotImplementedException();
    public short GetInt16(int i) => throw new NotImplementedException();
    public int GetInt32(int i) => throw new NotImplementedException();
    public long GetInt64(int i) => throw new NotImplementedException();
    public int GetOrdinal(string name) => throw new NotImplementedException();
    public DataTable? GetSchemaTable() => null;
    public int GetValues(object[] values) => throw new NotImplementedException();
    public bool NextResult() => throw new NotImplementedException();
}

public sealed class TestSourceFactory : IImportSourceFactory
{
    public IImportSource OpenSource(string filePath, Encoding? encoding) => new TestCsvImportSource(filePath);
    public bool IsExclusiveOpen(string filePath) => false;
}

public sealed class TabularImportScannerTests
{
    private static string WriteCsv(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tiscanner_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }

    private static TabularImportScanner NewScanner(string csv)
    {
        return new TabularImportScanner(new TestSourceFactory())
        {
            FilePath = csv
        };
    }

    [Fact]
    public async Task Scan_DetectsHeadersKindsAndPreview()
    {
        string path = WriteCsv("id,price,code,dateiso\n1,10.5,001,2024-01-15\n2,20.75,002,2024-02-01\n3,1,003,2024-03-01\n");
        var sc = NewScanner(path);
        try
        {
            Assert.True(sc.OpenSource());
            Assert.Equal([Path.GetFileName(path).Replace('.', '_')], sc.SheetNames);

            SheetScanResult result = (await sc.ScanSheetAsync(sc.SheetNames[0]))!;

            Assert.NotNull(result);
            Assert.Equal(["ID", "PRICE", "CODE", "DATEISO"], result.NormalizedHeaders);
            Assert.Equal(4, result.RawValueLengths.Length);
            Assert.Equal(3, result.PreviewRows.Count);
            Assert.Equal(ImportColumnKind.Integer, result.DetectedTypes[0].Kind);
            Assert.Equal(ImportColumnKind.Numeric, result.DetectedTypes[1].Kind);
            Assert.Equal(ImportColumnKind.Nvarchar, result.DetectedTypes[2].Kind); // leading zero stays text
            Assert.Equal(ImportColumnKind.Date, result.DetectedTypes[3].Kind);
        }
        finally
        {
            sc.DisposeSource();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Validate_ReportsInvalidOverrideAgainstSelectedPlan()
    {
        string path = WriteCsv("code\n1\nnot-an-int\n\n");
        var sc = NewScanner(path);
        try
        {
            Assert.True(sc.OpenSource());
            string sheet = sc.SheetNames[0];
            SheetScanResult scan = (await sc.ScanSheetAsync(sheet))!;

            var plan = new SheetPlan(sheet,
                new IImportColumn[] { new ImportColumn("CODE", ImportColumnKind.Integer) }, scan.PreviewRows, scan.RowsCount);

            IReadOnlyList<ImportValidationError> errors = await sc.ValidateSelectedSheetsAsync([sheet], _ => plan);

            ImportValidationError error = Assert.Single(errors);
            Assert.Equal(sheet, error.SheetName);
            Assert.Equal(3, error.RowNumber);
            Assert.Equal("CODE", error.ColumnName);
            Assert.Equal(ImportColumnKind.Integer, error.SelectedKind);
            Assert.Equal("not-an-int", error.Value);
        }
        finally
        {
            sc.DisposeSource();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CreateJobs_StreamsRowsPerSelectedPlan()
    {
        string path = WriteCsv("id,price\n1,10.5\n2,20.75\n");
        var sc = NewScanner(path);
        try
        {
            Assert.True(sc.OpenSource());
            string sheet = sc.SheetNames[0];
            var plan = new SheetPlan(sheet,
                new IImportColumn[]
                {
                    new ImportColumn("ID", ImportColumnKind.Integer),
                    new ImportColumn("PRICE", ImportColumnKind.Numeric, 16, 2)
                },
                new string[][] { ["1", "10.5"] },
                1);

            int rows = 0;
            string[]? headers = null;
            await foreach (IImportJob job in sc.CreateJobs([sheet], _ => plan))
            {
                headers = job.ColumnHeadersNames.ToArray();
                while (job.AsReader.Read())
                {
                    rows++;
                }
            }

            Assert.Equal(["ID", "PRICE"], headers!);
            Assert.Equal(2, rows);
        }
        finally
        {
            sc.DisposeSource();
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(DatabaseKind.Netezza, null, "TAB", 0, "TAB")]
    [InlineData(DatabaseKind.Netezza, "dbo", "TAB", 1, "dbo.TAB_1")]
    [InlineData(DatabaseKind.Netezza, "dbo", "TAB", 2, "dbo.TAB_2")]
    [InlineData(DatabaseKind.Oracle, "scott", "TAB", 1, "TAB_1")]
    [InlineData(DatabaseKind.Oracle, null, "TAB", 0, "TAB")]
    public void BuildTableName_AppliesSchemaAndSuffixDependingOnDialect(DatabaseKind kind, string? schema, string mask, int index, string expected)
    {
        Assert.Equal(expected, TabularImportScanner.BuildTableName(kind, schema, mask, index));
    }
}
