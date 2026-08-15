using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Parser;

/// <summary>Strict parser for the SQLite SQL surface.</summary>
public sealed partial class SqliteSqlParser : NzSqlParser
{
    public SqliteSqlParser(Token<NzToken>[] tokens) : base(tokens)
    {
    }

    public override Statement? Parse()
    {
        SkipSemicolons();
        if (_pos >= _tokens.Length)
            return null;

        switch (Peek().Kind)
        {
            case NzToken.Merge:
                AddParserError("MERGE is not supported by SQLite; use INSERT ... ON CONFLICT", Peek(), "PAR001");
                Advance();
                return null;
            case NzToken.Groom or NzToken.Generate or NzToken.Distribute or NzToken.Organize:
                AddParserError($"{Peek().Kind} is not supported by SQLite", Peek(), "PAR001");
                Advance();
                return null;
            case NzToken.SqlitePragma or NzToken.SqliteAttach or NzToken.SqliteDetach
                or NzToken.SqliteVacuum or NzToken.SqliteSavepoint or NzToken.Reindex:
                // PRAGMA / ATTACH / DETACH / VACUUM / SAVEPOINT / REINDEX are
                // consumed as opaque command tails (same treatment the shared
                // parser gives SHOW / COPY / RESET).
                return ParseCommandTailFallback(Peek());
            case NzToken.Rollback when Peek(1).Kind == NzToken.To:
                // ROLLBACK TO [SAVEPOINT] name
                return ParseCommandTailFallback(Peek());
            case NzToken.Replace when Peek(1).Kind == NzToken.Into:
                // REPLACE INTO ... is SQLite shorthand for INSERT OR REPLACE.
                return ParseInsert();
        }

        // RELEASE [SAVEPOINT] name / ANALYZE [schema.]name — RELEASE and ANALYZE
        // are not shared keywords.
        if (Peek().Kind == NzToken.Identifier &&
            (Peek().ToStringValue().Equals("RELEASE", StringComparison.OrdinalIgnoreCase) ||
             Peek().ToStringValue().Equals("ANALYZE", StringComparison.OrdinalIgnoreCase)))
            return ParseCommandTailFallback(Peek());

        var result = base.Parse();
        if (result is null)
            SynchronizeStatement();
        return result;
    }

    protected override bool SupportsEmptyQualifiedNameSegment => false;

    protected override bool AllowWindowNameAfterOver => true;

    protected override Expression? TryParseDialectPrimary()
    {
        if (Peek().Kind == NzToken.SqliteBlobLiteral)
        {
            var token = Advance();
            return new Literal(FromToken(token), LiteralKind.Blob, token.ToStringValue());
        }

        return null;
    }

    protected override (TableName Table, Token<NzToken> FirstToken) ParseTableName()
    {
        var first = ExpectNameToken();
        var (firstName, firstQuote) = ParseSqliteIdentifier(first);
        if (Peek().Kind != NzToken.Dot)
            return (new TableName(firstName, NameQuote: firstQuote), first);

        Advance();
        if (Peek().Kind == NzToken.Dot)
        {
            AddParserError("SQLite names cannot contain an empty qualified-name segment (..)", Peek(), "PAR001");
            Advance();
        }

        var second = ExpectNameToken();
        var (secondName, secondQuote) = ParseSqliteIdentifier(second);
        if (Peek().Kind == NzToken.Dot)
        {
            AddParserError("SQLite names contain at most schema.table", Peek(), "PAR001");
            while (Peek().Kind == NzToken.Dot)
            {
                Advance();
                if (Peek().Kind != NzToken.Unknown && Peek().Kind != NzToken.Semicolon)
                    Advance();
            }
        }

        return (new TableName(secondName, Schema: firstName,
            NameQuote: secondQuote, SchemaQuote: firstQuote), first);
    }

