using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Dialects.Access;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.Tests.NetezzaSqlParser;

namespace JustyBase.NetezzaSql.Tests;

public sealed class AccessSqlParserTests
{
    private static (IReadOnlyList<Statement> Statements, IReadOnlyList<ValidationError> Errors) Parse(string sql)
    {
        var tokens = AccessLexer.Tokenize(sql).ToArray();
        var parser = new AccessSqlParser(tokens);
        var statements = new List<Statement>();
        var errors = new List<ValidationError>();
        while (parser.Position < tokens.Length)
        {
            var before = parser.Errors.Count;
            var statement = parser.Parse();
            errors.AddRange(parser.Errors.Skip(before));
            if (statement is null)
                break;
            statements.Add(statement);
        }
        return (statements, errors);
    }

    [Fact]
    public void Lexer_RecognizesAccessForms()
    {
        var tokens = AccessLexer.Tokenize(
            "SELECT DISTINCTROW TOP 10 PERCENT [na]]me], `code`, \" \", #2026-01-02#, @p, :q, ? FROM t & x TRANSFORM PIVOT");

        Assert.Contains(tokens, token => token.Kind == NzToken.AccessBracketedIdentifier);
        Assert.Contains(tokens, token => token.Kind == NzToken.AccessBacktickIdentifier);
        Assert.Contains(tokens, token => token.Kind == NzToken.AccessDateLiteral);
        Assert.Contains(tokens, token => token.Kind == NzToken.AccessNamedParameter);
        Assert.Contains(tokens, token => token.Kind == NzToken.StringLiteral && token.ToStringValue() == "\" \"");
        Assert.Contains(tokens, token => token.Kind == NzToken.AccessAmpersand);
        Assert.Contains(tokens, token => token.Kind == NzToken.AccessDistinctRow);
        Assert.Contains(tokens, token => token.Kind == NzToken.AccessTop);
        Assert.Contains(tokens, token => token.Kind == NzToken.AccessPercent);
        Assert.Contains(tokens, token => token.Kind == NzToken.AccessTransform);
        Assert.Contains(tokens, token => token.Kind == NzToken.AccessPivot);
    }

