using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.NetezzaSqlParser.Parser;

public partial class NzSqlParser
{
    private Token<NzToken>[] _tokens;
    private int _pos;
    private readonly List<ValidationError> _errors = new();
    private static readonly KeywordTypoChecker _typoChecker = new();

    public NzSqlParser(Token<NzToken>[] tokens)
    {
        _tokens = tokens;
    }

    public IReadOnlyList<ValidationError> Errors => _errors
        .Select(error => error.Code is "PARSE001" or "PARSE002"
            ? error with { Code = "PAR001" }
            : error)
        .ToList();

    public int Position => _pos;

    public void ResetTokens(Token<NzToken>[] tokens)
    {
        _tokens = tokens;
        _pos = 0;
        _errors.Clear();
    }

    private Token<NzToken> Peek(int ahead = 0) =>
        _pos + ahead < _tokens.Length ? _tokens[_pos + ahead] :
            new Token<NzToken>(NzToken.Unknown, TextSpan.Empty);

    private Token<NzToken> Advance() => _tokens[_pos++];

    private bool Match(NzToken kind)
    {
        if (Peek().Kind == kind) { _pos++; return true; }
        return false;
    }

    private static bool IsClauseStartKeyword(NzToken kind) => kind switch
    {
        NzToken.From or NzToken.Where or NzToken.GroupBy or NzToken.Having
            or NzToken.OrderBy or NzToken.Limit or NzToken.Into or NzToken.Fetch
            or NzToken.Union or NzToken.Intersect or NzToken.Except or NzToken.MinusSet
            or NzToken.Semicolon => true,
        _ => false
    };

    private static readonly HashSet<NzToken> SyncTokensExpr = new()
    {
        NzToken.Semicolon, NzToken.RParen, NzToken.Comma,
        NzToken.From, NzToken.Where, NzToken.GroupBy, NzToken.Having,
        NzToken.OrderBy, NzToken.Limit, NzToken.Fetch,
        NzToken.Union, NzToken.Intersect, NzToken.Except, NzToken.MinusSet,
        NzToken.And, NzToken.Or, NzToken.Then, NzToken.Else, NzToken.End,
        NzToken.Unknown
    };

    private void SynchronizeTo(HashSet<NzToken> syncSet)
    {
        while (!syncSet.Contains(Peek().Kind))
            Advance();
    }

