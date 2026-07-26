using JustyBase.Netezza;

namespace JustyBase.Netezza.Tests;

public sealed class NetezzaErrorLocatorTests
{
    [Fact]
    public void TryLocate_EmptyMessage_ReturnsFalse()
    {
        Assert.False(NetezzaErrorLocator.TryLocate("", fromOleDb: false, ReadOnlySpan<char>.Empty, out _));
    }

    [Fact]
    public void TryLocate_AttributeNotFound_ReturnsName()
    {
        Assert.True(NetezzaErrorLocator.TryLocate(
            "ERROR: Attribute 'MY_COL' not found",
            fromOleDb: false,
            "select MY_COL from t".AsSpan(),
            out var location));
        Assert.Equal("MY_COL", location.Word);
    }

    [Fact]
    public void LocateInSql_AlreadyExists_FindsToken()
    {
        const string sql = "CREATE TABLE ADMIN.T (ID INT)";
        var (offset, length) = NetezzaErrorLocator.LocateInSql(
            "ERROR: CREATE TABLE: object \"ADMIN.T\" already exists.",
            sql);
        Assert.True(offset >= 0);
        Assert.Equal("ADMIN.T".Length, length);
        Assert.Equal("ADMIN.T", sql.Substring(offset, length));
    }

    [Fact]
    public void LocateInSql_ExceptAtChar_UsesSliceOffset()
    {
        const string sql = "select FOO from t where FOO = 1";
        const string msg = "ERROR [42000] ERROR: syntax error ^ found \"FOO\" (at char 23) expecting";
        var (offset, length) = NetezzaErrorLocator.LocateInSql(msg, sql);
        Assert.Equal(sql.LastIndexOf("FOO", StringComparison.Ordinal), offset);
        Assert.Equal(3, length);
    }

