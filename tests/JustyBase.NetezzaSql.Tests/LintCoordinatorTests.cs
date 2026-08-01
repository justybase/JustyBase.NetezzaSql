using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.NetezzaSqlLsp.Services;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class LintCoordinatorTests
{
    [Fact]
    public void LintCoordinator_ReusesDocumentAndDialectRuntime()
    {
        using var parsingCoordinator = new DocumentParsingCoordinator();
        using var lintCoordinator = new LintCoordinator(parsingCoordinator);
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES"));

        const string sql = "SELECT * FROM EMPLOYEES;";
        var first = lintCoordinator.Lint(sql, schema, SqlDialect.Oracle, "file:///lint.sql");
        var second = lintCoordinator.Lint(sql, schema, SqlDialect.Oracle, "file:///lint.sql");

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Same(
            parsingCoordinator.GetOrCreate("file:///lint.sql", SqlDialect.Oracle),
            parsingCoordinator.GetOrCreate("file:///lint.sql", SqlDialect.Oracle));
    }

    [Fact]
    public void SymbolCollector_UsesRequestedDialectLexer()
    {
        var oracle = DocumentSymbolService.GetDocumentSymbols(
            "SELECT * FROM HR.EMPLOYEES@PROD", SqlDialect.Oracle);
        var netezza = DocumentSymbolService.GetDocumentSymbols(
            "SELECT * FROM HR.EMPLOYEES@PROD", SqlDialect.Netezza);

        Assert.NotEmpty(oracle);
        Assert.Empty(netezza);
    }
}
