using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

public partial class NzSqlVisitor
{
    private void ValidateGroupByRules(SelectStatement stmt)
    {
        var hasAggregates = stmt.SelectList.Any(SelectItemHasTopLevelAggregate);
        if (!hasAggregates) return;

        if (stmt.GroupBy is { Count: > 0 })
        {
            ValidateGroupBySelectAlignment(stmt);
            if (stmt.OrderBy is not null)
                ValidateOrderByInGroupedQuery(stmt);
        }
        else
        {
            ValidateSelectAggregatesWithoutGroupBy(stmt);
        }
    }

    private void ValidateGroupBySelectAlignment(SelectStatement stmt)
    {
        var groupSignatures = BuildGroupBySignatureSet(stmt);
        foreach (var item in stmt.SelectList)
        {
            if (item.Expression is StarExpression) continue;
            if (SelectItemHasTopLevelAggregate(item)) continue;
            if (SelectItemHasWindowFunction(item.Expression)) continue;
            if (IsExpressionDeterministic(item.Expression)) continue;

            // GROUP BY validates column dependencies, not the textual shape of
            // the expression.  Thus SELECT a + b is valid for GROUP BY a, b.
            var selectedColumns = GetColumnReferenceKeys(item.Expression);
            if (selectedColumns.Count > 0 && selectedColumns.IsSubsetOf(BuildGroupedColumnSet(stmt)))
                continue;

            var alias = item.Alias?.ToUpperInvariant();
            var signature = NormalizeExpressionSignature(item.Expression);
            if (groupSignatures.Contains(signature) ||
                (alias is not null && groupSignatures.Contains(alias)))
                continue;

            AddError("Non-aggregated SELECT item must appear in GROUP BY clause",
                "error", "SQL028", item.Expression.Position);
        }
    }

    private void ValidateSelectAggregatesWithoutGroupBy(SelectStatement stmt)
    {
        foreach (var item in stmt.SelectList)
        {
            if (item.Expression is StarExpression) continue;
            if (SelectItemHasTopLevelAggregate(item)) continue;
            if (SelectItemHasWindowFunction(item.Expression)) continue;
            if (IsExpressionDeterministic(item.Expression)) continue;

            AddError("Column must be aggregated or included in GROUP BY when aggregate functions are present",
                "error", "SQL028", item.Expression.Position);
        }
    }

