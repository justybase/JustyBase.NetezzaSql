using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

public partial class OracleSqlParser
{
    // ====== SELECT Statement (Oracle) ======

    // Port of the reference oracle/sql/parser.ts select-statement override:
    // adds START WITH / CONNECT BY hierarchical clauses and ORDER SIBLINGS BY,
    // and intentionally drops the LIMIT clause so leftover LIMIT tokens surface
    // as unexpected-token diagnostics (Netezza-only syntax rejection).

    protected override SelectStatement ParseSelectStatement(WithClause? with = null)
    {
        var sel = Expect(NzToken.Select);

        // SELECT DISTINCT / SELECT ALL
        bool distinct = false;
        if (Peek().Kind == NzToken.Distinct) { distinct = true; Advance(); }
        else if (Peek().Kind == NzToken.All) { Advance(); }

        var items = ParseSelectList();

        // INTO clause (PL/SQL: SELECT ... INTO var [, ...] FROM ...)
        var hasInto = false;
        if (Peek().Kind == NzToken.Into)
        {
            hasInto = true;
            Advance();
            while (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier or NzToken.OracleBindVariable)
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

        if (Peek().Kind == NzToken.From)
        {
            Advance(); // FROM
            from = ParseTableReferences();
        }

        if (Peek().Kind == NzToken.Where)
        {
            Advance(); // WHERE
            where = ParseExpression();
        }

        // Hierarchical query clauses.
        if (Peek().Kind == NzToken.Start && Peek(1).Kind == NzToken.With)
        {
            Advance(); // START
            Advance(); // WITH
            ParseExpression();
        }
        if (Peek().Kind == NzToken.OracleConnect)
        {
            Advance(); // CONNECT
            Expect(NzToken.OracleBy);
            if (Peek().Kind == NzToken.OracleNocycle) Advance();
            if (Peek().Kind == NzToken.OraclePrior) Advance();
            ParseExpression();
        }

        if (Peek().Kind == NzToken.GroupBy)
        {
            Advance(); // GROUP BY
            groupBy = ParseExpressionList();
        }

        if (Peek().Kind == NzToken.Having)
        {
            Advance(); // HAVING
            having = ParseExpression();
        }

        // ORDER SIBLINGS BY wins over the shared ORDER BY keyword.
        if (Peek().Kind == NzToken.OracleOrderSiblingsBy)
        {
            Advance(); // ORDER SIBLINGS BY
            orderBy = ParseOrderByItems();
        }
        else if (Peek().Kind == NzToken.OrderBy)
        {
            Advance(); // ORDER BY
            orderBy = ParseOrderByItems();
        }

        // NOTE: LIMIT is intentionally not handled here; the leftover token
        // produces an unexpected-token diagnostic at statement level,
        // mirroring the reference Oracle parser which has no LIMIT alternative.

        // Shared ANSI OFFSET/FETCH, including Oracle's PERCENT and WITH TIES
        // extensions. OFFSET-only remains distinct from the compatibility
        // LimitClause so callers can inspect the original syntax.
        LimitClause? limit = null;
        OffsetFetchClause? offsetFetch = null;
        if (Peek().Kind is NzToken.Offset or NzToken.Fetch)
        {
            offsetFetch = ParseOffsetFetchClause();
            if (offsetFetch.FetchCount is not null)
                limit = new LimitClause(offsetFetch.Position, offsetFetch.FetchCount.Value,
                    offsetFetch.Offset, LimitClauseSyntax.Fetch);
        }

        // Set operations: UNION / INTERSECT / EXCEPT
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

        var result = new SelectStatement(FromToken(sel), distinct ? new SelectModifier(true, false) : null, items, from, where, groupBy, having,
            orderBy, limit, setOps.Count > 0 ? setOps : null, compoundSelects.Count > 0 ? compoundSelects : null, with, hasInto);
        return result with { OffsetFetch = offsetFetch };
    }

    // ====== Dialect Primary Expressions ======

    protected override Expression? TryParseDialectPrimary()
    {
        var t = Peek();

        // Qualified function call tokenized as one OracleQualifiedFunction:
        // DBMS_METADATA.GET_DDL('TABLE', 'T') → schema DBMS_METADATA, name GET_DDL.
        if (t.Kind == NzToken.OracleQualifiedFunction)
        {
            var id = Advance();
            var full = StripQuotes(id.ToStringValue());
            var call = (FunctionCall)ParseFunctionCall(id);
            var lastDot = full.LastIndexOf('.');
            if (lastDot > 0)
                return call with { Name = full[(lastDot + 1)..], Schema = full[..lastDot] };
            return call;
        }

        // Bind variables are treated like parameter markers (category Parameter
        // in the reference lexer).
        if (t.Kind == NzToken.OracleBindVariable)
        {
            Advance();
            return new ParameterExpression(FromToken(t));
        }

        return null;
    }

    // ====== Table Source Suffix: Database Links and PIVOT/UNPIVOT ======

    protected override void ParseTableSourceSuffix(ref string? alias, ref SourcePosition? aliasPosition)
    {
        // Database link: HR.EMPLOYEES@PROD [AS] alias
        if (Peek().Kind == NzToken.OracleAtSign)
        {
            Advance();
            ExpectNameToken(); // database link name
            if (alias is null)
            {
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
                    if (nxt != NzToken.Dot && nxt != NzToken.LParen)
                    {
                        var aliasToken = Advance();
                        alias = aliasToken.ToStringValue();
                        aliasPosition = FromToken(aliasToken);
                    }
                }
            }
        }

        // PIVOT / UNPIVOT clause: consumed as a balanced parenthesized group.
        if (Peek().Kind is NzToken.OraclePivot or NzToken.OracleUnpivot)
        {
            Advance();
            Expect(NzToken.LParen);
            var depth = 1;
            while (depth > 0 && Peek().Kind != NzToken.Unknown)
            {
                if (Peek().Kind == NzToken.LParen) depth++;
                else if (Peek().Kind == NzToken.RParen) depth--;
                Advance();
            }
        }
    }
}
