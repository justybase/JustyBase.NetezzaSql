using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

public partial class MssqlSqlParser
{
    // Port of the mssql selectStatement override: SELECT [DISTINCT|ALL]
    // [TOP (n)|TOP n [PERCENT] [WITH TIES]] ... with no LIMIT; OFFSET/FETCH
    // (FETCH NEXT and legacy FETCH FIRST) reuse the shared clause.

    protected override SelectStatement ParseSelectStatement(WithClause? with = null)
    {
        var sel = Expect(NzToken.Select);

        bool distinct = false;
        if (Peek().Kind == NzToken.Distinct) { distinct = true; Advance(); }
        else if (Peek().Kind == NzToken.All) { Advance(); }

        IReadOnlyList<Token<NzToken>>? topTokens = null;
        if (Peek().Kind == NzToken.MssqlTop)
            topTokens = ParseTopClause();

        var items = ParseSelectList();

        var hasInto = false;
        if (Peek().Kind == NzToken.Into)
        {
            hasInto = true;
            Advance();
            while (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier
                or NzToken.MssqlBracketedIdentifier)
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

        // LIMIT is intentionally not consumed — leftover token surfaces as PAR001.

        if (Peek().Kind is NzToken.Offset or NzToken.Fetch)
        {
            offsetFetch = ParseOffsetFetchClause();
            if (offsetFetch.FetchCount is not null)
                limit = new LimitClause(offsetFetch.Position, offsetFetch.FetchCount.Value,
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
        return result with { OffsetFetch = offsetFetch, TopTokens = topTokens };
    }

    // TOP (n) | TOP n [PERCENT] [WITH TIES] | TOP @var — opaque token range.
    private IReadOnlyList<Token<NzToken>> ParseTopClause()
    {
        var start = _pos;
        Advance(); // TOP

        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            if (Peek().Kind == NzToken.NumberLiteral || Peek().Kind == NzToken.MssqlVariable)
                Advance();
            if (Peek().Kind == NzToken.RParen)
                Advance();
        }
        else if (Peek().Kind == NzToken.NumberLiteral || Peek().Kind == NzToken.MssqlVariable)
        {
            Advance();
        }

        if (Peek().Kind == NzToken.Identifier &&
            Peek().ToStringValue().Equals("PERCENT", StringComparison.OrdinalIgnoreCase))
            Advance();

        if (Peek().Kind == NzToken.With && Peek(1).Kind == NzToken.Ties)
        {
            Advance();
            Advance();
        }
        else if (Peek().Kind == NzToken.Ties)
        {
            Advance();
        }

        return _tokens[start.._pos];
    }

    // CROSS APPLY / OUTER APPLY chain after a table source.
    protected override TableReference ParseTableReference()
    {
        var source = ParseTableSource();
        List<JoinClause>? joins = null;
        List<ApplyClause>? applies = null;

        while (true)
        {
            if (Peek().Kind is NzToken.MssqlCrossApply or NzToken.MssqlOuterApply)
            {
                var applyTok = Advance();
                var applySource = ParseMssqlApplySource();
                applies ??= new List<ApplyClause>();
                applies.Add(new ApplyClause(FromToken(applyTok), applyTok.Kind == NzToken.MssqlOuterApply, applySource));
                continue;
            }

            var join = TryParseJoinClause();
            if (join is null) break;
            joins ??= new List<JoinClause>();
            joins.Add(join);
        }

        return new TableReference(source.Position, source, joins, applies);
    }

    // T-SQL table hints: FROM t WITH (NOLOCK, INDEX (ix_1)). Consumed as an
    // opaque balanced-parenthesis range so the leftover WITH cannot cascade
    // into ParseWithTopLevel CTE errors.
    protected override void TryParseTableHints()
    {
        if (Peek().Kind != NzToken.With || Peek(1).Kind != NzToken.LParen)
            return;
        Advance(); // WITH
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
            Advance();
        }
    }

    // Names are stored without T-SQL quoting, matching the reference visitor's
    // stripIdentifierQuoting ([[escaped]] brackets are unescaped too).
    protected override (TableName Table, Token<NzToken> FirstToken) ParseTableName()
    {
        var (table, first) = base.ParseTableName();
        var cleaned = table with
        {
            Name = StripMssqlName(table.Name),
            Schema = table.Schema is null ? null : StripMssqlName(table.Schema),
            Database = table.Database is null ? null : StripMssqlName(table.Database)
        };
        return (cleaned, first);
    }

    // CROSS/OUTER APPLY source: a table-valued function name(args) [alias] or a
    // regular table/subquery source. Function args are consumed as a balanced
    // parenthesis range (opaque), matching the reference parser's token stream.
    private TableSource ParseMssqlApplySource()
    {
        if (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier or NzToken.MssqlBracketedIdentifier)
        {
            var save = _pos;
            var (name, first) = ParseTableName();
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
                    Advance();
                }

                string? alias = null;
                if (Peek().Kind == NzToken.As)
                {
                    Advance();
                    alias = ExpectNameToken().ToStringValue();
                }
                else if (IsContextualIdentifier(Peek().Kind) && Peek(1).Kind != NzToken.LParen)
                {
                    alias = Advance().ToStringValue();
                }
                return new TableSource(FromToken(first), name, null, alias);
            }
            _pos = save;
        }

        return ParseTableSource();
    }

    protected override Expression? TryParseDialectPrimary()
    {
        if (Peek().Kind == NzToken.MssqlVariable)
        {
            var t = Advance();
            return new ParameterExpression(FromToken(t));
        }
        return null;
    }
}
