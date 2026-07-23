namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzRenameFormattingTests
{
    private static string FormatSqlRenameReplacement(string oldName, string newName)
    {
        if (newName.StartsWith('"') && newName.EndsWith('"'))
            return newName;
        if (oldName.StartsWith('"'))
            return '"' + newName.Replace("\"", "\"\"") + '"';
        return newName;
    }

    [Fact]
    public void FormatSqlRenameReplacement_KeepsPlainIdentifiersUnquoted()
    {
        var result = FormatSqlRenameReplacement("ALIAS1", "NEXT_ALIAS");
        Assert.Equal("NEXT_ALIAS", result);
    }

    [Fact]
    public void FormatSqlRenameReplacement_PreservesQuotedAndEscapesEmbeddedQuotes()
    {
        var result = FormatSqlRenameReplacement("\"Sales Alias\"", "Quarter \"A\"");
        Assert.Equal("\"Quarter \"\"A\"\"\"", result);
    }

    [Fact]
    public void FormatSqlRenameReplacement_AcceptsQuotedNewNameAndNormalizes()
    {
        var result = FormatSqlRenameReplacement("\"Sales Alias\"", "\"Quarter Alias\"");
        Assert.Equal("\"Quarter Alias\"", result);
    }
}
