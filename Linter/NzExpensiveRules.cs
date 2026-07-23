using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;

namespace JustyBase.NetezzaSqlParser.Linter;

// ====== NZ101: SELECT * with JOIN ======
/// <summary>
/// Detects SELECT * when JOINs are present — columns are ambiguous and
/// more data than needed is typically fetched. Unlike the Cheap NZ001 rule
/// (which uses regex), this rule uses the AST to accurately detect JOINs
/// regardless of subquery nesting or string literals that look like SELECT.
/// </summary>
public class RuleNZ101_SelectStarWithJoin : LintRule
{
    public override string Id => "NZ101";
    public override string Name => "SELECT * with JOIN";
    public override string Description => "SELECT * with JOINs may return ambiguous column names and unnecessary data";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Expensive;
    public override int Priority => 80;

    public override IEnumerable<LintIssue> Check(string sql) => [];

    public override IEnumerable<LintIssue> CheckStatement(Statement stmt)
    {
        if (stmt is not SelectStatement select) yield break;
        if (select.From is null || select.From.Count == 0) yield break;

        // Check if any FROM item has JOINs
        var hasJoins = select.From.Any(f => f.Joins is not null && f.Joins.Count > 0);
        if (!hasJoins) yield break;

        // Check if SELECT list contains StarExpression
        var hasStar = select.SelectList.Any(si => si.Expression is StarExpression);
        if (!hasStar) yield break;

        // Find the first StarExpression position
        var star = select.SelectList.First(si => si.Expression is StarExpression);
        yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
            star.Position.Absolute, star.Position.Absolute + 1);
    }
}

// ====== NZ102: Missing ON in JOIN ======
/// <summary>
/// Detects JOIN clauses that lack an ON condition.
/// While NZ004 (CROSS JOIN) catches explicit CROSS JOIN, this catches
/// INNER/LEFT/RIGHT JOINs without ON, which is almost always a bug.
/// AST-based for accuracy — regex approaches are fragile with nested subqueries.
/// </summary>
public class RuleNZ102_MissingJoinCondition : LintRule
{
    public override string Id => "NZ102";
    public override string Name => "Missing JOIN condition";
    public override string Description => "JOIN without ON condition produces a Cartesian product";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Expensive;
    public override int Priority => 90;

    public override IEnumerable<LintIssue> Check(string sql) => [];

    public override IEnumerable<LintIssue> CheckStatement(Statement stmt)
    {
        if (stmt is not SelectStatement select) yield break;
        if (select.From is null) yield break;

        foreach (var fromItem in select.From)
        {
            if (fromItem.Joins is null) continue;
            foreach (var join in fromItem.Joins)
            {
                // Skip CROSS JOIN — that's intentional via NZ004
                if (join.Type == JoinType.Cross) continue;
                if (join.OnCondition is null && (join.UsingColumns is null || join.UsingColumns.Count == 0))
                {
                    yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
                        join.Position.Absolute, join.Position.Absolute + 4);
                }
            }
        }
    }
}

// ====== NZ103: Aggregates without GROUP BY ======
/// <summary>
/// Detects when a SELECT mixes aggregate functions (COUNT, SUM, AVG, MIN, MAX)
/// with bare column references without a GROUP BY clause.
/// This is typically invalid SQL or indicates a logic bug.
/// AST-based because we need to distinguish aggregate function calls from scalar ones.
/// </summary>
public class RuleNZ103_AggregateWithoutGroupBy : LintRule
{
    public override string Id => "NZ103";
    public override string Name => "Aggregate without GROUP BY";
    public override string Description => "Mixing aggregate functions with bare columns requires GROUP BY";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Expensive;
    public override int Priority => 90;

    private static readonly HashSet<string> AggregateFuncs = new(
        NetezzaSqlCatalog.BuiltinFunctions
            .Where(function => function.Category == NetezzaFunctionCategory.Aggregate)
            .Select(function => function.Name)
            .Concat(["ARRAY_AGG", "CORR", "COVAR_POP", "COVAR_SAMP"]),
        StringComparer.OrdinalIgnoreCase);

    public override IEnumerable<LintIssue> Check(string sql) => [];

