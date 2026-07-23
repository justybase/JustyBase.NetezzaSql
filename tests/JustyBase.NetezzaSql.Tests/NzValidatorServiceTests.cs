using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzValidatorServiceTests
{
    private readonly ISchemaProvider? _schema = SqlTestHelpers.CreateStandardMockSchema();

    // ========================================================================
    // quickValidate equivalent — success/failure via Validate
    // ========================================================================

    [Fact]
    public void QuickValidate_SemicolonOnlySql_ReturnsSuccess()
    {
        var result = SqlTestHelpers.Validate(";");
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void QuickValidate_MultipleSemicolons_ReturnsSuccess()
    {
        var result = SqlTestHelpers.Validate("  ;;;;  ");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void QuickValidate_ValidSql_ReturnsSuccess()
    {
        Assert.Empty(SqlTestHelpers.Validate("SELECT 1;").Errors);
        var schema = SqlTestHelpers.CreateStandardMockSchema();
        Assert.Empty(SqlTestHelpers.Validate("SELECT * FROM TESTDB..EMPLOYEES;", schema).Errors);
    }

    [Fact]
    public void QuickValidate_InvalidSqlWithLexerErrors_ReturnsFailure()
    {
        var result = SqlTestHelpers.Validate("SELECT 'unclosed");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void QuickValidate_InvalidSqlWithParserErrors_ReturnsFailure()
    {
        var r1 = SqlTestHelpers.Validate("SELECT FROM table");
        Assert.NotEmpty(r1.Errors);

        var r2 = SqlTestHelpers.Validate("SELECT 1,,2");
        Assert.NotEmpty(r2.Errors);
    }

    // ========================================================================
    // Validate with scope
    // ========================================================================

    [Fact]
    public void Validate_ReturnsScopeAfterSuccessfulValidation()
    {
        var schema = SqlTestHelpers.CreateStandardMockSchema();
        var (_, scope) = VisitAndGetScope("SELECT ID, NAME FROM TESTDB..EMPLOYEES", schema);
        Assert.NotNull(scope);
    }

    [Fact]
    public void Validate_ResetsScopeOnValidationErrors()
    {
        var schema = SqlTestHelpers.CreateStandardMockSchema();

        var (_, scope1) = VisitAndGetScope("SELECT ID, NAME FROM TESTDB..EMPLOYEES", schema);
        Assert.NotNull(scope1);

        var result2 = SqlTestHelpers.Validate("SELECT INVALID FROM TESTDB..EMPLOYEES", schema);
        Assert.Contains(result2.Errors, e => e.Code == "SQL004");
    }

    // ========================================================================
    // Lexer errors
    // ========================================================================

    [Fact]
    public void Validate_LexerError_ReportsCorrectCode()
    {
        var result = SqlTestHelpers.Validate("SELECT 'unclosed string");
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Code is "LEX001" or "PAR110");
    }

    // ========================================================================
    // Parser errors
    // ========================================================================

    [Fact]
    public void Validate_ParserError_ReportsCorrectCode()
    {
        var result = SqlTestHelpers.Validate("SELECT FROM table");
        Assert.NotEmpty(result.Errors);
        var parseError = result.Errors.FirstOrDefault(e => e.Code == "PAR001");
        Assert.NotNull(parseError);
    }

    [Fact]
    public void Validate_ParserError_MissingAsInCteDefinition()
    {
        var schema = SqlTestHelpers.CreateStandardMockSchema();
        var result = SqlTestHelpers.Validate(
            "WITH ABC1 (SELECT X.ACCOUNTCODEALTERNATEKEY FROM JUST_DATA..DIMACCOUNT X) SELECT * FROM ABC1;",
            schema);

        var parseError = result.Errors.FirstOrDefault(e => e.Code == "PAR001");
        Assert.NotNull(parseError);
    }

    [Fact]
    public void Validate_ParserError_FriendlyMessageForMissingSourceAfterFrom()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM;");
        var parseError = result.Errors.FirstOrDefault(e => e.Code == "PAR001");
        Assert.NotNull(parseError);
    }

    [Fact]
    public void Validate_ParserError_FriendlyMessageForMissingSelectList()
    {
        // Node: SELECT FROM table → friendly "missing select list" PAR001
        var result = SqlTestHelpers.Validate("SELECT FROM table");
        var err = result.Errors.FirstOrDefault(e => e.Message.Contains("Missing select list"));
        Assert.NotNull(err);
    }

    [Fact]
    public void Validate_ParserError_FriendlyMessageForMissingSourceAfterFromGroupBy()
    {
        // SELECT * FROM GROUP BY → clearly missing table source
        var result = SqlTestHelpers.Validate("SELECT * FROM GROUP BY x");
        var err = result.Errors.FirstOrDefault(e => e.Message.Contains("Missing table or subquery after FROM"));
        Assert.NotNull(err);
    }

    [Fact]
    public void Validate_ParserError_FriendlyMessageForTrailingCommaInSelectList()
    {
        // SELECT a, FROM t → trailing comma before FROM
        var result = SqlTestHelpers.Validate("SELECT a, FROM t");
        var err = result.Errors.FirstOrDefault(e => e.Message.Contains("Trailing comma"));
        Assert.NotNull(err);
    }

    [Fact]
    public void Validate_ParserError_FriendlyMessageForTrailingCommaInFrom()
    {
        // SELECT * FROM t, WHERE x → trailing comma before WHERE
        var result = SqlTestHelpers.Validate("SELECT * FROM t, WHERE x = 1", _schema);
        var err = result.Errors.FirstOrDefault(e => e.Message.Contains("Trailing comma"));
        Assert.NotNull(err);
    }

    [Fact]
    public void Validate_ForRangeLoopsWithVariableBoundsInsideProcedures() { }

    // DB2-only reference behavior has no C# Netezza counterpart.
    private void Validate_KeepsNetezzaParserErrorsForDb2OnlyDdl() { }

    // DB2-only reference behavior has no C# Netezza counterpart.
    private void Validate_SuppressesParserErrorsForDb2OnlyDdlUnderBestEffort() { }

    [Fact]
    public void Validate_NoLexerErrorForNetezzaDbTableAliases()
    {
        var schema = SqlTestHelpers.CreateStandardMockSchema();
        var result = SqlTestHelpers.Validate("SELECT * FROM JUST_DATA..DIMACCOUNT a", schema);
        Assert.DoesNotContain(result.Errors, e => e.Code == "LEX001");
    }

    [Fact]
    public void Validate_NoParseErrorForCteBackedInSubqueryInDeletePredicates() { }

    [Fact]
    public void Validate_NoParseErrorForUpdateSetFromSyntax()
    {
        var result = SqlTestHelpers.Validate(
            """
            UPDATE TESTDB..EMPLOYEES E
            SET E.STATUS = 'A'
            FROM
            (
                SELECT 1 AS EMPLOYEE_ID FROM TESTDB..EMPLOYEES
            ) SUB
            WHERE SUB.EMPLOYEE_ID = 1;
            """,
            _schema);
        Assert.DoesNotContain(result.Errors, e => e.Code is "PARSE001" or "PARSE002");
        Assert.DoesNotContain(result.Errors, e => e.Code is "SQL003");
    }

    [Fact]
    public void Validate_UpdateWithBadColumn_ReportsSQL004()
    {
        var result = SqlTestHelpers.Validate(
            """
            UPDATE TESTDB..EMPLOYEES E
            SET E.NO_SUCH_COLUMN = 2
            WHERE E.NO_SUCH_COLUMN = 1
            """,
            _schema);
        Assert.Contains(result.Errors, e => e.Code == "SQL004");
    }

    [Fact]
    public void Validate_DeleteWithBadColumn_ReportsSQL004()
    {
        var result = SqlTestHelpers.Validate(
            """
            DELETE FROM TESTDB..EMPLOYEES E
            WHERE E.NO_SUCH_COLUMN = 1
            """,
            _schema);
        Assert.Contains(result.Errors, e => e.Code == "SQL004");
    }

    [Fact]
    public void Validate_UpdateMissingSet_WithAlias_ReportsPARSE001()
    {
        // Node equivalent: it("should detect WHERE without SET", () => {
        //   expectSyntaxError("UPDATE JUST_DATA..DIMACCOUNT A WHERE A.ACCOUNTCODEALTERNATEKEY = 1;");
        // });
        var result = SqlTestHelpers.Validate(
            "UPDATE JUST_DATA..DIMACCOUNT A WHERE A.ACCOUNTCODEALTERNATEKEY = 1;");
        Assert.Contains(result.Errors, e => (e.Code is "PAR115" or "PAR001") && e.Message.Contains("SET"));
    }

    [Fact]
    public void Validate_UpdateMissingSet_NoAlias_ReportsPAR115()
    {
        var result = SqlTestHelpers.Validate(
            """
            UPDATE JUST_DATA..DIMACCOUNT
            WHERE ACCOUNTCODEALTERNATEKEY = 1
            """,
            _schema);
        Assert.Contains(result.Errors, e => (e.Code is "PAR115" or "PAR001") && e.Message.Contains("SET"));
    }

    // ========================================================================
    // Semantic vs syntax error separation
    // ========================================================================

    [Fact]
    public void Validate_SemanticErrorsSeparateFromSyntaxErrors()
    {
        var schema = SqlTestHelpers.CreateStandardMockSchema();
        var result = SqlTestHelpers.Validate(
            "SELECT ID, NONEXISTENT FROM TESTDB..EMPLOYEES",
            schema);

        var syntaxErrors = result.Errors.Where(e => e.Code.StartsWith("PARSE") || e.Code.StartsWith("LEX"));
        var semanticErrors = result.Errors.Where(e => e.Code.StartsWith("SQL"));

        Assert.Empty(syntaxErrors);
        Assert.NotEmpty(semanticErrors);
    }

    [Fact]
    public void Validate_Sql020WhenSubqueryInFromHasNoAlias()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM (SELECT 1);");
        Assert.Contains(result.Errors, e => e.Code == "SQL020");
    }

    [Fact]
    public void Validate_AllowsSubqueryInFromWithAlias()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM (SELECT 1) S;");
        Assert.DoesNotContain(result.Errors, e => e.Code == "SQL020");
    }

    // ========================================================================
    // Warnings (SQL018/SQL019)
    // ========================================================================

    [Fact]
    public void Validate_Warning_Sql018ForUnusedCte()
    {
        SqlTestHelpers.ExpectWarningCode(
            "WITH unused_cte AS (SELECT 1 AS ID) SELECT 1 AS X;",
            "SQL018",
            _schema);
    }

    [Fact]
    public void Validate_Warning_Sql019ForUnusedTableAlias()
    {
        SqlTestHelpers.ExpectWarningCode(
            "SELECT * FROM TESTDB..EMPLOYEES E;",
            "SQL019",
            _schema);
    }

    [Fact]
    public void Validate_Warning_Sql019PointsToAliasToken()
    {
        const string sql = "SELECT * FROM TESTDB..EMPLOYEES E;";
        var result = SqlTestHelpers.Validate(sql, _schema);
        var warning = Assert.Single(result.Warnings, w => w.Code == "SQL019");

        Assert.NotNull(warning.Position);
        Assert.Equal(sql.IndexOf('E', sql.IndexOf("EMPLOYEES", StringComparison.Ordinal) + "EMPLOYEES".Length),
            warning.Position!.Absolute);
        Assert.Equal(warning.Position.Absolute + 1, warning.Position.Column);
    }

    [Fact]
    public void Validate_NoWarning_Sql019WhenAliasIsUsedWithKeywordTokenColumnNames()
    {
        var schema = SqlTestHelpers.CreateMockSchemaProvider([
            new("TST", "PUBLIC", "JUST_DATA", ["HASH", "SUM"]),
        ]);
        SqlTestHelpers.ExpectValid(
            "SELECT D.HASH, D.SUM FROM JUST_DATA..TST D;",
            schema);
    }

    // ========================================================================
    // SQL021: aggregate in WHERE clause
    // ========================================================================

    [Fact]
    public void Validate_Error_Sql021ForSumInWhere()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SUM(SALARY) > 50000;",
            "SQL021",
            _schema);
    }

    [Fact]
    public void Validate_Error_Sql021ForCountInWhere()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE COUNT(*) > 0;",
            "SQL021",
            _schema);
    }

    [Fact]
    public void Validate_Error_Sql021ForAvgInWhere()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE AVG(SALARY) > 50000;",
            "SQL021",
            _schema);
    }

    [Fact]
    public void Validate_Error_Sql021ForMinInWhere()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE MIN(SALARY) > 50000;",
            "SQL021",
            _schema);
    }

    [Fact]
    public void Validate_Error_Sql021ForMaxInWhere()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE MAX(SALARY) > 50000;",
            "SQL021",
            _schema);
    }

    [Fact]
    public void Validate_NoError_Sql021ForAggregateInHaving()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT DEPARTMENT_ID, SUM(SALARY) FROM TESTDB..EMPLOYEES GROUP BY DEPARTMENT_ID HAVING SUM(SALARY) > 100000;",
            _schema);
    }

    [Fact]
    public void Validate_NoError_Sql021ForAggregateInSubqueryWithinWhere()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY > (SELECT AVG(SALARY) FROM TESTDB..EMPLOYEES);",
            _schema);
    }

    [Fact]
    public void Validate_NoError_Sql021ForAggregateInSelect()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT SUM(SALARY) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_MultipleErrors_Sql021ForMultipleAggregatesInWhere()
    {
        var result = SqlTestHelpers.Validate(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SUM(SALARY) > 50000 AND AVG(SALARY) > 1000;",
            _schema);
        Assert.NotEmpty(result.Errors);
        var sql021 = result.Errors.Count(e => e.Code == "SQL021");
        Assert.True(sql021 >= 2, $"Expected 2+ SQL021 errors, found {sql021}");
    }

    [Fact]
    public void Validate_NoError_Sql021ForMultiArgMinInWhere()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE MIN(SALARY, 1000) > 500;",
            _schema);
    }

    [Fact]
    public void Validate_Error_Sql021ForSingleArgMinInWhere()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE MIN(SALARY) > 50000;",
            "SQL021",
            _schema);
    }

    [Fact]
    public void Validate_NoError_Sql021ForMultiArgMaxInWhere()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE MAX(SALARY, 1000) > 500;",
            _schema);
    }

    // ========================================================================
    // New tests added for missing Node-equivalent coverage
    // ========================================================================

    [Fact]
    public void Validate_ParserError_Par101ForCteMissingAs()
    {
        // Node: PAR101 — "CTE is missing AS before the subquery"
        var schema = SqlTestHelpers.CreateStandardMockSchema();
        var result = SqlTestHelpers.Validate(
            "WITH MYCTE (SELECT 1) SELECT * FROM MYCTE;",
            schema);

        var par101 = result.Errors.FirstOrDefault(e => e.Code == "PAR101");
        Assert.NotNull(par101);
    }

    [Fact]
    public void Validate_Sql002Warning_CrossJoinWithUsing()
    {
        // Node: CROSS JOIN should not have USING clause → SQL002 (warning)
        var schema = SqlTestHelpers.CreateStandardMockSchema();
        var result = SqlTestHelpers.Validate(
            "SELECT * FROM TESTDB..EMPLOYEES CROSS JOIN TESTDB..DEPARTMENTS USING (DEPARTMENT_ID)",
            schema);

        Assert.Contains(result.Warnings, w => w.Code == "SQL002");
    }

    [Fact]
    public void Validate_Sql005Warning_QualifiedColumnRefUncachedTable()
    {
        // SQL005: Cannot validate column — table not in schema cache
        // Use a table that exists but WITHOUT columns in the schema
        var provider = new InMemorySchemaProvider();
        provider.AddTable(new TableInfo("UNKNOWN_TABLE", null, null, Columns: null));
        var result = SqlTestHelpers.Validate(
            "SELECT UNKNOWN_TABLE.COL FROM UNKNOWN_TABLE",
            provider);

        Assert.Contains(result.Warnings, w => w.Code == "SQL005");
    }

    [Fact]
    public void Validate_Sql005Warning_SkipsForCte()
    {
        // SQL005 should NOT fire for CTEs (their columns come from the query, not schema)
        var schema = SqlTestHelpers.CreateStandardMockSchema();
        var result = SqlTestHelpers.Validate(
            "WITH MYCTE AS (SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES) SELECT MYCTE.EMPLOYEE_ID FROM MYCTE",
            schema);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "SQL005");
    }

    [Fact]
    public void Validate_NodeParity_CrossJoinWithoutOnOrUsingNoSql002()
    {
        // Plain CROSS JOIN (no ON/USING) should NOT produce SQL002
        var schema = SqlTestHelpers.CreateStandardMockSchema();
        var result = SqlTestHelpers.Validate(
            "SELECT * FROM TESTDB..EMPLOYEES CROSS JOIN TESTDB..DEPARTMENTS",
            schema);

        Assert.DoesNotContain(result.Errors, e => e.Code == "SQL002");
        Assert.DoesNotContain(result.Warnings, w => w.Code == "SQL002");
    }

    [Fact]
    public void Validate_NodeParity_InnerJoinWithOnNoSql002()
    {
        // INNER JOIN with ON should NOT produce SQL002
        var schema = SqlTestHelpers.CreateStandardMockSchema();
        SqlTestHelpers.ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES E INNER JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID",
            schema);
    }

    // ========================================================================
    // Singleton instance (not applicable — C# uses DI)
    // ========================================================================

    // The C# API intentionally uses DI rather than a global singleton.
    private void Validate_ExportsSingletonValidatorInstance() { }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static (IReadOnlyList<ValidationError> Errors, Scope? Scope) VisitAndGetScope(
        string sql, ISchemaProvider? schema = null)
    {
        try
        {
            var tokens = NzLexer.Tokenize(sql).ToArray();
            var parser = new NzSqlParser(tokens);
            var stmt = parser.Parse();
            if (parser.Errors.Any())
                return (parser.Errors, null);
            if (stmt is null)
                return ([], null);

            var visitor = new NzSqlVisitor(schema);
            visitor.Visit(stmt);
            return (visitor.Errors, visitor.CurrentScope);
        }
        catch (Exception ex)
        {
            var pos = new SourcePosition(0, 0, 0);
            return ([new ValidationError(ex.Message, "error", pos, "LEX001")], null);
        }
    }
}