    [Fact]
    public void TryLocate_ExceptAtChar_SetsCharIndexInSlice()
    {
        const string sql = "select FOO from t";
        const string msg = "ERROR [42000] ERROR: syntax error ^ found \"FOO\" (at char 8) expecting";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, sql.AsSpan(), out var location));
        Assert.Equal("FOO", location.Word);
        Assert.Equal(7, location.CharIndexInSlice);
    }

    [Fact]
    public void TryLocate_ExceptAtChar_SkipsLeadingWhitespaceInSlice()
    {
        const string sql = "  select FOO from t";
        const string msg = "ERROR [42000] ERROR: syntax error ^ found \"FOO\" (at char 8) expecting";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, sql.AsSpan(), out var location));
        Assert.Equal(9, location.CharIndexInSlice);
    }

    [Fact]
    public void TryLocate_FoundAtCharFallback_FromOleDb()
    {
        const string sql = "select BAR from t";
        const string msg = "some driver ^ found \"BAR\" (at char 8) tail";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: true, sql.AsSpan(), out var location));
        Assert.Equal("BAR", location.Word);
        Assert.Equal(7, location.CharIndexInSlice);
    }

    [Fact]
    public void LocateInSql_AmbiguousColumn_SkipsQualifiedReference()
    {
        const string sql = "select a.id, id from a";
        var (offset, length) = NetezzaErrorLocator.LocateInSql(
            "ERROR: Column reference \"ID\" is ambiguous",
            sql);
        Assert.Equal(sql.IndexOf(", id", StringComparison.Ordinal) + 2, offset);
        Assert.Equal(2, length);
    }

    [Theory]
    [InlineData("ERROR: 'SET search_path'", "search_path")]
    [InlineData("ERROR: DROP TABLE: object \"SCH.T\", incorrect type.", "SCH.T")]
    [InlineData("ERROR: transformColumnType: error reading type 'INT4'", "INT4")]
    [InlineData("ERROR: GROOM VERSIONS must be run on DB.SCH.TBL before any other GROOM operation", "DB.SCH.TBL")]
    [InlineData("ERROR: Attribute 'X' is repeated. Must have an appropriate alias.", "X")]
    [InlineData("ERROR: relation does not exist DB.SCH.MISSING", "MISSING")]
    [InlineData("ERROR: Function 'FOO(1)' does not exist", "FOO")]
    [InlineData("ERROR: Option 'BADOPT' is not recognized", "BADOPT")]
    [InlineData("ERROR: Table name \"T\" specified more than once", "T")]
    [InlineData("ERROR: DROP DATABASE: could not acquire lock for \"MYDB\"", "MYDB")]
    public void TryLocate_RegexPatterns_ReturnExpectedWord(string message, string expectedWord)
    {
        Assert.True(NetezzaErrorLocator.TryLocate(message, fromOleDb: false, message.AsSpan(), out var location));
        Assert.Equal(expectedWord, location.Word);
    }

    [Fact]
    public void TryLocate_GroupError_UsesFoundInSqlSlice()
    {
        const string sql = "select COL1 from t";
        const string msg = "ERROR: Attribute COL1 must be GROUPed or used in an aggregate function";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, sql.AsSpan(), out var location));
        Assert.Equal("COL1", location.Word);
    }

    [Fact]
    public void TryLocate_GroupErrorQualified_UsesSecondPattern()
    {
        const string msg = "ERROR: Attribute T.COL1 must be GROUPed or used in an aggregate function";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, ReadOnlySpan<char>.Empty, out var location));
        Assert.Equal("COL1", location.Word);
    }

    [Fact]
    public void TryLocate_PermissionDenied_ExtractsQuotedObject()
    {
        const string msg = "ERROR [HY000] ERROR:  Permission denied on \"DB.SCH.TBL\"";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, ReadOnlySpan<char>.Empty, out var location));
        Assert.Equal("DB.SCH.TBL", location.Word);
    }

    [Fact]
    public void TryLocate_ObjectAlreadyExists_Hy000()
    {
        const string msg = "ERROR [HY000] ERROR:  CREATE TABLE: object \"SCH.T\" already exists";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, ReadOnlySpan<char>.Empty, out var location));
        Assert.Equal("SCH.T", location.Word);
    }

    [Fact]
    public void TryLocate_SchemaDoesNotExist()
    {
        const string msg = "ERROR [HY000] ERROR:  Schema 'SCH' does not exist";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, ReadOnlySpan<char>.Empty, out var location));
        Assert.Equal("SCH", location.Word);
    }

    [Fact]
    public void TryLocate_RelationNotFound_42S02()
    {
        const string msg = "ERROR [42S02] ERROR: relation does not exist DB.SCH.TBL";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, ReadOnlySpan<char>.Empty, out var location));
        Assert.Equal("TBL", location.Word);
    }

    [Fact]
    public void TryLocate_AttributeMissing_42S22()
    {
        const string msg = "ERROR [42S22] ERROR:  Attribute 'COL_X' not found in table";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, ReadOnlySpan<char>.Empty, out var location));
        Assert.Equal("COL_X", location.Word);
    }

    [Fact]
    public void TryLocate_GroomVersionsMustRunOn()
    {
        const string msg = "ERROR [HY000] ERROR:  GROOM VERSIONS must be run on SCH.T before other";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, ReadOnlySpan<char>.Empty, out var location));
        Assert.Equal("SCH.T", location.Word);
    }

    [Fact]
    public void TryLocate_RepeatedAttribute_Hy000()
    {
        const string msg = "ERROR [HY000] ERROR:  Attribute COL is repeated in select list";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, ReadOnlySpan<char>.Empty, out var location));
        Assert.Equal("COL", location.Word);
    }

    [Fact]
    public void TryLocate_MustBeGrouped_Hy000()
    {
        const string msg = "ERROR [HY000] ERROR:  Attribute COL must be GROUPed in query";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, ReadOnlySpan<char>.Empty, out var location));
        Assert.Equal("COL", location.Word);
    }

    [Fact]
    public void TryLocate_InvalidOptionName_Hy000()
    {
        const string msg = "ERROR [HY000] ERROR:  BADOPT is not a valid option name for table";
        Assert.True(NetezzaErrorLocator.TryLocate(msg, fromOleDb: false, ReadOnlySpan<char>.Empty, out var location));
        Assert.Equal("BADOPT", location.Word);
    }

    [Fact]
    public void LocateInSql_WordNotInSql_ReturnsNegative()
    {
        var (offset, length) = NetezzaErrorLocator.LocateInSql(
            "ERROR: Attribute 'MISSING' not found",
            "select 1".AsSpan());
        Assert.Equal(-1, offset);
        Assert.Equal(-1, length);
    }

    [Fact]
    public void LocateInSql_CharIndexBeyondSlice_ReturnsNegative()
    {
        const string sql = "short";
        const string msg = "ERROR [42000] ERROR: syntax error ^ found \"X\" (at char 99) expecting";
        var (offset, length) = NetezzaErrorLocator.LocateInSql(msg, sql);
        Assert.Equal(-1, offset);
        Assert.Equal(-1, length);
    }
}
