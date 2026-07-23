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
    // ====== Expressions (simple recursive descent) ======

    private Expression ParseExpression()
    {
        return ParseOrExpression();
    }

    private Expression ParseOrExpression()
    {
        var left = ParseAndExpression();
        while (Peek().Kind == NzToken.Or)
        {
            var op = Advance();
            var right = ParseAndExpression();
            left = new BinaryExpression(FromToken(op), BinaryOperator.Or, left, right);
        }
        return left;
    }

    private Expression ParseAndExpression()
    {
        var left = ParseComparison();
        while (Peek().Kind == NzToken.And)
        {
            var op = Advance();
            var right = ParseComparison();
            left = new BinaryExpression(FromToken(op), BinaryOperator.And, left, right);
        }
        return left;
    }

    private Expression ParseComparison()
    {
        var left = ParseAdditive();
        var opKind = Peek().Kind;

        if (IsComparisonOp(opKind) || (opKind == NzToken.Not && Peek(1).Kind is NzToken.Like or NzToken.Ilike))
        {
            var isNot = false;
            if (opKind == NzToken.Not)
            {
                Advance(); // NOT
                isNot = true;
            }
            var op = Advance();
            var binop = opKind switch
            {
                NzToken.EqualsOp => BinaryOperator.Equals,
                NzToken.NotEquals => BinaryOperator.NotEquals,
                NzToken.LessThan => BinaryOperator.LessThan,
                NzToken.GreaterThan => BinaryOperator.GreaterThan,
                NzToken.LessThanEquals => BinaryOperator.LessThanEquals,
                NzToken.GreaterThanEquals => BinaryOperator.GreaterThanEquals,
                NzToken.Like => isNot ? BinaryOperator.NotLike : BinaryOperator.Like,
                NzToken.Ilike => isNot ? BinaryOperator.NotIlike : BinaryOperator.Ilike,
                _ => BinaryOperator.Equals
            };

            if (Peek().Kind is NzToken.Any or NzToken.Some or NzToken.All)
            {
                var quantifier = Peek().Kind switch
                {
                    NzToken.Any => QuantifierKind.Any,
                    NzToken.Some => QuantifierKind.Some,
                    NzToken.All => QuantifierKind.All,
                    _ => QuantifierKind.Any
                };
                Advance();
                Expect(NzToken.LParen);
                Expression right;
                if (Peek().Kind == NzToken.Select || Peek().Kind == NzToken.With)
                {
                    WithClause? subqueryWith = null;
                    if (Peek().Kind == NzToken.With)
                        subqueryWith = ParseWithClause();
                    right = new SubqueryExpression(FromToken(op), ParseSelectStatement(subqueryWith));
                }
                else
                {
                    right = new InExpression(FromToken(op), left, false, ParseExpressionList(), null);
                }
                Expect(NzToken.RParen);
                left = new QuantifiedComparisonExpression(FromToken(op), binop, quantifier, left, right);
            }
            else
            {
                var right = ParseAdditive();
                left = new BinaryExpression(FromToken(op), binop, left, right);

                if (binop is BinaryOperator.Like or BinaryOperator.Ilike && Peek().Kind == NzToken.Escape)
                {
                    Advance();
                    ParseAdditive();
                }
            }
        }
        else if (opKind == NzToken.Is)
        {
            var isTok = Advance(); // IS
            var isNot = false;
            if (Peek().Kind == NzToken.Not)
            {
                Advance(); // NOT
                isNot = true;
            }
            var right = ParseAdditive(); // NULL or other value
            var binop = isNot ? BinaryOperator.IsNot : BinaryOperator.Is;
            left = new BinaryExpression(FromToken(isTok), binop, left, right);
        }
        else if (opKind == NzToken.Between || (opKind == NzToken.Not && Peek(1).Kind == NzToken.Between))
        {
            var isNot = false;
            if (opKind == NzToken.Not)
            {
                Advance(); // NOT
                isNot = true;
            }
            Advance(); // BETWEEN
            var low = ParseAdditive();
            Expect(NzToken.And);
            var high = ParseAdditive();
            left = new BetweenExpression(left.Position, left, isNot, low, high);
        }
        else if (opKind == NzToken.In || (opKind == NzToken.Not && Peek(1).Kind == NzToken.In))
        {
            var isNot = false;
            if (opKind == NzToken.Not)
            {
                Advance(); // NOT
                isNot = true;
            }
            Advance(); // IN
            Expect(NzToken.LParen);
            if (Peek().Kind == NzToken.Select)
            {
                var subquery = ParseSelectStatement();
                Expect(NzToken.RParen);
                left = new InExpression(left.Position, left, isNot, null, subquery);
            }
            else
            {
                var exprs = ParseExpressionList();
                Expect(NzToken.RParen);
                left = new InExpression(left.Position, left, isNot, exprs, null);
            }
        }

        return left;
    }

    private bool IsComparisonOp(NzToken k) => k is
        NzToken.EqualsOp or NzToken.NotEquals or NzToken.LessThan or NzToken.GreaterThan or
        NzToken.LessThanEquals or NzToken.GreaterThanEquals or NzToken.Like or NzToken.Ilike;

    private Expression ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (true)
        {
            var k = Peek().Kind;
            if (k == NzToken.Plus)
            {
                var op = Advance();
                left = new BinaryExpression(FromToken(op), BinaryOperator.Plus, left, ParseMultiplicative());
            }
            else if (k == NzToken.Minus)
            {
                var op = Advance();
                left = new BinaryExpression(FromToken(op), BinaryOperator.Minus, left, ParseMultiplicative());
            }
            else if (k == NzToken.Concat)
            {
                var op = Advance();
                left = new BinaryExpression(FromToken(op), BinaryOperator.Concat, left, ParseMultiplicative());
            }
            else break;
        }
        return left;
    }

    private Expression ParseMultiplicative()
    {
        var left = ParseUnary();
        while (true)
        {
            var k = Peek().Kind;
            if (k == NzToken.Multiply)
            {
                var op = Advance();
                left = new BinaryExpression(FromToken(op), BinaryOperator.Multiply, left, ParseUnary());
            }
            else if (k == NzToken.Divide)
            {
                var op = Advance();
                left = new BinaryExpression(FromToken(op), BinaryOperator.Divide, left, ParseUnary());
            }
            else if (k == NzToken.Modulo)
            {
                var op = Advance();
                left = new BinaryExpression(FromToken(op), BinaryOperator.Modulo, left, ParseUnary());
            }
            else break;
        }
        return left;
    }

    private Expression ParseUnary()
    {
        Expression expr;
        if (Peek().Kind == NzToken.Not)
        {
            var op = Advance();
            var operand = ParseUnary();
            expr = new UnaryExpression(FromToken(op), UnaryOperator.Not, operand);
        }
        else if (Peek().Kind == NzToken.Minus)
        {
            var op = Advance();
            var operand = ParseUnary();
            expr = new UnaryExpression(FromToken(op), UnaryOperator.Minus, operand);
        }
        else
        {
            expr = ParsePrimary();
        }

        // Postfix :: type cast (e.g. 1::VARCHAR, col::NUMERIC(10,2))
        while (Peek().Kind == NzToken.DoubleColon)
        {
            Advance();
            var type = ParseDataType();
            expr = new CastFunctionExpression(expr.Position, expr, type);
        }

        return expr;
    }

    private Expression ParsePrimary()
    {
        var t = Peek();

        if (t.Kind == NzToken.NumberLiteral)
            return new Literal(FromToken(Advance()), LiteralKind.Number, t.ToStringValue());

        if (t.Kind == NzToken.StringLiteral)
            return new Literal(FromToken(Advance()), LiteralKind.String, t.ToStringValue());

        if (t.Kind == NzToken.Null)
            return new Literal(FromToken(Advance()), LiteralKind.Null, "NULL");

        if (t.Kind == NzToken.Multiply)
            return new StarExpression(FromToken(Advance()), null);

        if (t.Kind == NzToken.LParen)
        {
            Advance();
            if (Peek().Kind == NzToken.Select)
            {
                var subquery = ParseSelectStatement();
                Expect(NzToken.RParen);
                return new SubqueryExpression(FromToken(t), subquery);
            }
            var expr = ParseExpression();
            if (Peek().Kind == NzToken.RParen)
                Advance();
            else if (IsClauseStartKeyword(Peek().Kind) || Peek().Kind == NzToken.Semicolon || Peek().Kind == NzToken.Unknown)
            {
                AddParserError("Unclosed parenthesis — expected ')'", t, "PAR107");
                SynchronizeTo(SyncTokensExpr);
            }
            else
                Expect(NzToken.RParen);
            return expr;
        }

        if (t.Kind == NzToken.Exists)
        {
            var e = Advance();
            Expect(NzToken.LParen);
            var subquery = ParseSelectStatement();
            Expect(NzToken.RParen);
            return new ExistsExpression(FromToken(e), subquery);
        }

        if (t.Kind == NzToken.Case)
            return ParseCaseExpression();

        if (t.Kind == NzToken.Cast)
            return ParseCastExpression();

        if (t.Kind == NzToken.Extract)
        {
            var extract = Advance();
            Expect(NzToken.LParen);
            var field = ExpectNameToken().ToStringValue();
            Expect(NzToken.From);
            var source = ParseExpression();
            Expect(NzToken.RParen);
            return new ExtractExpression(FromToken(extract), StripQuotes(field), source);
        }

        // Type literal: INTERVAL 'value' [qualifier]
        if (t.Kind == NzToken.Identifier && t.ToStringValue().Equals("INTERVAL", StringComparison.OrdinalIgnoreCase))
        {
            var iv = Advance();
            if (Peek().Kind == NzToken.StringLiteral)
                Advance();
            if (Peek().Kind == NzToken.Identifier)
                Advance();
            return new Literal(FromToken(iv), LiteralKind.String, iv.ToStringValue());
        }

        if (t.Kind == NzToken.Next)
        {
            var n = Advance();
            Expect(NzToken.Value);
            Expect(NzToken.For);
            var (seq, _) = ParseTableName();
            return new SequenceValueExpression(FromToken(n), seq, true);
        }

        if (t.Kind == NzToken.RefTable)
            return new ColumnReference(FromToken(Advance()), null, "REFTABLE");

        if (t.Kind is NzToken.DollarIdentifier or NzToken.DollarNumber
            or NzToken.BracedVariable or NzToken.BracesOnlyVariable)
            return new ColumnReference(FromToken(Advance()), null, t.ToStringValue());

        if (t.Kind == NzToken.Parameter)
            return new ParameterExpression(FromToken(Advance()));

        if (IsContextualIdentifier(t.Kind))
        {
            var id = Advance();
            var idStr = StripQuotes(id.ToStringValue());
            if (Peek().Kind == NzToken.StringLiteral)
            {
                var literal = Advance();
                return new Literal(FromToken(id), LiteralKind.String, literal.ToStringValue());
            }
            if (IsContextualIdentifier(t.Kind) && Peek().Kind == NzToken.LParen)
            {
                return ParseFunctionCall(id);
            }
            if (Peek().Kind == NzToken.Dot)
            {
                Advance(); // dot
                if (Peek().Kind == NzToken.Multiply)
                {
                    var star = Advance();
                    return new ColumnReference(FromToken(id), idStr, "*");
                }
                var col = ExpectNameToken();
                return new ColumnReference(FromToken(id), idStr, StripQuotes(col.ToStringValue()));
            }
            return new ColumnReference(FromToken(id), null, idStr);
        }

        // Check for keyword typo in expression context
        var typoSuggestion = _typoChecker.CheckTypo(t.ToStringValue());
        if (typoSuggestion is not null)
        {
            AddParserError($"Unknown keyword '{t.ToStringValue()}'. Did you mean '{typoSuggestion}'?",
                t, "PAR004");
        }
        else
        {
            AddParserError($"Unexpected token {DescribeToken(t)} in expression", t, "PAR103");
        }
        SynchronizeTo(SyncTokensExpr);
        return new Literal(FromToken(t), LiteralKind.Null, "NULL");
    }

    private Expression ParseFunctionCall(Token<NzToken> name)
    {
        Expect(NzToken.LParen);
        var args = new List<Expression>();
        bool distinct = false;
        bool star = false;
        WithinGroupClause? withinGroup = null;
        var functionName = StripQuotes(name.ToStringValue());

        // Handle DISTINCT, ALL, or * argument
        if (Peek().Kind == NzToken.Distinct) { distinct = true; Advance(); }
        else if (Peek().Kind == NzToken.All) { Advance(); }
        if (Peek().Kind == NzToken.Multiply) { star = true; Advance(); }
        else if (Peek().Kind != NzToken.RParen && !star)
        {
            args.Add(ParseExpression());
            while (Peek().Kind == NzToken.Comma)
            {
                Advance();
                args.Add(ParseExpression());
            }
        }

        // Netezza's GROUP_CONCAT extensions place SEPARATOR and ORDER BY
        // inside the function argument list rather than after the closing
        // parenthesis. Keep the separator as a second argument and preserve
        // the sort expression in the existing WITHIN GROUP AST node.
        if (Peek().Kind == NzToken.Identifier &&
            string.Equals(Peek().ToStringValue(), "SEPARATOR", StringComparison.OrdinalIgnoreCase) &&
            (functionName.Equals("GROUP_CONCAT", StringComparison.OrdinalIgnoreCase) ||
             functionName.Equals("GROUP_CONCAT_SORT", StringComparison.OrdinalIgnoreCase)))
        {
            Advance();
            if (Peek().Kind != NzToken.RParen)
                args.Add(ParseExpression());
        }

        if (Peek().Kind == NzToken.OrderBy &&
            functionName.Equals("GROUP_CONCAT_SORT", StringComparison.OrdinalIgnoreCase))
        {
            var orderBy = Advance();
            withinGroup = new WithinGroupClause(FromToken(orderBy), ParseOrderByItems());
        }

        Expect(NzToken.RParen);

        if (withinGroup is null && (Peek().Kind == NzToken.Within ||
            (Peek().Kind == NzToken.Identifier && string.Equals(Peek().ToStringValue(), "WITHIN", StringComparison.OrdinalIgnoreCase))))
        {
            var within = Advance();
            if (Peek().Kind is NzToken.Identifier or NzToken.GroupBy &&
                string.Equals(Peek().ToStringValue(), "GROUP", StringComparison.OrdinalIgnoreCase))
                Advance();
            else
                _errors.Add(new ValidationError("Expected GROUP after WITHIN", "error",
                    SourcePosition.FromToken(Peek()), "PARSE001"));

            Expect(NzToken.LParen);
            IReadOnlyList<OrderByItem>? orderBy = null;
            if (Peek().Kind == NzToken.OrderBy)
            {
                Advance();
                orderBy = ParseOrderByItems();
            }
            Expect(NzToken.RParen);
            withinGroup = new WithinGroupClause(FromToken(within), orderBy);
        }

        // Optional FILTER clause
        FilterClause? filter = null;
        if (Peek().Kind == NzToken.Filter)
            filter = ParseFilterClause();

        // Optional OVER clause
        OverClause? over = null;
        if (Peek().Kind == NzToken.Over)
            over = ParseOverClause();

        return new FunctionCall(FromToken(name), functionName, null,
            distinct, star, args.Count > 0 ? args : null, filter, over, withinGroup);
    }

    private FilterClause ParseFilterClause()
    {
        var pos = Advance(); // FILTER
        Expect(NzToken.LParen);
        Expect(NzToken.Where);
        var where = ParseExpression();
        Expect(NzToken.RParen);
        return new FilterClause(FromToken(pos), where);
    }

    private OverClause ParseOverClause()
    {
        var pos = Advance(); // OVER
        IReadOnlyList<Expression>? partitionBy = null;
        IReadOnlyList<OrderByItem>? orderBy = null;
        WindowFrame? frame = null;

        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            // PARTITION BY ...
            if (Peek().Kind == NzToken.PartitionBy)
            {
                Advance();
                partitionBy = ParseExpressionList();
            }
            // ORDER BY ...
            if (Peek().Kind == NzToken.OrderBy)
            {
                Advance();
                orderBy = ParseOrderByItems();
            }
            // ROWS|RANGE|GROUPS ...
            if (Peek().Kind is NzToken.Rows or NzToken.Range or NzToken.Groups)
            {
                frame = ParseWindowFrame();
            }
            Expect(NzToken.RParen);
        }
        else
        {
            _errors.Add(new ValidationError(
                "Expected '(' after OVER", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));
        }

        return new OverClause(FromToken(pos), partitionBy, orderBy, frame);
    }

    private WindowFrame ParseWindowFrame()
    {
        var pos = Peek();
        var unit = Advance().Kind switch
        {
            NzToken.Rows => WindowFrameUnit.Rows,
            NzToken.Range => WindowFrameUnit.Range,
            NzToken.Groups => WindowFrameUnit.Groups,
            _ => WindowFrameUnit.Rows
        };

        FrameBound? start;
        FrameBound? end = null;

        if (Peek().Kind == NzToken.Between)
        {
            Advance();
            start = ParseFrameBound();
            Expect(NzToken.And);
            end = ParseFrameBound();
        }
        else
        {
            start = ParseFrameBound();
        }

        // Optional EXCLUDE clause
        ExcludeClause? exclude = null;
        if (Peek().Kind == NzToken.Exclude)
        {
            exclude = ParseExcludeClause();
        }

        return new WindowFrame(FromToken(pos), unit, start, end, exclude);
    }

    private FrameBound ParseFrameBound()
    {
        var pos = Peek();
        var tok = Peek();

        switch (tok.Kind)
        {
            case NzToken.Unbounded:
                Advance();
                var dir = Peek().Kind switch
                {
                    NzToken.Preceding => FrameBoundKind.UnboundedPreceding,
                    NzToken.Following => FrameBoundKind.UnboundedFollowing,
                    _ => FrameBoundKind.UnboundedPreceding
                };
                if (Peek().Kind is NzToken.Preceding or NzToken.Following)
                    Advance();
                else
                    _errors.Add(new ValidationError(
                        $"Expected PRECEDING or FOLLOWING after UNBOUNDED, got {Peek().Kind}", "error",
                        SourcePosition.FromToken(Peek()), "PARSE001"));
                return new FrameBound(FromToken(pos), dir, null);

            case NzToken.Current:
                Advance();
                Expect(NzToken.Row);
                return new FrameBound(FromToken(pos), FrameBoundKind.CurrentRow, null);

            case NzToken.NumberLiteral:
                Advance();
                long? num = long.TryParse(tok.ToStringValue(), out var v) ? v : null;
                var dir2 = Peek().Kind switch
                {
                    NzToken.Preceding => FrameBoundKind.Preceding,
                    NzToken.Following => FrameBoundKind.Following,
                    _ => FrameBoundKind.Preceding
                };
                if (Peek().Kind is NzToken.Preceding or NzToken.Following)
                    Advance();
                else
                    _errors.Add(new ValidationError(
                        $"Expected PRECEDING or FOLLOWING after number, got {Peek().Kind}", "error",
                        SourcePosition.FromToken(Peek()), "PARSE001"));
                return new FrameBound(FromToken(pos), dir2, num);

            case NzToken.Preceding:
                _errors.Add(new ValidationError(
                    "Expected UNBOUNDED or number before PRECEDING", "error",
                    SourcePosition.FromToken(tok), "PARSE001"));
                Advance();
                return new FrameBound(FromToken(pos), FrameBoundKind.Preceding, null);

            case NzToken.Following:
                _errors.Add(new ValidationError(
                    "Expected UNBOUNDED or number before FOLLOWING", "error",
                    SourcePosition.FromToken(tok), "PARSE001"));
                Advance();
                return new FrameBound(FromToken(pos), FrameBoundKind.Following, null);

            default:
                _errors.Add(new ValidationError(
                    $"Unexpected token {tok.Kind} in frame bound", "error",
                    SourcePosition.FromToken(tok), "PARSE001"));
                return new FrameBound(FromToken(pos), FrameBoundKind.CurrentRow, null);
        }
    }

    private ExcludeClause ParseExcludeClause()
    {
        var pos = Advance(); // EXCLUDE
        ExcludeKind kind;
        var t = Peek();

        if (t.Kind == NzToken.Identifier && string.Equals(t.ToStringValue(), "NO", StringComparison.OrdinalIgnoreCase))
        {
            Advance(); // NO
            if (Peek().Kind == NzToken.Identifier)
                Advance(); // OTHERS (as identifier)
            kind = ExcludeKind.NoOthers;
        }
        else if (t.Kind == NzToken.Current)
        {
            Advance(); // CURRENT
            Expect(NzToken.Row);
            kind = ExcludeKind.CurrentRow;
        }
        else if (t.Kind == NzToken.Identifier && string.Equals(t.ToStringValue(), "GROUP", StringComparison.OrdinalIgnoreCase))
        {
            Advance(); // GROUP
            kind = ExcludeKind.Group;
        }
        else if (t.Kind == NzToken.Ties)
        {
            Advance(); // TIES
            kind = ExcludeKind.Ties;
        }
        else
        {
            kind = ExcludeKind.NoOthers;
        }

        return new ExcludeClause(FromToken(pos), kind);
    }

    private Expression ParseCaseExpression()
    {
        var c = Expect(NzToken.Case);
        Expression? caseValue = null;

        if (Peek().Kind != NzToken.When)
        {
            caseValue = ParseExpression();
        }

        var whens = new List<WhenThenClause>();

        while (Peek().Kind == NzToken.When)
        {
            var w = Advance();
            var cond = ParseExpression();
            Expect(NzToken.Then);
            var val = ParseExpression();
            whens.Add(new WhenThenClause(FromToken(w), cond, val));
        }

        if (whens.Count == 0)
        {
            AddParserError("CASE expression must have at least one WHEN clause", c, "PAR005");
            SynchronizeTo(SyncTokensExpr);
            return new CaseExpression(FromToken(c), caseValue, whens, null);
        }

        Expression? elseExpr = null;
        if (Peek().Kind == NzToken.Else)
        {
            Advance();
            elseExpr = ParseExpression();
        }

        if (Peek().Kind == NzToken.End)
            Advance();
        else
        {
            AddParserError("CASE expression must end with END keyword", c, "PAR108");
            SynchronizeTo(SyncTokensExpr);
        }
        return new CaseExpression(FromToken(c), caseValue, whens, elseExpr);
    }

    private Expression ParseCastExpression()
    {
        var c = Expect(NzToken.Cast);
        Expect(NzToken.LParen);
        var expr = ParseExpression();
        Expect(NzToken.As);
        var type = ParseDataType();
        Expect(NzToken.RParen);
        return new CastExpression(FromToken(c), expr, type);
    }

    private DataTypeInfo ParseDataType()
    {
        var first = Peek().Kind == NzToken.QuotedIdentifier
            ? Advance()
            : Expect(NzToken.Identifier);
        var firstVal = StripQuotes(first.ToStringValue());
        var firstUpper = firstVal.ToUpperInvariant();

        // INTERVAL qualifier: e.g. INTERVAL HOUR TO MINUTE
        if (firstUpper == "INTERVAL")
        {
            var parts = new List<string> { firstVal };
            if (Peek().Kind == NzToken.Identifier)
            {
                parts.Add(Advance().ToStringValue());
                if (Peek().Kind == NzToken.To)
                {
                    Advance();
                    parts.Add("TO");
                    parts.Add(Expect(NzToken.Identifier).ToStringValue());
                }
            }
            return new DataTypeInfo(FromToken(first), string.Join(" ", parts), null);
        }

        var nameParts = new List<string> { firstVal };
        while (Peek().Kind == NzToken.Identifier)
            nameParts.Add(Advance().ToStringValue());

        IReadOnlyList<string>? args = null;
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var argList = new List<string>();
            while (true)
            {
                if (Peek().Kind == NzToken.NumberLiteral)
                    argList.Add(Advance().ToStringValue());
                else if (Peek().Kind == NzToken.Identifier || Peek().Kind == NzToken.Any)
                    argList.Add(Advance().ToStringValue());
                else break;
                if (Peek().Kind == NzToken.Comma) { Advance(); continue; }
                break;
            }
            Expect(NzToken.RParen);
            if (argList.Count > 0) args = argList;
        }

        return new DataTypeInfo(FromToken(first), string.Join(" ", nameParts), args);
    }

    // ====== Helpers ======

    private IReadOnlyList<Expression> ParseExpressionList()
    {
        var list = new List<Expression>();
        list.Add(ParseExpression());
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            list.Add(ParseExpression());
        }
        return list;
    }

    private IReadOnlyList<OrderByItem> ParseOrderByItems()
    {
        var items = new List<OrderByItem>();
        items.Add(ParseOrderByItem());
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            items.Add(ParseOrderByItem());
        }
        return items;
    }

    private OrderByItem ParseOrderByItem()
    {
        var expr = ParseExpression();
        bool desc = false;
        bool nullsFirst = false;

        if (Peek().Kind == NzToken.Desc) { Advance(); desc = true; }
        else if (Peek().Kind == NzToken.Asc) { Advance(); }

        if (Peek().Kind == NzToken.Nulls)
        {
            Advance();
            if (Peek().Kind == NzToken.First) { Advance(); nullsFirst = true; }
            else if (Peek().Kind == NzToken.Identifier &&
                string.Equals(Peek().ToStringValue(), "LAST", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
            }
            else
            {
                _errors.Add(new ValidationError("Expected FIRST or LAST after NULLS", "error",
                    SourcePosition.FromToken(Peek()), "PARSE001"));
            }
        }

        return new OrderByItem(expr.Position, expr, desc, nullsFirst);
    }
}
