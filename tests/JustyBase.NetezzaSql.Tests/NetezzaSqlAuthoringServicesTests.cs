using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

public sealed class NetezzaSqlAuthoringServicesTests
{
    [Fact]
    public void SymbolService_ReturnsCTEDefinitionLocation()
    {
        var text = "WITH cte AS (SELECT 1) SELECT * FROM cte";
        var definitionIndex = text.IndexOf("cte", StringComparison.Ordinal);
        var usageIndex = text.LastIndexOf("cte", StringComparison.Ordinal);

        var result = NzSymbolService.GetDefinition(text, usageIndex + 1);

        Assert.NotNull(result);
        Assert.Equal(definitionIndex, result!.StartAbsolute);
        Assert.Equal(definitionIndex + 3, result.EndAbsolute);
    }

    [Fact]
    public void SymbolService_ReturnsDeclarationAndUsageForCTE()
    {
        var text = "WITH cte AS (SELECT 1) SELECT * FROM cte";
        var definitionIndex = text.IndexOf("cte", StringComparison.Ordinal);
        var usageIndex = text.LastIndexOf("cte", StringComparison.Ordinal);

        var result = NzSymbolService.GetReferences(text, usageIndex + 1);

        Assert.Equal(2, result.Count);
        Assert.Equal(definitionIndex, result[0].StartAbsolute);
        Assert.Equal(usageIndex, result[1].StartAbsolute);
    }

    [Fact]
    public void SemanticClassifier_WithSchema_ColorsTableAndColumn()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES", Columns:
        [
            new ColumnInfo("EMPLOYEE_ID"),
            new ColumnInfo("SALARY"),
        ]));

        var classifier = new NzSemanticTokenClassifier(schema);
        const string sql = "SELECT EMPLOYEE_ID FROM EMPLOYEES";
        var spans = classifier.Classify(sql);

        Assert.NotEmpty(spans);
        Assert.Contains(spans, s => s.Kind == SemanticTokenKind.Table);
        Assert.Contains(spans, s => s.Kind == SemanticTokenKind.Column);
        Assert.Contains(spans, s => s.Kind == SemanticTokenKind.Keyword);
    }

    [Fact]
    public void SymbolCollector_ReturnsStatementsAndCTEDefinitions()
    {
        var text = "WITH cte AS (SELECT 1) SELECT * FROM cte";

        var index = NzSymbolCollector.Collect(text);

        Assert.Contains(index.Occurrences, o => o.Name == "cte" && o.Kind == SqlSymbolKind.Cte && o.IsDefinition);
        Assert.True(index.Occurrences.Count >= 2);
    }
}
