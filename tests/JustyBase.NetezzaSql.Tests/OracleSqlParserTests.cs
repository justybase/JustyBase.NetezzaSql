using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using Superpower.Model;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Oracle dialect parser tests. Port of src/__tests__/sqlParser/oracleParser.test.ts
/// from the reference TypeScript project. Statements are parsed with OracleLexer +
/// OracleSqlParser; parse errors are collected across all statements.
/// </summary>
public class OracleSqlParserTests
{
    private static (IReadOnlyList<Statement> Statements, IReadOnlyList<ValidationError> Errors) ParseOracle(string sql)
    {
        var tokens = OracleLexer.Tokenize(sql).ToArray();
        var parser = new OracleSqlParser(tokens);
        var statements = new List<Statement>();
        var errors = new List<ValidationError>();
        while (parser.Position < tokens.Length)
        {
            var before = parser.Errors.Count;
            var stmt = parser.Parse();
            for (var i = before; i < parser.Errors.Count; i++)
                errors.Add(parser.Errors[i]);
            if (stmt is null) break;
            statements.Add(stmt);
        }
        return (statements, errors);
    }

    // ====== Reference corpus: qualified binds, package calls, db links ======

    [Fact]
    public void ParseOracle_QualifiedBindsPackageCallsDbLinksAndTimestampTimeZones_Valid()
    {
        var sql = """
            SELECT DBMS_METADATA.GET_DDL('TABLE', 'T') FROM HR.EMPLOYEES@PROD;
            CREATE TABLE t (event_at TIMESTAMP WITH TIME ZONE);
            CREATE OR REPLACE SYNONYM s FOR HR.EMPLOYEES;
            BEGIN IF :NEW.ID IS NULL THEN :NEW.ID := seq.NEXTVAL; END IF; END;
            BEGIN COMMIT; ROLLBACK; BEGIN NULL; END; END;
            """;
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.Equal(5, statements.Count);
    }

    [Fact]
    public void ParseOracle_QualifiedFunctionCall_ResolvesSchemaAndName()
    {
        var sql = "SELECT DBMS_METADATA.GET_DDL('TABLE', 'T') FROM dual;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        var call = Assert.IsType<FunctionCall>(select.SelectList![0].Expression);
        Assert.Equal("GET_DDL", call.Name);
        Assert.Equal("DBMS_METADATA", call.Schema);
    }

    [Fact]
    public void ParseOracle_DatabaseLink_Valid()
    {
        var sql = "SELECT * FROM HR.EMPLOYEES@PROD;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.NotNull(select.From);
    }

    [Fact]
    public void ParseOracle_DatabaseLinkWithAlias_Valid()
    {
        var sql = "SELECT e.employee_id FROM HR.EMPLOYEES@PROD e WHERE e.employee_id = 1;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.NotNull(select.From);
        Assert.Equal("e", select.From![0].Source.Alias, ignoreCase: true);
    }

    [Fact]
    public void ParseOracle_DatabaseLinkWithAsAlias_Valid()
    {
        var sql = "SELECT * FROM HR.EMPLOYEES@PROD AS emp;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Equal("emp", select.From![0].Source.Alias, ignoreCase: true);
    }

