using JustyBase.Core.Database;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Sanity tests for the shared SQL word-list contract. The enum member set is
/// the union of the host icon vocabularies — renames here must be mirrored by
/// the host adapters (Avalonia DbWordListProvider / Legacy LegacyDbWordListProvider).
/// </summary>
public sealed class SqlWordListContractTests
{
    [Fact]
    public void Kind_enum_covers_both_host_vocabularies()
    {
        string[] members = Enum.GetNames<SqlWordListKind>();
        Assert.Contains(nameof(SqlWordListKind.Database), members);
        Assert.Contains(nameof(SqlWordListKind.Schema), members);
        Assert.Contains(nameof(SqlWordListKind.Table), members);
        Assert.Contains(nameof(SqlWordListKind.View), members);
        Assert.Contains(nameof(SqlWordListKind.Procedure), members);
        Assert.Contains(nameof(SqlWordListKind.Synonym), members);
        Assert.Contains(nameof(SqlWordListKind.ExternalTable), members);
        Assert.Contains(nameof(SqlWordListKind.Function), members);
        Assert.Contains(nameof(SqlWordListKind.Column), members);
        Assert.Contains(nameof(SqlWordListKind.Alias), members);
        Assert.Contains(nameof(SqlWordListKind.With), members);
        Assert.Contains(nameof(SqlWordListKind.TempTable), members);
        Assert.Contains(nameof(SqlWordListKind.Subquery), members);
        Assert.Contains(nameof(SqlWordListKind.Keyword), members);
        Assert.Contains(nameof(SqlWordListKind.DataType), members);
        Assert.Contains(nameof(SqlWordListKind.Variable), members);
        Assert.Contains(nameof(SqlWordListKind.Snippet), members);
        Assert.Contains(nameof(SqlWordListKind.Reference), members);
    }

    [Fact]
    public void Item_has_neutral_defaults()
    {
        var item = new SqlWordListItem("EMPLOYEES", SqlWordListKind.Table);
        Assert.Equal("EMPLOYEES", item.Label);
        Assert.Equal(SqlWordListKind.Table, item.Kind);
        Assert.Null(item.Detail);
        Assert.Null(item.Description);

        var detailed = item with { Detail = "Table", Description = "emps" };
        Assert.Equal("Table", detailed.Detail);
        Assert.Equal("emps", detailed.Description);
    }

    [Fact]
    public void Empty_request_has_empty_hints_and_optional_scoping()
    {
        SqlWordListRequest request = SqlWordListRequest.Empty("EMP", "conn", "JUST_DATA");
        Assert.Equal("EMP", request.Fragment);
        Assert.Equal("conn", request.ConnectionName);
        Assert.Equal("JUST_DATA", request.DatabaseName);
        Assert.Empty(request.AliasDbTable);
        Assert.Empty(request.SubqueryHints);
        Assert.Empty(request.WithHints);
        Assert.Empty(request.TempTableHints);
    }
}
