using JustyBase.ImportExport.Import;
using JustyBase.NetezzaDdl;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ImportEngineTests
{
    private static readonly IImportColumn[] Columns =
    [
        new ImportColumn("ID", ImportColumnKind.Integer),
        new ImportColumn("AMOUNT", ImportColumnKind.Numeric, 16, 2),
        new ImportColumn("LABEL", ImportColumnKind.Nvarchar, 20)
    ];

    [Fact]
    public void BatchInsert_SingleRow_ProducesValueTuple()
    {
        string sql = BatchInsertEngine.BuildInsertStatement("T", Columns);

        Assert.Equal("INSERT INTO T (ID, AMOUNT, LABEL) VALUES (@p0, @p1, @p2)", sql);
    }

    [Fact]
    public void BatchInsert_MultipleRows_ProducesConsecutiveValueTuples()
    {
        string sql = BatchInsertEngine.BuildInsertStatement("dbo.T", Columns, rowCount: 2);

        Assert.Equal(
            "INSERT INTO dbo.T (ID, AMOUNT, LABEL) VALUES (@p0, @p1, @p2), (@p3, @p4, @p5)",
            sql);
    }

    [Fact]
    public void BatchInsert_RejectsZeroColumnsOrRows()
    {
        Assert.Throws<ArgumentException>(() => BatchInsertEngine.BuildInsertStatement("T", Array.Empty<IImportColumn>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => BatchInsertEngine.BuildInsertStatement("T", Columns, 0));
    }

    [Fact]
    public void ImportEngineOptions_DefaultsToRemoteDotnetAndAmbientUsing()
    {
        var options = new ImportEngineOptions();

        Assert.Equal(NetezzaImportUsingOptions.DefaultRemoteSource, options.RemoteSource);
        Assert.Null(options.TempLogDirectory);
        Assert.Null(options.UsingOptions);
    }
}
