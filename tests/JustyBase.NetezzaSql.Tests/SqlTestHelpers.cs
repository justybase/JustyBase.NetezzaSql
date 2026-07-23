using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using Superpower.Model;

namespace JustyBase.Tests.NetezzaSqlParser;

public static class SqlTestHelpers
{
    // ===== Validation helpers =====

    public static ValidationResult Validate(string sql, ISchemaProvider? schema = null)
    {
        var allErrors = new List<ValidationError>();
        var allWarnings = new List<WarningInfo>();

        // Run structural scanner before lexer to catch unclosed strings, comments, etc.
        allErrors.AddRange(NzSqlStructuralScanner.Scan(sql));
        if (allErrors.Any(e => e.Code is "PAR110" or "PAR111"))
            return new ValidationResult(allErrors, Array.Empty<WarningInfo>());

        Token<NzToken>[] tokens;
        try
        {
            tokens = NzLexer.Tokenize(sql).ToArray();
        }
        catch (Exception ex)
        {
            return new ValidationResult(
                new[] { new ValidationError(ex.Message, "error", new SourcePosition(0, 0, 0), "LEX001") },
                Array.Empty<WarningInfo>());
        }

        // Parse all statements sequentially
        var visitor = new NzSqlVisitor(schema);
        visitor.ResetKeepMultiStatementScope();

        var parser = new NzSqlParser(tokens);
        var stmt = parser.Parse();
        allErrors.AddRange(parser.Errors);

        var firstParser = parser;
        var firstPos = parser.Position;

        if (allErrors.Any())
        {
            return new ValidationResult(allErrors, Array.Empty<WarningInfo>());
        }

        if (stmt is not null)
        {
            visitor.Visit(stmt);
            allErrors.AddRange(visitor.Errors.Where(e => e.Severity == "error"));
            allWarnings.AddRange(visitor.Errors.Where(e => e.Severity != "error")
                .Select(e => new WarningInfo(e.Code, e.Message, e.Position)));
        }

        // Try to parse remaining statements sequentially
        var remainingStart = parser.Position;
        bool firstSubParse = true;
        while (remainingStart < tokens.Length)
        {
            // Skip semicolons between statements
            while (remainingStart < tokens.Length && tokens[remainingStart].Kind == NzToken.Semicolon)
                remainingStart++;

            if (remainingStart >= tokens.Length) break;

            // Create sub-array of remaining tokens
            var subTokens = new Token<NzToken>[tokens.Length - remainingStart];
            Array.Copy(tokens, remainingStart, subTokens, 0, subTokens.Length);

            var subParser = new NzSqlParser(subTokens);
            var subStmt = subParser.Parse();
            allErrors.AddRange(subParser.Errors);

            if (subStmt is not null && !allErrors.Any(e => e.Code.StartsWith("PAR") || e.Code.StartsWith("LEX")))
            {
                visitor.Visit(subStmt);
                allErrors.AddRange(visitor.Errors.Where(e => e.Severity == "error"));
                allWarnings.AddRange(visitor.Errors.Where(e => e.Severity != "error")
                    .Select(e => new WarningInfo(e.Code, e.Message, e.Position)));
            }

            var consumedInThisPass = subParser.Position;
            if (consumedInThisPass == 0)
            {
                // No progress, check if there are unconsumed meaningful tokens
                if (firstSubParse)
                {
                    var hasUnconsumedMeaningful = false;
                    for (int i = 0; i < subTokens.Length; i++)
                    {
                        if (subTokens[i].Kind != NzToken.Semicolon)
                        {
                            hasUnconsumedMeaningful = true;
                            break;
                        }
                    }
                    if (hasUnconsumedMeaningful)
                    {
                        var pos = SourcePosition.FromToken(subTokens[0]);
                        allErrors.Add(new ValidationError(
                            $"Unexpected token after statement: '{subTokens[0]}'", "error", pos, "PARSE002"));
                    }
                }
                break;
            }
            firstSubParse = false;
            remainingStart += consumedInThisPass;
        }

        if (allErrors.Any())
        {
            return new ValidationResult(allErrors, Array.Empty<WarningInfo>());
        }

        return new ValidationResult(allErrors, allWarnings);
    }

