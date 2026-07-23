using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlValidatorDmlTests
{
    private readonly ISchemaProvider? _schema = SqlTestHelpers.CreateStandardMockSchema();

    // ===== INSERT Valid =====

    [Fact]
    public void Insert_WithColumnsAndValues()
    {
        SqlTestHelpers.ExpectValid(
            "INSERT INTO TESTDB..FILMS (CODE, TITLE, DID) VALUES ('AA001', 'Test Film', 100);", _schema);
    }

    [Fact]
    public void Insert_MultiRowValues()
    {
        SqlTestHelpers.ExpectValid(
            "INSERT INTO TESTDB..FILMS (CODE, TITLE) VALUES ('A', 'Film A'), ('B', 'Film B');", _schema);
    }

    [Fact]
    public void Insert_SelectFromQuery()
    {
        SqlTestHelpers.ExpectValid(
            "INSERT INTO TESTDB..FILMS (CODE, TITLE) SELECT PRODUCT_ID, PRODUCT_NAME FROM TESTDB..PRODUCTS;", _schema);
    }

    [Fact]
    public void Insert_NoColumnListValues()
    {
        SqlTestHelpers.ExpectValid(
            "INSERT INTO TESTDB..FILMS VALUES ('AA001', 'Test', 100);", _schema);
    }

    [Fact]
    public void Insert_NoColumnListSelect()
    {
        SqlTestHelpers.ExpectValid(
            "INSERT INTO TESTDB..ORDERS SELECT * FROM TESTDB..ORDERS;", _schema);
    }

    [Fact]
    public void Insert_WithSchemaPrefix()
    {
        SqlTestHelpers.ExpectValid(
            "INSERT INTO TESTDB.PUBLIC.EMPLOYEES (EMPLOYEE_ID, FIRST_NAME) VALUES (1, 'John');", _schema);
    }

    [Fact]
    public void Insert_SingleColumn()
    {
        SqlTestHelpers.ExpectValid(
            "INSERT INTO TESTDB..FILMS (CODE) VALUES ('X001');", _schema);
    }

    [Fact]
    public void Insert_NullValue()
    {
        SqlTestHelpers.ExpectValid(
            "INSERT INTO TESTDB..FILMS (CODE, TITLE) VALUES ('X001', NULL);", _schema);
    }

    [Fact]
    public void Insert_ExpressionValue()
    {
        SqlTestHelpers.ExpectValid(
            "INSERT INTO TESTDB..FILMS (CODE, DID) VALUES ('X001', 1 + 2);", _schema);
    }

    [Fact]
    public void Insert_EmptyValuesList()
    {
        SqlTestHelpers.ExpectValid(
            "INSERT INTO TESTDB..FILMS (CODE) VALUES ('X001');", _schema);
    }

    // ===== INSERT Syntax Errors =====

    [Fact]
    public void Insert_MissingInto()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "INSERT TESTDB..FILMS VALUES (1);", _schema);
    }

    [Fact]
    public void Insert_MissingValues()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "INSERT INTO TESTDB..FILMS (CODE, TITLE);", _schema);
    }

    [Fact]
    public void Insert_MissingParens()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "INSERT INTO TESTDB..FILMS VALUES (1, 2;", _schema);
    }

    [Fact]
    public void Insert_TrailingComma()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "INSERT INTO TESTDB..FILMS (CODE, TITLE) VALUES (1, 2,);", _schema);
    }

    [Fact]
    public void Insert_EmptyRow()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "INSERT INTO TESTDB..FILMS VALUES ();", _schema);
    }

    [Fact]
    public void Insert_NoValuesOrSelect()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "INSERT INTO TESTDB..FILMS;", _schema);
    }

    [Fact]
    public void Insert_MissingTable()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "INSERT INTO VALUES (1);", _schema);
    }

    [Fact]
    public void Insert_MissingOpenParen()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "INSERT INTO TESTDB..FILMS VALUES 1, 2);", _schema);
    }

    [Fact]
    public void Insert_MissingParenForEmptyList()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "INSERT INTO TESTDB..FILMS (CODE, TITLE VALUES (1, 2);", _schema);
    }

    // ===== UPDATE Valid =====

    [Fact]
    public void Update_WithWhere()
    {
        SqlTestHelpers.ExpectValid(
            "UPDATE TESTDB..EMPLOYEES SET SALARY = 50000 WHERE EMPLOYEE_ID = 100;", _schema);
    }

    [Fact]
    public void Update_MultiColumn()
    {
        SqlTestHelpers.ExpectValid(
            "UPDATE TESTDB..EMPLOYEES SET SALARY = 50000, STATUS = 'Active' WHERE EMPLOYEE_ID = 100;", _schema);
    }

    [Fact]
    public void Update_ExpressionValue()
    {
        SqlTestHelpers.ExpectErrorCode(
            "UPDATE TESTDB..EMPLOYEES SET SALARY = SALARY * 1.1;", "SQL044", _schema);
    }

    [Fact]
    public void Update_StringValue()
    {
        SqlTestHelpers.ExpectErrorCode(
            "UPDATE TESTDB..EMPLOYEES SET FIRST_NAME = 'John';", "SQL044", _schema);
    }

    [Fact]
    public void Update_MultipleRowsNoWhere()
    {
        SqlTestHelpers.ExpectErrorCode(
            "UPDATE TESTDB..EMPLOYEES SET STATUS = 'Active';", "SQL044", _schema);
    }

    [Fact]
    public void Update_WithSchemaPrefix()
    {
        SqlTestHelpers.ExpectValid(
            "UPDATE TESTDB.PUBLIC.EMPLOYEES SET SALARY = 60000 WHERE EMPLOYEE_ID = 200;", _schema);
    }

    [Fact]
    public void Update_WithFunctionExpression()
    {
        SqlTestHelpers.ExpectErrorCode(
            "UPDATE TESTDB..EMPLOYEES SET FIRST_NAME = UPPER(FIRST_NAME);", "SQL044", _schema);
    }

    [Fact]
    public void Update_WithCastExpression()
    {
        SqlTestHelpers.ExpectErrorCode(
            "UPDATE TESTDB..EMPLOYEES SET SALARY = CAST(SALARY AS INT);", "SQL044", _schema);
    }

    [Fact]
    public void Update_WithCaseExpression()
    {
        SqlTestHelpers.ExpectErrorCode(
            "UPDATE TESTDB..EMPLOYEES SET SALARY = CASE WHEN EMPLOYEE_ID > 100 THEN 1 ELSE 0 END;", "SQL044", _schema);
    }

    [Fact]
    public void Update_WithComplexWhere()
    {
        SqlTestHelpers.ExpectValid(
            "UPDATE TESTDB..EMPLOYEES SET SALARY = 50000 WHERE DEPARTMENT_ID = 10 AND STATUS = 'Active';", _schema);
    }

    // ===== UPDATE Syntax Errors =====

    [Fact]
    public void Update_MissingSet()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "UPDATE TESTDB..EMPLOYEES SALARY = 50000;", _schema);
    }

    [Fact]
    public void Update_MissingColumn()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "UPDATE TESTDB..EMPLOYEES SET = 50000;", _schema);
    }

    [Fact]
    public void Update_MissingEquals()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "UPDATE TESTDB..EMPLOYEES SET SALARY 50000;", _schema);
    }

    [Fact]
    public void Update_MissingValue()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "UPDATE TESTDB..EMPLOYEES SET SALARY =;", _schema);
    }

    [Fact]
    public void Update_IncompleteWhere()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "UPDATE TESTDB..EMPLOYEES SET SALARY = 50000 WHERE;", _schema);
    }

    [Fact]
    public void Update_TrailingComma()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "UPDATE TESTDB..EMPLOYEES SET SALARY = 50000,;", _schema);
    }

    // ===== DELETE Valid =====

    [Fact]
    public void Delete_WithWhere()
    {
        SqlTestHelpers.ExpectValid(
            "DELETE FROM TESTDB..EMPLOYEES WHERE EMPLOYEE_ID = 100;", _schema);
    }

    [Fact]
    public void Delete_AllRows()
    {
        SqlTestHelpers.ExpectErrorCode(
            "DELETE FROM TESTDB..EMPLOYEES;", "SQL043", _schema);
    }

    [Fact]
    public void Delete_WithComplexWhere()
    {
        SqlTestHelpers.ExpectValid(
            "DELETE FROM TESTDB..EMPLOYEES WHERE DEPARTMENT_ID = 10 AND STATUS = 'Terminated';", _schema);
    }

    [Fact]
    public void Delete_WithSchemaPrefix()
    {
        SqlTestHelpers.ExpectValid(
            "DELETE FROM TESTDB.PUBLIC.EMPLOYEES WHERE EMPLOYEE_ID = 300;", _schema);
    }

    [Fact]
    public void Delete_WithInWhere()
    {
        SqlTestHelpers.ExpectValid(
            "DELETE FROM TESTDB..ORDERS WHERE ORDER_ID IN (SELECT ORDER_ID FROM TESTDB..ORDER_ITEMS WHERE PRODUCT_ID = 1);", _schema);
    }

    [Fact]
    public void Delete_WithLike()
    {
        SqlTestHelpers.ExpectValid(
            "DELETE FROM TESTDB..EMPLOYEES WHERE FIRST_NAME LIKE 'J%';", _schema);
    }

    [Fact]
    public void Delete_WithBetween()
    {
        SqlTestHelpers.ExpectValid(
            "DELETE FROM TESTDB..EMPLOYEES WHERE EMPLOYEE_ID BETWEEN 1 AND 10;", _schema);
    }

    [Fact]
    public void Delete_WithIsNull()
    {
        SqlTestHelpers.ExpectValid(
            "DELETE FROM TESTDB..EMPLOYEES WHERE MANAGER_ID IS NULL;", _schema);
    }

    // ===== DELETE Syntax Errors =====

    [Fact]
    public void Delete_MissingFrom()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "DELETE TESTDB..EMPLOYEES WHERE EMPLOYEE_ID = 100;", _schema);
    }

    [Fact]
    public void Delete_MissingTable()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "DELETE FROM WHERE EMPLOYEE_ID = 100;", _schema);
    }

    [Fact]
    public void Delete_MissingWhereExpression()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "DELETE FROM TESTDB..EMPLOYEES WHERE;", _schema);
    }

    [Fact]
    public void Delete_InvalidTableName()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "DELETE FROM ;", _schema);
    }

    // ===== Additional: Parser direct tests =====

    private static Statement? ParseDml(string sql)
    {
        var tokens = NzLexer.Tokenize(sql).ToArray();
        return new NzSqlParser(tokens).Parse();
    }

    [Fact]
    public void ParseInsert_BasicStructure()
    {
        var result = ParseDml("INSERT INTO t VALUES (1)");
        Assert.NotNull(result);
        Assert.IsType<InsertStatement>(result);
        var insert = (InsertStatement)result;
        Assert.Equal("t", insert.Target.Name);
        Assert.NotNull(insert.Values);
        Assert.Single(insert.Values);
    }

    [Fact]
    public void ParseInsert_WithColumns()
    {
        var result = ParseDml("INSERT INTO t (a, b) VALUES (1, 2)");
        Assert.NotNull(result);
        var insert = (InsertStatement)result;
        Assert.NotNull(insert.Columns);
        Assert.Equal(2, insert.Columns.Count);
        Assert.Equal("a", insert.Columns[0]);
        Assert.Equal("b", insert.Columns[1]);
    }

    [Fact]
    public void ParseInsert_MultiRow()
    {
        var result = ParseDml("INSERT INTO t VALUES (1, 2), (3, 4)");
        Assert.NotNull(result);
        var insert = (InsertStatement)result;
        Assert.NotNull(insert.Values);
        Assert.Equal(2, insert.Values.Count);
        Assert.Equal(2, insert.Values[0].Count);
        Assert.Equal(2, insert.Values[1].Count);
    }

    [Fact]
    public void ParseInsert_SelectQuery()
    {
        var result = ParseDml("INSERT INTO t SELECT * FROM s");
        Assert.NotNull(result);
        var insert = (InsertStatement)result;
        Assert.NotNull(insert.SourceQuery);
        Assert.Null(insert.Values);
    }

    [Fact]
    public void ParseUpdate_BasicStructure()
    {
        var result = ParseDml("UPDATE t SET c = 1 WHERE d = 2");
        Assert.NotNull(result);
        Assert.IsType<UpdateStatement>(result);
        var update = (UpdateStatement)result;
        Assert.Equal("t", update.Target.Name);
        Assert.Single(update.SetItems);
        Assert.Equal("c", update.SetItems[0].Column.Name);
        Assert.NotNull(update.Where);
    }

    [Fact]
    public void ParseUpdate_MultiSet()
    {
        var result = ParseDml("UPDATE t SET a = 1, b = 2");
        Assert.NotNull(result);
        var update = (UpdateStatement)result;
        Assert.Equal(2, update.SetItems.Count);
        Assert.Equal("a", update.SetItems[0].Column.Name);
        Assert.Equal("b", update.SetItems[1].Column.Name);
    }

    [Fact]
    public void ParseDelete_BasicStructure()
    {
        var result = ParseDml("DELETE FROM t WHERE c = 1");
        Assert.NotNull(result);
        Assert.IsType<DeleteStatement>(result);
        var delete = (DeleteStatement)result;
        Assert.Equal("t", delete.Target.Name);
        Assert.NotNull(delete.Where);
    }

    [Fact]
    public void ParseDelete_NoWhere()
    {
        var result = ParseDml("DELETE FROM t");
        Assert.NotNull(result);
        var delete = (DeleteStatement)result;
        Assert.Equal("t", delete.Target.Name);
        Assert.Null(delete.Where);
    }

    [Fact]
    public void ParseInsert_QualifiedTableName()
    {
        var result = ParseDml("INSERT INTO s.t VALUES (1)");
        Assert.NotNull(result);
        var insert = (InsertStatement)result;
        Assert.Equal("t", insert.Target.Name);
        Assert.Equal("s", insert.Target.Schema);
    }

    [Fact]
    public void ParseInsert_DatabaseDotDotTable()
    {
        var result = ParseDml("INSERT INTO db..t VALUES (1)");
        Assert.NotNull(result);
        var insert = (InsertStatement)result;
        Assert.Equal("t", insert.Target.Name);
        Assert.Equal("db", insert.Target.Database);
        Assert.Null(insert.Target.Schema);
    }

    [Fact]
    public void ParseUpdate_NoWhere()
    {
        var result = ParseDml("UPDATE t SET c = 1");
        Assert.NotNull(result);
        var update = (UpdateStatement)result;
        Assert.Null(update.Where);
    }

    [Fact]
    public void Parse_ReturnsNullForUnknown()
    {
        var result = ParseDml("SOME_UNKNOWN_COMMAND foo bar");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_CreateTableNowSupported()
    {
        var result = ParseDml("CREATE TABLE t (a INT)");
        Assert.NotNull(result);
        Assert.IsType<JustyBase.NetezzaSqlParser.Ast.CreateTableStatement>(result);
    }
}
