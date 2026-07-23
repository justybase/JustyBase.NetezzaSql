using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlLsp.Workspace;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

public sealed class LspRequestServiceCoverageTests
{
    [Fact]
    public void WorkspaceAndAuthoringRequests_ReturnUsefulResults()
    {
        const string uri = "file:///request.sql";
        const string sql = "WITH cte AS (SELECT 1) SELECT HASH(1), * FROM cte";
        var docs = new DocumentManager();
        docs.OpenOrUpdate(uri, sql, 1);
        docs.UpdateText(uri, sql, 2);

        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("CTE", Columns: [new ColumnInfo("ID")]));
        var completion = CompletionService.GetCompletions("SELECT ", 0, 7, schema);
        var hover = HoverService.GetHover("SELECT HASH(1)", 0, 7, schema);
        var signature = SignatureHelpService.GetSignatureHelp("SELECT HASH(", 0, 12);
        var diagnostics = LintService.Lint("SELECT * FROM missing_table", schema);

        Assert.True(docs.IsOpen(uri));
        Assert.Equal(sql, docs.GetText(uri));
        Assert.Contains(uri, docs.GetAllUris());
        Assert.NotEmpty(completion.Items!);
        Assert.Null(HoverService.GetHover(string.Empty, 0, 0, schema));
        Assert.Null(SignatureHelpService.GetSignatureHelp(string.Empty, 0, 0));
        Assert.NotEmpty(diagnostics);

        docs.Close(uri);
        Assert.False(docs.IsOpen(uri));
    }

    [Fact]
    public void LintService_HandlesEmptySyntaxAndSemanticInputs()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMP", Columns: [new ColumnInfo("ID")]));

        Assert.Empty(LintService.Lint(string.Empty, schema));
        Assert.NotEmpty(LintService.Lint("SELECT * FROM EMP", null));
        Assert.NotEmpty(LintService.Lint("SELECT FROM EMP", schema));
    }
}
