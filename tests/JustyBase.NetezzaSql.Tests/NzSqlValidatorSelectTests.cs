using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.Tests.NetezzaSqlParser;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlValidatorSelectTests
{
    private readonly ISchemaProvider _schema;

    public NzSqlValidatorSelectTests()
    {
        _schema = SqlTestHelpers.CreateStandardMockSchema();
    }

    // ========================================================================
    // SELECT — valid syntax
    // ========================================================================

    [Fact]
    public void Validate_Select_LiteralOnly()
    {
        SqlTestHelpers.ExpectValid("SELECT 1;");
    }

    [Fact]
    public void Validate_Select_MultipleLiteralsAndAliases()
    {
        SqlTestHelpers.ExpectValid("SELECT 1 AS A, 'hello' AS B, 3.14 AS C;");
    }

    [Fact]
    public void Validate_Select_Distinct()
    {
        SqlTestHelpers.ExpectValid("SELECT DISTINCT DEPARTMENT_ID FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Select_All()
    {
        SqlTestHelpers.ExpectValid("SELECT ALL DEPARTMENT_ID FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Select_ArithmeticExpressions()
    {
        SqlTestHelpers.ExpectValid("SELECT E.SALARY * 1.1 AS RAISED_SALARY FROM TESTDB..EMPLOYEES E;", _schema);
    }

    [Fact]
    public void Validate_Select_StringConcatenation()
    {
        SqlTestHelpers.ExpectValid("SELECT E.FIRST_NAME || ' ' || E.LAST_NAME AS FULL_NAME FROM TESTDB..EMPLOYEES E;", _schema);
    }

    [Fact]
    public void Validate_Select_UnaryMinus()
    {
        SqlTestHelpers.ExpectValid("SELECT -1 AS NEG;");
    }

    [Fact]
    public void Validate_Select_NextValueForSequence()
    {
        SqlTestHelpers.ExpectValid("SELECT NEXT VALUE FOR sequence1, 1;");
        SqlTestHelpers.ExpectValid("SELECT NEXT VALUE FOR TESTDB..sequence1, 1;", _schema);
    }

    [Fact]
    public void Validate_Select_NestedParenthesizedExpressions()
    {
        SqlTestHelpers.ExpectValid("SELECT (1 + 2) * (3 - 4) AS CALC;");
    }

    [Fact]
    public void Validate_Select_IsNull()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE MANAGER_ID IS NULL;", _schema);
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE MANAGER_ID IS NOT NULL;", _schema);
    }

    [Fact]
    public void Validate_Select_Between()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY BETWEEN 1000 AND 5000;", _schema);
    }

    [Fact]
    public void Validate_Select_NotBetween()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE NOT SALARY BETWEEN 1000 AND 5000;", _schema);
    }

    [Fact]
    public void Validate_Select_InList()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE DEPARTMENT_ID IN (1, 2, 3);", _schema);
    }

    [Fact]
    public void Validate_Select_InSubquery()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES E WHERE E.DEPARTMENT_ID IN (SELECT D.DEPARTMENT_ID FROM TESTDB..DEPARTMENTS D);", _schema);
    }

    [Fact]
    public void Validate_Select_Like()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE FIRST_NAME LIKE 'J%';", _schema);
    }

    [Fact]
    public void Validate_Select_LikeEscape()
    {
        SqlTestHelpers.ExpectValid("SELECT 'TXT' LIKE 'A' ESCAPE '\\';");
    }

    [Fact]
    public void Validate_Select_TableWithFinal()
    {
        SqlTestHelpers.ExpectValid("SELECT F.* FROM TABLE WITH FINAL (TESTDB.PUBLIC.FLUID_FN()) F;", _schema);
    }

    [Fact]
    public void Validate_Select_GroupByAndHaving()
    {
        SqlTestHelpers.ExpectValid("SELECT DEPARTMENT_ID, COUNT(*) AS CNT FROM TESTDB..EMPLOYEES GROUP BY DEPARTMENT_ID HAVING COUNT(*) > 5;", _schema);
    }

    [Fact]
    public void Validate_Select_OrderByAscDesc()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES ORDER BY SALARY DESC, FIRST_NAME ASC;", _schema);
    }

    [Fact]
    public void Validate_Select_LimitAndOffset()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES ORDER BY EMPLOYEE_ID LIMIT 10 OFFSET 20;", _schema);
    }

    [Fact]
    public void Validate_Select_CommaJoin()
    {
        SqlTestHelpers.ExpectValid("SELECT E.FIRST_NAME, D.DEPARTMENT_NAME FROM TESTDB..EMPLOYEES E, TESTDB..DEPARTMENTS D WHERE E.DEPARTMENT_ID = D.DEPARTMENT_ID;", _schema);
    }

    [Fact]
    public void Validate_Select_CrossJoin()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES CROSS JOIN TESTDB..DEPARTMENTS;", _schema);
    }

    [Fact]
    public void Validate_Select_FullOuterJoin()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES E FULL OUTER JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;", _schema);
    }

    [Fact]
    public void Validate_Select_RightJoin()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES E RIGHT JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;", _schema);
    }

    [Fact]
    public void Validate_Select_RightOuterJoin()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES E RIGHT OUTER JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;", _schema);
    }

    [Fact]
    public void Validate_Select_FullJoin()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES E FULL JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;", _schema);
    }

    [Fact]
    public void Validate_Select_MultipleJoinsChained()
    {
        SqlTestHelpers.ExpectValid(
            """
            SELECT OI.ITEM_ID, P.PRODUCT_NAME, O.ORDER_DATE
            FROM TESTDB..ORDER_ITEMS OI
            JOIN TESTDB..ORDERS O ON OI.ORDER_ID = O.ORDER_ID
            JOIN TESTDB..PRODUCTS P ON OI.PRODUCT_ID = P.PRODUCT_ID;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Select_AliasedSubqueryInFrom()
    {
        SqlTestHelpers.ExpectValid("SELECT S.TOTAL FROM (SELECT SUM(SALARY) AS TOTAL FROM TESTDB..EMPLOYEES) S;", _schema);
    }

    [Fact]
    public void Validate_Select_NestedSubqueryInWhere()
    {
        SqlTestHelpers.ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY > (SELECT AVG(SALARY) FROM TESTDB..EMPLOYEES);", _schema);
    }

    [Fact]
    public void Validate_Select_ComplexWithComments()
    {
        SqlTestHelpers.ExpectValid(
            """
            -- Get top employees
            SELECT /* columns */ E.FIRST_NAME, E.SALARY
            FROM TESTDB..EMPLOYEES E
            WHERE E.SALARY > 1000 -- filter low salaries
            ORDER BY E.SALARY DESC
            LIMIT 10;
            """,
            _schema);
    }

    // ========================================================================
    // SELECT — syntax errors
    // ========================================================================

    [Fact]
    public void Validate_Select_SyntaxError_MissingFromKeyword()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT EMPLOYEE_ID TESTDB..EMPLOYEES;");
    }

    [Fact]
    public void Validate_Select_SyntaxError_DoubleComma()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT 1, , 2;");
    }

    [Fact]
    public void Validate_Select_SyntaxError_TrailingComma()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT 1, 2, FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_EmptySelectList()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_MissingWhereCondition()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES WHERE;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_MissingRightOperand()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY = ;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_UnclosedParenthesis()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT (1 + 2 AS X;");
    }

    [Fact]
    public void Validate_Select_SyntaxError_ExtraClosingParenthesis()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT 1 + 2) AS X;");
    }

    [Fact]
    public void Validate_Select_SyntaxError_MissingOnInJoin()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES E JOIN TESTDB..DEPARTMENTS D E.DEPARTMENT_ID = D.DEPARTMENT_ID;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_TypoSelcet()
    {
        SqlTestHelpers.ExpectSyntaxError("SELCET 1;");
    }

    [Fact]
    public void Validate_Select_SyntaxError_TypoLefftJoin()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES E LEFFT JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_TypoForm()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT 1 FORM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_TypoWher()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES WHER SALARY > 100;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_MissingGroupBy()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT DEPARTMENT_ID, COUNT(*) FROM TESTDB..EMPLOYEES GROUP BY;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_MissingHaving()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT DEPARTMENT_ID FROM TESTDB..EMPLOYEES GROUP BY DEPARTMENT_ID HAVING;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_MissingOrderBy()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES ORDER BY;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_MissingLimit()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES LIMIT;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_EmptyInList()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES WHERE DEPARTMENT_ID IN ();", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_MissingAndInBetween()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY BETWEEN 1000 5000;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_DoubleOperators()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY = = 100;", _schema);
    }

    [Fact]
    public void Validate_Select_SyntaxError_MissingThenInCase()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT CASE WHEN 1 = 1 'yes' END;");
    }

    [Fact]
    public void Validate_Select_SyntaxError_MissingEndInCase()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT CASE WHEN 1 = 1 THEN 'yes';");
    }

    [Fact]
    public void Validate_Select_SyntaxError_MissingWhenInCase()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT CASE 1 = 1 THEN 'yes' END;");
    }

    // ========================================================================
    // CASE expressions — valid
    // ========================================================================

    [Fact]
    public void Validate_Case_Searched()
    {
        SqlTestHelpers.ExpectValid("SELECT CASE WHEN 1 = 1 THEN 'yes' ELSE 'no' END AS RESULT;");
    }

    [Fact]
    public void Validate_Case_ValueBased()
    {
        SqlTestHelpers.ExpectValid("SELECT CASE STATUS WHEN 'A' THEN 'Active' WHEN 'I' THEN 'Inactive' ELSE 'Unknown' END AS LABEL FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Case_WithoutElse()
    {
        SqlTestHelpers.ExpectValid("SELECT CASE WHEN SALARY > 5000 THEN 'High' END AS TIER FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Case_Nested()
    {
        SqlTestHelpers.ExpectValid(
            """
            SELECT CASE
                WHEN SALARY > 5000 THEN CASE WHEN DEPARTMENT_ID = 1 THEN 'High-Sales' ELSE 'High-Other' END
                ELSE 'Low'
            END AS TIER
            FROM TESTDB..EMPLOYEES;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Case_MultipleWhenClauses()
    {
        SqlTestHelpers.ExpectValid(
            """
            SELECT CASE
                WHEN SALARY > 10000 THEN 'Executive'
                WHEN SALARY > 5000 THEN 'Senior'
                WHEN SALARY > 2000 THEN 'Mid'
                ELSE 'Junior'
            END AS LEVEL
            FROM TESTDB..EMPLOYEES;
            """,
            _schema);
    }
}
