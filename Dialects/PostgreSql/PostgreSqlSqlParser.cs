using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Parser;

/// <summary>Strict parser for the PostgreSQL SQL surface.</summary>
public sealed partial class PostgreSqlSqlParser : NzSqlParser
{
    private bool _unsupportedSyntaxReported;

    public PostgreSqlSqlParser(Token<NzToken>[] tokens) : base(tokens, strictNetezzaIntervalSyntax: false)
    {
    }

    protected override bool SupportsValuesQuery => true;
    protected override bool AllowOrderedAggregateArguments => true;
    protected override bool AllowWindowNameAfterOver => true;

    protected override bool SupportsFetchFirst => true;

    protected override bool SupportsTimestamptz => true;

    protected override bool SupportsMultiRowValues => true;

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
                var isStorageClause = (word.Equals("MINUS", StringComparison.OrdinalIgnoreCase) && next == NzToken.Select)
                    || statementStart
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

        SkipSemicolons();
        if (_pos >= _tokens.Length) return null;
        var result = Peek().Kind switch
        {
            NzToken.Values => ParseValuesQuery(),
            NzToken.Explain => ParsePostgreSqlExplain(),
            NzToken.Merge => ParsePostgreSqlMerge(),
            NzToken.PostgreSqlDo => ParsePostgreSqlDo(),
            NzToken.Drop => ParsePostgreSqlDrop(),
            NzToken.Generate => RejectPostgreSqlCommand("GENERATE STATISTICS is not PostgreSQL syntax"),
            _ => base.Parse(),
        };
        if (result is null)
            SynchronizeStatement();
        return result;
    }

    protected override bool SupportsEmptyQualifiedNameSegment => false;

    protected override (TableName Table, Token<NzToken> FirstToken) ParseTableName()
    {
        var first = ExpectNameToken();
        var firstName = PostgreSqlName(first);
        if (Peek().Kind != NzToken.Dot)
            return (new TableName(firstName, NameQuote: QuoteOf(first)), first);

        Advance();
        if (Peek().Kind == NzToken.Dot)
        {
            AddParserError("PostgreSQL relation names cannot contain an empty qualified-name segment (..)", Peek(), "PAR001");
            Advance();
        }

        var second = ExpectNameToken();
        var secondName = PostgreSqlName(second);
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
        var lateral = Peek().Kind == NzToken.PostgreSqlLateral;
        if (lateral) Advance();
        if (IsContextualIdentifier(Peek().Kind) && Peek(1).Kind == NzToken.LParen)
        {
            var name = Advance();
            FunctionCall function;
            if (name.ToStringValue().Equals("JSON_TABLE", StringComparison.OrdinalIgnoreCase))
                function = ParsePostgreSqlJsonTable(name);
            else function = (FunctionCall)ParseFunctionCall(name);
            var ordinality = Peek().Kind == NzToken.With;
            if (ordinality) { Advance(); ExpectPostgreSqlWord("ORDINALITY"); }
            var alias = ParseAliasName(out var aliasQuote);
            if (aliasQuote is null) alias = alias?.ToLowerInvariant();
            var columns = Peek().Kind == NzToken.LParen ? ParsePostgreSqlIdentifierList() : function.ResultColumnNames;
            return new TableSource(FromToken(name), null, null, alias, FunctionSource: true,
                Lateral: lateral, TableFunction: function, AliasQuote: aliasQuote) { ColumnAliases = columns, WithOrdinality = ordinality };
        }
        var source = base.ParseTableSource();
        if (source.Subquery is not null && Peek().Kind == NzToken.LParen)
            source = source with { ColumnAliases = ParsePostgreSqlIdentifierList() };
        return lateral ? source with { Lateral = true } : source;
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

        if (IsContextualIdentifier(Peek().Kind) && Peek(1).Kind == NzToken.Dot
            && IsContextualIdentifier(Peek(2).Kind) && Peek(3).Kind == NzToken.LParen)
        {
            var schema = Advance();
            Advance();
            return ((FunctionCall)ParseFunctionCall(Advance())) with { Schema = StripQuotes(schema.ToStringValue()) };
        }
        if (IsContextualIdentifier(Peek().Kind) && Peek(1).Kind != NzToken.StringLiteral)
        {
            var first = Advance();
            if (Peek().Kind == NzToken.LParen) return ParseFunctionCall(first);
            if (Peek().Kind == NzToken.Dot)
            {
                Advance();
                var second = Peek();
                if (!IsContextualIdentifier(second.Kind) && second.Kind != NzToken.Multiply)
                {
                    AddParserError("Expected column after qualifier", second, "PAR001");
                    return new ColumnReference(FromToken(first), PostgreSqlName(first), "", QualifierQuote: QuoteOf(first));
                }
                Advance();
                return new ColumnReference(FromToken(first), PostgreSqlName(first), PostgreSqlName(second),
                    NameQuote: QuoteOf(second), QualifierQuote: QuoteOf(first));
            }
            return new ColumnReference(FromToken(first), null, PostgreSqlName(first), NameQuote: QuoteOf(first));
        }
        return null;
    }

    private static string PostgreSqlName(Token<NzToken> token) =>
        token.Kind == NzToken.QuotedIdentifier ? StripQuotes(token.ToStringValue()) : token.ToStringValue().ToLowerInvariant();

    protected override DataTypeInfo ParseDataType()
    {
        var first = ExpectNameToken();
        var parts = new List<string> { StripQuotes(first.ToStringValue()) };
        while (PostgreSqlWordIs("PRECISION") || PostgreSqlWordIs("VARYING") || PostgreSqlWordIs("WITH")
            || PostgreSqlWordIs("WITHOUT") || PostgreSqlWordIs("TIME") || PostgreSqlWordIs("ZONE"))
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
