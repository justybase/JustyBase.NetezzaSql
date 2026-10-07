using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class LintEngineCoordinatorTests
{
    [Fact]
    public void LintEngine_WithSharedRuntime_ReusesDocumentAndDialectRuntime()
    {
        using var parsingCoordinator = new DocumentParsingCoordinator();
        var runtime = parsingCoordinator.GetOrCreate("file:///lint.sql", SqlDialect.Oracle);
        using var engine = new LintEngine(SqlDialect.Oracle, runtime);
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES"));

        const string sql = "SELECT * FROM EMPLOYEES;";
        var first = engine.RunFullLint(new LintConfig(sql, schema, "file:///lint.sql", Dialect: SqlDialect.Oracle));
        var second = engine.RunFullLint(new LintConfig(sql, schema, "file:///lint.sql", Dialect: SqlDialect.Oracle));

        Assert.Equal(
            first.Issues.Select(i => (i.RuleId, i.Message, i.StartOffset, i.EndOffset)),
            second.Issues.Select(i => (i.RuleId, i.Message, i.StartOffset, i.EndOffset)));
        Assert.Same(
            parsingCoordinator.GetOrCreate("file:///lint.sql", SqlDialect.Oracle),
            parsingCoordinator.GetOrCreate("file:///lint.sql", SqlDialect.Oracle));
    }

    [Fact]
    public void ParseSession_UsesRequestedDialectLexer()
    {
        using var oracleSession = new DocumentParseSession(SqlDialect.Oracle);
        var oracle = oracleSession.GetOrParse("SELECT * FROM HR.EMPLOYEES@PROD");

        using var netezzaSession = new DocumentParseSession(SqlDialect.Netezza);
        var netezza = netezzaSession.GetOrParse("SELECT * FROM HR.EMPLOYEES@PROD");

        Assert.True(oracle.Valid);
        Assert.False(netezza.Valid);
    }
}
