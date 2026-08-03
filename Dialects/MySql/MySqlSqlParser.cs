using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Parser;

/// <summary>MySQL 8 parser for the common high-frequency SQL surface.</summary>
public sealed partial class MySqlSqlParser : NzSqlParser
{
    public MySqlSqlParser(Token<NzToken>[] tokens) : base(tokens)
    {
    }

    public override Statement? Parse()
    {
        SkipSemicolons();
        if (_pos >= _tokens.Length)
            return null;

        if (Peek().Kind == NzToken.Merge)
        {
            AddParserError("MERGE is not supported by MySQL", Peek(), "PAR001");
            Advance();
            return null;
        }

        if (Peek().Kind is NzToken.Groom or NzToken.Generate or NzToken.Distribute or NzToken.Organize)
        {
            AddParserError($"{Peek().Kind} is not supported by MySQL", Peek(), "PAR001");
            Advance();
            return null;
        }

        var result = base.Parse();
        if (result is null)
            SynchronizeStatement();
        return result;
    }

    protected override (TableName Table, Token<NzToken> FirstToken) ParseTableName()
    {
        var first = ExpectNameToken();
        var (firstName, firstQuote) = ParseTableIdentifier(first);
        if (Peek().Kind != NzToken.Dot)
            return (new TableName(firstName, NameQuote: firstQuote), first);

        Advance();
        if (Peek().Kind == NzToken.Dot)
        {
            AddParserError("Empty qualified-name segment (..) is not supported in MySQL", Peek(), "PAR001");
            Advance();
        }

        var second = ExpectNameToken();
        var (secondName, secondQuote) = ParseTableIdentifier(second);
        if (Peek().Kind == NzToken.Dot)
        {
            AddParserError("MySQL qualified names contain at most database.table", Peek(), "PAR001");
            while (Peek().Kind == NzToken.Dot)
            {
                Advance();
                if (Peek().Kind != NzToken.Unknown && Peek().Kind != NzToken.Semicolon)
                    Advance();
            }
        }

        return (new TableName(secondName, Database: firstName,
            MySqlDatabaseQualified: true, NameQuote: secondQuote, DatabaseQuote: firstQuote), first);
    }

    private static (string Name, char? Quote) ParseTableIdentifier(Token<NzToken> token)
    {
        var value = token.ToStringValue();
        if (token.Kind == NzToken.MySqlBacktickIdentifier)
            return (StripQuotes(value), '`');

        return (StripQuotes(value), null);
    }

    protected override LimitClause ParseLimitClause()
    {
        var limitToken = Advance();
        if (Peek().Kind != NzToken.NumberLiteral)
        {
            AddParserError("Expected number after LIMIT", Peek(), "PAR001");
            return new LimitClause(FromToken(limitToken), 0, null);
        }

        var first = int.Parse(Advance().ToStringValue());
        if (Peek().Kind == NzToken.Comma)
        {
            Advance();
            var hasSecond = Peek().Kind == NzToken.NumberLiteral;
            var second = hasSecond
                ? int.Parse(Advance().ToStringValue())
                : 0;
            if (!hasSecond)
                AddParserError("Expected row count after LIMIT offset comma", Peek(), "PAR001");
            return new LimitClause(FromToken(limitToken), second, first, LimitClauseSyntax.MySqlComma);
        }

        int? offset = null;
        if (Peek().Kind == NzToken.Offset)
        {
            Advance();
            if (Peek().Kind == NzToken.NumberLiteral)
                offset = int.Parse(Advance().ToStringValue());
            else
                AddParserError("Expected number after LIMIT OFFSET", Peek(), "PAR001");
        }

        return new LimitClause(FromToken(limitToken), first, offset);
    }

    protected override OffsetFetchClause ParseOffsetFetchClause()
    {
        var clause = base.ParseOffsetFetchClause();
        AddParserError("ANSI OFFSET/FETCH is not supported by MySQL; use LIMIT", Peek(), "PAR001");
        return clause;
    }

    protected override Expression? TryParseDialectPrimary()
    {
        if (Peek().Kind == NzToken.If && Peek(1).Kind == NzToken.LParen)
            return ParseFunctionCall(Advance());
        return null;
    }

    protected override DataTypeInfo ParseDataType()
    {
        var first = Peek();
        if (first.Kind is not (NzToken.Identifier or NzToken.QuotedIdentifier or NzToken.MySqlBacktickIdentifier
            or NzToken.Set or NzToken.Type or NzToken.Value))
            return base.ParseDataType();

        Advance();
        var name = StripQuotes(first.ToStringValue());
        var parameters = new List<string>();
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var depth = 1;
            while (depth > 0 && Peek().Kind != NzToken.Unknown)
            {
                if (Peek().Kind == NzToken.LParen) depth++;
                else if (Peek().Kind == NzToken.RParen)
                {
                    depth--;
                    if (depth == 0)
                    {
                        Advance();
                        break;
                    }
                }
                if (depth > 0)
                    parameters.Add(Advance().ToStringValue());
            }
        }

        return new DataTypeInfo(FromToken(first), name,
            parameters.Count == 0 ? null : parameters);
    }

    protected override bool IsDialectColumnClauseStart()
    {
        if (Peek().Kind is NzToken.Comment or NzToken.On)
            return true;

        return Peek().Kind == NzToken.Identifier &&
            (Peek().ToStringValue().Equals("AUTO_INCREMENT", StringComparison.OrdinalIgnoreCase)
             || Peek().ToStringValue().Equals("COLLATE", StringComparison.OrdinalIgnoreCase)
             || Peek().ToStringValue().Equals("GENERATED", StringComparison.OrdinalIgnoreCase));
    }

    protected override bool TryParseDialectColumnClause()
    {
        if (!IsDialectColumnClauseStart())
            return false;

        var depth = 0;
        while (Peek().Kind is not (NzToken.Comma or NzToken.RParen or NzToken.Unknown or NzToken.Semicolon))
        {
            if (Peek().Kind == NzToken.LParen) depth++;
            else if (Peek().Kind == NzToken.RParen && depth > 0) depth--;
            var token = Advance();
            CurrentDialectColumnTokens?.Add(token);
        }
        return true;
    }

    protected override IReadOnlyList<Token<NzToken>>? ParseDialectTableOptions()
    {
        if (Peek().Kind is NzToken.Unknown or NzToken.Semicolon)
            return null;

        var options = new List<Token<NzToken>>();
        while (Peek().Kind is not (NzToken.Unknown or NzToken.Semicolon))
            options.Add(Advance());
        return options.Count == 0 ? null : options;
    }
}