    public static void ExpectValid(string sql, ISchemaProvider? schema = null)
    {
        var result = Validate(sql, schema);
        Assert.Empty(result.Errors);
    }

    public static void ExpectSyntaxError(string sql, ISchemaProvider? schema = null)
    {
        var result = Validate(sql, schema);
        Assert.Contains(result.Errors, e => e.Code.StartsWith("PAR") || e.Code.StartsWith("LEX"));
    }

    public static void ExpectErrorCode(string sql, string code, ISchemaProvider? schema = null)
    {
        var result = Validate(sql, schema);
        Assert.Contains(result.Errors, e => e.Code == code);
    }

    public static void ExpectWarningCode(string sql, string code, ISchemaProvider? schema = null)
    {
        var result = Validate(sql, schema);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Code == code);
    }

    // ===== Schema Provider =====

    public static ISchemaProvider CreateMockSchemaProvider(IEnumerable<TableDefinition> tables)
    {
        var provider = new InMemorySchemaProvider();
        foreach (var t in tables)
        {
            var columns = (t.Columns ?? []).Select(c => new ColumnInfo(c)).ToList();
            provider.AddTable(new TableInfo(t.Name, t.Schema, t.Database, Columns: columns));
        }
        return provider;
    }

    public static ISchemaProvider CreateStandardMockSchema()
    {
        return CreateMockSchemaProvider([
            new("EMPLOYEES", "PUBLIC", "TESTDB", ["EMPLOYEE_ID", "FIRST_NAME", "LAST_NAME", "DEPARTMENT_ID", "SALARY", "HIRE_DATE", "MANAGER_ID", "STATUS"]),
            new("DEPARTMENTS", "PUBLIC", "TESTDB", ["DEPARTMENT_ID", "DEPARTMENT_NAME", "LOCATION_ID"]),
            new("ORDERS", "PUBLIC", "TESTDB", ["ORDER_ID", "CUSTOMER_ID", "ORDER_DATE", "TOTAL_AMOUNT", "STATUS"]),
            new("ORDER_ITEMS", "PUBLIC", "TESTDB", ["ITEM_ID", "ORDER_ID", "PRODUCT_ID", "QUANTITY", "UNIT_PRICE"]),
            new("PRODUCTS", "PUBLIC", "TESTDB", ["PRODUCT_ID", "PRODUCT_NAME", "CATEGORY", "PRICE"]),
            new("FILMS", "PUBLIC", "TESTDB", ["CODE", "TITLE", "DID", "DATE_PROD", "KIND", "LEN"]),
            // Common test tables used by DDL tests
            new("MY_TABLE", "PUBLIC", "TESTDB", ["ID", "NAME"]),
            new("OLD_TABLE", "PUBLIC", "TESTDB", ["ID", "NAME"]),
            new("NEW_TABLE", "PUBLIC", "TESTDB", ["ID", "NAME"]),
            new("T1", "PUBLIC", "TESTDB", ["ID"]),
            new("T2", "PUBLIC", "TESTDB", ["ID"]),
            new("T3", "PUBLIC", "TESTDB", ["ID"]),
            new("SEQ_1", "PUBLIC", "TESTDB", []),
            new("EMP_VIEW", "PUBLIC", "TESTDB", ["EMPLOYEE_ID", "FIRST_NAME"]),
            new("V_EMP", "PUBLIC", "TESTDB", ["EMPLOYEE_ID", "FIRST_NAME"]),
            new("MY_TABLE", "MYSCHEMA", "MYDB", ["ID", "NAME"]),
        ]);
    }

    public static IReadOnlyList<LintIssue> LintParity(string sql, ISchemaProvider? schema = null)
    {
        using var engine = new LintEngine();
        var config = new LintConfig(sql, Schema: schema, DocumentUri: Guid.NewGuid().ToString());
        return engine.RunFullLint(config).Issues;
    }
}

public record TableDefinition(string Name, string? Schema = null, string? Database = null, IReadOnlyList<string>? Columns = null);

public record ValidationResult(IReadOnlyList<ValidationError> Errors, IReadOnlyList<WarningInfo> Warnings);

public record WarningInfo(string Code, string Message, SourcePosition? Position = null);
