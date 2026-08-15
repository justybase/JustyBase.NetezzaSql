using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Parser;

public sealed partial class SqliteSqlParser
{
    // Mirrors the shared SELECT grammar and adds the SQLite WINDOW name AS (...)
    // clause between HAVING and ORDER BY (kept as an opaque token range).

    protected override SelectStatement ParseSelectStatement(WithClause? with = null)
    {
        var sel = Expect(NzToken.Select);

        bool distinct = false;
        if (Peek().Kind == NzToken.Distinct) { distinct = true; Advance(); }
        else if (Peek().Kind == NzToken.All) { Advance(); }

        var items = ParseSelectList();

        var hasInto = false;
        if (Peek().Kind == NzToken.Into)
        {
            hasInto = true;
            Advance();
            while (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
            {
                Advance();
                if (Peek().Kind == NzToken.Comma) Advance(); else break;
            }
        }

        IReadOnlyList<TableReference>? from = null;
        Expression? where = null;
        IReadOnlyList<Expression>? groupBy = null;
        Expression? having = null;
        IReadOnlyList<Token<NzToken>>? windowTokens = null;
        IReadOnlyList<OrderByItem>? orderBy = null;
        LimitClause? limit = null;
        OffsetFetchClause? offsetFetch = null;

        if (Peek().Kind == NzToken.From)
        {
            Advance();
            from = ParseTableReferences();
        }

        if (Peek().Kind == NzToken.Where)
        {
            Advance();
            where = ParseExpression();
        }

        if (Peek().Kind == NzToken.GroupBy)
        {
            Advance();
            groupBy = ParseExpressionList();
        }

        if (Peek().Kind == NzToken.Having)
        {
            Advance();
            having = ParseExpression();
        }

        if (Peek().Kind == NzToken.SqliteWindow)
        {
            var tokens = new List<Token<NzToken>>();
            while (Peek().Kind is NzToken.SqliteWindow or NzToken.Comma)
            {
                tokens.Add(Advance());
                var depth = 0;
                while (Peek().Kind != NzToken.Unknown)
                {
                    var token = Advance();
                    tokens.Add(token);
                    if (token.Kind == NzToken.LParen) depth++;
                    else if (token.Kind == NzToken.RParen)
                    {
                        depth--;
                        if (depth == 0)
                            break;
                    }
                }
            }
            windowTokens = tokens;
        }

        if (Peek().Kind == NzToken.OrderBy)
        {
            Advance();
            orderBy = ParseOrderByItems();
        }

        if (Peek().Kind == NzToken.Limit)
            limit = ParseLimitClause();

        if (limit is not null && Peek().Kind is NzToken.Where or NzToken.GroupBy or NzToken.Having or NzToken.OrderBy)
        {
            AddParserError($"Unexpected '{Peek().Kind}' after LIMIT clause", Peek(), "PARSE001");
        }

        if (Peek().Kind == NzToken.Offset)
            offsetFetch = ParseOffsetFetchClause();
        if (Peek().Kind == NzToken.Fetch)
        {
            offsetFetch = ParseOffsetFetchClause();
            limit = new LimitClause(offsetFetch.Position, offsetFetch.FetchCount ?? 1,
                offsetFetch.Offset, LimitClauseSyntax.Fetch);
        }

        var setOps = new List<SetOperation>();
        var compoundSelects = new List<SelectStatement>();
        while (IsSetOperationStart())
        {
            var opTok = Advance();
            var setType = opTok.Kind switch
            {
                NzToken.Union => SetOperationType.Union,
                NzToken.Intersect => SetOperationType.Intersect,
                NzToken.Except or NzToken.MinusSet => SetOperationType.Except,
                _ => SetOperationType.Except,
            };
            var all = false;
            if (Peek().Kind == NzToken.All) { all = true; Advance(); }
            else if (Peek().Kind == NzToken.Distinct) Advance();

            setOps.Add(new SetOperation(FromToken(opTok), setType, all));

            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                WithClause? nestedWith = null;
                if (Peek().Kind == NzToken.With) nestedWith = ParseWithClause();
                compoundSelects.Add(ParseSelectStatement(nestedWith));
                Expect(NzToken.RParen);
            }
            else
            {
                WithClause? nestedWith = null;
                if (Peek().Kind == NzToken.With) nestedWith = ParseWithClause();
                compoundSelects.Add(ParseSelectStatement(nestedWith));
            }
        }

        var result = new SelectStatement(FromToken(sel), distinct ? new SelectModifier(true, false) : null,
            items, from, where, groupBy, having, orderBy, limit,
            setOps.Count > 0 ? setOps : null, compoundSelects.Count > 0 ? compoundSelects : null,
            with, hasInto);
        return result with { OffsetFetch = offsetFetch, SqliteWindowTokens = windowTokens };
    }
}