    public override IEnumerable<LintIssue> CheckStatement(Statement stmt)
    {
        if (stmt is not SelectStatement select) yield break;
        // If there IS a GROUP BY, aggregates are properly scoped
        if (select.GroupBy is not null && select.GroupBy.Count > 0) yield break;

        bool hasAggregate = false;
        bool hasBareColumn = false;
        var bareColumnPositions = new List<SourcePosition>();

        foreach (var item in select.SelectList)
        {
            if (ContainsAggregate(item.Expression))
            {
                hasAggregate = true;
            }
            else if (item.Expression is ColumnReference colRef)
            {
                hasBareColumn = true;
                bareColumnPositions.Add(item.Position);
            }
            // Subquery expressions are self-contained — ignore for this check
            else if (item.Expression is SubqueryExpression) { }
            // Literals, type literals, parameters are fine
            else if (item.Expression is Literal) { }
            else if (item.Expression is ParameterExpression) { }
            else if (item.Expression is TypeLiteral) { }
            // Other expressions (binary, unary, case, etc.) that may contain columns
            else
            {
                // Check if this complex expression contains bare column references
                var cols = new List<string>();
                CollectColumnRefs(item.Expression, cols);
                if (cols.Count > 0)
                {
                    hasBareColumn = true;
                    bareColumnPositions.Add(item.Position);
                }
            }
        }

        if (hasAggregate && hasBareColumn)
        {
            // Report on the first bare column reference
            var pos = bareColumnPositions[0];
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
                pos.Absolute, pos.Absolute + 1);
        }
    }

    private static bool ContainsAggregate(Expression expr)
    {
        return expr switch
        {
            FunctionCall fn => AggregateFuncs.Contains(fn.Name) || HasNestedAggregate(fn.Arguments),
            SubqueryExpression => false, // self-contained
            CaseExpression caseExpr => caseExpr.WhenClauses.Any(wc => ContainsAggregate(wc.When) || ContainsAggregate(wc.Then))
                || (caseExpr.ElseClause is not null && ContainsAggregate(caseExpr.ElseClause)),
            BinaryExpression bin => ContainsAggregate(bin.Left) || ContainsAggregate(bin.Right),
            UnaryExpression un => ContainsAggregate(un.Operand),
            CastExpression cast => ContainsAggregate(cast.Expression),
            CastFunctionExpression castFn => ContainsAggregate(castFn.Expression),
            InExpression ine => ContainsAggregate(ine.Left)
                || (ine.Values is not null && ine.Values.Any(ContainsAggregate)),
            BetweenExpression be => ContainsAggregate(be.Value) || ContainsAggregate(be.Low) || ContainsAggregate(be.High),
            _ => false
        };
    }

    private static bool HasNestedAggregate(IReadOnlyList<Expression>? args)
    {
        if (args is null) return false;
        return args.Any(ContainsAggregate);
    }

    private static void CollectColumnRefs(Expression expr, List<string> cols)
    {
        switch (expr)
        {
            case ColumnReference col:
                cols.Add(col.Name);
                break;
            case FunctionCall fn when fn.Arguments is not null:
                // Recurse into function arguments to find bare column references.
                // Aggregate functions (COUNT, SUM, etc.) are already detected by
                // ContainsAggregate() — they won't reach this branch because the
                // expression type check in the caller handles FunctionCall via
                // ContainsAggregate() first. This branch handles scalar functions
                // like UPPER(col), CONCAT(col, ...), COALESCE(col, 0), etc.
                foreach (var arg in fn.Arguments)
                    CollectColumnRefs(arg, cols);
                break;
            case BinaryExpression bin:
                CollectColumnRefs(bin.Left, cols);
                CollectColumnRefs(bin.Right, cols);
                break;
            case UnaryExpression un:
                CollectColumnRefs(un.Operand, cols);
                break;
            case CaseExpression caseExpr:
                foreach (var wc in caseExpr.WhenClauses)
                {
                    CollectColumnRefs(wc.When, cols);
                    CollectColumnRefs(wc.Then, cols);
                }
                if (caseExpr.ElseClause is not null)
                    CollectColumnRefs(caseExpr.ElseClause, cols);
                break;
            case CastExpression cast:
                CollectColumnRefs(cast.Expression, cols);
                break;
            case CastFunctionExpression castFn:
                CollectColumnRefs(castFn.Expression, cols);
                break;
            case InExpression ine:
                CollectColumnRefs(ine.Left, cols);
                if (ine.Values is not null)
                    foreach (var v in ine.Values) CollectColumnRefs(v, cols);
                break;
            case BetweenExpression be:
                CollectColumnRefs(be.Value, cols);
                CollectColumnRefs(be.Low, cols);
                CollectColumnRefs(be.High, cols);
                break;
            case ExtractExpression ext:
                CollectColumnRefs(ext.Source, cols);
                break;
            case IsExpression ise:
                CollectColumnRefs(ise.Left, cols);
                break;
        }
    }
}

