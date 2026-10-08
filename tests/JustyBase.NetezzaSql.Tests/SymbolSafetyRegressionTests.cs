using JustyBase.NetezzaSqlParser.Authoring;
using Xunit;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class SymbolSafetyRegressionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("\"unfinished")]
    [InlineData("\"bad\"quote\"")]
    [InlineData("next\nname")]
    public void Rename_RejectsMalformedReplacement(string newName)
        => Assert.Null(NzRenameService.GetRenameEdits("SELECT a.ID FROM T a", 7, newName));

    [Fact]
    public void Rename_RejectsCaptureAndStaleOccurrenceSet()
    {
        const string sql = "SELECT a.ID FROM T a JOIN U b ON a.ID=b.ID";
        Assert.Null(NzRenameService.GetRenameEdits(sql, 7, "b"));
        var symbol = NzRenameService.GetRenameInfo(sql, 7)!;
        var changed = sql.Replace("a.ID", "b.ID");
        Assert.Equal(changed, NzRenameService.ApplyRename(changed, symbol, "next_alias"));
    }

    [Theory]
    [InlineData("SELECT a. FROM T a")]
    [InlineData("WITH q AS (SELECT * FROM T a) SELECT q.")]
    [InlineData("SELECT * FROM (SELECT ID FROM T) d WHERE d.")]
    public void IncompleteSql_OnlyReturnsProvenIdentifiers(string sql)
    {
        var edits = NzRenameService.GetRenameEdits(sql, sql.LastIndexOf('.'), "next_alias");
        if (edits is null) return;
        Assert.All(edits, edit => Assert.Contains(sql[edit.StartOffset..edit.EndOffset], new[] { "a", "q", "d" }));
    }
}
