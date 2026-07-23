using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class CompletionWildcardResolverTests
{
    private readonly InMemorySchemaProvider _schema;
    private readonly CompletionWildcardResolver _resolver;

    public CompletionWildcardResolverTests()
    {
        _schema = (InMemorySchemaProvider)SqlTestHelpers.CreateStandardMockSchema();
        _resolver = new CompletionWildcardResolver(_schema);
    }

    private CompletionItem? Resolve(string sql, int? cursor = null)
    {
        cursor ??= sql.IndexOf('*', StringComparison.Ordinal) + 1;
        var tokens = NzLexer.Tokenize(sql).ToArray();
        var scope = new CompletionScopeProvider(_schema).TryBuild(sql);
        var collector = new TokenScopeCollector(_schema);
        collector.Collect(tokens, sql.Length);
        return _resolver.TryResolveWildcardSnippet(sql, cursor.Value, collector, scope, tokens);
    }

    [Fact]
    public void Wildcard_TableAlias_ExpandsColumns()
    {
        var item = Resolve("SELECT e.* FROM TESTDB..EMPLOYEES e");
        Assert.NotNull(item);
        Assert.Equal(CompletionKind.Snippet, item!.Kind);
        Assert.Contains("e.EMPLOYEE_ID", item.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wildcard_SubqueryAlias_ExpandsInferredColumns()
    {
        var sql = "SELECT sq.* FROM (SELECT EMPLOYEE_ID, FIRST_NAME FROM TESTDB..EMPLOYEES) sq";
        var item = Resolve(sql);
        Assert.NotNull(item);
        Assert.Contains("sq.EMPLOYEE_ID", item!.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sq.FIRST_NAME", item!.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wildcard_CteAlias_ExpandsColumns()
    {
        var sql = "WITH cte AS (SELECT EMPLOYEE_ID, SALARY FROM TESTDB..EMPLOYEES) SELECT cte.* FROM cte";
        var item = Resolve(sql);
        Assert.NotNull(item);
        Assert.Contains("cte.EMPLOYEE_ID", item!.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wildcard_QualifiedTableName_ExpandsWithoutAlias()
    {
        var sql = "SELECT EMPLOYEES.* FROM TESTDB..EMPLOYEES";
        var item = Resolve(sql);
        Assert.NotNull(item);
        Assert.Contains("EMPLOYEES.EMPLOYEE_ID", item!.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wildcard_JoinAlias_ResolvesSecondTable()
    {
        var sql = "SELECT d.* FROM TESTDB..EMPLOYEES e JOIN TESTDB..DEPARTMENTS d ON 1=1";
        var item = Resolve(sql);
        Assert.NotNull(item);
        Assert.Contains("d.DEPARTMENT_ID", item!.Label, StringComparison.OrdinalIgnoreCase);
    }
}
