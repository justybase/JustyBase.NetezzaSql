using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Caching;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ParserConformanceTests
{
    [Fact]
    public void Parse_CteJoinAndWindowQuery_ProducesSelectAst()
    {
        using var runtime = new ParsingRuntime();
        var result = runtime.Parse("WITH c AS (SELECT id FROM source) SELECT ROW_NUMBER() OVER (ORDER BY id) AS n FROM c JOIN target t ON c.id = t.id;");

        Assert.True(result.Valid);
        var statement = Assert.IsType<SelectStatement>(Assert.Single(result.Statements));
        Assert.NotNull(statement.With);
        Assert.Single(Assert.Single(statement.From!).Joins!);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_CreateTableWithNetezzaStorageClauses_ProducesTypedStatement()
    {
        using var runtime = new ParsingRuntime();
        var result = runtime.Parse("CREATE TABLE DB.S.ORDERS (ID INTEGER NOT NULL DEFAULT 0) DISTRIBUTE ON (ID) ORGANIZE ON (ID);");

        Assert.True(result.Valid);
        var statement = Assert.IsType<CreateTableStatement>(Assert.Single(result.Statements));
        Assert.NotNull(statement.Distribute);
        Assert.NotNull(statement.Organize);
        Assert.True(Assert.Single(statement.Columns!).NotNull);
    }

    [Fact]
    public void Parse_InvalidCreateTable_ReturnsDiagnosticWithSourceOffset()
    {
        using var runtime = new ParsingRuntime();
        var result = runtime.Parse("CREATE TABLE missing");

        Assert.False(result.Valid);
        var error = Assert.Single(result.Errors);
        Assert.True(error.Code is "PAR001" or "PAR121");
        Assert.True(error.Position.Absolute >= 0);
    }

    [Fact]
    public void Parse_AlterTable_PreservesTheAlteredObjectName()
    {
        using var runtime = new ParsingRuntime();
        var result = runtime.Parse("ALTER TABLE DB.S.ORDERS ADD COLUMN STATUS VARCHAR(20);");

        Assert.True(result.Valid);
        var statement = Assert.IsType<AlterTableStatement>(Assert.Single(result.Statements));
        Assert.Equal("ORDERS", statement.Table.Name);
        Assert.Equal("S", statement.Table.Schema);
        Assert.Equal("DB", statement.Table.Database);
    }
}