    [Fact]
    public void Parse_SelectWithAccessModifiersAndLiterals()
    {
        var (statements, errors) = Parse(
            "SELECT DISTINCTROW TOP 10 PERCENT [first name] & \" \" & [last name] AS full_name, #2026-01-02# AS created FROM [people] WHERE [id] = @id ORDER BY [last name]");

        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.True(select.Modifier!.DistinctRow);
        Assert.True(select.AccessOptions!.Top!.Percent);
        Assert.IsType<BinaryExpression>(select.SelectList[0].Expression);
        Assert.Equal(LiteralKind.Date, Assert.IsType<Literal>(select.SelectList[1].Expression).Kind);
        var formatted = NzSqlFormatter.Format(select);
        Assert.Contains("DISTINCTROW", formatted, StringComparison.Ordinal);
        Assert.Contains("TOP 10 PERCENT", formatted, StringComparison.Ordinal);
        Assert.Contains("[first name]", formatted, StringComparison.Ordinal);
        Assert.Contains("[people]", formatted, StringComparison.Ordinal);
        Assert.Contains(" & ", formatted, StringComparison.Ordinal);
        Assert.Contains("\" \"", formatted, StringComparison.Ordinal);
        Assert.Contains("#2026-01-02#", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NotLike_PreservesNegatedComparison()
    {
        var (statements, errors) = Parse("SELECT * FROM people WHERE name NOT LIKE 'A*'");

        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        var comparison = Assert.IsType<BinaryExpression>(select.Where);
        Assert.Equal(BinaryOperator.NotLike, comparison.Operator);
    }

    [Fact]
    public void Parse_ParametersAndCrosstab()
    {
        var (parameterized, parameterErrors) = Parse(
            "PARAMETERS [fromDate] DateTime, [limit] Long; SELECT TOP 10 * FROM sales WHERE sold_at >= [fromDate]");
        var parameters = Assert.IsType<AccessParameterizedStatement>(Assert.Single(parameterized));
        Assert.Empty(parameterErrors);
        Assert.Equal(2, parameters.Parameters.Declarations.Count);
        Assert.IsType<SelectStatement>(parameters.Body);

        var (crosstabStatements, crosstabErrors) = Parse(
            "TRANSFORM Sum(amount) AS total SELECT category FROM sales GROUP BY category PIVOT month IN (1, 2, 3)");
        var crosstab = Assert.IsType<AccessCrosstabStatement>(Assert.Single(crosstabStatements));
        Assert.Empty(crosstabErrors);
        Assert.Equal("month", Assert.IsType<ColumnReference>(crosstab.PivotExpression).Name);
        Assert.Equal(3, crosstab.InValues!.Count);
        Assert.Contains("TRANSFORM", NzSqlFormatter.Format(crosstab), StringComparison.Ordinal);
        Assert.Contains("PIVOT", NzSqlFormatter.Format(crosstab), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AccessDdlAndIndexes()
    {
        var (tableStatements, tableErrors) = Parse(
            "CREATE TABLE [orders] ([id] COUNTER AUTOINCREMENT, [description] TEXT(255) WITH COMPRESSION, amount CURRENCY NOT NULL)");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(tableStatements));
        Assert.Empty(tableErrors);
        Assert.Equal(3, table.Columns!.Count);
        Assert.NotNull(table.Columns[0].MySqlAttributeTokens);

        var (indexStatements, indexErrors) = Parse("CREATE UNIQUE INDEX ix_orders ON orders (id, amount)");
        var index = Assert.IsType<AccessCreateIndexStatement>(Assert.Single(indexStatements));
        Assert.Empty(indexErrors);
        Assert.True(index.Unique);
        Assert.Equal(2, index.Columns.Count);
    }

    [Fact]
    public void Runtime_UsesAccessParserCatalogAndRules()
    {
        Assert.IsType<AccessSqlParser>(DialectRuntime.CreateParser(
            AccessLexer.Tokenize("SELECT 1").ToArray(), SqlDialect.Access));
        Assert.Same(AccessSqlCatalog.Instance, DialectRuntime.AuthoringCatalog(SqlDialect.Access));
        Assert.Same(AccessSqlCatalog.Instance, DialectRuntime.AuthoringCatalogOrNull(SqlDialect.Access));
        Assert.Contains("TOP", AccessSqlCatalog.Instance.CompletionKeywords);
        Assert.True(AccessSqlCatalog.Instance.TryGetFunction("Nz", out _));
        Assert.True(AccessSqlCatalog.Instance.TryGetDataType("COUNTER", out _));
        Assert.Contains(DialectRuntime.QualityRules(SqlDialect.Access).AllRules,
            rule => rule.Id == "ACC005");
    }

    [Fact]
    public async Task AccessAuthoring_UseAccessDialect()
    {
        using var session = new DocumentParseSession(SqlDialect.Access);
        var parsed = session.GetOrParse(
            "SELECT TOP 5 [first name] & \" \" & [last name] FROM [people] WHERE [id] = @id;");

        Assert.True(parsed.Valid);
        Assert.Empty(parsed.Errors);

        var catalog = DialectRuntime.AuthoringCatalog(SqlDialect.Access);
        var completions = await CompletionOrchestrator.GetCompletions(
            "SELECT", 6, null, SqlDialect.Access);
        Assert.Contains(completions.EngineItems, item =>
            item.Label.Equals("NZ", StringComparison.OrdinalIgnoreCase));

        const string hoverSql = "SELECT Nz(value, 0) FROM people";
        var hover = NzHoverService.GetHover(hoverSql, 8, null, catalog: catalog, dialect: SqlDialect.Access);
        Assert.NotNull(hover);
        Assert.Contains("NZ", hover!.Content, StringComparison.OrdinalIgnoreCase);

        const string sigSql = "SELECT Nz(";
        var signatures = NzSignatureHelpService.GetSignatureHelp(sigSql, sigSql.Length, catalog: catalog, dialect: SqlDialect.Access);
        Assert.NotNull(signatures);
        Assert.Contains(signatures!.Signatures, signature =>
            signature.Label.Contains("Nz", StringComparison.OrdinalIgnoreCase));

        var semantic = new NzSemanticTokenClassifier(null, null, SqlDialect.Access)
            .Classify("SELECT TOP 5 [people].[id] FROM [people] WHERE [id] = @id");
        Assert.Contains(semantic, span => span.Kind == SemanticTokenKind.Parameter);
    }

    [Fact]
    public void AccessCapabilitiesAndAliases_ReflectJetSyntax()
    {
        var capabilities = SqlDialectCapabilitiesCatalog.For(SqlDialect.Access);
        Assert.False(capabilities.SupportsMerge);
        Assert.False(capabilities.SupportsFetchFirst);
        Assert.False(capabilities.SupportsAnsiOffsetFetch);
        Assert.False(capabilities.SupportsLimit);

        Assert.Equal(SqlDialect.Access, DialectRuntime.ParseName("jet"));
        Assert.Equal(SqlDialect.Access, DialectRuntime.ParseName("access"));
    }

    [Fact]
    public void AccessRename_PreservesBracketedIdentifiers()
    {
        const string sql = "SELECT [a].id FROM [orders] AS [a] WHERE [a].id > 0;";
        var first = sql.IndexOf("[a]", StringComparison.Ordinal);
        var second = sql.IndexOf("[a]", first + 1, StringComparison.Ordinal);
        var third = sql.IndexOf("[a]", second + 1, StringComparison.Ordinal);
        var occurrences = new[]
        {
            new SymbolOccurrence(1, "a", SqlSymbolKind.Alias, first, first + 3, true, 1),
            new SymbolOccurrence(2, "a", SqlSymbolKind.Alias, second, second + 3, false, 1),
            new SymbolOccurrence(3, "a", SqlSymbolKind.Alias, third, third + 3, false, 1),
        };

        var renamed = NzRenameService.ApplyRename(sql, new SqlRenameInfo("a", SqlSymbolKind.Alias, occurrences), "order archive");
        Assert.Contains("[order archive]", renamed, StringComparison.Ordinal);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(renamed, @"\[order archive\]", System.Text.RegularExpressions.RegexOptions.None).Count);
    }

    [Fact]
    public void AccessLint_ReportsAccessSpecificSafetyIssues()
    {
        var issues = DialectRuntime.QualityRules(SqlDialect.Access).AllRules
            .SelectMany(r => r.Check("SELECT TOP @limit * FROM [orders]")).ToList();

        Assert.Contains(issues, issue => issue.RuleId == "ACC001");
        Assert.Contains(issues, issue => issue.RuleId == "ACC004");
        Assert.Equal("Access SQL", DialectRuntime.DiagnosticSource(SqlDialect.Access));
    }

    [Fact]
    public void AccessSelect_ParsesExternalDatabaseOwnerOptionAndRejectsLimit()
    {
        var (statements, errors) = Parse(
            "SELECT TOP @limit * FROM [orders] IN 'archive.accdb' WITH OWNERACCESS OPTION");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.Equal("'archive.accdb'", select.AccessOptions!.ExternalDatabase);
        Assert.True(select.AccessOptions.WithOwnerAccessOption);

        var (_, limitErrors) = Parse("SELECT * FROM orders LIMIT 1");
        Assert.Contains(limitErrors, error => error.Code == "ACC001");
    }

    [Fact]
    public void AccessCreateTable_ConsumesIdentityClause()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE orders (id INTEGER IDENTITY(1, 1), name TEXT(80))");

        Assert.IsType<CreateTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
    }

    [Fact]
    public void AccessDml_ParsesDeleteWildcardAndJoinedUpdate()
    {
        var (updateStatements, updateErrors) = Parse(
            "UPDATE orders INNER JOIN order_items ON orders.order_id = order_items.order_id "
            + "SET orders.status = 'closed' WHERE orders.order_id = 1");
        var update = Assert.IsType<UpdateStatement>(Assert.Single(updateStatements));
        Assert.Empty(updateErrors);
        Assert.NotNull(update.AccessTargetReference);
        Assert.Single(update.AccessTargetReference!.Joins!);
        Assert.Contains("INNER JOIN", NzSqlFormatter.Format(update), StringComparison.Ordinal);

        var (deleteStatements, deleteErrors) = Parse(
            "DELETE * FROM orders INNER JOIN order_items ON orders.order_id = order_items.order_id "
            + "WHERE orders.order_id = 1");
        var delete = Assert.IsType<DeleteStatement>(Assert.Single(deleteStatements));
        Assert.Empty(deleteErrors);
        Assert.True(delete.AccessWildcard);
        Assert.NotNull(delete.AccessTargetReference);
        Assert.Contains("DELETE * FROM", NzSqlFormatter.Format(delete), StringComparison.Ordinal);
    }

    [Fact]
    public void AccessFormatting_PreservesQuotedAliasesColumnsAndIndexDirections()
    {
        var (aliasStatements, aliasErrors) = Parse(
            "SELECT o AS [display name] FROM orders AS [table alias]");
        var aliasSelect = Assert.IsType<SelectStatement>(Assert.Single(aliasStatements));
        Assert.Empty(aliasErrors);
        var aliasSql = NzSqlFormatter.Format(aliasSelect);
        Assert.Contains("AS [display name]", aliasSql, StringComparison.Ordinal);
        Assert.Contains("AS [table alias]", aliasSql, StringComparison.Ordinal);

        var (updateStatements, updateErrors) = Parse(
            "UPDATE [people] SET [full name] = 1 WHERE [id] = 1");
        var updateSql = NzSqlFormatter.Format(Assert.IsType<UpdateStatement>(Assert.Single(updateStatements)));
        Assert.Empty(updateErrors);
        Assert.Contains("UPDATE [people]", updateSql, StringComparison.Ordinal);
        Assert.Contains("[full name] = 1", updateSql, StringComparison.Ordinal);

        var (indexStatements, indexErrors) = Parse(
            "CREATE INDEX ix_people ON people ([full name], value DESC)");
        var indexSql = NzSqlFormatter.Format(Assert.IsType<AccessCreateIndexStatement>(Assert.Single(indexStatements)));
        Assert.Empty(indexErrors);
        Assert.Contains("[full name], value DESC", indexSql, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessLint_DoesNotFlagQuotedIdentifierText()
    {
        var issues = DialectRuntime.QualityRules(SqlDialect.Access).AllRules
            .SelectMany(r => r.Check("SELECT TOP 5 [LIMIT] FROM people")).ToList();

        Assert.DoesNotContain(issues, issue => issue.RuleId == "ACC005");
    }

    [Fact]
    public void AccessCatalog_ContainsProviderFunctionsAndCompoundTypes()
    {
        foreach (var function in new[] { "STR", "CLONG", "IPMT", "PPMT", "RATE", "NPV", "MIRR" })
            Assert.True(AccessSqlCatalog.Instance.TryGetFunction(function, out _), function);

        foreach (var type in new[] { "SHORT INTEGER", "LONG INTEGER", "BIG INTEGER" })
            Assert.True(AccessSqlCatalog.Instance.TryGetDataType(type, out _), type);
    }

    [Fact]
    public void AccessVisitor_RecognizesDeclaredParameters()
    {
        var (statements, parseErrors) = Parse(
            "PARAMETERS [p] Long; SELECT * FROM orders WHERE order_id = [p]");
        var statement = Assert.IsType<AccessParameterizedStatement>(Assert.Single(statements));
        Assert.Empty(parseErrors);

        var visitor = new NzSqlVisitor(
            SqlTestHelpers.CreateStandardMockSchema(), AccessSqlCatalog.Instance);
        visitor.Visit(statement);

        Assert.DoesNotContain(visitor.Errors, error => error.Code is "SQL004" or "SQL013");
    }
}
