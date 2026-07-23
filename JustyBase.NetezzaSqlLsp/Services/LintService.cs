using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.NetezzaSqlLsp.Protocol;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>Produces LSP diagnostics for a SQL document.</summary>
public static class LintService
{
    /// <summary>Lints the given SQL text using regex rules and semantic (parser + visitor) validation.</summary>
    /// <param name="sql">The SQL source text.</param>
    /// <param name="schema">Optional schema provider for semantic validation.</param>
    /// <returns>A list of LSP diagnostics.</returns>
    public static IReadOnlyList<Diagnostic> Lint(string sql, ISchemaProvider? schema)
    {
        var issues = new List<Diagnostic>();

        if (string.IsNullOrEmpty(sql))
            return issues;

        // Pre-compute line start offsets for O(1) position conversion
        var lineOffsets = ComputeLineOffsets(sql);

        // 1. Text-based regex rules (NZ001-NZ020, NZP001-NZP013)
        foreach (var rule in NzLintRules.AllRules)
        {
            foreach (var result in rule.Check(sql))
            {
                issues.Add(MapLintIssue(result, sql, lineOffsets));
            }
        }

        // 2. Parser + visitor semantic validation
        if (schema is not null)
        {
            try
            {
                var tokens = NzLexer.Tokenize(sql).ToArray();
                var parser = new NzSqlParser(tokens);
                Statement? stmt;

                // Dedup set for parser errors
                var seenParserErrors = new HashSet<(string message, int offset)>();

                while (true)
                {
                    var errorsBefore = parser.Errors.Count;
                    stmt = parser.Parse();

                    for (int i = errorsBefore; i < parser.Errors.Count; i++)
                    {
                        var perr = parser.Errors[i];
                        if (perr.Position.Absolute >= sql.Length) continue;
                        if (!seenParserErrors.Add((perr.Message, perr.Position.Absolute)))
                            continue;
                        issues.Add(MapParserError(perr, sql, lineOffsets));
                    }

                    if (stmt is null) break;

                    var visitor = new NzSqlVisitor(schema);
                    visitor.Visit(stmt);

                    foreach (var err in visitor.Errors)
                    {
                        if (err.Position.Absolute >= sql.Length) continue;
                        issues.Add(MapVisitorError(err, sql, lineOffsets));
                    }
                }
            }
            catch
            {
                // Parser errors are non-fatal
            }
        }

        return issues;
    }

    private static int[] ComputeLineOffsets(string text)
    {
        var offsets = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                offsets.Add(i + 1);
        }
        return offsets.ToArray();
    }

    private static Position OffsetToPosition(int offset, int[] lineOffsets)
    {
        if (offset <= 0) return new Position(0, 0);
        int line = 0;
        for (int i = 1; i < lineOffsets.Length; i++)
        {
            if (lineOffsets[i] > offset)
            {
                line = i - 1;
                return new Position(line, offset - lineOffsets[line]);
            }
        }
        line = lineOffsets.Length - 1;
        return new Position(line, offset - lineOffsets[line]);
    }

    private static Diagnostic MapLintIssue(LintIssue issue, string sql, int[] lineOffsets)
    {
        var startPos = OffsetToPosition(issue.StartOffset, lineOffsets);
        int endOffset = Math.Min(issue.EndOffset, sql.Length);
        var endPos = OffsetToPosition(endOffset, lineOffsets);

        if (startPos.Line == endPos.Line && startPos.Character == endPos.Character)
        {
            endPos = new Position(endPos.Line, endPos.Character + 1);
        }

        return new Diagnostic(
            new Protocol.Range(startPos, endPos),
            issue.Severity == LintSeverity.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            issue.RuleId,
            "Netezza SQL",
            issue.Message
        );
    }

    private static Diagnostic MapParserError(ValidationError perr, string sql, int[] lineOffsets)
    {
        var start = OffsetToPosition(perr.Position.Absolute, lineOffsets);
        int endOffset = perr.EndColumn > 0
            ? perr.Position.Absolute + Math.Max(perr.EndColumn - perr.Position.Column, 1)
            : perr.Position.Absolute + 1;
        var end = OffsetToPosition(Math.Min(endOffset, sql.Length), lineOffsets);

        Dictionary<string, object?>? data = null;
        if (perr.SuggestedFix is not null)
            data = new Dictionary<string, object?> { ["suggestedFix"] = perr.SuggestedFix };

        return new Diagnostic(
            new Protocol.Range(start, end),
            perr.Severity == "error" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            perr.Code,
            "Netezza SQL",
            perr.Message,
            Data: data
        );
    }

    private static Diagnostic MapVisitorError(ValidationError err, string sql, int[] lineOffsets)
    {
        var start = OffsetToPosition(err.Position.Absolute, lineOffsets);
        int endOffset;
        if (err.EndColumn > 0)
        {
            var tokenLength = err.EndColumn - err.Position.Column;
            endOffset = err.Position.Absolute + Math.Max(tokenLength, 1);
        }
        else
        {
            endOffset = err.Position.Absolute + 1;
        }
        var end = OffsetToPosition(Math.Min(endOffset, sql.Length), lineOffsets);

        return new Diagnostic(
            new Protocol.Range(start, end),
            err.Severity == "error" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            err.Code,
            "Netezza SQL",
            err.Message
        );
    }
}
