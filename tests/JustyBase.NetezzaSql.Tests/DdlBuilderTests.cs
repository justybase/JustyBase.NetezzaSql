using JustyBase.NetezzaDdl;
using JustyBase.NetezzaDdl.Models;

namespace JustyBase.NetezzaSql.Tests;

public sealed class DdlBuilderTests
{
    private readonly NetezzaDdlTextBuilder _builder = new();

    [Fact]
    public void NameHelper_ParsesReferenceConnectionStringShape()
    {
        var settings = NetezzaNameHelper.ParseConnectionString(
            "DRIVER={NetezzaSQL};SERVER=myhost;PORT=5480;DATABASE=mydb;UID=admin;PWD=secret;");

        Assert.Equal("myhost", settings.Host);
        Assert.Equal(5480, settings.Port);
        Assert.Equal("mydb", settings.Database);
        Assert.Equal("admin", settings.User);
        Assert.Equal("secret", settings.Password);
    }

    [Theory]
    [InlineData("CHARACTER VARYING", "CHARACTER VARYING(ANY)")]
    [InlineData("NATIONAL CHARACTER VARYING", "NATIONAL CHARACTER VARYING(ANY)")]
    [InlineData("NATIONAL CHARACTER", "NATIONAL CHARACTER(ANY)")]
    [InlineData("CHARACTER", "CHARACTER(ANY)")]
    [InlineData("CHARACTER(10)", "CHARACTER(10)")]
    [InlineData("INTEGER", "INTEGER")]
    [InlineData("  character varying  ", "CHARACTER VARYING(ANY)")]
    public void NameHelper_FixesProcedureReturnTypeLikeReference(string input, string expected)
        => Assert.Equal(expected, NetezzaNameHelper.FixProcedureReturnType(input));

    [Theory]
    [InlineData("TIMESTYLE", "24HOUR", "TIMESTYLE '24HOUR'")]
    [InlineData("REMOTESOURCE", "odbc", "REMOTESOURCE 'odbc'")]
    [InlineData("ESCAPECHAR", "\\", "ESCAPECHAR '\\'")]
    [InlineData("DECIMALDELIM", ".", "DECIMALDELIM '.'")]
    [InlineData("LOGDIR", "/tmp/logs", "LOGDIR '/tmp/logs'")]
    [InlineData("QUOTEDVALUE", "double", "QUOTEDVALUE 'double'")]
    [InlineData("NULLVALUE", "NULL", "NULLVALUE 'NULL'")]
    [InlineData("DATESTYLE", "YMD", "DATESTYLE 'YMD'")]
    [InlineData("DATEDELIM", "-", "DATEDELIM '-'")]
    [InlineData("TIMEDELIM", ":", "TIMEDELIM ':'")]
    [InlineData("BOOLSTYLE", "T_F", "BOOLSTYLE 'T_F'")]
    [InlineData("FORMAT", "text", "FORMAT 'text'")]
    [InlineData("RECORDDELIM", "\\n", "RECORDDELIM '\\n'")]
    [InlineData("DATETIMEDELIM", " ", "DATETIMEDELIM ' '")]
    [InlineData("REJECTFILE", "/tmp/rej.txt", "REJECTFILE '/tmp/rej.txt'")]
    public void ExternalTable_EmitsEachStringOption(string option, string value, string expected)
    {
        var options = option switch
        {
            "TIMESTYLE" => new NetezzaExternalTableOptions { Timestyle = value },
            "REMOTESOURCE" => new NetezzaExternalTableOptions { RemoteSource = value },
            "ESCAPECHAR" => new NetezzaExternalTableOptions { EscapeChar = value },
            "DECIMALDELIM" => new NetezzaExternalTableOptions { DecimalDelim = value },
            "LOGDIR" => new NetezzaExternalTableOptions { LogDir = value },
            "QUOTEDVALUE" => new NetezzaExternalTableOptions { QuotedValue = value },
            "NULLVALUE" => new NetezzaExternalTableOptions { NullValue = value },
            "DATESTYLE" => new NetezzaExternalTableOptions { DateStyle = value },
            "DATEDELIM" => new NetezzaExternalTableOptions { DateDelim = value },
            "TIMEDELIM" => new NetezzaExternalTableOptions { TimeDelim = value },
            "BOOLSTYLE" => new NetezzaExternalTableOptions { BoolStyle = value },
            "FORMAT" => new NetezzaExternalTableOptions { Format = value },
            "RECORDDELIM" => new NetezzaExternalTableOptions { RecordDelim = value },
            "DATETIMEDELIM" => new NetezzaExternalTableOptions { DateTimeDelim = value },
            "REJECTFILE" => new NetezzaExternalTableOptions { RejectFile = value },
            _ => new NetezzaExternalTableOptions()
        };
        var ddl = _builder.BuildCreateExternal(new("DB", "S", "T", [], options));
        Assert.Contains(expected, ddl);
    }

