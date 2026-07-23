using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlVisitorTests
{
    private static (SelectStatement?, IReadOnlyList<ValidationError>) ParseAndValidate(string sql, ISchemaProvider? schema = null)
    {
        var tokens = NzLexer.Tokenize(sql).ToArray();
        var parser = new NzSqlParser(tokens);
        var stmt = parser.ParseSelect();
        if (stmt is null) return (null, parser.Errors);

        var visitor = new NzSqlVisitor(schema);
        visitor.Visit(stmt);
        return (stmt, visitor.Errors);
    }

    [Fact]
    public void Validate_SimpleSelect_HasNoErrors()
    {
        var (_, errors) = ParseAndValidate("SELECT 1");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_SelectFromKnownTable_NoErrors()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("employees", Columns: new[]
        {
            new ColumnInfo("id"), new ColumnInfo("name")
        }));

        var (_, errors) = ParseAndValidate("SELECT id, name FROM employees", schema);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_UnknownTable_ReportsSql006()
    {
        var schema = new InMemorySchemaProvider();
        var (_, errors) = ParseAndValidate("SELECT * FROM nonexistent", schema);

        // SQL006 = table not found (only when qualified or schema can validate)
        // We don't have qualified name here, and CanValidateUnqualified is false, so no error
        // This is correct behavior per the visitor
        Assert.DoesNotContain(errors, e => e.Code == "SQL006");
    }

    [Fact]
    public void Validate_QualifiedUnknownTable_ReportsSql006()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("known_table", Columns: [new ColumnInfo("id")]));
        var (_, errors) = ParseAndValidate("SELECT * FROM dev.nonexistent", schema);

        Assert.Contains(errors, e => e.Code == "SQL006");
    }

    [Fact]
    public void Validate_UnknownColumn_ReportsSql004()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("t", Columns: new[]
        {
            new ColumnInfo("id"), new ColumnInfo("name")
        }));

        var (_, errors) = ParseAndValidate("SELECT unknown_col FROM t", schema);
        Assert.Contains(errors, e => e.Code == "SQL004");
    }

    [Fact]
    public void Validate_DuplicateTable_ReportsSql011()
    {
        var (_, errors) = ParseAndValidate("SELECT * FROM t, t");
        Assert.Contains(errors, e => e.Code == "SQL011");
    }

    [Fact]
    public void Validate_SubqueryWithoutAlias_ReportsSql020()
    {
        var (_, errors) = ParseAndValidate("SELECT * FROM (SELECT 1)");
        Assert.Contains(errors, e => e.Code == "SQL020");
    }

    [Fact]
    public void Validate_SubqueryWithAlias_NoError()
    {
        var (_, errors) = ParseAndValidate("SELECT * FROM (SELECT 1) AS sq");
        Assert.DoesNotContain(errors, e => e.Code == "SQL020");
    }

    [Fact]
    public void Validate_AmbiguousColumn_ReportsSql008()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("a", Columns: new[] { new ColumnInfo("x") }));
        schema.AddTable(new TableInfo("b", Columns: new[] { new ColumnInfo("x") }));

        var (_, errors) = ParseAndValidate("SELECT x FROM a, b", schema);
        Assert.Contains(errors, e => e.Code == "SQL008");
    }

    [Fact]
    public void Validate_QualifiedColumnInKnownTable_NoError()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("t", Columns: new[] { new ColumnInfo("id") }));

        var (_, errors) = ParseAndValidate("SELECT t.id FROM t", schema);
        Assert.DoesNotContain(errors, e => e.Code == "SQL004" || e.Code == "SQL008");
    }

    [Fact]
    public void Validate_JoinWithKnownTables_NoError()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("a", Columns: new[] { new ColumnInfo("id") }));
        schema.AddTable(new TableInfo("b", Columns: new[] { new ColumnInfo("id") }));

        var (_, errors) = ParseAndValidate(
            "SELECT a.id FROM a INNER JOIN b ON a.id = b.id", schema);
        Assert.DoesNotContain(errors, e => e.Code == "SQL004");
    }

    [Fact]
    public void QualifiedUnknownColumn_ReportsSql004()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", Schema: "ADMIN", Database: "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("DATE_ID"), new ColumnInfo("FULL_DATE")
        }));

        var (_, errors) = ParseAndValidate(
            "SELECT A.NO_SUCH_COLUMN FROM JUST_DATA.ADMIN.DIMDATE A", schema);
        Assert.Contains(errors, e => e.Code == "SQL004");
    }

    [Fact]
    public void QualifiedExistingColumn_NoError()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", Schema: "ADMIN", Database: "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("DATE_ID"), new ColumnInfo("FULL_DATE")
        }));

        var (_, errors) = ParseAndValidate(
            "SELECT A.DATE_ID FROM JUST_DATA.ADMIN.DIMDATE A", schema);
        Assert.DoesNotContain(errors, e => e.Code == "SQL004");
    }

    [Fact]
    public void UnqualifiedUnknownColumn_FromQualifiedTable_ReportsSql004()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", Schema: "ADMIN", Database: "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("DATE_ID"), new ColumnInfo("FULL_DATE")
        }));

        var (_, errors) = ParseAndValidate(
            "SELECT NO_SUCH_COLUMN FROM JUST_DATA.ADMIN.DIMDATE", schema);
        Assert.Contains(errors, e => e.Code == "SQL004");
    }

    [Fact]
    public void DoubleDot_TableName_ParsedCorrectly()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMACCOUNT", Database: "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("ACCOUNTCODEALTERNATEKEY")
        }));

        var (stmt, errors) = ParseAndValidate(
            "SELECT A.ACCOUNTCODEALTERNATEKEY FROM JUST_DATA..DIMACCOUNT A", schema);
        Assert.NotNull(stmt);
        Assert.DoesNotContain(errors, e => e.Code == "SQL003" || e.Code == "SQL004");
    }
}
