using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorRemainingTests
{
    [Fact]
    public void Validate_SetOps_ParenthesizedUnionAll()
    {
        ExpectValid(
            "(SELECT EMPLOYEE_ID FROM EMPLOYEES) UNION ALL (SELECT EMPLOYEE_ID FROM DEPARTMENTS);");
    }


    [Fact]
    public void Validate_SetOps_ParenthesizedIntersect()
    {
        ExpectValid(
            "(SELECT EMPLOYEE_ID FROM EMPLOYEES) INTERSECT (SELECT DEPARTMENT_ID FROM DEPARTMENTS);");
    }


    [Fact]
    public void Validate_SetOps_ParenthesizedExcept()
    {
        ExpectValid(
            "(SELECT EMPLOYEE_ID FROM EMPLOYEES) EXCEPT (SELECT DEPARTMENT_ID FROM DEPARTMENTS);");
    }


    [Fact]
    public void Validate_SetOps_ThreeWayUnion()
    {
        ExpectValid("(SELECT 1) UNION (SELECT 2) UNION (SELECT 3);");
    }


    [Fact]
    public void Validate_SetOps_ExceptWithUnionOnRight()
    {
        ExpectValid(
            """
            SELECT * FROM TESTDB..EMPLOYEES
            EXCEPT
            (
            SELECT * FROM TESTDB..EMPLOYEES
            UNION
            SELECT * FROM TESTDB..EMPLOYEES
            );
            """,
            _schema);
    }

    // ========================================================================
    // Quantified comparisons — ANY / SOME / ALL
    // ========================================================================

    [Fact]
    public void Validate_Quantified_GtAny()
    {
        ExpectValid(
            "SELECT * FROM EMPLOYEES WHERE SALARY > ANY (SELECT SALARY FROM DEPARTMENTS);");
    }


    [Fact]
    public void Validate_Advanced_SimpleUnionAll()
    {
        ExpectValid("SELECT 1 AS A UNION ALL SELECT 2 AS A;");
    }


    [Fact]
    public void Validate_Advanced_SimpleIntersect()
    {
        ExpectValid("SELECT 1 AS A INTERSECT SELECT 1 AS A;");
    }


    [Fact]
    public void Validate_Advanced_SimpleExcept()
    {
        ExpectValid("SELECT 1 AS A EXCEPT SELECT 2 AS A;");
    }


    [Fact]
    public void Validate_Advanced_ChainedUnionAll()
    {
        ExpectValid(
            "SELECT 1 AS A UNION ALL SELECT 2 AS A UNION ALL SELECT 3 AS A;");
    }


    [Fact]
    public void Validate_NodeParity_ParenthesizedUnionIntersect()
    {
        ExpectValid(
            "(SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES) INTERSECT (SELECT DEPARTMENT_ID FROM TESTDB..DEPARTMENTS);",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_NestedParenthesizedUnion()
    {
        // Three-way parenthesized: (SELECT 1) UNION (SELECT 2) UNION (SELECT 3)
        ExpectValid(
            "(SELECT 1) UNION (SELECT 2) UNION (SELECT 3);");
    }
}
