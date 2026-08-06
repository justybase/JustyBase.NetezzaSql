using JustyBase.ImportExport.Import;
using JustyBase.NetezzaDdl;

namespace JustyBase.NetezzaSql.Tests;

public sealed class NetezzaImportUsingClauseTests
{
    /// <summary>Builds the USING clause exactly as <see cref="NetezzaExternalTableImportEngine"/> does for a typed pipe import.</summary>
    private static NetezzaImportUsingOptions EngineOptions(string? tempLogDirectory = "C:\\temp\\logs", int maxErrors = 0, string remoteSource = "dotnet")
        => new()
        {
            RemoteSource = remoteSource,
            Delimiter = "\\t",
            SkipRows = 1,
            NullValue = "",
            EncodingName = "utf-8",
            EscapeChar = "\\",
            TimeStyle = "24HOUR",
            // The typed pipe writes booleans as 1/0 (TypeCode.Boolean path).
            BoolStyle = "1_0",
            MaxErrors = maxErrors,
            LogDirectory = tempLogDirectory
        };

    [Fact]
    public void EngineClause_EscapesTabDelimiterAndKeeps24HourBoolStyle()
    {
        string clause = NetezzaImportSql.BuildUsingClause(EngineOptions());

        Assert.Contains("DELIMITER '\\t'", clause);
        Assert.Contains("TIMESTYLE '24HOUR'", clause);
        Assert.Contains("BOOLSTYLE '1_0'", clause);
        Assert.Contains("ESCAPECHAR '\\'", clause);
        Assert.Contains("NULLVALUE ''", clause);
    }

    [Fact]
    public void EngineClause_AlwaysEmitsRemoteSourceSkipRowsAndMaxErrors()
    {
        string clause = NetezzaImportSql.BuildUsingClause(EngineOptions(maxErrors: 0));

        Assert.Contains("REMOTESOURCE 'dotnet'", clause);
        Assert.Contains("SKIPROWS 1", clause);
        Assert.Contains("MAXERRORS 0", clause);
        Assert.Contains("LOGDIR 'C:\\temp\\logs'", clause);
    }

    [Fact]
    public void EngineClause_ObeysAmbientDefaultWhenNoLogDirConfigured()
    {
        // MaxErrors = 0 must stay present even with defaults so bad rows surface as errors.
        string clause = NetezzaImportSql.BuildUsingClause(new NetezzaImportUsingOptions
        {
            BoolStyle = "1_0",
            TimeStyle = "24HOUR",
            MaxErrors = 0,
            SkipRows = 1
        });

        Assert.Contains("BOOLSTYLE '1_0'", clause);
        Assert.Contains("MAXERRORS 0", clause);
        Assert.Contains("SKIPROWS 1", clause);
        Assert.DoesNotContain("LOGDIR", clause);
    }

    [Fact]
    public void BuildInsertSql_AppendsEngineUsingClauseToExternalPipeInsert()
    {
        string[] headers = ["A INTEGER", "B NVARCHAR(20)"];
        string sql = NetezzaImportEngine.BuildInsertSql(
            "T",
            "JDE12345",
            headers,
            EngineOptions());

        Assert.StartsWith("INSERT INTO ", sql);
        Assert.EndsWith("LOGDIR 'C:\\temp\\logs');", sql);
        Assert.Contains("BOOLSTYLE '1_0'", sql);
        Assert.Contains("MAXERRORS 0", sql);
    }

    [Fact]
    public void BuildInsertSql_WithTargetColumns_MapsIntoExistingTablePositionally()
    {
        string[] source = ["A INTEGER", "B NVARCHAR(20)"];
        string sql = NetezzaImportEngine.BuildInsertSql(
            "TARGET_T",
            "JDE12345",
            source,
            EngineOptions(),
            insertTargetColumns: ["TARGET_A", "TARGET_B"]);

        Assert.StartsWith("INSERT INTO TARGET_T (TARGET_A,TARGET_B) SELECT * FROM EXTERNAL", sql);
        Assert.Contains("(A INTEGER,B NVARCHAR(20))", sql);
        Assert.Contains("BOOLSTYLE '1_0'", sql);
    }
}
