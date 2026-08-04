using JustyBase.Core.Database;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class EngineSqlWordListRequestBuilderTests
{
    private readonly EngineSqlWordListRequestBuilder _builder = new();

    [Fact]
    public void Build_attaches_schema_qualified_alias_hints()
    {
        const string sql = "SELECT * FROM ADMIN.EMP X WHERE X.";
        var request = _builder.Build(sql, sql.Length, "conn", "JUST_DATA");

        Assert.Equal("X.", request.Fragment);
        Assert.Equal("conn", request.ConnectionName);
        Assert.True(request.AliasDbTable.TryGetValue("ADMIN.EMP", out var aliases));
        Assert.Contains(aliases, a => a.Equals("X", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_attaches_double_dot_database_alias_hints()
    {
        const string sql = "SELECT * FROM JUST_DATA..DIMACCOUNT X WHERE X.";
        var request = _builder.Build(sql, sql.Length, "conn", "JUST_DATA");

        Assert.True(request.AliasDbTable.TryGetValue("JUST_DATA..DIMACCOUNT", out var aliases));
        Assert.Contains(aliases, a => a.Equals("X", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_attaches_with_cte_column_hints()
    {
        const string sql = "WITH cte AS (SELECT id, name FROM employees) SELECT * FROM cte WHERE cte.";
        var request = _builder.Build(sql, sql.Length, "conn", "JUST_DATA");

        Assert.True(request.WithHints.TryGetValue("cte", out var columns));
        Assert.Contains(columns, c => c.Equals("id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(columns, c => c.Equals("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_works_after_semicolon_boundary()
    {
        const string sql = "SELECT 1; SELECT * FROM ADMIN.EMP X WHERE X.";
        var request = _builder.Build(sql, sql.Length, "conn", "JUST_DATA");

        Assert.True(request.AliasDbTable.TryGetValue("ADMIN.EMP", out var aliases));
        Assert.Contains(aliases, a => a.Equals("X", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_without_hints_returns_empty_dictionaries()
    {
        const string sql = "SELECT * FROM employees";
        var request = _builder.Build(sql, sql.Length, "conn", "JUST_DATA");

        Assert.Equal("employees", request.Fragment);
        Assert.Empty(request.AliasDbTable);
        Assert.Empty(request.WithHints);
        Assert.Empty(request.TempTableHints);
    }

    [Fact]
    public void Build_accepts_dialect_parameter()
    {
        var db2Builder = new EngineSqlWordListRequestBuilder(SqlDialect.Db2);
        const string sql = "SELECT * FROM JBL_LIVE.JBL_ORDERS O WHERE O.";
        var request = db2Builder.Build(sql, sql.Length, "db2-cloud", "TESTDB");

        Assert.Equal("O.", request.Fragment);
        Assert.Equal("db2-cloud", request.ConnectionName);
        Assert.True(request.AliasDbTable.TryGetValue("JBL_LIVE.JBL_ORDERS", out var aliases));
        Assert.Contains(aliases, a => a.Equals("O", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_with_empty_text_returns_empty_request()
    {
        var request = _builder.Build(string.Empty, 0, "conn", "JUST_DATA");
        Assert.Equal(string.Empty, request.Fragment);
        Assert.Empty(request.AliasDbTable);
    }
}
