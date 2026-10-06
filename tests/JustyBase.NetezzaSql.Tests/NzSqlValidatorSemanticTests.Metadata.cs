using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorSemanticTests
{
    [Fact]
    public void Validate_Semantic_NonExistentColumnInUpdateSet()
    {
        SqlTestHelpers.ExpectErrorCode(
            "UPDATE TESTDB..EMPLOYEES SET FAKE_COLUMN = 'x';",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_Semantic_NonExistentColumnInDeleteWhere()
    {
        SqlTestHelpers.ExpectErrorCode(
            "DELETE FROM TESTDB..EMPLOYEES WHERE FAKE_COLUMN = 1;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_Semantic_InvalidDbTableForm()
    {
        var result = SqlTestHelpers.Validate("SELECT 1 FROM TESTDB.EMPLOYEES;", _schema);
        Assert.Contains(result.Errors, e => e.Code == "SQL007");
    }


    [Fact]
    public void Validate_Function_CountDistinctColumn()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT COUNT(DISTINCT DEPARTMENT_ID) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    // ========================================================================
    // Semantic validation - additional patterns
    // ========================================================================

    [Fact]
    public void Validate_SemanticAdditional_ColumnNotInTable()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT NONEXISTENT_COL FROM TESTDB..EMPLOYEES;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_TableNotInDatabase()
    {
        SqlTestHelpers.ExpectErrorCode("SELECT * FROM TESTDB..NONEXISTENT_TABLE;", "SQL006", _schema);
    }


    [Fact]
    public void Validate_DoubleDotExistingTable_NoError()
    {
        var schema = SqlTestHelpers.CreateMockSchemaProvider([
            new("DIMACCOUNT", "ADMIN", "JUST_DATA", ["ID", "NAME"])
        ]);
        SqlTestHelpers.ExpectValid("SELECT * FROM JUST_DATA..DIMACCOUNT;", schema);
        SqlTestHelpers.ExpectValid("SELECT ID FROM JUST_DATA..DIMACCOUNT;", schema);
    }


    [Fact]
    public void Validate_DoubleDotAfterDelayedSchemaSync_NoError()
    {
        // Reproduce the bug: if .. lookup fails BEFORE table is loaded (absent cached),
        // then AddTable must clear the absent cache for DB..TABLE form too.
        var provider = new InMemorySchemaProvider();

        // Add some other table so HasTables() returns true
        provider.AddTable(new TableInfo("OTHER", "PUBLIC", "TESTDB",
            Columns: new[] { new ColumnInfo("X") }));

        // First lookup: DIMACCOUNT not yet loaded → absent cached for JUST_DATA..DIMACCOUNT
        Assert.Null(provider.GetTable("JUST_DATA", null, "DIMACCOUNT"));

        // Now the table is loaded via schema sync
        provider.AddTable(new TableInfo("DIMACCOUNT", "ADMIN", "JUST_DATA",
            Columns: new[] { new ColumnInfo("ID"), new ColumnInfo("NAME") }));

        // After AddTable, .. lookup should find it
        var result = provider.GetTable("JUST_DATA", null, "DIMACCOUNT");
        Assert.NotNull(result);
        Assert.Equal("DIMACCOUNT", result.Name);

        Assert.True(provider.TableExists("JUST_DATA", null, "DIMACCOUNT"));
        Assert.True(provider.TableExists("JUST_DATA", "ADMIN", "DIMACCOUNT"));
        SqlTestHelpers.ExpectValid("SELECT * FROM JUST_DATA..DIMACCOUNT;", provider);
        SqlTestHelpers.ExpectValid("SELECT * FROM JUST_DATA.ADMIN.DIMACCOUNT;", provider);
    }


    [Fact]
    public void Validate_SemanticAdditional_InvalidDatabaseTableFormWithSchema()
    {
        SqlTestHelpers.ExpectErrorCode("SELECT * FROM TESTDB.EMPLOYEES;", "SQL007", _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_NonExistentColumnInWhereClause()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE FAKE_COLUMN = 1;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_NonExistentColumnInOrderBy()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY FAKE_COLUMN;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_NonExistentColumnInGroupBy()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT FAKE_COLUMN, COUNT(*) FROM TESTDB..EMPLOYEES GROUP BY FAKE_COLUMN;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_JoinColumnExistence_FakeColumnInLeftTableOn()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT * FROM TESTDB..EMPLOYEES E
JOIN TESTDB..DEPARTMENTS D ON E.FAKE_COLUMN = D.DEPARTMENT_ID;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_JoinColumnExistence_FakeColumnInRightTableOn()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT * FROM TESTDB..EMPLOYEES E
JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.FAKE_COLUMN;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_JoinColumnExistence_ColumnsFromJoinedSubqueries()
    {
        SqlTestHelpers.ExpectValid(
            @"SELECT A.ID, B.NAME
FROM (SELECT EMPLOYEE_ID AS ID FROM TESTDB..EMPLOYEES) A
JOIN (SELECT DEPARTMENT_ID AS ID, DEPARTMENT_NAME AS NAME FROM TESTDB..DEPARTMENTS) B
ON A.ID = B.ID;",
            _schema);
    }


    [Fact]
    public void Validate_JoinColumnExistence_NonExistentColumnFromJoinedSubquery()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT A.ID, B.FAKE_NAME
FROM (SELECT EMPLOYEE_ID AS ID FROM TESTDB..EMPLOYEES) A
JOIN (SELECT DEPARTMENT_ID AS ID FROM TESTDB..DEPARTMENTS) B
ON A.ID = B.ID;",
            "SQL004",
            _schema);
    }

    // ========================================================================
    // ADVANCED: Object existence (table/CTE/alias/subquery)
    // ========================================================================

    [Fact]
    public void Validate_ObjectExistence_CteDefinedEarlierInSameQuery()
    {
        SqlTestHelpers.ExpectValid(@"WITH MY_CTE AS (SELECT 1 AS COL)
SELECT * FROM MY_CTE;");
    }


    [Fact]
    public void Validate_ComplexError_NonExistentColumnInAnalyticsPartitionBy()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT 
    ROW_NUMBER() OVER (PARTITION BY FAKE_COLUMN ORDER BY EMPLOYEE_ID) AS RN
FROM TESTDB..EMPLOYEES;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_ComplexError_NonExistentColumnInAnalyticsOrderBy()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT 
    ROW_NUMBER() OVER (PARTITION BY DEPARTMENT_ID ORDER BY FAKE_COLUMN) AS RN
FROM TESTDB..EMPLOYEES;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_ComplexError_NonExistentColumnInGroupByRollup()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT COUNT(*) FROM TESTDB..EMPLOYEES GROUP BY ROLLUP(FAKE_COLUMN);",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_ComplexError_NonExistentColumnInCaseWhen()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT 
    CASE WHEN FAKE_COLUMN > 100 THEN 'High' ELSE 'Low' END
FROM TESTDB..EMPLOYEES;",
            "SQL004",
            _schema);
    }

    // ========================================================================
    // SQL004 enhanced message: table name + position
    // ========================================================================

    [Fact]
    public void SQL004_MessageContainsTableName_Qualified()
    {
        var result = SqlTestHelpers.Validate(
            "SELECT E.NO_SUCH_COLUMN FROM TESTDB..EMPLOYEES E;",
            _schema);

        var diag = Assert.Single(result.Errors, d => d.Code == "SQL004");
        Assert.Contains("EMPLOYEES", diag.Message);
        Assert.Contains("NO_SUCH_COLUMN", diag.Message);
        Assert.StartsWith("SQL004:", diag.Message);
    }
}
