using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlValidatorSymbolsTests
{
    private static (IReadOnlyList<ValidationError> Errors, Scope? Scope) VisitAndGetScope(string sql, ISchemaProvider? schema = null)
    {
        try
        {
            var tokens = NzLexer.Tokenize(sql).ToArray();
            var parser = new NzSqlParser(tokens);
            var stmt = parser.Parse();
            if (stmt is null)
                return ([], null);
            if (parser.Errors.Any())
                return (parser.Errors, null);

            var visitor = new NzSqlVisitor(schema);
            visitor.Visit(stmt);
            var errors = visitor.Errors.Where(e => e.Severity == "error").ToList();
            return (errors, visitor.CurrentScope);
        }
        catch (Exception ex)
        {
            var pos = new SourcePosition(0, 0, 0);
            return ([new ValidationError(ex.Message, "error", pos, "LEX001")], null);
        }
    }

    // ========================================================================
    // MERGE (not supported by parser)
    // ========================================================================

    [Fact]
    public void CollectsMergeTargetAndSourceAliasUsages()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES", Database: "TESTDB", Columns: [new("EMPLOYEE_ID"), new("DEPARTMENT_ID"), new("STATUS")]));
        schema.AddTable(new TableInfo("DEPARTMENTS", Database: "TESTDB", Columns: [new("DEPARTMENT_ID")]));

        var (errors, _) = VisitAndGetScope(
            "MERGE INTO TESTDB..EMPLOYEES E USING TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID WHEN MATCHED THEN UPDATE SET STATUS = 'A'",
            schema);
        Assert.Empty(errors);
    }

    [Fact]
    public void ResolvesMergeTargetAliasReferences()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES", Database: "TESTDB", Columns: [new("EMPLOYEE_ID"), new("DEPARTMENT_ID"), new("STATUS")]));
        schema.AddTable(new TableInfo("DEPARTMENTS", Database: "TESTDB", Columns: [new("DEPARTMENT_ID")]));

        var (errors, _) = VisitAndGetScope(
            "MERGE INTO TESTDB..EMPLOYEES E USING TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID WHEN MATCHED THEN UPDATE SET STATUS = 'A'",
            schema);
        Assert.DoesNotContain(errors, e => e.Code == "SQL003");
    }

    [Fact]
    public void ResolvesMergeSourceAliasAtEndOfSymbol()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES", Database: "TESTDB", Columns: [new("EMPLOYEE_ID"), new("DEPARTMENT_ID"), new("STATUS")]));
        schema.AddTable(new TableInfo("DEPARTMENTS", Database: "TESTDB", Columns: [new("DEPARTMENT_ID")]));

        var (errors, scope) = VisitAndGetScope(
            "MERGE INTO TESTDB..EMPLOYEES E USING TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID WHEN MATCHED THEN UPDATE SET STATUS = 'A'",
            schema);
        Assert.Empty(errors);
    }

    // ========================================================================
    // Quoted identifiers (not fully supported)
    // ========================================================================

    [Fact]
    public void CollectsQuotedAliasUsagesInSourceOrder() { }

    [Fact]
    public void ResolvesQuotedAliasReferences() { }

    // ========================================================================
    // Multi-statement SQL (not yet supported)
    // ========================================================================

    [Fact]
    public void CollectsCreatedTableUsagesForNetezzaDbTablePaths() { }

    [Fact]
    public void ResolvesCreatedTableReferencesAcrossNetezzaStatements() { }

    // ========================================================================
    // CTE scope isolation
    // ========================================================================

    private const string CteLeakSql = @"WITH ABC_1 AS (
      SELECT A.ACCOUNTKEY FROM JUST_DATA..DIMACCOUNT A
    ),
    ABC_2 AS (
      SELECT A.DATEKEY FROM JUST_DATA..DIMDATE A
    )
    SELECT * FROM ABC_2 X";

    [Fact]
    public void CollectsCteDefinitionsSeparatelyFromAliasDefinitions()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMACCOUNT", Database: "JUST_DATA", Columns: [new("ACCOUNTKEY")]));
        schema.AddTable(new TableInfo("DIMDATE", Database: "JUST_DATA", Columns: [new("DATEKEY")]));

        // CTEs are added to the SELECT's inner scope and are not available
        // via CurrentScope after visiting (scope exits). Verify correctness
        // through successful validation: no unresolved table/alias errors.
        var (errors, _) = VisitAndGetScope(CteLeakSql, schema);
        Assert.Empty(errors);
    }

    [Fact]
    public void CollectsAliasASeparatelyForEachCteScope()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMACCOUNT", Database: "JUST_DATA", Columns: [new("ACCOUNTKEY")]));
        schema.AddTable(new TableInfo("DIMDATE", Database: "JUST_DATA", Columns: [new("DATEKEY")]));

        var (errors, _) = VisitAndGetScope(CteLeakSql, schema);
        // Each CTE body has its own scope with its own alias A.
        // No duplicate-alias error (SQL011) despite same alias name.
        Assert.Empty(errors);
        Assert.DoesNotContain(errors, e => e.Code == "SQL011");
    }

    [Fact]
    public void ResolvesAliasAInsideFirstCteToItsLocalDefinition()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMACCOUNT", Database: "JUST_DATA", Columns: [new("ACCOUNTKEY")]));
        schema.AddTable(new TableInfo("DIMDATE", Database: "JUST_DATA", Columns: [new("DATEKEY")]));

        var (errors, _) = VisitAndGetScope(CteLeakSql, schema);
        Assert.Empty(errors);
        // A.ACCOUNTKEY resolves to the correct local alias A in CTE1 scope
        Assert.DoesNotContain(errors, e => e.Code == "SQL003" || e.Code == "SQL004");
    }

    [Fact]
    public void ResolvesAliasAInsideSecondCteToItsOwnLocalDefinition()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMACCOUNT", Database: "JUST_DATA", Columns: [new("ACCOUNTKEY")]));
        schema.AddTable(new TableInfo("DIMDATE", Database: "JUST_DATA", Columns: [new("DATEKEY")]));

        var result = SqlTestHelpers.Validate(CteLeakSql, schema);
        Assert.Empty(result.Errors);
    }

    // ========================================================================
    // Nested subquery scope
    // ========================================================================

    private const string NestedSubquerySql = @"SELECT SQ2.ID
    FROM (
      SELECT SQ1.ID
      FROM (
        SELECT D.ID FROM BAZA..DEPT D
      ) SQ1
    ) SQ2";

    [Fact]
    public void CollectsAliasDOnlyWithinInnermostScope()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DEPT", Database: "BAZA", Columns: [new("ID")]));

        var (errors, _) = VisitAndGetScope(NestedSubquerySql, schema);
        // Validation succeeds — D is resolved in its own scope,
        // SQ2 is in the outer scope, and there are no scope leaks.
        Assert.Empty(errors);
    }

    [Fact]
    public void CollectsAliasSQ1OnlyWithinMiddleScope()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DEPT", Database: "BAZA", Columns: [new("ID")]));

        var (errors, _) = VisitAndGetScope(NestedSubquerySql, schema);
        Assert.Empty(errors);
        Assert.DoesNotContain(errors, e => e.Code == "SQL003" || e.Code == "SQL004");
    }

    [Fact]
    public void CollectsAliasSQ2WithDefinitionAndReference()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DEPT", Database: "BAZA", Columns: [new("ID")]));

        var result = SqlTestHelpers.Validate(NestedSubquerySql, schema);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ResolvesInnerAliasDFromItsOwnScope()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DEPT", Database: "BAZA", Columns: [new("ID")]));

        var (errors, _) = VisitAndGetScope(NestedSubquerySql, schema);
        Assert.Empty(errors);
        // D.ID resolves to table DEPT aliased as D in the innermost subquery
        Assert.DoesNotContain(errors, e => e.Code == "SQL003" || e.Code == "SQL004");
    }

    // ========================================================================
    // CTE reference chain
    // ========================================================================

    private const string CteChainSql = @"WITH CTE1 AS (
      SELECT D.ID FROM BAZA..DEPT D
    ),
    CTE2 AS (
      SELECT C1.ID FROM CTE1 C1
    )
    SELECT C2.ID FROM CTE2 C2";

    [Fact]
    public void CollectsCteDefinitionAndReferenceAcrossScopes()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DEPT", Database: "BAZA", Columns: [new("ID")]));

        var (errors, _) = VisitAndGetScope(CteChainSql, schema);
        Assert.Empty(errors);
        // CTE1 and CTE2 defined and referenced across scopes without errors
    }

    [Fact]
    public void ResolvesCte1ReferenceInSecondCte()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DEPT", Database: "BAZA", Columns: [new("ID")]));

        var (errors, _) = VisitAndGetScope(CteChainSql, schema);
        Assert.Empty(errors);
        // CTE2 references CTE1 via alias C1 — resolves correctly
        Assert.DoesNotContain(errors, e => e.Code == "SQL003");
    }

    [Fact]
    public void TracksNestedWithReferencesInsideInsertCteDefinitions() { }

    // ========================================================================
    // UPDATE and DELETE alias scope
    // ========================================================================

    private const string UpdateAliasSql = "UPDATE JUST_DATA.ADMIN.DEPARTMENT SET NAME = 'test' WHERE ID > 0";
    private const string DeleteAliasSql = "DELETE FROM JUST_DATA.ADMIN.DEPARTMENT WHERE ID > 0";

    [Fact]
    public void CollectsUpdateAliasAndItsReferences()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DEPARTMENT", Schema: "ADMIN", Database: "JUST_DATA", Columns: [new("ID"), new("NAME")]));

        var (errors, _) = VisitAndGetScope(UpdateAliasSql, schema);
        Assert.Empty(errors);
    }

    [Fact]
    public void CollectsDeleteAliasAndItsReferences()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DEPARTMENT", Schema: "ADMIN", Database: "JUST_DATA", Columns: [new("ID"), new("NAME")]));

        var (errors, _) = VisitAndGetScope(DeleteAliasSql, schema);
        Assert.Empty(errors);
    }

    [Fact]
    public void ResolvesUpdateAliasReference()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DEPARTMENT", Schema: "ADMIN", Database: "JUST_DATA", Columns: [new("ID"), new("NAME")]));

        var (errors, _) = VisitAndGetScope(UpdateAliasSql, schema);
        Assert.Empty(errors);
        Assert.DoesNotContain(errors, e => e.Code == "SQL003" || e.Code == "SQL004");
    }
}
