using JustyBase.NetezzaDdl;

namespace JustyBase.NetezzaSql.Tests;

public sealed class DdlHelperConformanceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("MYTABLE")]
    [InlineData("MY_TABLE_123")]
    [InlineData("mytable")]
    [InlineData("MyTable")]
    [InlineData("MY TABLE")]
    [InlineData("MY-TABLE")]
    [InlineData("MY.TABLE")]
    [InlineData("MY\"TABLE")]
    [InlineData("123TABLE")]
    public void QuoteNameIfNeeded_MatchesReference(string input)
    {
        var expected = input switch
        {
            "" or "MYTABLE" or "MY_TABLE_123" => input,
            _ => "\"" + input.Replace("\"", "\"\"") + "\""
        };
        Assert.Equal(expected, NetezzaNameHelper.QuoteNameIfNeeded(input));
    }

    [Theory]
    [InlineData("", null, null, null, null)]
    [InlineData("SERVER=host;DATABASE=db", "host", null, "db", null)]
    [InlineData("server=host;port=1234;database=db;uid=user;pwd=pass", "host", "1234", "db", "user")]
    [InlineData("SERVER=host;PWD=pass=word=123", "host", null, null, null)]
    public void ParseConnectionString_HandlesReferenceInputs(string input, string? host, string? port, string? database, string? user)
    {
        var result = NetezzaNameHelper.ParseConnectionString(input);
        Assert.Equal(host, result.Host);
        Assert.Equal(port is null ? null : int.Parse(port), result.Port);
        Assert.Equal(database, result.Database);
        Assert.Equal(user, result.User);
    }

    [Fact]
    public void ParseConnectionString_PreservesEqualsInPassword()
        => Assert.Equal("pass=word=123", NetezzaNameHelper.ParseConnectionString("PWD=pass=word=123").Password);

    [Theory]
    [InlineData("", "")]
    [InlineData("CHARACTER VARYING", "CHARACTER VARYING(ANY)")]
    [InlineData("NATIONAL CHARACTER VARYING", "NATIONAL CHARACTER VARYING(ANY)")]
    [InlineData("NATIONAL CHARACTER", "NATIONAL CHARACTER(ANY)")]
    [InlineData("CHARACTER", "CHARACTER(ANY)")]
    [InlineData("CHARACTER VARYING(255)", "CHARACTER VARYING(255)")]
    [InlineData("CHARACTER(10)", "CHARACTER(10)")]
    [InlineData("INTEGER", "INTEGER")]
    [InlineData("NUMERIC(10,2)", "NUMERIC(10,2)")]
    [InlineData("BOOLEAN", "BOOLEAN")]
    public void FixProcedureReturnType_MatchesReference(string input, string expected)
        => Assert.Equal(expected, NetezzaNameHelper.FixProcedureReturnType(input));
}
