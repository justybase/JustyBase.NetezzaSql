using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Parser;

/// <summary>Strict parser for the PostgreSQL SQL surface.</summary>
public sealed partial class PostgreSqlSqlParser : NzSqlParser
{
    private bool _unsupportedSyntaxReported;

    public PostgreSqlSqlParser(Token<NzToken>[] tokens) : base(tokens)
    {
    }

    public override Statement? Parse()
    {
        // Report unsupported storage syntax even when it appears in a DDL
        // tail that the common parser intentionally leaves opaque. The lexer
        // still gives these words a dedicated token, but they remain usable as
        // identifiers in ordinary PostgreSQL expressions and relation names.
        if (!_unsupportedSyntaxReported)
        {
            var statementStart = true;
            var sawGroom = false;
            for (var i = 0; i < _tokens.Length; i++)
            {
                var token = _tokens[i];
                if (token.Kind == NzToken.Semicolon)
                {
                    statementStart = true;
                    sawGroom = false;
                    continue;
                }

                if (token.Kind != NzToken.PostgreSqlUnsupportedNetezza)
                {
                    statementStart = false;
                    continue;
                }

                var next = i + 1 < _tokens.Length ? _tokens[i + 1].Kind : NzToken.Unknown;
                var word = token.ToStringValue();
                var isStorageClause = statementStart
                    || (next == NzToken.On && !word.Equals("GROOM", StringComparison.OrdinalIgnoreCase))
                    || (word.Equals("GROOM", StringComparison.OrdinalIgnoreCase) && next == NzToken.Table)
                    || (sawGroom && (word.Equals("VERSIONS", StringComparison.OrdinalIgnoreCase)
                        || word.Equals("RECLAIM", StringComparison.OrdinalIgnoreCase)
                        || word.Equals("BACKUPSET", StringComparison.OrdinalIgnoreCase)));
                if (isStorageClause)
                    AddParserError($"{word} is not supported by PostgreSQL", token, "PAR001");

                sawGroom |= word.Equals("GROOM", StringComparison.OrdinalIgnoreCase);
                statementStart = false;
            }
            _unsupportedSyntaxReported = true;
        }

        var result = base.Parse();
        if (result is null)
            SynchronizeStatement();
        return result;
    }

    protected override bool SupportsEmptyQualifiedNameSegment => false;

    protected override (TableName Table, Token<NzToken> FirstToken) ParseTableName()
    {
        var first = ExpectNameToken();
        var firstName = StripQuotes(first.ToStringValue());
        if (Peek().Kind != NzToken.Dot)
            return (new TableName(firstName, NameQuote: QuoteOf(first)), first);

        Advance();
        if (Peek().Kind == NzToken.Dot)
        {
            AddParserError("PostgreSQL relation names cannot contain an empty qualified-name segment (..)", Peek(), "PAR001");
            Advance();
        }

        var second = ExpectNameToken();
        var secondName = StripQuotes(second.ToStringValue());
        if (Peek().Kind == NzToken.Dot)
        {
            AddParserError("PostgreSQL relation names contain at most schema.table", Peek(), "PAR001");
            while (Peek().Kind == NzToken.Dot)
            {
                Advance();
                if (Peek().Kind != NzToken.Unknown && Peek().Kind != NzToken.Semicolon)
                    Advance();
            }
        }

        return (new TableName(secondName, Schema: firstName,
            NameQuote: QuoteOf(second), SchemaQuote: QuoteOf(first)), first);
    }

    protected override TableSource ParseTableSource()
    {
        if (Peek().Kind != NzToken.PostgreSqlLateral)
            return base.ParseTableSource();

        var lateral = Advance();
        if (IsContextualIdentifier(Peek().Kind) && Peek(1).Kind == NzToken.LParen)
        {
            var functionName = Advance();
            var function = (FunctionCall)ParseFunctionCall(functionName);
            string? functionAlias = null;
            SourcePosition? functionAliasPosition = null;
            if (Peek().Kind == NzToken.As)
                Advance();
            if (IsContextualIdentifier(Peek().Kind))
            {
                var aliasToken = Advance();
                functionAlias = StripQuotes(aliasToken.ToStringValue());
                functionAliasPosition = FromToken(aliasToken);
            }
            ParseTableSourceSuffix(ref functionAlias, ref functionAliasPosition);
            return new TableSource(FromToken(lateral), null, null, functionAlias,
                AliasPosition: functionAliasPosition, Lateral: true, TableFunction: function);
        }

        if (Peek().Kind != NzToken.LParen)
        {
            AddParserError("LATERAL must be followed by a parenthesized subquery", Peek(), "PAR001");
            return new TableSource(FromToken(lateral), null, null, null, Lateral: true);
        }

        var lp = Advance();
        var query = ParseSelectStatement();
        Expect(NzToken.RParen);
        string? alias = null;
        SourcePosition? aliasPosition = null;
        if (Peek().Kind == NzToken.As)
            Advance();
        if (IsContextualIdentifier(Peek().Kind))
        {
            var aliasToken = Advance();
            alias = StripQuotes(aliasToken.ToStringValue());
            aliasPosition = FromToken(aliasToken);
        }
        ParseTableSourceSuffix(ref alias, ref aliasPosition);
        return new TableSource(FromToken(lp), null, query, alias,
            AliasPosition: aliasPosition, Lateral: true);
    }

    protected override Expression? TryParseDialectPrimary()
    {
        if (Peek().Kind == NzToken.PostgreSqlArray)
        {
            var array = Advance();
            Expect(NzToken.LBracket);
            var items = new List<Expression>();
            if (Peek().Kind != NzToken.RBracket)
            {
                items.Add(ParseExpression());
                while (Peek().Kind == NzToken.Comma)
                {
                    Advance();
                    items.Add(ParseExpression());
                }
            }
            Expect(NzToken.RBracket);
            return new ArrayExpression(FromToken(array), items);
        }

        return null;
    }

    protected override DataTypeInfo ParseDataType()
    {
        var first = ExpectNameToken();
        var parts = new List<string> { StripQuotes(first.ToStringValue()) };
        while (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier or NzToken.With)
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

        if (Peek().Kind == NzToken.LBracket && Peek(1).Kind == NzToken.RBracket)
        {
            Advance();
            Advance();
            parts[^1] += "[]";
        }

        return new DataTypeInfo(FromToken(first), string.Join(" ", parts), parameters);
    }

    private static char? QuoteOf(Token<NzToken> token)
        => token.Kind == NzToken.QuotedIdentifier ? '"' : null;
}