    private void ValidateOrderByInGroupedQuery(SelectStatement stmt)
    {
        if (stmt.GroupBy is null || stmt.OrderBy is null) return;

        var groupSignatures = BuildGroupBySignatureSet(stmt);
        var outputAliases = GetCurrentSelectOutputAliases() ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var orderItem in stmt.OrderBy)
        {
            if (ExpressionContainsAggregate(orderItem.Expression)) continue;
            if (SelectItemHasWindowFunction(orderItem.Expression)) continue;

            var signature = NormalizeExpressionSignature(orderItem.Expression);
            if (groupSignatures.Contains(signature)) continue;
            if (outputAliases.Contains(signature)) continue;

            AddError("ORDER BY expression must appear in GROUP BY clause for grouped queries",
                "warning", "SQL030", orderItem.Expression.Position);
        }
    }

    private HashSet<string> BuildGroupBySignatureSet(SelectStatement stmt)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (stmt.GroupBy is null) return set;

        foreach (var expr in stmt.GroupBy)
        {
            if (IsExpressionPureLiteral(expr)) continue;
            if (SelectItemHasWindowFunction(expr)) continue;
            set.Add(NormalizeExpressionSignature(expr));
        }

        return set;
    }

    private HashSet<string> BuildGroupedColumnSet(SelectStatement stmt)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (stmt.GroupBy is null) return set;
        foreach (var expression in stmt.GroupBy)
            set.UnionWith(GetColumnReferenceKeys(expression));
        return set;
    }

    private static HashSet<string> GetColumnReferenceKeys(Expression expression)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectColumnReferenceKeys(expression, result);
        return result;
    }

    private static void CollectColumnReferenceKeys(Expression expression, HashSet<string> result)
    {
        switch (expression)
        {
            case ColumnReference column:
                result.Add(column.Qualifier is null ? column.Name : $"{column.Qualifier}.{column.Name}");
                return;
            case BinaryExpression binary:
                CollectColumnReferenceKeys(binary.Left, result);
                CollectColumnReferenceKeys(binary.Right, result);
                return;
            case UnaryExpression unary:
                CollectColumnReferenceKeys(unary.Operand, result);
                return;
            case FunctionCall function:
                if (function.Arguments is not null)
                    foreach (var argument in function.Arguments) CollectColumnReferenceKeys(argument, result);
                return;
            case CaseExpression @case:
                if (@case.Value is not null) CollectColumnReferenceKeys(@case.Value, result);
                foreach (var branch in @case.WhenClauses)
                {
                    CollectColumnReferenceKeys(branch.When, result);
                    CollectColumnReferenceKeys(branch.Then, result);
                }
                if (@case.ElseClause is not null) CollectColumnReferenceKeys(@case.ElseClause, result);
                return;
            case CastExpression cast:
                CollectColumnReferenceKeys(cast.Expression, result);
                return;
            case CastFunctionExpression castFunction:
                CollectColumnReferenceKeys(castFunction.Expression, result);
                return;
            case InExpression @in:
                CollectColumnReferenceKeys(@in.Left, result);
                if (@in.Values is not null)
                    foreach (var value in @in.Values) CollectColumnReferenceKeys(value, result);
                return;
            case BetweenExpression between:
                CollectColumnReferenceKeys(between.Value, result);
                CollectColumnReferenceKeys(between.Low, result);
                CollectColumnReferenceKeys(between.High, result);
                return;
            case IsExpression isExpression:
                CollectColumnReferenceKeys(isExpression.Left, result);
                return;
        }
    }

    private static bool SelectItemHasTopLevelAggregate(SelectItem item) =>
        ExpressionContainsAggregate(item.Expression);

    private static bool SelectItemHasWindowFunction(Expression expr) =>
        ExpressionContainsWindowFunction(expr);

    private static bool IsExpressionPureLiteral(Expression expr) =>
        expr is Literal;

    private bool IsExpressionDeterministic(Expression expr)
    {
        if (IsExpressionPureLiteral(expr)) return true;
        if (ExpressionContainsSubquery(expr)) return false;
        if (ExpressionContainsAggregate(expr)) return false;
        if (!AllColumnRefsAreSessionConstants(expr)) return false;
        return expr is Literal || expr is ColumnReference;
    }

    private static bool AllColumnRefsAreSessionConstants(Expression expr)
    {
        return expr switch
        {
            ColumnReference cr when cr.Qualifier is null =>
                SpecialBuiltinValues.Contains(cr.Name.ToUpperInvariant()),
            ColumnReference => false,
            BinaryExpression b =>
                AllColumnRefsAreSessionConstants(b.Left) && AllColumnRefsAreSessionConstants(b.Right),
            UnaryExpression u => AllColumnRefsAreSessionConstants(u.Operand),
            CastExpression c => AllColumnRefsAreSessionConstants(c.Expression),
            CastFunctionExpression cf => AllColumnRefsAreSessionConstants(cf.Expression),
            _ => !ExpressionContainsColumnRef(expr)
        };
    }

    private static bool ExpressionContainsColumnRef(Expression expr) => expr switch
    {
        ColumnReference => true,
        BinaryExpression b => ExpressionContainsColumnRef(b.Left) || ExpressionContainsColumnRef(b.Right),
        UnaryExpression u => ExpressionContainsColumnRef(u.Operand),
        FunctionCall fc => fc.Arguments?.Any(ExpressionContainsColumnRef) == true,
        CaseExpression ce =>
            (ce.Value is not null && ExpressionContainsColumnRef(ce.Value)) ||
            ce.WhenClauses.Any(w => ExpressionContainsColumnRef(w.When) || ExpressionContainsColumnRef(w.Then)) ||
            (ce.ElseClause is not null && ExpressionContainsColumnRef(ce.ElseClause)),
        CastExpression c => ExpressionContainsColumnRef(c.Expression),
        CastFunctionExpression cf => ExpressionContainsColumnRef(cf.Expression),
        InExpression ie =>
            ExpressionContainsColumnRef(ie.Left) ||
            (ie.Values?.Any(ExpressionContainsColumnRef) == true),
        BetweenExpression be =>
            ExpressionContainsColumnRef(be.Value) ||
            ExpressionContainsColumnRef(be.Low) ||
            ExpressionContainsColumnRef(be.High),
        IsExpression i => ExpressionContainsColumnRef(i.Left),
        _ => false
    };

    private static bool ExpressionContainsAggregate(Expression expr) => expr switch
    {
        FunctionCall fc when fc.Over is not null =>
            fc.Arguments?.Any(ExpressionContainsAggregate) == true,
        FunctionCall fc when IsAggregateFunction(fc) => true,
        FunctionCall fc => fc.Arguments?.Any(ExpressionContainsAggregate) == true,
        BinaryExpression b => ExpressionContainsAggregate(b.Left) || ExpressionContainsAggregate(b.Right),
        UnaryExpression u => ExpressionContainsAggregate(u.Operand),
        CaseExpression ce =>
            (ce.Value is not null && ExpressionContainsAggregate(ce.Value)) ||
            ce.WhenClauses.Any(w => ExpressionContainsAggregate(w.When) || ExpressionContainsAggregate(w.Then)) ||
            (ce.ElseClause is not null && ExpressionContainsAggregate(ce.ElseClause)),
        CastExpression c => ExpressionContainsAggregate(c.Expression),
        CastFunctionExpression cf => ExpressionContainsAggregate(cf.Expression),
        InExpression ie =>
            ExpressionContainsAggregate(ie.Left) ||
            (ie.Values?.Any(ExpressionContainsAggregate) == true),
        BetweenExpression be =>
            ExpressionContainsAggregate(be.Value) ||
            ExpressionContainsAggregate(be.Low) ||
            ExpressionContainsAggregate(be.High),
        _ => false
    };

    private static bool ExpressionContainsWindowFunction(Expression expr) => expr switch
    {
        FunctionCall fc when fc.Over is not null => true,
        FunctionCall fc => fc.Arguments?.Any(ExpressionContainsWindowFunction) == true,
        BinaryExpression b =>
            ExpressionContainsWindowFunction(b.Left) || ExpressionContainsWindowFunction(b.Right),
        UnaryExpression u => ExpressionContainsWindowFunction(u.Operand),
        CaseExpression ce =>
            (ce.Value is not null && ExpressionContainsWindowFunction(ce.Value)) ||
            ce.WhenClauses.Any(w =>
                ExpressionContainsWindowFunction(w.When) || ExpressionContainsWindowFunction(w.Then)) ||
            (ce.ElseClause is not null && ExpressionContainsWindowFunction(ce.ElseClause)),
        CastExpression c => ExpressionContainsWindowFunction(c.Expression),
        CastFunctionExpression cf => ExpressionContainsWindowFunction(cf.Expression),
        _ => false
    };

    private static bool ExpressionContainsSubquery(Expression expr) => expr switch
    {
        ExistsExpression or SubqueryExpression => true,
        InExpression ie when ie.Subquery is not null => true,
        QuantifiedComparisonExpression qc when qc.Right is SubqueryExpression => true,
        FunctionCall fc => fc.Arguments?.Any(ExpressionContainsSubquery) == true,
        BinaryExpression b =>
            ExpressionContainsSubquery(b.Left) || ExpressionContainsSubquery(b.Right),
        CaseExpression ce =>
            ce.WhenClauses.Any(w => ExpressionContainsSubquery(w.When) || ExpressionContainsSubquery(w.Then)) ||
            (ce.ElseClause is not null && ExpressionContainsSubquery(ce.ElseClause)),
        _ => false
    };

    private static bool IsAggregateFunction(FunctionCall fc)
    {
        if (fc.Over is not null) return false;
        var upper = fc.Name.ToUpperInvariant();
        if (AlwaysAggregateFunctions.Contains(upper)) return true;
        if (!AggregateFunctions.Contains(upper)) return false;
        if (fc.Distinct || fc.StarArgument) return true;
        return (fc.Arguments?.Count ?? 0) <= 1;
    }

    private static string NormalizeExpressionSignature(Expression expr) => expr switch
    {
        ColumnReference cr => cr.Qualifier is not null ? $"{cr.Qualifier}.{cr.Name}" : cr.Name,
        Literal l => l.Value,
        FunctionCall fc => fc.Name.ToUpperInvariant(),
        _ => expr.GetType().Name
    };
}