    private void SynchronizeStatement()
    {
        while (Peek().Kind != NzToken.Semicolon && Peek().Kind != NzToken.Unknown)
        {
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                var depth = 1;
                while (depth > 0 && Peek().Kind != NzToken.Unknown)
                {
                    if (Peek().Kind == NzToken.LParen) depth++;
                    else if (Peek().Kind == NzToken.RParen) depth--;
                    Advance();
                }
            }
            else
            {
                Advance();
            }
        }
    }

    private static bool IsContextualIdentifier(NzToken kind) => kind is
        NzToken.Identifier or NzToken.QuotedIdentifier or NzToken.Replace
        or NzToken.Owner or NzToken.Hash or NzToken.Start or NzToken.Out or NzToken.Inout
        or NzToken.Perform or NzToken.Reverse or NzToken.Warning or NzToken.Within;

    private bool IsSetOperationStart() => Peek().Kind is NzToken.Union or NzToken.Intersect or NzToken.Except or NzToken.MinusSet
        || (Peek().Kind == NzToken.Identifier &&
            string.Equals(Peek().ToStringValue(), "MINUS", StringComparison.OrdinalIgnoreCase));

    private static bool IsUnambiguousTableSourceEnd(NzToken kind) => kind switch
    {
        NzToken.Where or NzToken.GroupBy or NzToken.OrderBy or NzToken.Having
            or NzToken.Limit or NzToken.Fetch or NzToken.Union or NzToken.Intersect
            or NzToken.Except or NzToken.MinusSet or NzToken.Semicolon => true,
        _ => false
    };

    private Token<NzToken> Expect(NzToken kind, string? context = null)
    {
        var t = Peek();
        if (t.Kind == kind) { _pos++; return t; }
        var msg = context ?? $"Expected {DescribeToken(kind)}, got {DescribeToken(t)}";
        _errors.Add(new ValidationError(msg, "error",
            SourcePosition.FromToken(t), "PARSE001",
            EndLine: t.Position.Line,
            EndColumn: t.Position.Column + Math.Max(t.Span.Length, 1)));
        return t;
    }

    private static string DescribeToken(Token<NzToken> t) =>
        t.Kind == NzToken.Unknown ? "end of input" : t.Kind.ToString();

    private static string DescribeToken(NzToken kind) =>
        kind == NzToken.Unknown ? "end of input" : kind.ToString();

    private void AddParserError(string message, Token<NzToken> t, string code)
    {
        _errors.Add(new ValidationError(message, "error",
            SourcePosition.FromToken(t), code,
            EndLine: t.Position.Line,
            EndColumn: t.Position.Column + Math.Max(t.Span.Length, 1)));
    }

    private static (int EndLine, int EndColumn) EndOfToken(Token<NzToken> t) =>
        (t.Position.Line, t.Position.Column + Math.Max(t.Span.Length, 1));

    private SourcePosition FromToken(Token<NzToken> t) => SourcePosition.FromToken(t);

    // ====== Top-Level Dispatch ======

    public Statement? Parse()
    {
        SkipSemicolons();
        if (_pos >= _tokens.Length)
            return null;
        var k = Peek().Kind;
        Statement? result = k switch
        {
            NzToken.Select or NzToken.LParen => ParseSelect(),
            NzToken.With => ParseWithTopLevel(),
            NzToken.Insert => ParseInsert(),
            NzToken.Update => ParseUpdate(),
            NzToken.Delete => ParseDelete(),
            NzToken.Create => ParseCreate(),
            NzToken.Drop => ParseDrop(),
            NzToken.Alter => ParseAlterTable(),
            NzToken.Truncate => ParseTruncate(),
            NzToken.Explain => ParseExplain(),
            NzToken.Groom => ParseGroom(),
            NzToken.Generate => ParseGenerateStatistics(),
            NzToken.Comment => ParseCommentOn(),
            NzToken.Grant => ParseGrant(),
            NzToken.Revoke => ParseRevoke(),
            NzToken.Commit => ParseCommit(),
            NzToken.Rollback => ParseRollback(),
            NzToken.Call or NzToken.Exec or NzToken.Execute => ParseCall(),
            NzToken.Merge => ParseMerge(),
            NzToken.Lock or NzToken.Show or NzToken.Copy or NzToken.Reindex
                or NzToken.Reset or NzToken.Begin or NzToken.Set
                or NzToken.AtSet => ParseCommandTailFallback(Peek()),
            _ => ReportUnexpectedTopLevelToken()
        };

        // After any failed parse (null result), synchronize to the next statement
        if (result is null)
            SynchronizeStatement();

        return result;
    }

    private Statement? ReportUnexpectedTopLevelToken()
    {
        var t = Peek();
        var typoSuggestion = _typoChecker.CheckTypo(t.ToStringValue());
        var msg = typoSuggestion is not null
            ? $"Unexpected token {t.Kind}. Did you mean '{typoSuggestion}'?"
            : $"Unexpected token {DescribeToken(t)}";
        _errors.Add(new ValidationError(msg, "error",
            SourcePosition.FromToken(t),
            typoSuggestion is not null ? "PAR004" : "PAR001",
            EndLine: t.Position.Line,
            EndColumn: t.Position.Column + Math.Max(t.Span.Length, 1),
            SuggestedFix: typoSuggestion));
        Advance();
        return null;
    }

    private Statement? ParseWithTopLevel()
    {
        var with = ParseWithClause();
        if (Peek().Kind == NzToken.Select)
            return ParseSelectStatement(with);
        if (Peek().Kind == NzToken.Insert)
            return ParseInsertWithClause(with);
        AddParserError("WITH clause must be followed by SELECT or INSERT",
            Peek(), "PAR109");
        return null;
    }

    private void SkipSemicolons()
    {
        while (Peek().Kind == NzToken.Semicolon) Advance();
    }

    // ====== Helpers ======

    private static string FormatTableName(TableName t)
    {
        if (t.Database is not null && t.Schema is not null) return $"{t.Database}.{t.Schema}.{t.Name}";
        if (t.Database is not null) return $"{t.Database}..{t.Name}";
        if (t.Schema is not null) return $"{t.Schema}.{t.Name}";
        return t.Name;
    }

    private string ParseIdentifier()
    {
        var t = ExpectNameToken();
        return t.ToStringValue();
    }

    private bool IsGroupKeyword()
    {
        var k = Peek().Kind;
        if (k == NzToken.Identifier)
            return string.Equals(Peek().ToStringValue(), "GROUP", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private void ParseCommandTail()
    {
        while (Peek().Kind != NzToken.Semicolon && Peek().Kind != NzToken.Unknown)
        {
            // Consume balanced parentheses within command tail
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                var depth = 1;
                while (depth > 0 && Peek().Kind != NzToken.Unknown)
                {
                    if (Peek().Kind == NzToken.LParen) depth++;
                    else if (Peek().Kind == NzToken.RParen) depth--;
                    Advance();
                }
            }
            else
            {
                Advance();
            }
        }
    }

    private Statement ParseCommandTailFallback(Token<NzToken> startTok)
    {
        ParseCommandTail();
        return new SetStatement(FromToken(startTok));
    }
}
