using CatalogSql = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql;
using JustyBase.NetezzaDdl;
using JustyBase.NetezzaDdl.Models;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

const string sql = "SELECT account_id, amount FROM sales WHERE amount > 0";

PrintHeader("Parse and format");
var tokens = NzLexer.Tokenize(sql).ToArray();
var parser = new NzSqlParser(tokens);
var statement = parser.Parse();

if (statement is not null && parser.Errors.Count == 0)
{
    Console.WriteLine(NzSqlFormatter.Format(statement));
}
else
{
    PrintDiagnostics(parser.Errors);
}

PrintHeader("Schema-aware linting");
var schema = new InMemorySchemaProvider();
schema.AddTable(new TableInfo(
    Name: "SALES",
    Schema: "ADMIN",
    Database: "ANALYTICS",
    Columns: new[]
    {
        new ColumnInfo("ACCOUNT_ID", DataType: "INTEGER"),
        new ColumnInfo("AMOUNT", DataType: "NUMERIC(12,2)")
    }));

using var validator = new SqlValidator(schema);
var lintResult = validator.Validate(sql, documentUri: "sample.sql");

foreach (var issue in lintResult.Issues)
    Console.WriteLine($"{issue.RuleId}: {issue.Message}");

Console.WriteLine($"Issues: {lintResult.Issues.Count}");

PrintHeader("Completion");
const string completionSql = "SELECT * FROM ";
var completionEngine = new NzCompletionEngine(schema);
var completions = completionEngine.GetCompletions(completionSql, completionSql.Length);

foreach (var item in completions.Take(5))
    Console.WriteLine($"{item.Kind}: {item.Label}");

PrintHeader("Generate DDL");
var tableInput = new NetezzaTableDdlInput(
    Database: "ANALYTICS",
    Schema: "ADMIN",
    TableName: "SALES_COPY",
    Columns: new[]
    {
        new NetezzaColumnDdl("ACCOUNT_ID", "INTEGER", NotNull: true),
        new NetezzaColumnDdl("AMOUNT", "NUMERIC(12,2)")
    },
    DistributeColumns: new[] { "ACCOUNT_ID" },
    TableComment: "Created by the JustyBase sample");

Console.WriteLine(new NetezzaDdlTextBuilder().BuildCreateTable(tableInput));

PrintHeader("Catalog SQL");
Console.WriteLine(CatalogSql.GetSqlOfColumns("ANALYTICS"));

static void PrintHeader(string title)
{
    Console.WriteLine();
    Console.WriteLine($"=== {title} ===");
}

static void PrintDiagnostics(IReadOnlyList<ValidationError> errors)
{
    foreach (var error in errors)
        Console.WriteLine($"{error.Code}: {error.Message}");
}