// ====== NZ104: Unused CTE ======
/// <summary>
/// Detects CTEs defined in a WITH clause that are never referenced in the
/// main query or any subsequent CTE definition.
/// This is a code smell — unused CTEs add noise and may indicate dead code.
/// AST-based because we need accurate name resolution across CTE scope.
/// </summary>
public class RuleNZ104_UnusedCte : LintRule
{
    public override string Id => "NZ104";
    public override string Name => "Unused CTE";
    public override string Description => "CTE defined but never referenced in the query";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Expensive;
    public override int Priority => 70;

    public override IEnumerable<LintIssue> Check(string sql) => [];

    public override IEnumerable<LintIssue> CheckStatement(Statement stmt)
    {
        if (stmt is not SelectStatement select) yield break;
        if (select.With is null) yield break;

        var ctes = select.With.Ctes;
        if (ctes.Count == 0) yield break;

        // Collect all CTE names that are defined
        var cteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cte in ctes)
            cteNames.Add(cte.Name);

        // Collect all table references in the main query and in other CTEs
        var referencedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectTableReferences(select, referencedNames);

        // Also collect references from CTE definitions themselves (a CTE can reference an earlier CTE)
        foreach (var cte in ctes)
            CollectTableReferences(cte.Query, referencedNames);

        // Report CTEs that are defined but never referenced
        foreach (var cte in ctes)
        {
            if (!referencedNames.Contains(cte.Name))
            {
                yield return new LintIssue(Id,
                    $"{Id}: CTE '{cte.Name}' is defined but never used", DefaultSeverity,
                    cte.Position.Absolute, cte.Position.Absolute + cte.Name.Length);
            }
        }
    }

    private static void CollectTableReferences(SelectStatement select, HashSet<string> names)
    {
        if (select.From is null) return;

        foreach (var fromItem in select.From)
        {
            CollectTableSourceRefs(fromItem.Source, names);
            if (fromItem.Joins is null) continue;
            foreach (var join in fromItem.Joins)
                CollectTableSourceRefs(join.Source, names);
        }

        // Also check subqueries in expressions
        if (select.Where is not null)
            CollectExprTableRefs(select.Where, names);

        if (select.Having is not null)
            CollectExprTableRefs(select.Having, names);
    }

    private static void CollectTableSourceRefs(TableSource source, HashSet<string> names)
    {
        if (source.Table is not null)
        {
            names.Add(source.Table.Name);
            if (source.Alias is not null)
                names.Add(source.Alias);
        }

        if (source.Subquery is not null)
            CollectTableReferences(source.Subquery, names);
    }

    private static void CollectExprTableRefs(Expression expr, HashSet<string> names)
    {
        switch (expr)
        {
            case SubqueryExpression sub:
                CollectTableReferences(sub.Query, names);
                break;
            case BinaryExpression bin:
                CollectExprTableRefs(bin.Left, names);
                CollectExprTableRefs(bin.Right, names);
                break;
            case UnaryExpression un:
                CollectExprTableRefs(un.Operand, names);
                break;
            case InExpression ine:
                if (ine.Subquery is not null)
                    CollectTableReferences(ine.Subquery, names);
                break;
            case ExistsExpression exists:
                CollectTableReferences(exists.Subquery, names);
                break;
            case CaseExpression caseExpr:
                foreach (var wc in caseExpr.WhenClauses)
                {
                    CollectExprTableRefs(wc.When, names);
                    CollectExprTableRefs(wc.Then, names);
                }
                if (caseExpr.ElseClause is not null)
                    CollectExprTableRefs(caseExpr.ElseClause, names);
                break;
        }
    }
}

