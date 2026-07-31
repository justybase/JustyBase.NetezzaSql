using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

public partial class Db2SqlParser
{
    // Port of the Db2 selectStatement override: no LIMIT; after ORDER/FETCH
    // accept OPTIMIZE FOR, FOR READ ONLY|UPDATE, and isolation WITH UR|CS|RS|RR.

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
        IReadOnlyList<OrderByItem>? orderBy = null;
        LimitClause? limit = null;

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

        // LIMIT is intentionally not consumed — leftover token surfaces as PAR001.

        if (Peek().Kind == NzToken.Fetch)
        {
            var fetchTok = Advance();
            Expect(NzToken.First);
            var count = 1;
            if (Peek().Kind == NzToken.NumberLiteral)
                count = int.Parse(Advance().ToStringValue());
            if (Peek().Kind is NzToken.Row or NzToken.Rows)
                Advance();
            Expect(NzToken.Only);
            limit = new LimitClause(FromToken(fetchTok), count, null);
        }

        if (Peek().Kind == NzToken.Db2OptimizeFor)
        {
            Advance();
            Expect(NzToken.NumberLiteral);
            if (Peek().Kind is NzToken.Row or NzToken.Rows)
                Advance();
        }

        if (Peek().Kind is NzToken.Db2ForReadOnly or NzToken.Db2ForUpdate)
            Advance();

        if (Peek().Kind is NzToken.Db2WithUr or NzToken.Db2WithCs
            or NzToken.Db2WithRs or NzToken.Db2WithRr)
            Advance();

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

        return new SelectStatement(FromToken(sel), distinct ? new SelectModifier(true, false) : null, items, from, where, groupBy, having,
            orderBy, limit, setOps.Count > 0 ? setOps : null, compoundSelects.Count > 0 ? compoundSelects : null, with, hasInto);
    }

    protected override TableSource ParseTableSource()
    {
        if (Peek().Kind is NzToken.Db2FinalTable or NzToken.Db2OldTable or NzToken.Db2NewTable)
        {
            var tok = Advance();
            Expect(NzToken.LParen);
            // Opaque DML/select body: consume balanced parentheses content.
            var depth = 1;
            while (depth > 0 && Peek().Kind != NzToken.Unknown)
            {
                if (Peek().Kind == NzToken.LParen) depth++;
                else if (Peek().Kind == NzToken.RParen) depth--;
                if (depth == 0)
                {
                    Advance(); // closing )
                    break;
                }
                Advance();
            }

            string? alias = null;
            SourcePosition? aliasPosition = null;
            if (Peek().Kind == NzToken.As)
            {
                Advance();
                var aliasToken = ExpectNameToken();
                alias = aliasToken.ToStringValue();
                aliasPosition = FromToken(aliasToken);
            }
            else if (IsContextualIdentifier(Peek().Kind))
            {
                var nxt = Peek(1).Kind;
                if (nxt is not (NzToken.LParen or NzToken.Dot))
                {
                    var aliasToken = Advance();
                    alias = aliasToken.ToStringValue();
                    aliasPosition = FromToken(aliasToken);
                }
            }

            return new TableSource(FromToken(tok), null, null, alias,
                FunctionSource: true, AliasPosition: aliasPosition);
        }

        return base.ParseTableSource();
    }
}
