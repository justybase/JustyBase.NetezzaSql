using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.NetezzaSql.Tests;

public sealed class MySqlSqlParserTests
{
    private static (IReadOnlyList<Statement> Statements, IReadOnlyList<ValidationError> Errors) Parse(string sql)
    {
        var tokens = MySqlLexer.Tokenize(sql).ToArray();
        var parser = new MySqlSqlParser(tokens);
        var statements = new List<Statement>();
        while (parser.Position < tokens.Length)
        {
            var statement = parser.Parse();
            if (statement is not null) statements.Add(statement);
            else if (parser.Position >= tokens.Length) break;
        }
        return (statements, parser.Errors);
    }

    [Fact]
    public void ParseMySql_ReferenceSelectsAndIfHaveNoErrors()
    {
        var (statements, errors) = Parse("SELECT IF(manager_id > 0, 'Y', 'N') FROM departments # comment");

        Assert.Single(statements);
        Assert.Empty(errors);
    }

    [Fact]
    public void ParseMySql_LimitFormsMapToAst()
    {
        var comma = Assert.IsType<SelectStatement>(Assert.Single(Parse("SELECT * FROM `departments` LIMIT 5, 10").Statements));
        var offset = Assert.IsType<SelectStatement>(Assert.Single(Parse("SELECT * FROM departments LIMIT 10 OFFSET 5").Statements));

        Assert.Equal(LimitClauseSyntax.MySqlComma, comma.Limit!.Syntax);
        Assert.Equal(10, comma.Limit.Limit);
        Assert.Equal(5, comma.Limit.Offset);
        Assert.Equal(10, offset.Limit!.Limit);
        Assert.Equal(5, offset.Limit.Offset);
    }

    [Fact]
    public void ParseMySql_InsertIgnoreAndDuplicateUpdate()
    {
        var (statements, errors) = Parse(
            "INSERT IGNORE INTO TESTDB.departments (id) VALUES (1) ON DUPLICATE KEY UPDATE id = 2");
        var insert = Assert.IsType<InsertStatement>(Assert.Single(statements));

        Assert.Empty(errors);
        Assert.True(insert.MySqlIgnore);
        Assert.NotNull(insert.MySqlOnDuplicateKeyUpdateTokens);
        Assert.Equal("TESTDB", insert.Target.Database);
        Assert.Equal("departments", insert.Target.Name);
    }

    [Fact]
    public void FormatMySql_DatabaseQualifiedAndBacktickNamesUseSingleDot()
    {
        var (statements, errors) = Parse("SELECT * FROM `TESTDB`.`order`");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.NotNull(select.From);
        var table = select.From![0].Source.Table;

        Assert.Empty(errors);
        Assert.Equal("TESTDB", table!.Database);
        Assert.Equal("order", table.Name);
        Assert.True(table.MySqlDatabaseQualified);

        var formatted = NzSqlFormatter.Format(select);
        Assert.Contains("`TESTDB`.`order`", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("..", formatted, StringComparison.Ordinal);

        var (plainStatements, plainErrors) = Parse("SELECT * FROM TESTDB.departments");
        var plainFormatted = NzSqlFormatter.Format(
            Assert.IsType<SelectStatement>(Assert.Single(plainStatements)));
        Assert.Empty(plainErrors);
        Assert.Contains("TESTDB.departments", plainFormatted, StringComparison.Ordinal);
        Assert.DoesNotContain("TESTDB..departments", plainFormatted, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseMySql_KeywordBackedColumnClausesHaveNoErrors()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE events (created_at TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT 'changed')");

        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));

        Assert.Empty(errors);
        Assert.NotNull(table.Columns![0].MySqlAttributeTokens);
        Assert.Contains(table.Columns[0].MySqlAttributeTokens!,
            token => token.Kind == NzToken.On);
        Assert.Contains(table.Columns[0].MySqlAttributeTokens!,
            token => token.Kind == NzToken.Comment);
    }

    [Fact]
    public void ParseMySql_CreateTableSupportsTypesAttributesAndOptions()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE IF NOT EXISTS `departments_tmp` (id INT PRIMARY KEY AUTO_INCREMENT, budget DECIMAL(10,2), flags SET('a','b'), CHECK (budget > 0), department_name VARCHAR(100)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));

        Assert.Empty(errors);
        Assert.Equal("departments_tmp", table.Table.Name);
        Assert.Equal("DECIMAL", table.Columns![1].Type.Name);
        Assert.Equal("SET", table.Columns[2].Type.Name);
        Assert.NotNull(table.MySqlTableOptionTokens);
        Assert.Contains(table.Columns[0].Type.Name, new[] { "INT" });
    }

    [Theory]
    [InlineData("SELECT * FROM TESTDB.TESTDB.departments")]
    [InlineData("SELECT * FROM TESTDB..departments")]
    [InlineData("MERGE INTO departments USING source ON departments.id = source.id")]
    public void ParseMySql_RejectsUnsupportedShapes(string sql)
    {
        var (_, errors) = Parse(sql);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void DialectRuntime_UsesMySqlComponents()
    {
        Assert.Equal(SqlDialect.MySql, DialectRuntime.ParseName("mysql"));
        Assert.Equal("MySQL SQL", DialectRuntime.DiagnosticSource(SqlDialect.MySql));
        Assert.IsType<MySqlSqlParser>(DialectRuntime.CreateParser(
            DialectRuntime.Tokenize("SELECT 1", SqlDialect.MySql).ToArray(), SqlDialect.MySql));
        Assert.Same(MySqlSqlCatalog.Instance, DialectRuntime.AuthoringCatalog(SqlDialect.MySql));
        Assert.Empty(DialectRuntime.QualityRules(SqlDialect.MySql).AllRules);
    }
}