// ====== NZ105: DISTINCT with ORDER BY on non-selected column ======
/// <summary>
/// Detects when a DISTINCT query has ORDER BY on columns not in the SELECT list.
/// This is invalid SQL — with DISTINCT, ORDER BY can only reference columns that
/// appear in the SELECT list.
/// AST-based because we need to distinguish column references in ORDER BY and
/// compare them with the SELECT list (including aliases).
/// </summary>
public class RuleNZ105_DistinctOrderByMismatch : LintRule
{
    public override string Id => "NZ105";
    public override string Name => "DISTINCT ORDER BY mismatch";
    public override string Description => "With DISTINCT, ORDER BY columns must appear in the SELECT list";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Expensive;
    public override int Priority => 85;

    public override IEnumerable<LintIssue> Check(string sql) => [];

    public override IEnumerable<LintIssue> CheckStatement(Statement stmt)
    {
        if (stmt is not SelectStatement select) yield break;
        if (select.Modifier is null || !select.Modifier.Distinct) yield break;
        if (select.OrderBy is null || select.OrderBy.Count == 0) yield break;

        // Build set of column names and aliases in SELECT list
        var selectColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in select.SelectList)
        {
            if (item.Alias is not null)
                selectColumns.Add(item.Alias);
            if (item.Expression is ColumnReference col)
                selectColumns.Add(col.Name);
        }

        // Check each ORDER BY item
        foreach (var orderBy in select.OrderBy)
        {
            if (orderBy.Expression is ColumnReference orderCol)
            {
                if (!selectColumns.Contains(orderCol.Name))
                {
                    yield return new LintIssue(Id,
                        $"{Id}: ORDER BY column '{orderCol.Name}' not in SELECT list with DISTINCT",
                        DefaultSeverity,
                        orderBy.Position.Absolute,
                        orderBy.Position.Absolute + orderCol.Name.Length);
                }
            }
            else if (orderBy.Expression is Literal lit && lit.Kind == LiteralKind.Number)
            {
                // Ordinal position (e.g., ORDER BY 1) — always valid
                continue;
            }
            else if (orderBy.Expression is not ColumnReference)
            {
                // Complex expression in ORDER BY with DISTINCT — could be valid or not
                // Report as potential issue if it's not a standard reference
                yield return new LintIssue(Id,
                    $"{Id}: Complex expression in ORDER BY may not be valid with DISTINCT",
                    LintSeverity.Warning,
                    orderBy.Position.Absolute,
                    orderBy.Position.Absolute + 1);
            }
        }
    }
}

// ====== NZ106: COMMIT/ROLLBACK in script context ======
/// <summary>
/// Flags COMMIT and ROLLBACK statements as potentially risky in ad-hoc
/// SQL scripts. Stray COMMIT/ROLLBACK may affect outer transaction context
/// or run outside a transaction entirely.
/// This rule does NOT track BEGIN statements (state tracking across
/// disconnected analysis calls is unreliable with the current architecture).
/// Always flags COMMIT/ROLLBACK as a reminder to verify transaction context.
/// </summary>
public class RuleNZ106_TransactionStatement : LintRule
{
    public override string Id => "NZ106";
    public override string Name => "Transaction statement in script";
    public override string Description => "COMMIT or ROLLBACK in script — verify transaction context";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;
    public override RuleCost Cost => RuleCost.Expensive;
    public override int Priority => 70;

    public override IEnumerable<LintIssue> Check(string sql) => [];

    public override IEnumerable<LintIssue> CheckStatement(Statement stmt)
    {
        if (stmt is CommitStatement)
        {
            yield return new LintIssue(Id, $"{Id}: COMMIT statement in script — verify transaction context is intentional", DefaultSeverity,
                stmt.Position.Absolute, stmt.Position.Absolute + 6);
        }
        else if (stmt is RollbackStatement)
        {
            yield return new LintIssue(Id, $"{Id}: ROLLBACK statement in script — verify transaction context is intentional", DefaultSeverity,
                stmt.Position.Absolute, stmt.Position.Absolute + 8);
        }
    }
}

// ====== NZ107: Unused column alias ======
/// <summary>
/// Detects column aliases in SELECT that are never referenced in ORDER BY or HAVING.
/// If an alias is defined but never used for ordering or filtering, it may be
/// unnecessary noise and could be removed for clarity.
/// Only flags aliases on simple column references (not expressions or aggregates).
/// Skips aliases that match the original column name exactly (e.g., <c>col AS col</c>).
/// </summary>
public class RuleNZ107_UnusedColumnAlias : LintRule
{
    public override string Id => "NZ107";
    public override string Name => "Unused column alias";
    public override string Description => "Column alias defined but never used in ORDER BY or HAVING";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;
    public override RuleCost Cost => RuleCost.Expensive;
    public override int Priority => 50;

