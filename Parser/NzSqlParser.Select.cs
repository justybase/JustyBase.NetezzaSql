using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

/// <summary>
/// Hand-written recursive-descent parser for Netezza SQL.
/// Consumes Superpower tokens and produces an AST.
/// Partial class split across multiple files by grammar area.
/// </summary>
public partial class NzSqlParser
{
    // ====== Top-Level ======

    public SelectStatement? ParseSelect()
    {
        WithClause? with = null;
        if (Peek().Kind == NzToken.With)
        {
            with = ParseWithClause();
            if (Peek().Kind != NzToken.Select && Peek().Kind != NzToken.LParen)
            {
                _errors.Add(new ValidationError("WITH clause must be followed by a SELECT statement", "error",
                    SourcePosition.FromToken(Peek()), "PARSE002"));
                return null;
            }
        }

        // Handle parenthesized SELECT: (SELECT ...)
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            if (Peek().Kind == NzToken.With)
                with = ParseWithClause();
            var stmt = ParseSelectStatement(with);
            Expect(NzToken.RParen);

            // Handle set operations after parenthesized select
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

            if (setOps.Count > 0)
            {
                return new SelectStatement(stmt.Position, stmt.Modifier, stmt.SelectList, stmt.From,
                    stmt.Where, stmt.GroupBy, stmt.Having, stmt.OrderBy, stmt.Limit,
                    setOps, compoundSelects, stmt.With);
            }
            return stmt;
        }

