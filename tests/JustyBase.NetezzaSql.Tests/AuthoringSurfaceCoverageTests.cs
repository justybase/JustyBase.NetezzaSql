using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

public sealed class AuthoringSurfaceCoverageTests
{
    [Fact]
    public void SymbolService_ResolvesCteAndAliasDefinitionsAndReferences()
    {
        const string sql = "WITH cte AS (SELECT 1) SELECT a.id FROM cte a WHERE a.id > 0";
        var cteOffset = sql.LastIndexOf("cte", StringComparison.Ordinal);
        var aliasOffset = sql.LastIndexOf("a.id", StringComparison.Ordinal);

        var cte = NzSymbolService.GetSymbol(sql, cteOffset);
        var alias = NzSymbolService.GetSymbol(sql, aliasOffset);

        Assert.NotNull(cte);
        Assert.True(cte.Occurrences.Count >= 2);
        Assert.NotNull(NzSymbolService.GetDefinition(sql, cteOffset));
        Assert.True(NzSymbolService.GetReferences(sql, aliasOffset).Count >= 2);
        Assert.NotNull(alias);
        Assert.Null(NzSymbolService.GetSymbol(string.Empty, 0));
    }

    [Theory]
    [InlineData("SELECT e.id FROM employees e WHERE e.id > 0", "e.id", 3)]
    [InlineData("SELECT x.id FROM (SELECT id FROM employees) x WHERE x.id > 0", "x.id", 3)]
    [InlineData("WITH source AS (SELECT 1) SELECT * FROM source; WITH next_cte AS (SELECT 2) SELECT * FROM next_cte", "next_cte", 2)]
    public void SymbolService_ResolvesAliasesAndStatementLocalCtes(string sql, string target, int minimumOccurrences)
    {
        var offset = sql.LastIndexOf(target, StringComparison.Ordinal);
        var symbol = NzSymbolService.GetSymbol(sql, offset);

        Assert.NotNull(symbol);
        Assert.True(symbol.Occurrences.Count >= minimumOccurrences);
        Assert.NotEmpty(NzSymbolService.GetReferences(sql, offset));
    }

    [Theory]
    [InlineData("SELECT * FROM t", "SELECT", "Retrieve rows")]
    [InlineData("CREATE TABLE t (id INTEGER)", "INTEGER", "Integer")]
    [InlineData("SELECT CAST(a AS VARCHAR) FROM t", "VARCHAR", "Character")]
    public void HoverService_ProvidesKeywordAndDatatypeDocumentation(string sql, string token, string expected)
    {
        using var coordinator = new DocumentParsingCoordinator();
        var hover = NzHoverService.GetHover(sql, sql.IndexOf(token, StringComparison.Ordinal), null, coordinator, "file:///hover.sql");

        Assert.NotNull(hover);
        Assert.Contains(expected, hover.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HoverService_HandlesMissingMetadataAndInvalidPositions()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new("T", Columns: [new("ID")]));

        const string tableSql = "SELECT ID FROM T";
        Assert.NotNull(NzHoverService.GetHover(tableSql, tableSql.LastIndexOf('T'), schema));
        Assert.Null(NzHoverService.GetHover("   ", 1, schema));
    }

    [Fact]
    public void RenameService_ReplacesEveryOccurrenceWithoutTouchingStaleOffsets()
    {
        const string sql = "SELECT a.id FROM orders a WHERE a.id > 0";
        var symbol = NzSymbolService.GetSymbol(sql, sql.LastIndexOf("a.id", StringComparison.Ordinal));

        Assert.NotNull(symbol);
        var renamed = NzRenameService.ApplyRename(sql, symbol!, "order_alias");
        Assert.Contains("orders order_alias", renamed, StringComparison.Ordinal);
        Assert.Contains("order_alias.id", renamed, StringComparison.Ordinal);
        Assert.Equal(sql, NzRenameService.ApplyRename(sql, symbol, "invalid alias!"));
    }

    [Fact]
    public void Completion_HandlesQuotedAndCrossDatabaseTablePrefixes()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new("Orders", Schema: "Sales", Database: "Warehouse", Columns: [new("Id")]));
        var engine = new NzCompletionEngine(schema);

        Assert.NotEmpty(engine.GetCompletions("SELECT * FROM \"Warehouse\"..\"Orders\" ", "SELECT * FROM \"Warehouse\"..\"Orders\" ".Length));
        Assert.NotEmpty(engine.GetCompletions("SELECT * FROM WAREHOUSE..ORDERS o WHERE o.", "SELECT * FROM WAREHOUSE..ORDERS o WHERE o.".Length));
    }
}
