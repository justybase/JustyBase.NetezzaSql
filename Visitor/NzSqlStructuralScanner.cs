using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Linter;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Visitor;

public static class NzSqlStructuralScanner
{
    public static IEnumerable<ValidationError> Scan(string sql)
    {
        // PAR002: Consecutive commas
        for (int i = 0; i < sql.Length - 1; i++)
        {
            if (sql[i] == ',' && sql[i + 1] == ',')
            {
                if (LintHelpers.IsInsideStringOrComment(sql, i)) continue;
                var pos = SourcePosition.FromOffset(sql, i);
                yield return new ValidationError(
                    "Consecutive commas are not allowed",
                    "error", pos, "PAR002", pos.Line, pos.Column + 2);
            }
        }

        // PAR110: Unclosed string literal (odd number of single quotes)
        var inString = false;
        var stringStart = 0;
        var blockCommentDepth = 0;
        for (int i = 0; i < sql.Length; i++)
        {
            if (blockCommentDepth > 0)
            {
                if (i < sql.Length - 1 && sql[i] == '*' && sql[i + 1] == '/')
                {
                    blockCommentDepth--;
                    i++;
                }
                continue;
            }
            if (i < sql.Length - 1 && sql[i] == '/' && sql[i + 1] == '*')
            {
                blockCommentDepth++;
                i++;
                continue;
            }
            if (i < sql.Length - 1 && sql[i] == '-' && sql[i + 1] == '-')
            {
                // Skip to end of line
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }
            if (sql[i] == '\'')
            {
                if (!inString)
                {
                    inString = true;
                    stringStart = i;
                }
                else
                {
                    // Check for '' escape
                    if (i < sql.Length - 1 && sql[i + 1] == '\'')
                        i++;
                    else
                        inString = false;
                }
            }
        }
        if (inString)
        {
            var pos = SourcePosition.FromOffset(sql, stringStart);
            yield return new ValidationError(
                "Unclosed string literal — missing closing single quote",
                "error", pos, "PAR110", pos.Line, pos.Column + 1,
                SuggestedFix: "'");
        }

        // PAR111: Unclosed block comment
        var bcPos = -1;
        for (int i = 0; i < sql.Length - 1; i++)
        {
            if (sql[i] == '/' && sql[i + 1] == '*')
            {
                bcPos = i;
                blockCommentDepth = 1;
                i++;
                while (i < sql.Length - 1 && blockCommentDepth > 0)
                {
                    if (sql[i] == '/' && sql[i + 1] == '*')
                    {
                        blockCommentDepth++;
                        i++;
                    }
                    else if (sql[i] == '*' && sql[i + 1] == '/')
                    {
                        blockCommentDepth--;
                        i++;
                    }
                    i++;
                }
                break;
            }
        }
        if (bcPos >= 0 && blockCommentDepth > 0)
        {
            var pos = SourcePosition.FromOffset(sql, bcPos);
            yield return new ValidationError(
                "Unclosed block comment — missing */",
                "error", pos, "PAR111", pos.Line, pos.Column + 2,
                SuggestedFix: "*/");
        }

        Token<NzToken>[] tokens;
        try
        {
            tokens = NzLexer.Tokenize(sql).ToArray();
        }
        catch (Superpower.ParseException)
        {
            yield break;
        }

        // PAR003: Duplicate keywords
        var duplicateKeywords = new HashSet<NzToken>
        {
            NzToken.From, NzToken.Where, NzToken.Join, NzToken.On,
            NzToken.Select, NzToken.Insert, NzToken.Update, NzToken.Delete,
            NzToken.Create, NzToken.Drop, NzToken.Alter, NzToken.With,
            NzToken.GroupBy, NzToken.OrderBy, NzToken.Having, NzToken.Limit,
            NzToken.Offset, NzToken.Union, NzToken.Intersect, NzToken.Except
        };
        for (var i = 1; i < tokens.Length; i++)
        {
            if (tokens[i - 1].Kind != tokens[i].Kind || !duplicateKeywords.Contains(tokens[i].Kind))
                continue;

            var token = tokens[i];
            var pos = SourcePosition.FromToken(token);
            var image = token.ToStringValue().ToString().ToUpperInvariant();
            yield return new ValidationError(
                $"Duplicate '{image}' keyword detected. Remove the extra keyword.",
                "error", pos, "PAR003", pos.Line, pos.Column + Math.Max(image.Length, 1));
        }

        // PAR112: Mismatched parentheses (basic counting)
        var parenDepth = 0;
        var lastLParenPos = -1;
        for (var i = 0; i < tokens.Length; i++)
        {
            var tok = tokens[i];
            if (tok.Kind == NzToken.LParen)
            {
                if (parenDepth == 0) lastLParenPos = i;
                parenDepth++;
            }
            else if (tok.Kind == NzToken.RParen)
            {
                parenDepth--;
            }
        }
        if (parenDepth > 0 && lastLParenPos >= 0)
        {
            var token = tokens[lastLParenPos];
            var pos = SourcePosition.FromToken(token);
            yield return new ValidationError(
                "Mismatched parentheses — more '(' than ')'",
                "warning", pos, "PAR112", pos.Line, pos.Column + 1);
        }
    }
}