    public override IEnumerable<LintIssue> Check(string sql) => [];

    public override IEnumerable<LintIssue> CheckStatement(Statement stmt)
    {
        if (stmt is not SelectStatement select) yield break;
        if (select.SelectList.Count == 0) yield break;

        // Collect names referenced in ORDER BY and HAVING
        var referencedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (select.OrderBy is not null)
        {
            foreach (var ob in select.OrderBy)
            {
                if (ob.Expression is ColumnReference col)
                    referencedNames.Add(col.Name);
            }
        }
        if (select.Having is not null)
            CollectColumnNames(select.Having, referencedNames);

        foreach (var item in select.SelectList)
        {
            if (item.Alias is null) continue;
            if (item.Expression is not ColumnReference colRef) continue;
            // Skip trivial alias that matches the column name exactly
            if (string.Equals(item.Alias, colRef.Name, StringComparison.OrdinalIgnoreCase)) continue;

            // Flag if alias is never referenced in ORDER BY or HAVING
            if (!referencedNames.Contains(item.Alias))
            {
                yield return new LintIssue(Id,
                    $"{Id}: Column alias '{item.Alias}' for '{colRef.Name}' is never used in ORDER BY or HAVING",
                    DefaultSeverity,
                    item.Position.Absolute, item.Position.Absolute + item.Alias.Length);
            }
        }
    }

    private static void CollectColumnNames(Expression expr, HashSet<string> names)
    {
        switch (expr)
        {
            case ColumnReference col:
                names.Add(col.Name);
                break;
            case BinaryExpression bin:
                CollectColumnNames(bin.Left, names);
                CollectColumnNames(bin.Right, names);
                break;
            case UnaryExpression un:
                CollectColumnNames(un.Operand, names);
                break;
            case FunctionCall fn when fn.Arguments is not null:
                foreach (var arg in fn.Arguments)
                    CollectColumnNames(arg, names);
                break;
        }
    }
}

// ====== NZ108: Subquery in SELECT list (may be better as JOIN) ======
/// <summary>
/// Detects subqueries in the SELECT list that are not wrapped in an aggregate
/// function and could potentially be rewritten as a JOIN for better performance.
/// Scalar subqueries in SELECT are executed row-by-row and can be slow.
/// Rewriting as a LEFT JOIN or CROSS JOIN is typically more efficient.
/// This is an Information-level hint — not always applicable.
/// </summary>
public class RuleNZ108_SubqueryInSelect : LintRule
{
    public override string Id => "NZ108";
    public override string Name => "Subquery in SELECT list";
    public override string Description => "Subquery in SELECT may perform better as a JOIN";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;
    public override RuleCost Cost => RuleCost.Expensive;
    public override int Priority => 50;

    public override IEnumerable<LintIssue> Check(string sql) => [];

    public override IEnumerable<LintIssue> CheckStatement(Statement stmt)
    {
        if (stmt is not SelectStatement select) yield break;

        foreach (var item in select.SelectList)
        {
            if (item.Expression is SubqueryExpression sub)
            {
                yield return new LintIssue(Id,
                    $"{Id}: Subquery in SELECT list may perform better as a JOIN — consider rewriting",
                    DefaultSeverity,
                    sub.Position.Absolute, sub.Position.Absolute + 1);
            }
        }
    }
}

// ====== All expensive rules registry ======
public static class NzExpensiveRules
{
    public static readonly IReadOnlyList<LintRule> AllRules = new LintRule[]
    {
        new RuleNZ101_SelectStarWithJoin(),
        new RuleNZ102_MissingJoinCondition(),
        new RuleNZ103_AggregateWithoutGroupBy(),
        new RuleNZ104_UnusedCte(),
        new RuleNZ105_DistinctOrderByMismatch(),
        new RuleNZ106_TransactionStatement(),
        new RuleNZ107_UnusedColumnAlias(),
        new RuleNZ108_SubqueryInSelect(),
    };
}
