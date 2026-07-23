using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

public sealed class NetezzaSqlLspServicesTests
{
    [Fact]
    public void DefinitionService_ReturnsCTEDefinitionLocation()
    {
        var text = "WITH cte AS (SELECT 1) SELECT * FROM cte";
        var uri = "file:///test.sql";
        var definitionIndex = text.IndexOf("cte", StringComparison.Ordinal);
        var usageIndex = text.LastIndexOf("cte", StringComparison.Ordinal);

        var result = DefinitionService.GetDefinitions(text, 0, usageIndex + 1, uri);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(definitionIndex, result[0].Range.Start.Character);
        Assert.Equal(definitionIndex + 3, result[0].Range.End.Character);
    }

    [Fact]
    public void ReferencesService_ReturnsDeclarationAndUsageForCTE()
    {
        var text = "WITH cte AS (SELECT 1) SELECT * FROM cte";
        var uri = "file:///test.sql";
        var definitionIndex = text.IndexOf("cte", StringComparison.Ordinal);
        var usageIndex = text.LastIndexOf("cte", StringComparison.Ordinal);

        var result = ReferencesService.GetReferences(text, 0, usageIndex + 1, uri, includeDeclaration: true);

        Assert.Equal(2, result.Length);
        Assert.Equal(definitionIndex, result[0].Range.Start.Character);
        Assert.Equal(usageIndex, result[1].Range.Start.Character);
    }

    [Fact]
    public void SemanticTokensService_WithSchema_ColorsTableAndColumn()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES", Columns:
        [
            new ColumnInfo("EMPLOYEE_ID"),
            new ColumnInfo("SALARY"),
        ]));

        var classifier = new NzSemanticTokenClassifier(schema);
        const string sql = "SELECT EMPLOYEE_ID FROM EMPLOYEES";
        var result = SemanticTokensService.GetSemanticTokens(sql, classifier, "file:///test.sql");

        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data);

        var tokenTypes = new HashSet<uint>();
        for (int i = 3; i < result.Data!.Length; i += 5)
            tokenTypes.Add(result.Data[i]);

        Assert.Contains((uint)SemanticTokenKind.Table, tokenTypes);
        Assert.Contains((uint)SemanticTokenKind.Column, tokenTypes);
        Assert.Contains((uint)SemanticTokenKind.Keyword, tokenTypes);
    }

    [Fact]
    public void DocumentSymbolService_ReturnsStatementsAndCTEDefinitions()
    {
        var text = "WITH cte AS (SELECT 1) SELECT * FROM cte";

        var result = DocumentSymbolService.GetDocumentSymbols(text);

        Assert.Equal(2, result.Count(symbol => symbol.Name == "SELECT" && symbol.Kind == SymbolKind.Namespace));
        Assert.Contains(result, symbol => symbol.Name == "cte" && symbol.Kind == SymbolKind.Class);
    }
}