    [Theory]
    [InlineData("MAXERRORS", "10", "MAXERRORS 10")]
    [InlineData("SKIPROWS", "2", "SKIPROWS 2")]
    [InlineData("Y2BASE", "1970", "Y2BASE 1970")]
    [InlineData("SOCKETBUFSIZE", "8192", "SOCKETBUFSIZE 8192")]
    [InlineData("MAXROWS", "1000", "MAXROWS 1000")]
    [InlineData("RECORDLENGTH", "1024", "RECORDLENGTH 1024")]
    public void ExternalTable_EmitsEachNumericOption(string option, string value, string expected)
    {
        var options = option switch
        {
            "MAXERRORS" => new NetezzaExternalTableOptions { MaxErrors = long.Parse(value) },
            "SKIPROWS" => new NetezzaExternalTableOptions { SkipRows = long.Parse(value) },
            "Y2BASE" => new NetezzaExternalTableOptions { Y2Base = short.Parse(value) },
            "SOCKETBUFSIZE" => new NetezzaExternalTableOptions { SocketBufSize = int.Parse(value) },
            "MAXROWS" => new NetezzaExternalTableOptions { MaxRows = long.Parse(value) },
            "RECORDLENGTH" => new NetezzaExternalTableOptions { RecordLength = value },
            _ => new NetezzaExternalTableOptions()
        };
        var ddl = _builder.BuildCreateExternal(new("DB", "S", "T", [], options));
        Assert.Contains(expected, ddl);
    }

    [Fact]
    public void ExternalTable_EmitsAllBooleanOptionsUsingReferenceLiterals()
    {
        var ddl = _builder.BuildCreateExternal(new("DB", "S", "T", [], new NetezzaExternalTableOptions
        {
            CrInString = true, TruncString = true, IncludeHeader = true, Compress = "true",
            CtrlChars = false, IgnoreZero = false, FillRecord = false,
            TimeExtraZeros = true, LfInString = true, RequireQuotes = true
        }));

        Assert.Contains("CRINSTRING true", ddl);
        Assert.Contains("TRUNCSTRING true", ddl);
        Assert.Contains("INCLUDEHEADER true", ddl);
        Assert.Contains("CTRLCHARS false", ddl);
        Assert.Contains("IGNOREZERO false", ddl);
        Assert.Contains("FILLRECORD false", ddl);
        Assert.Contains("TIMEEXTRAZEROS true", ddl);
        Assert.Contains("LFINSTRING true", ddl);
        Assert.Contains("REQUIREQUOTES true", ddl);
    }

    [Fact]
    public void BuildCreateTable_EmitsConstraintsDistributionAndEscapedComments()
    {
        var ddl = _builder.BuildCreateTable(new NetezzaTableDdlInput(
            "DB", "SCHEMA", "ORDERS",
            [new NetezzaColumnDdl("Order Id", "INTEGER", "customer's order", "0", true)],
            ["Order Id"], TableComment: "owner's table"));

        Assert.Contains("\"Order Id\" INTEGER NOT NULL DEFAULT 0", ddl);
        Assert.Contains("DISTRIBUTE ON (\"Order Id\")", ddl);
        Assert.Contains("COMMENT ON TABLE DB.SCHEMA.ORDERS IS 'owner''s table';", ddl);
        Assert.Contains("COMMENT ON COLUMN DB.SCHEMA.ORDERS.\"Order Id\" IS 'customer''s order';", ddl);
    }

    [Fact]
    public void BuildCreateTable_StripsEmbeddedNotNullFromTypeName()
    {
        var ddl = _builder.BuildCreateTable(new NetezzaTableDdlInput(
            "DB", "S", "T",
            [
                new NetezzaColumnDdl("ID", "INTEGER NOT NULL", NotNull: true),
                new NetezzaColumnDdl("NAME", "VARCHAR(100) NOT NULL", NotNull: false)
            ]));

        Assert.Contains("ID INTEGER NOT NULL", ddl);
        Assert.Contains("NAME VARCHAR(100)", ddl);
        Assert.DoesNotContain("INTEGER NOT NULL NOT NULL", ddl);
        Assert.DoesNotContain("VARCHAR(100) NOT NULL", ddl);
    }