    protected override DataTypeInfo ParseDataType()
    {
        var first = Peek();
        if (first.Kind is not (NzToken.Identifier or NzToken.QuotedIdentifier
            or NzToken.MySqlBacktickIdentifier or NzToken.SqliteBracketedIdentifier))
        {
            // SQLite columns may omit their declared type entirely.
            return new DataTypeInfo(FromToken(first), string.Empty, null);
        }

        Advance();
        var (firstName, _) = ParseSqliteIdentifier(first);
        var parts = new List<string> { firstName };
        // Multi-word affinity names such as DOUBLE PRECISION / CHARACTER VARYING.
        while (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
            parts.Add(StripQuotes(Advance().ToStringValue()));

        IReadOnlyList<string>? parameters = null;
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var args = new List<string>();
            while (Peek().Kind is not (NzToken.RParen or NzToken.Unknown))
            {
                if (Peek().Kind == NzToken.Comma)
                {
                    Advance();
                    continue;
                }
                args.Add(Advance().ToStringValue());
            }
            Expect(NzToken.RParen);
            parameters = args.Count == 0 ? null : args;
        }

        return new DataTypeInfo(FromToken(first), string.Join(" ", parts), parameters);
    }

    private static (string Name, char? Quote) ParseSqliteIdentifier(Token<NzToken> token)
    {
        var value = token.ToStringValue();
        switch (token.Kind)
        {
            case NzToken.QuotedIdentifier:
                return (StripQuotes(value), '"');
            case NzToken.MySqlBacktickIdentifier:
                return (value.Trim('`'), null);
            case NzToken.SqliteBracketedIdentifier:
                // [name] or [na]]me]
                return (value.Trim('[', ']').Replace("]]", "]", StringComparison.Ordinal), null);
            default:
                return (StripQuotes(value), null);
        }
    }

    protected override LimitClause ParseLimitClause()
    {
        // SQLite extends the ANSI form with: LIMIT expr, expr (offset, count)
        // and LIMIT expr OFFSET expr where either operand may be a bound
        // parameter (?) or an expression, not just a literal.
        var limitToken = Advance();
        int startIndex = _pos - 1;
        bool plainNumbers = true;
        var first = SqliteParseLimitOperand(ref plainNumbers);
        if (Peek().Kind == NzToken.Comma)
        {
            Advance();
            var second = SqliteParseLimitOperand(ref plainNumbers);
            var commaClause = new LimitClause(FromToken(limitToken), second, first, LimitClauseSyntax.MySqlComma);
            return plainNumbers ? commaClause : commaClause with { SqliteTokens = SliceTokens(startIndex, _pos) };
        }

        int? offset = null;
        if (Peek().Kind == NzToken.Offset)
        {
            Advance();
            offset = SqliteParseLimitOperand(ref plainNumbers);
        }

        var clause = new LimitClause(FromToken(limitToken), first, offset);
        return plainNumbers ? clause : clause with { SqliteTokens = SliceTokens(startIndex, _pos) };
    }

    private int SqliteParseLimitOperand(ref bool plainNumbers)
    {
        if (Peek().Kind == NzToken.Parameter)
        {
            plainNumbers = false;
            Advance();
            return 0;
        }
        if (Peek().Kind == NzToken.Minus && Peek(1).Kind == NzToken.NumberLiteral)
        {
            Advance();
            Advance();
            return -1;
        }
        if (Peek().Kind != NzToken.NumberLiteral)
        {
            plainNumbers = false;
            AddParserError("Expected number or parameter after LIMIT/OFFSET", Peek(), "PAR001");
            return 0;
        }
        return int.TryParse(Advance().ToStringValue(), out var value) ? value : 0;
    }

    private IReadOnlyList<Token<NzToken>> SliceTokens(int start, int end)
    {
        var tokens = new Token<NzToken>[end - start];
        Array.Copy(_tokens, start, tokens, 0, end - start);
        return tokens;
    }

    protected override OffsetFetchClause ParseOffsetFetchClause()
    {
        var clause = base.ParseOffsetFetchClause();
        AddParserError("ANSI OFFSET/FETCH is not supported by SQLite; use LIMIT", Peek(), "PAR001");
        return clause;
    }
}
