using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzHoverServiceTests
{
    [Fact]
    public void Hover_OnFunction_ReturnsSignatureText()
    {
        var sql = "SELECT COUNT(id) FROM employees";
        var offset = sql.IndexOf("COUNT", StringComparison.Ordinal);

        var hover = NzHoverService.GetHover(sql, offset, schema: null);

        Assert.NotNull(hover);
        Assert.Contains("COUNT(expression)", hover!.Content);
    }

    [Fact]
    public void Hover_OnTable_ReturnsColumnsFromSchema()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("employees", Columns: [new ColumnInfo("id"), new ColumnInfo("name")]));
        var sql = "SELECT * FROM employees";
        var offset = sql.IndexOf("employees", StringComparison.Ordinal);

        var hover = NzHoverService.GetHover(sql, offset, schema);

        Assert.NotNull(hover);
        Assert.Contains("id", hover!.Content);
        Assert.Contains("name", hover.Content);
    }
}