    [Fact]
    public void BuildCreateTable_DimDateGoldenMatchesReferenceNotNullPlacement()
    {
        var columns = new NetezzaColumnDdl[]
        {
            new("DATEKEY", "INTEGER", NotNull: true),
            new("FULLDATEALTERNATEKEY", "TIMESTAMP", Description: "Full date alternate key"),
            new("DAYNUMBEROFWEEK", "INTEGER", Description: "test comment 2"),
            new("ENGLISHDAYNAMEOFWEEK", "NATIONAL CHARACTER VARYING(14)"),
            new("SPANISHDAYNAMEOFWEEK", "NATIONAL CHARACTER VARYING(14)"),
            new("FRENCHDAYNAMEOFWEEK", "NATIONAL CHARACTER VARYING(13)", Description: "comment 3"),
            new("DAYNUMBEROFMONTH", "INTEGER"),
            new("DAYNUMBEROFYEAR", "INTEGER"),
            new("WEEKNUMBEROFYEAR", "INTEGER"),
            new("ENGLISHMONTHNAME", "NATIONAL CHARACTER VARYING(14)"),
            new("SPANISHMONTHNAME", "NATIONAL CHARACTER VARYING(15)"),
            new("FRENCHMONTHNAME", "NATIONAL CHARACTER VARYING(14)"),
            new("MONTHNUMBEROFYEAR", "INTEGER"),
            new("CALENDARQUARTER", "INTEGER"),
            new("CALENDARYEAR", "INTEGER"),
            new("CALENDARSEMESTER", "INTEGER"),
            new("FISCALQUARTER", "INTEGER"),
            new("FISCALYEAR", "INTEGER"),
            new("FISCALSEMESTER", "INTEGER"),
        };

        var ddl = _builder.BuildCreateTable(new NetezzaTableDdlInput(
            "JUST_DATA", "ADMIN", "DIMDATE",
            columns,
            Keys: [new NetezzaKeyDdl('p', "PK_DIMDATE", ["DATEKEY"])],
            TableComment: "test comment"));

        Assert.Contains("DATEKEY INTEGER NOT NULL", ddl);
        Assert.Contains("FULLDATEALTERNATEKEY TIMESTAMP,", ddl);
        Assert.Contains("DAYNUMBEROFWEEK INTEGER,", ddl);
        Assert.Contains("ENGLISHDAYNAMEOFWEEK NATIONAL CHARACTER VARYING(14),", ddl);
        Assert.DoesNotContain("FULLDATEALTERNATEKEY TIMESTAMP NOT NULL", ddl);
        Assert.DoesNotContain("DAYNUMBEROFWEEK INTEGER NOT NULL", ddl);
        Assert.Contains("DISTRIBUTE ON RANDOM", ddl);
        Assert.Contains("ADD CONSTRAINT PK_DIMDATE PRIMARY KEY (DATEKEY)", ddl);
        Assert.Contains("COMMENT ON TABLE JUST_DATA.ADMIN.DIMDATE IS 'test comment';", ddl);
        Assert.Contains("COMMENT ON COLUMN JUST_DATA.ADMIN.DIMDATE.FULLDATEALTERNATEKEY IS 'Full date alternate key';", ddl);
        Assert.Contains("COMMENT ON COLUMN JUST_DATA.ADMIN.DIMDATE.DAYNUMBEROFWEEK IS 'test comment 2';", ddl);
        Assert.Contains("COMMENT ON COLUMN JUST_DATA.ADMIN.DIMDATE.FRENCHDAYNAMEOFWEEK IS 'comment 3';", ddl);
    }

    [Fact]
    public void BuildRecreateTable_UsesSameNotNullPlacementAsCreate()
    {
        var ddl = _builder.BuildRecreateTable(new NetezzaTableDdlInput(
            "JUST_DATA", "ADMIN", "DIMDATE",
            [
                new NetezzaColumnDdl("DATEKEY", "INTEGER", NotNull: true),
                new NetezzaColumnDdl("FULLDATEALTERNATEKEY", "TIMESTAMP")
            ],
            Keys: [new NetezzaKeyDdl('p', "PK_DIMDATE", ["DATEKEY"])]));

        Assert.Contains("DATEKEY INTEGER NOT NULL", ddl);
        Assert.Contains("FULLDATEALTERNATEKEY TIMESTAMP", ddl);
        Assert.DoesNotContain("FULLDATEALTERNATEKEY TIMESTAMP NOT NULL", ddl);
        Assert.Contains("INSERT INTO JUST_DATA.ADMIN.", ddl);
        Assert.Contains("SET PRIVILEGES TO", ddl);
    }

    [Fact]
    public void BuildCreateTable_EmptyColumnsReturnsReferenceComment()
    {
        var ddl = _builder.BuildCreateTable(new NetezzaTableDdlInput("MYDB", "ADMIN", "MYTABLE", []));

        Assert.Contains("-- Table MYDB.ADMIN.MYTABLE has no columns", ddl);
        Assert.DoesNotContain("CREATE TABLE", ddl);
    }

