using JustyBase.NetezzaSqlParser.Completion;
using Xunit;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class TemporalScopeRegressionTests
{
    [Theory]
    [InlineData("WITH hidden AS (SELECT 1 AS id) SELECT * FROM hidden; SELECT * FROM existing e WHERE |1=1")]
    [InlineData("SELECT * FROM (WITH hidden AS (SELECT 1 AS id) SELECT * FROM hidden) d WHERE |1=1")]
    public void Ctes_DoNotLeakPastTheirLexicalScope(string marked)
    {
        var scope = SqlScopeAtCursorResolver.Resolve(marked.Replace("|", ""), marked.IndexOf('|'));
        Assert.NotNull(scope);
        Assert.DoesNotContain(scope.VisibleCtes, name => name.Equals("hidden", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScriptQuery_StillExposesItsAlias()
    {
        const string marked = "CREATE TEMP TABLE t AS SELECT 1 AS id; SELECT a.id FROM t a WHERE |1=1; DROP TABLE t;";
        var scope = SqlScopeAtCursorResolver.Resolve(marked.Replace("|", ""), marked.IndexOf('|'));
        Assert.NotNull(scope);
        Assert.Contains(scope.VisibleRelations, relation => relation.Name == "t" && relation.Alias == "a"
            && relation.Kind == SqlScopeRelationKind.ScriptLocalTable);
    }

    [Theory]
    [InlineData("SELECT * FROM existing e WHERE |1=1; CREATE TEMP TABLE future_table AS SELECT 1 AS id;", "future_table", false)]
    [InlineData("CREATE TEMP TABLE t AS SELECT 1 AS id; SELECT * FROM t WHERE |1=1;", "t", true)]
    [InlineData("CREATE TEMP TABLE t AS SELECT 1 AS id; SELECT * FROM t WHERE |1=1; DROP TABLE t;", "t", true)]
    [InlineData("CREATE TEMP TABLE t AS SELECT 1 AS id; DROP TABLE t; SELECT * FROM t WHERE |1=1;", "t", false)]
    [InlineData("CREATE TEMP TABLE t AS SELECT 1 AS id; DROP TABLE t; CREATE TEMP TABLE t AS SELECT 2 AS id; SELECT * FROM t WHERE |1=1;", "t", true)]
    [InlineData("CREATE TEMP TABLE t AS SELECT 1 AS id; CREATE TEMP TABLE other AS SELECT 2 AS id; DROP TABLE other; SELECT * FROM t WHERE |1=1;", "t", true)]
    [InlineData("CREATE TABLE t AS SELECT 1 AS id; SELECT * FROM t WHERE |1=1; DROP TABLE t;", "t", true)]
    public void ScriptRelations_RespectCursorTime(string marked, string name, bool visible)
    {
        var cursor = marked.IndexOf('|');
        var sql = marked.Replace("|", "", StringComparison.Ordinal);
        var scope = SqlScopeAtCursorResolver.Resolve(sql, cursor);
        Assert.NotNull(scope);
        Assert.Equal(visible, scope.VisibleRelations.Any(relation =>
            relation.Kind == SqlScopeRelationKind.ScriptLocalTable
            && string.Equals(relation.Name, name, StringComparison.OrdinalIgnoreCase)));
    }
}
