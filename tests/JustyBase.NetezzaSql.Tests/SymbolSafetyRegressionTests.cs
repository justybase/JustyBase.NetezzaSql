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

    [Theory]
    [InlineData("SELECT a.ID FROM T a JOIN ORDERS ON ORDERS.ID = a.ID", "SELECT a.", "ORDERS")]
    [InlineData("SELECT a.ID FROM T a JOIN SALES.ORDERS ON ORDERS.ID = a.ID", "SELECT a.", "orders")]
    [InlineData("WITH A AS (SELECT 1 AS ID) SELECT * FROM A JOIN ORDERS ON ORDERS.ID = A.ID", "WITH A", "ORDERS")]
    public void Rename_RejectsCaptureOfUnaliasedPhysicalRelation(string sql, string anchor, string newName)
    {
        var offset = sql.IndexOf(anchor, StringComparison.Ordinal) + anchor.Length - 1;
        Assert.Null(NzRenameService.GetRenameEdits(sql, offset, newName));
    }

    [Fact]
    public void Rename_StillAllowsANameNoVisibleRelationExposes()
    {
        const string sql = "SELECT a.ID FROM T a JOIN ORDERS ON ORDERS.ID = a.ID";
        var edits = NzRenameService.GetRenameEdits(sql, sql.IndexOf("a.", StringComparison.Ordinal), "ACCT");
        Assert.NotNull(edits);
        Assert.Equal(3, edits!.Count);
    }
}