    [Fact]
    public void ParseOracle_AnonymousBlock_WithBindVariables_Valid()
    {
        var sql = "BEGIN IF :NEW.ID IS NULL THEN :NEW.ID := seq.NEXTVAL; END IF; END;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<OracleAnonymousBlockStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseOracle_AnonymousBlock_NestedBegins_Valid()
    {
        var sql = "BEGIN COMMIT; ROLLBACK; BEGIN NULL; END; END;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<OracleAnonymousBlockStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseOracle_AnonymousBlock_WithDeclareAndException_Valid()
    {
        var sql = """
            DECLARE
                v_count NUMBER;
            BEGIN
                SELECT COUNT(*) INTO v_count FROM employees;
            EXCEPTION
                WHEN OTHERS THEN NULL;
            END;
            """;
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<OracleAnonymousBlockStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseOracle_AnonymousBlock_NestedControlBlocks_Valid()
    {
        var sql = """
            BEGIN
                IF :NEW.ID IS NULL THEN
                    BEGIN
                        NULL;
                    END;
                END IF;
            END;
            """;
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<OracleAnonymousBlockStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseOracle_AnonymousBlock_ForAndWhileLoops_Valid()
    {
        var sql = """
            BEGIN
                FOR i IN 1..10 LOOP
                    NULL;
                END LOOP;
                WHILE TRUE LOOP
                    EXIT;
                END LOOP;
            END;
            """;
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<OracleAnonymousBlockStatement>(Assert.Single(statements));
    }

    // ====== Reference corpus: hierarchical queries ======

    [Fact]
    public void ParseOracle_HierarchicalQuery_Valid()
    {
        var sql = "SELECT employee_id, manager_id FROM employees START WITH employee_id = 100 CONNECT BY PRIOR employee_id = manager_id ORDER SIBLINGS BY employee_id;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.NotNull(select.From);
        Assert.NotNull(select.OrderBy);
        Assert.Single(select.OrderBy!);
    }

    // ====== Reference corpus: PIVOT ======

    [Fact]
    public void ParseOracle_PivotClauseWithAliases_Valid()
    {
        var sql = "SELECT * FROM (SELECT department_id, job_id, salary FROM employees) PIVOT (SUM(salary) FOR job_id IN ('IT' AS it, 'SALES' AS sales));";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<SelectStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseOracle_FetchFirstRowsOnly_Valid()
    {
        var sql = "SELECT * FROM employees FETCH FIRST 10 ROWS ONLY;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<SelectStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseOracle_FetchNextPercentWithTies_Valid()
    {
        var sql = "SELECT * FROM employees ORDER BY salary DESC FETCH NEXT 20 PERCENT ROWS WITH TIES;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<SelectStatement>(Assert.Single(statements));
    }

    // ====== Reference corpus: DML RETURNING INTO ======

    [Fact]
    public void ParseOracle_InsertReturningInto_Valid()
    {
        var sql = "INSERT INTO employees (employee_id) VALUES (1) RETURNING employee_id INTO :id;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        var insert = Assert.IsType<InsertStatement>(Assert.Single(statements));
        Assert.NotNull(insert.Returning);
        Assert.Equal(["employee_id"], insert.Returning.Columns);
        Assert.Equal([":id"], insert.Returning.IntoVariables);
    }

    [Fact]
    public void ParseOracle_UpdateReturningInto_Valid()
    {
        var sql = "UPDATE employees SET salary = 1 WHERE employee_id = 1 RETURNING salary INTO :s;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        var update = Assert.IsType<UpdateStatement>(Assert.Single(statements));
        Assert.NotNull(update.Returning);
    }

    [Fact]
    public void ParseOracle_DeleteReturningInto_Valid()
    {
        var sql = "DELETE FROM employees WHERE employee_id = 1 RETURNING employee_id INTO :id;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        var delete = Assert.IsType<DeleteStatement>(Assert.Single(statements));
        Assert.NotNull(delete.Returning);
    }

    [Fact]
    public void ParseOracle_ReturningInto_MultipleColumnsAndVariables_Valid()
    {
        var sql = "INSERT INTO employees (employee_id) VALUES (1) RETURNING employee_id, salary INTO :id, :s;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        var insert = Assert.IsType<InsertStatement>(Assert.Single(statements));
        Assert.NotNull(insert.Returning);
        Assert.Equal(["employee_id", "salary"], insert.Returning.Columns);
        Assert.Equal([":id", ":s"], insert.Returning.IntoVariables);
    }

    // ====== Reference corpus: quoted identifiers, CTE, q-strings ======

    [Fact]
    public void ParseOracle_QuotedIdentifiersAndCte_Valid()
    {
        var sql = "WITH \"X\" AS (SELECT 1 FROM dual) SELECT * FROM \"X\";";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<SelectStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseOracle_AlternativeQuotedString_Valid()
    {
        var sql = "SELECT q'[Oracle ''quoted'' text]' FROM dual;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<SelectStatement>(Assert.Single(statements));
    }

    // ====== Program units ======

    [Theory]
    [InlineData("CREATE OR REPLACE PROCEDURE sp AS BEGIN NULL; END;", OracleProgramUnitKind.Procedure)]
    [InlineData("CREATE OR REPLACE FUNCTION fn RETURN INTEGER AS BEGIN NULL; END;", OracleProgramUnitKind.Function)]
    [InlineData("CREATE OR REPLACE FUNCTION fn(x INTEGER) RETURN INTEGER IS BEGIN NULL; END;", OracleProgramUnitKind.Function)]
    [InlineData("CREATE OR REPLACE FUNCTION HR.FN RETURN INTEGER AS BEGIN NULL; END;", OracleProgramUnitKind.Function)]
    [InlineData("CREATE OR REPLACE PACKAGE pkg IS BEGIN NULL; END;", OracleProgramUnitKind.Package)]
    [InlineData("CREATE OR REPLACE PACKAGE BODY pkg AS BEGIN NULL; END;", OracleProgramUnitKind.PackageBody)]
    [InlineData("CREATE OR REPLACE TRIGGER trg BEFORE INSERT ON t BEGIN NULL; END;", OracleProgramUnitKind.Trigger)]
    public void ParseOracle_ProgramUnits_Valid(string sql, OracleProgramUnitKind expectedKind)
    {
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        var unit = Assert.IsType<OracleProgramUnitStatement>(Assert.Single(statements));
        Assert.Equal(expectedKind, unit.Kind);
    }

    [Theory]
    [InlineData("CREATE OR REPLACE FUNCTION f(p IN NUMBER, q IN OUT VARCHAR2 DEFAULT 'x') RETURN NUMBER IS v NUMBER := p; BEGIN RETURN v; END f;", OracleProgramUnitKind.Function)]
    [InlineData("CREATE OR REPLACE FUNCTION HR.CALC_TOTAL(P_AMOUNT IN NUMBER) RETURN NUMBER IS V_TOTAL NUMBER; BEGIN V_TOTAL := P_AMOUNT; RETURN V_TOTAL; END;", OracleProgramUnitKind.Function)]
    [InlineData("CREATE OR REPLACE PACKAGE pkg AS FUNCTION value RETURN NUMBER; END pkg;", OracleProgramUnitKind.Package)]
    [InlineData("CREATE OR REPLACE PACKAGE BODY pkg AS FUNCTION value RETURN NUMBER IS BEGIN RETURN 1; END value; END pkg;", OracleProgramUnitKind.PackageBody)]
    public void ParseOracle_ProgramUnitsWithRoutineMembers_Valid(string sql, OracleProgramUnitKind expectedKind)
    {
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        var unit = Assert.IsType<OracleProgramUnitStatement>(Assert.Single(statements));
        Assert.Equal(expectedKind, unit.Kind);
    }

    [Fact]
    public void ParseOracle_TriggerWithBeforeAndAfter_Valid()
    {
        var sql = "CREATE TRIGGER trg AFTER DELETE ON t FOR EACH ROW BEGIN NULL; END;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<OracleProgramUnitStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseOracle_UnitNameAfterEnd_Valid()
    {
        var sql = "CREATE OR REPLACE PACKAGE BODY pkg AS BEGIN NULL; END pkg;";
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.IsType<OracleProgramUnitStatement>(Assert.Single(statements));
    }

    [Theory]
    [InlineData("BEGIN NULL;")]
    [InlineData("CREATE OR REPLACE PACKAGE pkg IS BEGIN NULL;")]
    [InlineData("CREATE OR REPLACE PACKAGE BODY pkg AS BEGIN NULL;")]
    [InlineData("CREATE OR REPLACE TRIGGER trg BEFORE INSERT ON t BEGIN NULL;")]
    public void ParseOracle_UnterminatedBlocks_Errors(string sql)
    {
        var (_, errors) = ParseOracle(sql);
        Assert.NotEmpty(errors);
    }

    // ====== Netezza-only rejections ======

    [Theory]
    [InlineData("SELECT * FROM dual LIMIT 1;")]
    [InlineData("SELECT * FROM DB..TABLE;")]
    [InlineData("SELECT * FROM dual DISTRIBUTE ON (1);")]
    public void ParseOracle_RejectsNetezzaOnlySelectSyntax(string sql)
    {
        var (_, errors) = ParseOracle(sql);
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("CREATE EXTERNAL TABLE ext (id INTEGER) USING (DATAOBJECT '/tmp/x');")]
    [InlineData("CREATE TABLE t (id INTEGER) DISTRIBUTE ON (id);")]
    [InlineData("CREATE TABLE t (id INTEGER) ORGANIZE ON (id);")]
    public void ParseOracle_RejectsNetezzaOnlyCreateSyntax(string sql)
    {
        var (_, errors) = ParseOracle(sql);
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("GROOM TABLE t;")]
    [InlineData("GENERATE STATISTICS ON t;")]
    [InlineData("GENERATE EXPRESS STATISTICS ON t;")]
    public void ParseOracle_RejectsNetezzaOnlyUtilitySyntax(string sql)
    {
        var (_, errors) = ParseOracle(sql);
        Assert.NotEmpty(errors);
    }

    // ====== Oracle statements stay valid under the shared lexer ======

    [Fact]
    public void ParseOracle_SharedStatements_Valid()
    {
        var sql = """
            CREATE TABLE t (event_at TIMESTAMP WITH TIME ZONE);
            CREATE OR REPLACE SYNONYM s FOR HR.EMPLOYEES;
            """;
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.Equal(2, statements.Count);
    }

    [Fact]
    public void ParseOracle_SingleStatementAfterErrorRecovery_ParsesNextStatement()
    {
        var sql = """
            SELECT * FROM dual LIMIT 1;
            SELECT 1 FROM dual;
            """;
        var (statements, errors) = ParseOracle(sql);
        Assert.NotEmpty(errors);
        Assert.NotEmpty(statements);
        Assert.IsType<SelectStatement>(statements[^1]);
    }
}
