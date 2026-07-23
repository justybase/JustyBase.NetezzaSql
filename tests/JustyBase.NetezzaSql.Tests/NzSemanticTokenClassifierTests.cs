using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSemanticTokenClassifierTests
{
    private readonly NzSemanticTokenClassifier _classifier;
    private readonly InMemorySchemaProvider _schema;

    public NzSemanticTokenClassifierTests()
    {
        _schema = (InMemorySchemaProvider)SqlTestHelpers.CreateStandardMockSchema();
        _classifier = new NzSemanticTokenClassifier(_schema);
    }

    private static SemanticTokenKind KindAt(IReadOnlyList<SemanticTokenSpan> spans, string sql, string token)
    {
        var index = sql.IndexOf(token, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Token '{token}' not found in SQL.");
        var span = spans.FirstOrDefault(s => s.Start <= index && s.Start + s.Length > index);
        Assert.True(span.Length > 0, $"No semantic span covers '{token}'.");
        return span.Kind;
    }

    [Fact]
    public void SemanticToken_TableInFrom_ColorsTable()
    {
        const string sql = "SELECT * FROM EMPLOYEES";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Table, KindAt(spans, sql, "EMPLOYEES"));
    }

    [Fact]
    public void SemanticToken_ColumnInSelect_ColorsColumn()
    {
        const string sql = "SELECT EMPLOYEE_ID FROM EMPLOYEES";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Column, KindAt(spans, sql, "EMPLOYEE_ID"));
    }

    [Fact]
    public void SemanticToken_QualifiedAlias_ColorsAliasAndColumn()
    {
        const string sql = "SELECT e.SALARY FROM EMPLOYEES e";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Alias, KindAt(spans, sql, "e"));
        Assert.Equal(SemanticTokenKind.Column, KindAt(spans, sql, "SALARY"));
    }

    [Fact]
    public void SemanticToken_CteName_ColorsCte()
    {
        const string sql = "WITH cte AS (SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES) SELECT * FROM cte";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Cte, KindAt(spans, sql, "cte"));
    }

    [Fact]
    public void SemanticToken_UnknownColumn_FallsBackToIdentifier()
    {
        const string sql = "SELECT BAD FROM EMPLOYEES";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Identifier, KindAt(spans, sql, "BAD"));
    }

    [Fact]
    public void SemanticToken_UpdateTarget_ColorsTable()
    {
        const string sql = "UPDATE EMPLOYEES SET SALARY = 1";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Table, KindAt(spans, sql, "EMPLOYEES"));
    }

    [Fact]
    public void SemanticToken_MergeInto_ColorsTable()
    {
        const string sql = "MERGE INTO EMPLOYEES USING DEPARTMENTS d ON 1=1";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Table, KindAt(spans, sql, "EMPLOYEES"));
    }

    [Fact]
    public void SemanticToken_DbDotTable_ColorsTable()
    {
        const string sql = "SELECT * FROM TESTDB..EMPLOYEES";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Table, KindAt(spans, sql, "EMPLOYEES"));
    }

    [Fact]
    public void SemanticToken_FunctionCall_ColorsFunction()
    {
        const string sql = "SELECT COUNT(*) FROM EMPLOYEES";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Function, KindAt(spans, sql, "COUNT"));
    }

    [Fact]
    public void SemanticToken_Keyword_ColorsSelect()
    {
        const string sql = "SELECT 1";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Keyword, KindAt(spans, sql, "SELECT"));
    }

    [Fact]
    public void SemanticToken_StringLiteral_ColorsString()
    {
        const string sql = "SELECT 'hello'";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.String, KindAt(spans, sql, "'hello'"));
    }

    [Fact]
    public void SemanticToken_NumberLiteral_ColorsNumber()
    {
        const string sql = "SELECT 42";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Number, KindAt(spans, sql, "42"));
    }

    [Fact]
    public void SemanticToken_DataType_ColorsType()
    {
        const string sql = "CREATE TABLE t (id INT4)";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Type, KindAt(spans, sql, "INT4"));
    }

    [Fact]
    public void SemanticToken_Variable_ColorsVariable()
    {
        const string sql = "SELECT ${myvar}";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Variable, KindAt(spans, sql, "${myvar}"));
    }

    [Fact]
    public void SemanticToken_Comment_ColorsComment()
    {
        const string sql = "SELECT 1 -- comment";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Comment, KindAt(spans, sql, "-- comment"));
    }

    [Fact]
    public void SemanticToken_DeleteFrom_ColorsTable()
    {
        const string sql = "DELETE FROM EMPLOYEES";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Table, KindAt(spans, sql, "EMPLOYEES"));
    }

    [Fact]
    public void SemanticToken_InsertInto_ColorsTable()
    {
        const string sql = "INSERT INTO EMPLOYEES (EMPLOYEE_ID) VALUES (1)";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Table, KindAt(spans, sql, "EMPLOYEES"));
    }

    [Fact]
    public void SemanticToken_JoinTable_ColorsBothTables()
    {
        const string sql = "SELECT * FROM EMPLOYEES e JOIN DEPARTMENTS d ON 1=1";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Table, KindAt(spans, sql, "EMPLOYEES"));
        Assert.Equal(SemanticTokenKind.Table, KindAt(spans, sql, "DEPARTMENTS"));
    }

    [Fact]
    public void SemanticToken_WhereColumn_ColorsColumn()
    {
        const string sql = "SELECT * FROM EMPLOYEES WHERE SALARY > 1";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Column, KindAt(spans, sql, "SALARY"));
    }

    [Fact]
    public void SemanticToken_SetColumn_ColorsColumn()
    {
        const string sql = "UPDATE EMPLOYEES SET SALARY = 1";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Column, KindAt(spans, sql, "SALARY"));
    }

    [Fact]
    public void SemanticToken_TrueLiteral_ColorsKeywordWithDefaultLibrary()
    {
        const string sql = "SELECT TRUE";
        var spans = _classifier.Classify(sql);
        var span = spans.First(s =>
            s.Start <= sql.IndexOf("TRUE", StringComparison.Ordinal) &&
            s.Start + s.Length > sql.IndexOf("TRUE", StringComparison.Ordinal));
        Assert.Equal(SemanticTokenKind.Keyword, span.Kind);
        Assert.True(span.Modifiers.HasFlag(SemanticTokenModifiers.DefaultLibrary));
    }

    [Fact]
    public void SemanticToken_KnownFunctionWithoutParens_ColorsFunction()
    {
        const string sql = "SELECT NVL(a, b) FROM EMPLOYEES";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Function, KindAt(spans, sql, "NVL"));
    }

    [Fact]
    public void SemanticToken_SubqueryAlias_ColorsAlias()
    {
        const string sql = "SELECT sq.EMPLOYEE_ID FROM (SELECT EMPLOYEE_ID FROM EMPLOYEES) sq";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Alias, KindAt(spans, sql, "sq"));
    }

    [Fact]
    public void SemanticToken_UnrelatedTableColumnName_FallsBackToIdentifier()
    {
        const string sql = "SELECT STATUS FROM DEPARTMENTS";
        var spans = _classifier.Classify(sql);
        Assert.Equal(SemanticTokenKind.Identifier, KindAt(spans, sql, "STATUS"));
    }

    [Fact]
    public void SemanticToken_EmptySql_ReturnsEmpty()
    {
        var spans = _classifier.Classify("");
        Assert.Empty(spans);
    }

    [Fact]
    public void SemanticToken_LegendMatchesEnumOrder()
    {
        Assert.Equal(NzSemanticTokenClassifier.TokenTypesLegend.Length, Enum.GetValues<SemanticTokenKind>().Length);
    }
}
