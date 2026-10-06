using System.Text.Json;
using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;
using LspRange = JustyBase.NetezzaSqlLsp.Protocol.Range;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>
/// Builds LSP code actions from lint issues using the authoring quick-fix table.
/// Each result is reduced to a minimal single text edit.
/// </summary>
public static class CodeActionService
{
    /// <summary>Reconstructs a domain <see cref="LintIssue"/> from a published diagnostic.</summary>
    public static LintIssue? ToLintIssue(Diagnostic diagnostic, string sql)
    {
        var code = diagnostic.Code;
        if (string.IsNullOrEmpty(code))
            return null;

        var start = ReadInt(diagnostic, "startOffset")
            ?? LspTextUtilities.PositionToOffset(sql, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);
        var end = ReadInt(diagnostic, "endOffset")
            ?? LspTextUtilities.PositionToOffset(sql, diagnostic.Range.End.Line, diagnostic.Range.End.Character);
        if (end < start)
            end = start;

        var severity = diagnostic.Severity == DiagnosticSeverity.Error
            ? LintSeverity.Error
            : LintSeverity.Warning;

        return new LintIssue(
            code,
            diagnostic.Message,
            severity,
            start,
            end,
            SuggestedFix: ReadSuggestedFix(diagnostic));
    }

    /// <summary>Returns quick-fix code actions for the given issues.</summary>
    public static CodeAction[] GetCodeActions(
        string uri,
        string sql,
        IReadOnlyList<LintIssue> issues,
        ISchemaProvider? schema)
    {
        if (string.IsNullOrEmpty(sql) || issues.Count == 0)
            return Array.Empty<CodeAction>();

        var lineStarts = LspTextUtilities.ComputeLineStarts(sql);
        var actions = new List<CodeAction>();

        foreach (var issue in issues)
        {
            var fix = NzLintCodeActions.GetQuickFix(issue, sql, schema);
            if (fix is null)
                continue;

            var updated = fix.Value.Apply(sql);
            if (string.Equals(updated, sql, StringComparison.Ordinal))
                continue;

            var diagnostic = new Diagnostic(
                LspTextUtilities.ToRange(issue.StartOffset, issue.EndOffset, lineStarts),
                issue.Severity == LintSeverity.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                issue.RuleId,
                Source: null,
                issue.Message);

            actions.Add(new CodeAction(
                fix.Value.Description,
                "quickfix",
                new[] { diagnostic },
                new WorkspaceEdit(new Dictionary<string, TextEdit[]>
                {
                    [uri] = new[] { CreateMinimalEdit(sql, updated) }
                }),
                IsPreferred: true));
        }

        var safeIssues = issues.Where(issue => NzLintCodeActions.IsSafeForFixAll(issue.RuleId)).ToArray();
        if (safeIssues.Length > 1)
        {
            // Diagnostics carry offsets for the original document. Applying a
            // structural fix can invalidate later offsets, so fix-all is one pass.
            var fixedAll = NzLintCodeActions.ApplyAllSafeFixes(sql, safeIssues, schema, maxPasses: 1);
            if (!string.Equals(fixedAll, sql, StringComparison.Ordinal))
            {
                actions.Add(new CodeAction(
                    "Fix all safe issues",
                    "source.fixAll",
                    Diagnostics: null,
                    new WorkspaceEdit(new Dictionary<string, TextEdit[]>
                    {
                        [uri] = new[] { CreateMinimalEdit(sql, fixedAll) }
                    }),
                    IsPreferred: false));
            }
        }

        return actions.ToArray();
    }

    /// <summary>Checks whether a diagnostic intersects the range requested by the client.</summary>
    public static bool IntersectsRange(Diagnostic diagnostic, LspRange requestedRange, string sql)
    {
        var start = ReadInt(diagnostic, "startOffset")
            ?? LspTextUtilities.PositionToOffset(sql, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);
        var end = ReadInt(diagnostic, "endOffset")
            ?? LspTextUtilities.PositionToOffset(sql, diagnostic.Range.End.Line, diagnostic.Range.End.Character);
        var requestedStart = LspTextUtilities.PositionToOffset(sql, requestedRange.Start.Line, requestedRange.Start.Character);
        var requestedEnd = LspTextUtilities.PositionToOffset(sql, requestedRange.End.Line, requestedRange.End.Character);
        return start <= requestedEnd && end >= requestedStart;
    }

    private static string? ReadSuggestedFix(Diagnostic diagnostic)
    {
        if (diagnostic.Data is null || !diagnostic.Data.TryGetValue("suggestedFix", out var value) || value is null)
            return null;

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element when element.ValueKind != JsonValueKind.Null => element.ToString(),
            _ => value.ToString()
        };
    }

    private static int? ReadInt(Diagnostic diagnostic, string key)
    {
        if (diagnostic.Data is null || !diagnostic.Data.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            int integer => integer,
            long integer when integer is >= int.MinValue and <= int.MaxValue => (int)integer,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var integer) => integer,
            _ => null
        };
    }

    private static TextEdit CreateMinimalEdit(string original, string updated)
    {
        var prefix = 0;
        var commonLength = Math.Min(original.Length, updated.Length);
        while (prefix < commonLength && original[prefix] == updated[prefix]) prefix++;

        // Keep UTF-16 surrogate pairs and CRLF sequences intact at edit edges.
        while (prefix > 0 && (SplitsSurrogatePair(original, prefix) || SplitsSurrogatePair(updated, prefix)
                              || SplitsCrLf(original, prefix) || SplitsCrLf(updated, prefix)))
            prefix--;

        var suffix = 0;
        while (suffix < original.Length - prefix
               && suffix < updated.Length - prefix
               && original[original.Length - suffix - 1] == updated[updated.Length - suffix - 1])
        {
            suffix++;
        }

        while (suffix > 0)
        {
            var oldBoundary = original.Length - suffix;
            var newBoundary = updated.Length - suffix;
            if (!SplitsSurrogatePair(original, oldBoundary) && !SplitsSurrogatePair(updated, newBoundary)
                && !SplitsCrLf(original, oldBoundary) && !SplitsCrLf(updated, newBoundary))
                break;
            suffix--;
        }

        var oldEnd = original.Length - suffix;
        var newEnd = updated.Length - suffix;

        var lineStarts = LspTextUtilities.ComputeLineStarts(original);
        return new TextEdit(
            LspTextUtilities.ToRange(prefix, oldEnd, lineStarts),
            updated[prefix..newEnd]);
    }

    private static bool SplitsSurrogatePair(string text, int boundary) =>
        boundary > 0 && boundary < text.Length
        && char.IsHighSurrogate(text[boundary - 1])
        && char.IsLowSurrogate(text[boundary]);

    private static bool SplitsCrLf(string text, int boundary) =>
        boundary > 0 && boundary < text.Length && text[boundary - 1] == '\r' && text[boundary] == '\n';
}
