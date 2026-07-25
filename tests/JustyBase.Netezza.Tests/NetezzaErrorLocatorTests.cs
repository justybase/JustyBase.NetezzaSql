using JustyBase.Netezza;

namespace JustyBase.Netezza.Tests;

public sealed class NetezzaErrorLocatorTests
{
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
    public void LocateInSql_AmbiguousColumn_SkipsQualifiedReference()
    {
        const string sql = "select a.id, id from a";
        var (offset, length) = NetezzaErrorLocator.LocateInSql(
            "ERROR: Column reference \"ID\" is ambiguous",
            sql);
        Assert.Equal(sql.IndexOf(", id", StringComparison.Ordinal) + 2, offset);
        Assert.Equal(2, length);
    }
}