    [Fact]
    public void BuildCreateTable_DistributionOverrideWinsOverInput()
    {
        var result = new NetezzaDdlTextBuilder().BuildCreateTable(
            new NetezzaTableDdlInput("DB", "S", "T", [new NetezzaColumnDdl("ID", "INTEGER")], ["OLD"]), ["NEW"]);

        Assert.Contains("DISTRIBUTE ON (NEW)", result);
        Assert.DoesNotContain("DISTRIBUTE ON (OLD)", result);
    }

    [Fact]
    public void BuildCreateSequence_EmitsMinMaxAndCycleClauses()
    {
        var ddl = _builder.BuildCreateSequence(new NetezzaSequenceDdlInput(
            "JUST_DATA", "ADMIN", "SEQ1", "BIGINT", "10", "2", "1", "100", Cycle: true));

        Assert.Contains("CREATE SEQUENCE JUST_DATA.ADMIN.SEQ1", ddl);
        Assert.Contains("AS BIGINT", ddl);
        Assert.Contains("START WITH 10", ddl);
        Assert.Contains("INCREMENT BY 2", ddl);
        Assert.Contains("MINVALUE 1", ddl);
        Assert.Contains("MAXVALUE 100", ddl);
        Assert.Contains("CYCLE;", ddl);
    }

    [Fact]
    public void BuildCreateSequence_UsesNoMaxValueForIntegerDefault()
    {
        var ddl = _builder.BuildCreateSequence(new NetezzaSequenceDdlInput(
            "DB", "S", "SEQ", "INTEGER", "1", "1", MaxValue: "2147483647"));

        Assert.Contains("NO MAXVALUE", ddl);
        Assert.Contains("NO CYCLE;", ddl);
    }

    [Fact]
    public void MaintenanceSql_BuildsGroomAndStats()
    {
        Assert.Equal(
            "GROOM TABLE T1 VERSIONS RECLAIM BACKUPSET '42';",
            NetezzaMaintenanceSql.BuildGroom("T1", "VERSIONS", "42"));
        Assert.Equal(
            "GENERATE EXPRESS STATISTICS ON DB.SCH.TBL;",
            NetezzaMaintenanceSql.BuildGenerateStats("DB.SCH.TBL", express: true));
        Assert.Equal(
            "GENERATE STATISTICS ON SCH.TBL (COL1, COL2);",
            NetezzaMaintenanceSql.BuildGenerateStats("SCH.TBL", express: false, "COL1, COL2"));
    }

    [Fact]
    public void BuildCreateExternal_EmitsNotNullAndEscapesOptions()
    {
        var ddl = _builder.BuildCreateExternal(new NetezzaExternalDdlInput(
            "DB", "SCHEMA", "EXT",
            [new NetezzaColumnDdl("ID", "INTEGER", NotNull: true)],
            new NetezzaExternalTableOptions { DataObject = "file'part", Delimiter = "," }));

        Assert.Contains("ID INTEGER NOT NULL", ddl);
        Assert.Contains("DATAOBJECT('file''part')", ddl);
        Assert.EndsWith($"{Environment.NewLine}", ddl);
    }

    [Fact]
    public void BuildCreateProcedure_UsesNetezzaProcedureShape()
    {
        var ddl = _builder.BuildCreateProcedure(new NetezzaProcedureDdlInput(
            "DB", "SCHEMA", "do work", "INTEGER", "RETURN 1;", "p_id INTEGER", Description: "does it's work"));

        Assert.Contains("CREATE OR REPLACE PROCEDURE DB.SCHEMA.\"do work\"(p_id INTEGER)", ddl);
        Assert.Contains("EXECUTE AS CALLER", ddl);
        Assert.Contains("BEGIN_PROC", ddl);
        Assert.Contains("END_PROC;", ddl);
        Assert.Contains("COMMENT ON PROCEDURE \"do work\" IS 'does it''s work';", ddl);
    }

    [Fact]
    public void BuildCreateSynonym_QuotesEveryTargetPart()
    {
        var ddl = _builder.BuildCreateSynonym(new NetezzaSynonymDdlInput(
            "DB", "PUBLIC", "my synonym", "Other DB.Mixed Name.Table", Description: "alias"));

        Assert.Contains("CREATE SYNONYM DB.PUBLIC.\"my synonym\" FOR \"Other DB\".\"Mixed Name\".\"Table\";", ddl);
        Assert.Contains("COMMENT ON SYNONYM \"my synonym\" IS 'alias';", ddl);
    }
}
