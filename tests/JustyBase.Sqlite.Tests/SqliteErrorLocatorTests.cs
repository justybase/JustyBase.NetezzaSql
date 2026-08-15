using JustyBase.Sqlite.Diagnostics;

namespace JustyBase.Sqlite.Tests;

public sealed class SqliteErrorLocatorTests
{
    [Theory]
    [InlineData("near \"users\": syntax error", "users")]
    [InlineData("no such table: users", "users")]
    [InlineData("no such column: name", "name")]
    [InlineData("no such function: foo", "foo")]
    [InlineData("table users already exists", "users")]
    [InlineData("duplicate column name: id", "id")]
    [InlineData("UNIQUE constraint failed: users.id", "users.id")]
    public void TryLocate_FindsToken(string message, string expected)
    {
        Assert.True(SqliteErrorLocator.TryLocate(message, out var location));
        Assert.Equal(expected, location.Word);
    }

    [Fact]
    public void TryLocate_UnknownMessage_ReturnsFalse()
    {
        Assert.False(SqliteErrorLocator.TryLocate("database is locked", out _));
        Assert.False(SqliteErrorLocator.TryLocate("", out _));
        Assert.False(SqliteErrorLocator.TryLocate(null!, out _));
    }

    [Fact]
    public void LocateInSql_ResolvesOffsetAndLength()
    {
        const string sql = "SELECT * FROM users WHERE id = 1";
        var (offset, length) = SqliteErrorLocator.LocateInSql("no such table: users", sql);
        Assert.Equal(sql.IndexOf("users", StringComparison.Ordinal), offset);
        Assert.Equal("users".Length, length);
    }

    [Fact]
    public void LocateInSql_MissingWord_ReturnsNegative()
    {
        var (offset, length) = SqliteErrorLocator.LocateInSql("no such table: ghost", "SELECT 1");
        Assert.Equal((-1, -1), (offset, length));
    }
}
