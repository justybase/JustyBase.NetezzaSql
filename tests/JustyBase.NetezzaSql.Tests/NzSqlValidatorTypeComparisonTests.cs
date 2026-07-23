using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlValidatorTypeComparisonTests
{
    private readonly ISchemaProvider _schema;

    public NzSqlValidatorTypeComparisonTests()
    {
        var provider = new InMemorySchemaProvider();
        provider.AddTable(new TableInfo("T_TEXT", Columns:
        [
            new ColumnInfo("NAME", DataType: "VARCHAR"),
            new ColumnInfo("AMOUNT", DataType: "INTEGER"),
        ]));
        _schema = provider;
    }

    [Fact]
    public void Validate_Arithmetic_TextColumnWithNumericLiteral_WarnsSql025()
    {
        SqlTestHelpers.ExpectWarningCode(
            "SELECT NAME + 1 FROM T_TEXT;",
            "SQL025",
            _schema);
    }

    [Fact]
    public void Validate_Arithmetic_NumericColumnWithStringLiteral_WarnsSql025()
    {
        SqlTestHelpers.ExpectWarningCode(
            "SELECT AMOUNT + '1' FROM T_TEXT;",
            "SQL025",
            _schema);
    }

    [Fact]
    public void Validate_DeleteWithoutWhere_ReportsSql043()
    {
        SqlTestHelpers.ExpectErrorCode("DELETE FROM T_TEXT;", "SQL043", _schema);
    }

    [Fact]
    public void Validate_UpdateWithoutWhere_ReportsSql044()
    {
        SqlTestHelpers.ExpectErrorCode("UPDATE T_TEXT SET NAME = 'x';", "SQL044", _schema);
    }

    [Fact]
    public void Validate_UpdateAsAlias_ReportsSql046()
    {
        SqlTestHelpers.ExpectErrorCode("UPDATE T_TEXT AS t SET NAME = 'x' WHERE 1=1;", "SQL046", _schema);
    }

    [Fact]
    public void Validate_GroupBy_IgnoresWindowFunction()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT DEPARTMENT_ID, ROW_NUMBER() OVER (ORDER BY EMPLOYEE_ID) AS rn " +
            "FROM TESTDB..EMPLOYEES GROUP BY DEPARTMENT_ID;",
            SqlTestHelpers.CreateStandardMockSchema());
    }

    [Fact]
    public void Validate_GroupBy_IgnoresLiteral()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT DEPARTMENT_ID, COUNT(*) FROM TESTDB..EMPLOYEES GROUP BY DEPARTMENT_ID, 1;",
            SqlTestHelpers.CreateStandardMockSchema());
    }

    [Fact]
    public void Validate_WindowFunction_RequiresOrderBy()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT ROW_NUMBER() OVER (PARTITION BY DEPARTMENT_ID) FROM TESTDB..EMPLOYEES;",
            "SQL022",
            SqlTestHelpers.CreateStandardMockSchema());
    }

    [Fact]
    public void Validate_DoubleComma_ReportsPar002()
    {
        var result = SqlTestHelpers.Validate("SELECT 1,, 2;");
        Assert.Contains(result.Errors, e => e.Code is "PAR002" or "PAR001");
    }
}
