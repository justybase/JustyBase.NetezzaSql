using JustyBase.NetezzaSqlParser.Authoring;

namespace JustyBase.NetezzaSql.Tests;

public sealed class SqliteAuthoringCatalogTests
{
    private static readonly SqliteSqlCatalog Catalog = SqliteSqlCatalog.Instance;

    [Fact]
    public void Types_FollowSqliteAffinities()
    {
        Assert.True(Catalog.TryGetDataType("INTEGER", out _));
        Assert.True(Catalog.TryGetDataType("BLOB", out _));
        Assert.True(Catalog.TryGetDataType("DOUBLE PRECISION", out _));
        Assert.True(Catalog.TryGetDataType("VARCHAR", out _));
        Assert.False(Catalog.TryGetDataType("VARCHAR2", out _));
        Assert.False(Catalog.TryGetDataType("SERIAL", out _));
    }

    [Theory]
    [InlineData("JSON_EXTRACT")]
    [InlineData("GROUP_CONCAT")]
    [InlineData("STRFTIME")]
    [InlineData("IFNULL")]
    [InlineData("UNIXEPOCH")]
    [InlineData("RANDOMBLOB")]
    [InlineData("TOTAL")]
    public void Functions_IncludeSqliteBuiltins(string name)
    {
        Assert.True(Catalog.TryGetFunction(name, out var function));
        Assert.Equal(name, function.Name, ignoreCase: true);
    }

    [Fact]
    public void Functions_ExcludeNonSqliteLegacyNames()
    {
        Assert.False(Catalog.TryGetFunction("TO_CHAR", out _));
        Assert.False(Catalog.TryGetFunction("SYSDATE", out _));
    }

    [Fact]
    public void Keywords_IncludeSqliteSpecificWords()
    {
        Assert.Contains("AUTOINCREMENT", Catalog.Keywords);
        Assert.Contains("WITHOUT", Catalog.Keywords);
        Assert.Contains("STRICT", Catalog.Keywords);
        Assert.Contains("ROWID", Catalog.Keywords);
    }

    [Fact]
    public void CompletionKeywords_IncludeMultiWordClauses()
    {
        Assert.Contains("ON CONFLICT", Catalog.CompletionKeywords);
        Assert.Contains("DO NOTHING", Catalog.CompletionKeywords);
        Assert.Contains("DO UPDATE", Catalog.CompletionKeywords);
    }

    [Fact]
    public void FormatterProfile_IncludesLimitReturningWindow()
    {
        var clauses = Catalog.FormatterProfile.ClauseKeywords;
        Assert.Contains("LIMIT", clauses);
        Assert.Contains("RETURNING", clauses);
        Assert.Contains("WINDOW", clauses);
    }

    [Fact]
    public void DataTypeNames_FlattenAliases()
    {
        Assert.Contains("INT", Catalog.DataTypeNames);
        Assert.Contains("DOUBLE PRECISION", Catalog.DataTypeNames);
    }
}
