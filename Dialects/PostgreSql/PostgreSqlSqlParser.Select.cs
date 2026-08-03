using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.NetezzaSqlParser.Parser;

public sealed partial class PostgreSqlSqlParser
{
    protected override SelectStatement ParseSelectStatement(WithClause? with = null)
    {
        if (Peek().Kind != NzToken.Select || Peek(1).Kind != NzToken.Distinct
            || Peek(2).Kind != NzToken.On)
            return base.ParseSelectStatement(with);

        var select = Advance();
        Advance(); // DISTINCT
        Advance(); // ON
        Expect(NzToken.LParen);
        var distinctOn = new List<Expression> { ParseExpression() };
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            distinctOn.Add(ParseExpression());
        }
        Expect(NzToken.RParen);

        var items = ParseSelectList();
        IReadOnlyList<TableReference>? from = null;
        Expression? where = null;
        IReadOnlyList<Expression>? groupBy = null;
        Expression? having = null;
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
        if (Peek().Kind == NzToken.OrderBy)
        {
            Advance();
            orderBy = ParseOrderByItems();
        }
        if (Peek().Kind == NzToken.Limit)
            limit = ParseLimitClause();
        if (Peek().Kind == NzToken.Offset)
            offsetFetch = ParseOffsetFetchClause();
        if (Peek().Kind == NzToken.Fetch)
        {
            offsetFetch = ParseOffsetFetchClause();
            limit = new LimitClause(offsetFetch.Position, offsetFetch.FetchCount ?? 1,
                offsetFetch.Offset, LimitClauseSyntax.Fetch);
        }

        // DISTINCT ON is PostgreSQL-specific, but the remainder of the
        // SELECT grammar (including UNION/INTERSECT/EXCEPT) is shared with
        // the base parser and must still be consumed here.
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
                _ => SetOperationType.Except
            };
            var all = false;
            if (Peek().Kind == NzToken.All)
            {
                all = true;
                Advance();
            }
            else if (Peek().Kind == NzToken.Distinct)
            {
                Advance();
            }

            setOps.Add(new SetOperation(FromToken(opTok), setType, all));
            WithClause? nestedWith = null;
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                if (Peek().Kind == NzToken.With)
                    nestedWith = ParseWithClause();
                compoundSelects.Add(ParseSelectStatement(nestedWith));
                Expect(NzToken.RParen);
            }
            else
            {
                if (Peek().Kind == NzToken.With)
                    nestedWith = ParseWithClause();
                compoundSelects.Add(ParseSelectStatement(nestedWith));
            }
        }

        return new SelectStatement(FromToken(select), new SelectModifier(true, false), items,
            from, where, groupBy, having, orderBy, limit,
            setOps.Count > 0 ? setOps : null,
            compoundSelects.Count > 0 ? compoundSelects : null, with,
            OffsetFetch: offsetFetch, DistinctOn: distinctOn);
    }
}