        if (Peek().Kind != NzToken.Select) return null;
        return ParseSelectStatement(with);
    }

    // ====== CTE (WITH clause) ======

    private WithClause ParseWithClause()
    {
        var withTok = Expect(NzToken.With);
        bool recursive = false;
        if (Peek().Kind == NzToken.Recursive) { recursive = true; Advance(); }

        var ctes = new List<CteDefinition>();
        ctes.Add(ParseCteDefinition());
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            ctes.Add(ParseCteDefinition());
        }

        return new WithClause(FromToken(withTok), recursive, ctes);
    }

    private CteDefinition ParseCteDefinition()
    {
        var name = Expect(NzToken.Identifier).ToStringValue();
        IReadOnlyList<string>? columns = null;

        // Optional column list: (col1, col2, ...)
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var colList = new List<string>();
            colList.Add(Expect(NzToken.Identifier).ToStringValue());
            while (Peek().Kind == NzToken.Comma)
            {
                Advance();
                colList.Add(Expect(NzToken.Identifier).ToStringValue());
            }
            Expect(NzToken.RParen);
            columns = colList;
        }

        // AS keyword (required before subquery body)
        if (Peek().Kind == NzToken.As)
        {
            Advance();

            // Optional ALL keyword (Netezza extension: AS ALL)
            if (Peek().Kind == NzToken.All) Advance();
        }
        else
        {
            _errors.Add(new ValidationError(
                $"CTE '{name}' is missing AS before the subquery", "error",
                SourcePosition.FromToken(Peek()), "PAR101"));
        }

        Expect(NzToken.LParen);
        // Inside parens: either a select statement or a nested WITH...SELECT
        WithClause? nestedWith = null;
        if (Peek().Kind == NzToken.With)
            nestedWith = ParseWithClause();
        var query = ParseSelectStatement(nestedWith);

        // Handle UNION/UNION ALL inside CTE body (for recursive CTEs and compound CTEs)
        var compoundSelects = new List<SelectStatement>();
        while (IsSetOperationStart())
        {
            Advance(); // skip UNION/INTERSECT/EXCEPT/MINUS
            if (Peek().Kind == NzToken.All) Advance(); // UNION ALL
            WithClause? compoundWith = null;
            if (Peek().Kind == NzToken.With)
                compoundWith = ParseWithClause();
            compoundSelects.Add(ParseSelectStatement(compoundWith));
        }

        Expect(NzToken.RParen);

        if (compoundSelects.Count > 0)
        {
            query = new SelectStatement(query.Position, query.Modifier, query.SelectList, query.From,
                query.Where, query.GroupBy, query.Having, query.OrderBy, query.Limit,
                query.SetOperations, compoundSelects, query.With);
        }

        return new CteDefinition(SourcePosition.FromToken(Peek()), name, columns, query);
    }

    // ====== SELECT Statement ======

    private SelectStatement ParseSelectStatement(WithClause? with = null)
    {
        var sel = Expect(NzToken.Select);

        // SELECT DISTINCT / SELECT ALL
        bool distinct = false;
        if (Peek().Kind == NzToken.Distinct) { distinct = true; Advance(); }
        else if (Peek().Kind == NzToken.All) { Advance(); }

        var items = ParseSelectList();

        // INTO clause (NZPLSQL: SELECT ... INTO var [, ...] FROM ...)
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
            Advance(); // FROM
            from = ParseTableReferences();
        }

        if (Peek().Kind == NzToken.Where)
        {
            Advance(); // WHERE
            where = ParseExpression();
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

        if (Peek().Kind == NzToken.OrderBy)
        {
            Advance(); // ORDER BY
            orderBy = ParseOrderByItems();
        }

        if (Peek().Kind == NzToken.Limit)
        {
            var limitTok = Advance(); // LIMIT
            var limitVal = 0;
            if (Peek().Kind != NzToken.NumberLiteral)
            {
                _errors.Add(new ValidationError("Expected number after LIMIT", "error",
                    SourcePosition.FromToken(limitTok), "PARSE001"));
            }
            else
            {
                var num = Expect(NzToken.NumberLiteral);
                limitVal = int.Parse(num.ToStringValue());
            }

            int? offsetVal = null;
            if (Peek().Kind == NzToken.Offset)
            {
                Advance();
                if (Peek().Kind == NzToken.NumberLiteral)
                {
                    var offNum = Expect(NzToken.NumberLiteral);
                    offsetVal = int.Parse(offNum.ToStringValue());
                }
            }

            limit = new LimitClause(FromToken(limitTok), limitVal, offsetVal);
        }

        // Detect misplaced clause keywords after LIMIT (e.g. LIMIT 10 WHERE ...)
        if (limit is not null && Peek().Kind is NzToken.Where or NzToken.GroupBy or NzToken.Having or NzToken.OrderBy)
        {
            _errors.Add(new ValidationError($"Unexpected '{Peek().Kind}' after LIMIT clause", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));
        }

        if (Peek().Kind == NzToken.Fetch)
        {
            var fetchTok = Advance(); // FETCH
            Expect(NzToken.First);
            var count = 1;
            if (Peek().Kind == NzToken.NumberLiteral)
            {
                count = int.Parse(Advance().ToStringValue());
            }
            if (Peek().Kind is NzToken.Row or NzToken.Rows)
                Advance();
            Expect(NzToken.Only);
            limit = new LimitClause(FromToken(fetchTok), count, null);
        }

        // Handle set operations: UNION / INTERSECT / EXCEPT
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

    // ====== Select List ======

    private IReadOnlyList<SelectItem> ParseSelectList()
    {
        var items = new List<SelectItem>();

        // Detect missing SELECT list: SELECT FROM or SELECT WHERE etc.
        if (IsClauseStartKeyword(Peek().Kind) && Peek().Kind != NzToken.Into)
        {
            _errors.Add(new ValidationError(
                "Missing select list after SELECT", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));
            return items;
        }

        items.Add(ParseSelectItem());
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            // Detect trailing comma before clause keyword
            if (IsClauseStartKeyword(Peek().Kind))
            {
                _errors.Add(new ValidationError(
                    $"Trailing comma before {Peek().Kind}", "error",
                    SourcePosition.FromToken(Peek()), "PARSE001"));
                break;
            }
            items.Add(ParseSelectItem());
        }
        return items;
    }

    private SelectItem ParseSelectItem()
    {
        // Scalar subquery in SELECT list: (SELECT ...) AS alias
        if (Peek().Kind == NzToken.LParen)
        {
            var savedPos = _pos;
            Advance(); // LParen
            if (Peek().Kind == NzToken.Select || Peek().Kind == NzToken.With)
            {
                _pos = savedPos; // backtrack
                var subquery = ParseSubqueryExpression();
                var subAlias = ParseAliasName();
                return new SelectItem(subquery.Position, subquery, subAlias);
            }
            _pos = savedPos; // backtrack if not subquery
        }

        var expr = ParseExpression();
        string? alias = ParseAliasName();

        return new SelectItem(expr.Position, expr, alias);
    }

    private Expression ParseSubqueryExpression()
    {
        var lp = Expect(NzToken.LParen);
        var query = ParseSelectStatement();
        Expect(NzToken.RParen);
        return new SubqueryExpression(FromToken(lp), query);
    }

    private string? ParseAliasName()
    {
        if (Peek().Kind == NzToken.As)
        {
            Advance();
            if (IsContextualIdentifier(Peek().Kind))
            {
                return StripQuotes(Advance().ToStringValue());
            }
            _errors.Add(new ValidationError("Expected identifier after AS", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));
            return null;
        }
        if (IsContextualIdentifier(Peek().Kind))
        {
            return StripQuotes(Advance().ToStringValue());
        }
        return null;
    }

    private static string StripQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }

    // ====== Table References ======

    private IReadOnlyList<TableReference> ParseTableReferences()
    {
        var refs = new List<TableReference>();

        // Detect missing table source after FROM (only for unambiguous keywords)
        if (IsUnambiguousTableSourceEnd(Peek().Kind))
        {
            _errors.Add(new ValidationError(
                "Missing table or subquery after FROM", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));
            return refs;
        }

        refs.Add(ParseTableReference());
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            // Detect trailing comma before clause keyword
            if (IsUnambiguousTableSourceEnd(Peek().Kind))
            {
                _errors.Add(new ValidationError(
                    $"Trailing comma before {Peek().Kind}", "error",
                    SourcePosition.FromToken(Peek()), "PARSE001"));
                break;
            }
            refs.Add(ParseTableReference());
        }
        return refs;
    }

    private TableReference ParseTableReference()
    {
        var source = ParseTableSource();
        List<JoinClause>? joins = null;

        while (true)
        {
            var join = TryParseJoinClause();
            if (join is null) break;
            joins ??= new List<JoinClause>();
            joins.Add(join);
        }

        return new TableReference(source.Position, source, joins);
    }

    private TableSource ParseTableSource()
    {
        if (Peek().Kind == NzToken.Table)
        {
            var tableTok = Advance();
            if (Peek().Kind == NzToken.With)
            {
                Advance();
                Expect(NzToken.Final);
                Expect(NzToken.LParen);
                var (funcTable, funcFirst) = ParseTableName();
                if (Peek().Kind == NzToken.LParen)
                {
                    Advance();
                    if (Peek().Kind != NzToken.RParen)
                    {
                        ParseExpressionList();
                    }
                    Expect(NzToken.RParen);
                }
                Expect(NzToken.RParen);

                string? funcAlias = null;
                SourcePosition? funcAliasPosition = null;
                if (Peek().Kind == NzToken.As)
                {
                    Advance();
                    var aliasToken = ExpectNameToken();
                    funcAlias = aliasToken.ToStringValue();
                    funcAliasPosition = FromToken(aliasToken);
                }
                else if (IsContextualIdentifier(Peek().Kind))
                {
                    var nxt = Peek(1).Kind;
                    if (nxt != NzToken.LParen)
                    {
                        var aliasToken = Advance();
                        funcAlias = aliasToken.ToStringValue();
                        funcAliasPosition = FromToken(aliasToken);
                    }
                }

                return new TableSource(FromToken(tableTok), null, null, funcAlias,
                    FunctionSource: true, AliasPosition: funcAliasPosition);
            }
            _pos--; // backtrack: not TABLE WITH FINAL, treat Table as table name
        }

        if (Peek().Kind == NzToken.LParen)
        {
            var lp = Advance();
            var query = ParseSelectStatement();
            Expect(NzToken.RParen);
            string? alias = null;
            SourcePosition? aliasPosition = null;
            if (IsContextualIdentifier(Peek().Kind))
            {
                var aliasToken = Advance();
                alias = aliasToken.ToStringValue();
                aliasPosition = FromToken(aliasToken);
            }
            else if (Peek().Kind == NzToken.As)
            {
                Advance();
                var aliasToken = ExpectNameToken();
                alias = aliasToken.ToStringValue();
                aliasPosition = FromToken(aliasToken);
            }
            return new TableSource(FromToken(lp), null, query, alias, AliasPosition: aliasPosition);
        }

        var (table, firstToken) = ParseTableName();
        var tablePos = FromToken(firstToken);
        string? tableAlias = null;
        SourcePosition? tableAliasPosition = null;

        if (Peek().Kind == NzToken.As)
        {
            Advance();
            var aliasToken = ExpectNameToken();
            tableAlias = aliasToken.ToStringValue();
            tableAliasPosition = FromToken(aliasToken);
        }
        else if (IsContextualIdentifier(Peek().Kind))
        {
            // Check this is truly a table alias, not a keyword
            var nxt = Peek(1).Kind;
            if (nxt != NzToken.Dot && nxt != NzToken.LParen)
            {
                var aliasToken = Advance();
                tableAlias = aliasToken.ToStringValue();
                tableAliasPosition = FromToken(aliasToken);
            }
        }

        return new TableSource(tablePos, table, null, tableAlias, AliasPosition: tableAliasPosition);
    }

    private JoinClause? TryParseJoinClause()
    {
        var joinType = JoinType.Inner;
        bool natural = false;
        bool joinTypeSet = false;

        if (Peek().Kind == NzToken.Natural)
        {
            natural = true;
            Advance();
        }

        var k = Peek().Kind;
        if (k == NzToken.Inner) { Advance(); joinType = JoinType.Inner; joinTypeSet = true; }
        else if (k == NzToken.Left) { Advance(); joinType = JoinType.Left; joinTypeSet = true; }
        else if (k == NzToken.Right) { Advance(); joinType = JoinType.Right; joinTypeSet = true; }
        else if (k == NzToken.Full) { Advance(); joinType = JoinType.Full; joinTypeSet = true; }
        else if (k == NzToken.Cross) { Advance(); joinType = JoinType.Cross; joinTypeSet = true; }

        if (joinTypeSet)
        {
            var nk = Peek().Kind;
            if (nk is NzToken.Inner or NzToken.Left or NzToken.Right or NzToken.Full or NzToken.Cross)
            {
                _errors.Add(new ValidationError($"Invalid join type combination: {k} followed by {nk}", "error",
                    SourcePosition.FromToken(Peek()), "PARSE001"));
            }
        }

        if (Peek().Kind == NzToken.Outer) Advance();

        if (Peek().Kind != NzToken.Join) return null;
        var joinTok = Advance();

        var source = ParseTableSource();
        Expression? onExpr = null;
        IReadOnlyList<string>? usingColumns = null;

        if (Peek().Kind == NzToken.Using)
        {
            Advance();
            Expect(NzToken.LParen);
            usingColumns = ParseIdentifierList();
            Expect(NzToken.RParen);
        }
        else if (!natural && Peek().Kind == NzToken.On)
        {
            Advance();
            onExpr = ParseExpression();
        }
        else if (natural && Peek().Kind == NzToken.On)
        {
            _errors.Add(new ValidationError("NATURAL JOIN cannot have ON clause", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));
            Advance();
            onExpr = ParseExpression();
        }

        return new JoinClause(FromToken(joinTok), joinType, natural, source, onExpr, usingColumns);
    }

    // ====== Table Name ======

    private (TableName Table, Token<NzToken> FirstToken) ParseTableName()
    {
        var parts = new List<string?>();
        var first = ExpectNameToken();
        parts.Add(first.ToStringValue());

        while (Peek().Kind == NzToken.Dot)
        {
            Advance(); // consume dot
            if (Peek().Kind == NzToken.Dot)
            {
                // Double dot: database..table → empty schema
                parts.Add(null); // null = empty schema
                Advance(); // consume second dot
                parts.Add(ExpectNameToken().ToStringValue());
                break;
            }
            parts.Add(ExpectNameToken().ToStringValue());
        }

        var table = parts switch
        {
            [var a] => new TableName(a!),
            [var a, var b] => new TableName(b!, Schema: a),
            [var a, null, var c] => new TableName(c!, Database: a),
            [var a, var b, var c] => new TableName(c!, Schema: b, Database: a),
            _ => new TableName(parts[^1]!)
        };

        return (table, first);
    }

    private Token<NzToken> ExpectNameToken()
    {
        var t = Peek();
        if (t.Kind is NzToken.Identifier or NzToken.QuotedIdentifier or NzToken.Public
            or NzToken.Owner or NzToken.Hash or NzToken.Start)
        {
            return Advance();
        }
        if (t.Kind != NzToken.Unknown && !IsStructuralKeyword(t.Kind)
            && t.ToStringValue().Length > 0 && char.IsLetter(t.ToStringValue()[0]))
        {
            return Advance();
        }

        // Check for keyword typo
        var typoSuggestion = _typoChecker.CheckTypo(t.ToStringValue());
        if (typoSuggestion is not null)
        {
            AddParserError($"Unknown keyword '{t.ToStringValue()}'. Did you mean '{typoSuggestion}'?",
                t, "PAR004");
        }
        else
        {
            AddParserError($"Expected identifier or keyword, got {DescribeToken(t)}", t, "PAR001");
        }
        return t;
    }

    private static bool IsStructuralKeyword(NzToken kind) => kind is
        NzToken.Select or NzToken.From or NzToken.Where or NzToken.GroupBy
        or NzToken.OrderBy or NzToken.Having or NzToken.Limit or NzToken.Offset
        or NzToken.Into or NzToken.Values or NzToken.Set or NzToken.Join
        or NzToken.Inner or NzToken.Left or NzToken.Right or NzToken.Full
        or NzToken.Cross or NzToken.Natural or NzToken.On or NzToken.Union
        or NzToken.Intersect or NzToken.Except or NzToken.MinusSet or NzToken.Semicolon;
}
