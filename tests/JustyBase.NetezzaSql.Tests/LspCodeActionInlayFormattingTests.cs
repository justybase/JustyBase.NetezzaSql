using System.Text.Json;
using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;
using LspRange = JustyBase.NetezzaSqlLsp.Protocol.Range;

namespace JustyBase.NetezzaSql.Tests;

public sealed class LspCodeActionInlayFormattingTests
{
    private const string DocumentUri = "file:///query.sql";

    private static InMemorySchemaProvider CreateSchema()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES", "PUBLIC", "TESTDB", Columns:
        [
            new ColumnInfo("EMPLOYEE_ID", DataType: "INT4"),
            new ColumnInfo("FIRST_NAME", DataType: "VARCHAR(50)")
        ]));
        return schema;
    }

    [Fact]
    public void CodeActions_Sql007_ProducesQuickFixEdit()
    {
        const string sql = "SELECT * FROM DB1.EMP;";
        var issue = new LintIssue("SQL007", "bad", LintSeverity.Error, 14, 21);

        var actions = CodeActionService.GetCodeActions(DocumentUri, sql, new[] { issue }, schema: null);

        var action = Assert.Single(actions);
        Assert.Equal("quickfix", action.Kind);
        Assert.True(action.IsPreferred);
        var edit = Assert.Single(action.Edit!.Changes![DocumentUri]);
        var applied = sql[..edit.Range.Start.Character] + edit.NewText + sql[edit.Range.End.Character..];
        Assert.Equal("SELECT * FROM DB1..EMP;", applied);
        Assert.True(edit.Range.Start.Character > 0);
        Assert.Equal("SQL007", Assert.Single(action.Diagnostics!).Code);
    }

    [Fact]
    public void CodeActions_FixAll_WhenMultipleSafeIssues()
    {
        const string sql = "select 1 from dual;";
        var issues = new[]
        {
            new LintIssue("NZ007", "UPPERCASE", LintSeverity.Information, 0, 6),
            new LintIssue("NZ007", "UPPERCASE", LintSeverity.Information, 9, 13)
        };

        var actions = CodeActionService.GetCodeActions(DocumentUri, sql, issues, schema: null);

        var fixAll = Assert.Single(actions, action => action.Kind == "source.fixAll");
        var edit = Assert.Single(fixAll.Edit!.Changes![DocumentUri]);
        Assert.StartsWith("SELECT", edit.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeActions_NoFixAvailable_ReturnsEmpty()
    {
        var issue = new LintIssue("UNKNOWN", "n/a", LintSeverity.Warning, 0, 1);

        Assert.Empty(CodeActionService.GetCodeActions(DocumentUri, "SELECT 1", new[] { issue }, schema: null));
    }

    [Fact]
    public void ToLintIssue_ReadsSuggestedFixFromJsonElement()
    {
        var data = new Dictionary<string, object?> { ["suggestedFix"] = JsonSerializer.SerializeToElement("SELECT") };
        var diagnostic = new Diagnostic(
            new LspRange(new Position(0, 0), new Position(0, 5)),
            DiagnosticSeverity.Error,
            "PAR004",
            Source: null,
            "typo",
            data);

        var issue = CodeActionService.ToLintIssue(diagnostic, "SELEC 1;");

        Assert.NotNull(issue);
        Assert.Equal(0, issue!.StartOffset);
        Assert.Equal(5, issue.EndOffset);
        Assert.Equal(LintSeverity.Error, issue.Severity);
        Assert.Equal("SELECT", issue.SuggestedFix);
    }

    [Fact]
    public void CodeAction_SuggestedFixFromParserDiagnostic_IsOffered()
    {
        const string sql = "SELEC 1;";
        var diagnostic = new Diagnostic(
            new LspRange(new Position(0, 0), new Position(0, 5)),
            DiagnosticSeverity.Error,
            "PAR004",
            Source: "Netezza",
            Message: "Unknown keyword",
            Data: new Dictionary<string, object?>
            {
                ["suggestedFix"] = JsonSerializer.SerializeToElement("SELECT")
            });
        var issue = CodeActionService.ToLintIssue(diagnostic, sql)!;

        var action = Assert.Single(CodeActionService.GetCodeActions(DocumentUri, sql, new[] { issue }, null));
        var edit = Assert.Single(action.Edit!.Changes![DocumentUri]);
        var applied = sql[..edit.Range.Start.Character] + edit.NewText + sql[edit.Range.End.Character..];
        Assert.Equal("SELECT 1;", applied);
    }

    [Fact]
    public void CodeAction_LintServiceCarriesParserSuggestedFixInDiagnosticData()
    {
        const string sql = "SELEC 1;";
        var diagnostic = Assert.Single(LintService.Lint(sql, CreateSchema(), SqlDialect.Netezza), item => item.Code == "PAR004");
        var issue = CodeActionService.ToLintIssue(diagnostic, sql);

        Assert.NotNull(issue);
        Assert.Equal("SELECT", issue!.SuggestedFix);
        Assert.NotEmpty(CodeActionService.GetCodeActions(DocumentUri, sql, new[] { issue }, CreateSchema()));
    }

    [Fact]
    public void CodeAction_Sql012UsesNearbyTokenWhenDiagnosticRangeIsOneCharacter()
    {
        const string sql = "CREATE TABLE t (name VARCHAR);";
        var varchar = sql.IndexOf("VARCHAR", StringComparison.Ordinal);
        var diagnostic = new Diagnostic(
            new LspRange(new Position(0, varchar), new Position(0, varchar + 1)),
            DiagnosticSeverity.Warning,
            "SQL012",
            Source: "Netezza",
            Message: "VARCHAR should specify a length",
            Data: new Dictionary<string, object?>
            {
                ["startOffset"] = varchar,
                ["endOffset"] = varchar + 1
            });
        var issue = CodeActionService.ToLintIssue(diagnostic, sql)!;

        var action = Assert.Single(CodeActionService.GetCodeActions(DocumentUri, sql, new[] { issue }, null));
        var edit = Assert.Single(action.Edit!.Changes![DocumentUri]);
        var applied = sql[..edit.Range.Start.Character] + edit.NewText + sql[edit.Range.End.Character..];
        Assert.Equal("CREATE TABLE t (name VARCHAR(100));", applied);
    }

    [Fact]
    public void CodeAction_IntersectsOnlyRequestedRange()
    {
        const string sql = "SELECT * FROM t;\nDELETE FROM t;";
        var first = new Diagnostic(new LspRange(new Position(0, 7), new Position(0, 8)), DiagnosticSeverity.Warning, "NZ001", null, "star");
        var second = new Diagnostic(new LspRange(new Position(1, 0), new Position(1, 6)), DiagnosticSeverity.Error, "SQL043", null, "delete");

        Assert.True(CodeActionService.IntersectsRange(first, new LspRange(new Position(0, 0), new Position(0, 15)), sql));
        Assert.False(CodeActionService.IntersectsRange(second, new LspRange(new Position(0, 0), new Position(0, 15)), sql));
    }

    [Fact]
    public void InlayHints_QualifiedColumn_ShowsType()
    {
        var hints = InlayHintService.GetInlayHints(
            "SELECT E.EMPLOYEE_ID FROM EMPLOYEES E",
            CreateSchema(),
            SqlDialect.Netezza);

        var hint = Assert.Single(hints);
        Assert.Equal(": INT4", hint.Label);
        Assert.Equal(InlayHintKind.Type, hint.Kind);
        Assert.Equal(new Position(0, "SELECT E.EMPLOYEE_ID".Length), hint.Position);
    }

    [Fact]
    public void InlayHints_UnqualifiedUniqueColumn_ShowsType()
    {
        var hints = InlayHintService.GetInlayHints(
            "SELECT FIRST_NAME FROM EMPLOYEES",
            CreateSchema(),
            SqlDialect.Netezza);

        Assert.Equal(": VARCHAR(50)", Assert.Single(hints).Label);
    }

    [Fact]
    public void InlayHints_MultipleStatements_ReturnHintsForEachSelect()
    {
        const string sql = "SELECT EMPLOYEE_ID FROM EMPLOYEES; SELECT FIRST_NAME FROM EMPLOYEES;";

        var hints = InlayHintService.GetInlayHints(sql, CreateSchema(), SqlDialect.Netezza);

        Assert.Equal(new[] { ": INT4", ": VARCHAR(50)" }, hints.Select(hint => hint.Label));
        Assert.Equal(2, hints.Select(hint => hint.Position).Distinct().Count());
    }

    [Fact]
    public void InlayHints_RequestedRangeFiltersOtherStatements()
    {
        const string firstLine = "SELECT EMPLOYEE_ID FROM EMPLOYEES;";
        var sql = firstLine + "\nSELECT FIRST_NAME FROM EMPLOYEES;";
        var range = new LspRange(new Position(1, 0), new Position(1, sql.Length - firstLine.Length - 1));

        var hints = InlayHintService.GetInlayHints(sql, CreateSchema(), SqlDialect.Netezza, range);

        Assert.Equal(": VARCHAR(50)", Assert.Single(hints).Label);
    }

    [Fact]
    public void InlayHints_UnknownColumn_ReturnsEmpty()
    {
        var hints = InlayHintService.GetInlayHints(
            "SELECT BAD_COL FROM EMPLOYEES",
            CreateSchema(),
            SqlDialect.Netezza);

        Assert.Empty(hints);
    }

    [Fact]
    public void InlayHints_WithoutSchema_ReturnsEmpty()
    {
        Assert.Empty(InlayHintService.GetInlayHints("SELECT X FROM T", schema: null, SqlDialect.Netezza));
    }

    [Fact]
    public void InlayHints_ParseError_ReturnsEmpty()
    {
        Assert.Empty(InlayHintService.GetInlayHints("SELECT FROM", CreateSchema(), SqlDialect.Netezza));
    }

    [Fact]
    public void Formatting_SimpleSelect_ReturnsCanonicalSql()
    {
        var formatted = FormattingService.FormatDocument("select employee_id from employees;", SqlDialect.Netezza);

        Assert.NotNull(formatted);
        Assert.Contains("SELECT", formatted, StringComparison.Ordinal);
        Assert.EndsWith(";", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Formatting_CommentOnStatementIsSupported()
    {
        var formatted = FormattingService.FormatDocument("COMMENT ON TABLE employees IS 'sample';", SqlDialect.Netezza);

        Assert.NotNull(formatted);
        Assert.Contains("COMMENT ON TABLE", formatted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formatting_Comments_ReturnsNull()
    {
        Assert.Null(FormattingService.FormatDocument("-- comment\nSELECT 1;", SqlDialect.Netezza));
        Assert.Null(FormattingService.FormatDocument("SELECT 1 /* inline */;", SqlDialect.Netezza));
    }

    [Fact]
    public void Formatting_ParseError_ReturnsNull()
    {
        Assert.Null(FormattingService.FormatDocument("SELECT FROM;", SqlDialect.Netezza));
    }

    [Fact]
    public void Formatting_EmptyDocument_ReturnsNull()
    {
        Assert.Null(FormattingService.FormatDocument("", SqlDialect.Netezza));
    }
}
