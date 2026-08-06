using JustyBase.ImportExport.Import;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ImportSelectSqlBuilderTests
{
    [Fact]
    public void BuildAliasedColumnSelect_ListsColumnsExplicitlyWithAliasAndLimit()
    {
        string[] headers = ["ID INTEGER", "PRICE NUMERIC(16,2)", "\"Odd Name\" NVARCHAR(20)"];

        string sql = ImportSelectSqlBuilder.BuildAliasedColumnSelect("IMP_TAB", headers);

        Assert.Contains("SELECT", sql);
        Assert.Contains("T.ID", sql);
        Assert.Contains(", T.PRICE", sql);
        Assert.Contains("\"Odd Name\"", sql);
        Assert.Contains("IMP_TAB T", sql);
        Assert.Contains("FROM", sql);
        Assert.Contains("LIMIT 100", sql);
        Assert.DoesNotContain("SELECT *", sql);
    }

    [Fact]
    public void BuildAliasedColumnSelect_NoColumns_FallsBackToStar()
    {
        string sql = ImportSelectSqlBuilder.BuildAliasedColumnSelect("IMP_TAB", []);

        Assert.Equal("SELECT * FROM IMP_TAB T LIMIT 100", sql);
    }

    [Theory]
    [InlineData("A INTEGER", "A")]
    [InlineData("A", "A")]
    [InlineData("\"Weird Col\" VARCHAR(10)", "Weird Col")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void ExtractColumnNameFromHeaderDefinition_StripsTypeAndHonorsQuotes(string definition, string expected)
    {
        Assert.Equal(expected, ImportSelectSqlBuilder.ExtractColumnNameFromHeaderDefinition(definition));
    }
}
