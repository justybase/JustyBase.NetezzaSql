using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

public partial class NzSqlVisitor
{
    public void Visit(Expression expr)
    {
        switch (expr)
        {
            case Literal l: break;
            case ColumnReference cr: VisitColumnRef(cr); break;
            case StarExpression s: break;
            case BinaryExpression b: VisitBinary(b); break;
            case UnaryExpression u: Visit(u.Operand); break;
            case FunctionCall fc: VisitFunction(fc); break;
            case CaseExpression ce: VisitCase(ce); break;
            case CastExpression c: Visit(c.Expression); CheckTypeLength(c.TargetType, c.Position); break;
            case CastFunctionExpression cf: Visit(cf.Expression); CheckTypeLength(cf.TargetType, cf.Position); break;
            case ExistsExpression e: Visit(e.Subquery); break;
            case SubqueryExpression sq: Visit(sq.Query); break;
            case InExpression ie:
                Visit(ie.Left);
                if (ie.Values is not null) foreach (var v in ie.Values) Visit(v);
                if (ie.Subquery is not null) Visit(ie.Subquery);
                break;
            case QuantifiedComparisonExpression qc:
                Visit(qc.Left);
                Visit(qc.Right);
                break;
            case BetweenExpression be:
                Visit(be.Value);
                Visit(be.Low);
                Visit(be.High);
                break;
            case IsExpression i: Visit(i.Left); break;
            default: break;
        }
    }

    private void VisitColumnRef(ColumnReference cr)
    {
        if (_inProcedureContext) return;

        var upperName = cr.Name.ToUpperInvariant();

        // Qualified star (e.g. E.*) — just validate the table exists
        if (cr.Name == "*")
        {
            if (cr.Qualifier is not null)
            {
                var table = _scope.FindTable(cr.Qualifier);
                if (table is null)
                {
                    var endCol = cr.Position.Column + cr.Qualifier.Length;
                    AddError($"Table or alias '{cr.Qualifier}' not found in scope", "error", "SQL003", cr.Position,
                        cr.Position.Line, endCol);
                }
                else
                {
                    // A qualified wildcard (D.*) is a real use of the table
                    // alias. Count it before returning so SQL019 does not report
                    // aliases used only for wildcard projection as unused.
                    _usedAliases.Add(cr.Qualifier.ToUpperInvariant());
                }
            }
            return;
        }

        // Boolean literals are not columns
        if (cr.Qualifier is null && BooleanLiterals.Contains(upperName)) return;

        // Special built-in values
        if (cr.Qualifier is null && SpecialBuiltinValues.Contains(upperName)) return;

        // ORDER BY can reference SELECT output aliases
        if (_inOrderBy && cr.Qualifier is null)
        {
            var aliases = GetCurrentSelectOutputAliases();
            if (aliases is not null && aliases.Contains(upperName)) return;
        }

        // SELECT list aliases from earlier items
        if (_inSelectList && cr.Qualifier is null && _selectListAliasesSoFar.Contains(upperName)) return;

        // WHERE/GROUP BY/HAVING can reference SELECT aliases (Netezza)
        if (_canReferenceSelectAliases && cr.Qualifier is null)
        {
            var aliases = GetCurrentSelectOutputAliases();
            if (aliases is not null && aliases.Contains(upperName)) return;
        }

        // System columns (always accepted unqualified)
        if (cr.Qualifier is null && SystemColumns.Contains(upperName)) return;

        if (cr.Qualifier is not null)
        {
            // Qualified column reference: table.column
            var table = _scope.FindTable(cr.Qualifier);
            if (table is null)
            {
                var endCol = cr.Position.Column + cr.Qualifier.Length;
                AddError(
                    $"Table or alias '{cr.Qualifier}' not found in scope",
                    "error", "SQL003", cr.Position, cr.Position.Line, endCol);
                return;
            }
            _usedAliases.Add(cr.Qualifier.ToUpperInvariant());

            // System pseudo-columns are implicitly valid on any qualified table
            if (SystemColumns.Contains(upperName)) return;

            if (table.Columns is null || table.Columns.Count == 0)
            {
                // Table found but has no cached columns — warn that validation is not possible
                // Skip warning for CTEs and temp tables (their columns come from the query, not schema)
                if (!table.IsCte && !table.IsTempTable)
                {
                    AddError(
                        $"Cannot validate column '{cr.Name}' - table '{table.Name}' not found in schema cache. Try refreshing the schema or the table may not exist.",
                        "warning", "SQL005", cr.Position);
                }
            }
            else if (!table.Columns.Any(c => c.Name.Equals(cr.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var endCol = cr.Position.Column + cr.Name.Length;
                AddError(
                    $"SQL004: Column '{cr.Name}' not found in table '{table.Name}'",
                    "error", "SQL004", cr.Position, cr.Position.Line, endCol);
            }
            return;
        }

        // Unqualified column reference
        var visibleTables = _scope.GetAllVisibleTables();
        // Filter out unvalidated CTEs from column resolution (their columns are inferred, not yet validated)
        if (_validatingCteName is not null)
            visibleTables = visibleTables.Where(t => !t.IsCte || _validatedCtes.Contains(t.Name)).ToList();
        var tablesWithKnownColumns = visibleTables.Where(t => t.Columns is { Count: > 0 }).ToList();
        if (tablesWithKnownColumns.Count == 0) return;

        // Check current scope first to avoid false positives in subqueries
        var currentScopeTables = _scope.GetCurrentScopeTables()
            .Where(t => t.Columns is { Count: > 0 }).ToList();
        var currentMatches = currentScopeTables
            .Where(t => t.Columns!.Any(c => c.Name.Equals(cr.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (currentMatches.Count == 1) return;
        if (currentMatches.Count > 1)
        {
            AddError($"Column '{cr.Name}' is ambiguous", "error", "SQL008", cr.Position);
            return;
        }

        // Check all scopes
        var allMatches = tablesWithKnownColumns
            .Where(t => t.Columns!.Any(c => c.Name.Equals(cr.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (allMatches.Count == 0 && currentMatches.Count == 0)
        {
            // Only report if we have metadata (tables with known columns)
            var endCol = cr.Position.Column + cr.Name.Length;
            if (tablesWithKnownColumns.Count == 1)
            {
                var table = tablesWithKnownColumns[0];
                AddError($"SQL004: Column '{cr.Name}' not found in table '{table.Name}'",
                    "error", "SQL004", cr.Position, cr.Position.Line, endCol);
            }
            else
            {
                AddError($"SQL004: Column '{cr.Name}' not found",
                    "error", "SQL004", cr.Position, cr.Position.Line, endCol);
            }
        }
        else if (allMatches.Count > 1)
        {
            AddError($"Column '{cr.Name}' is ambiguous", "error", "SQL008", cr.Position);
        }
    }

    private void VisitBinary(BinaryExpression b)
    {
        Visit(b.Left);
        Visit(b.Right);

        if (IsArithmeticOperator(b.Operator))
            ValidateArithmeticOperands(b.Left, b.Right, b.Position);
        else if (IsComparisonOperator(b.Operator))
            ValidateComparisonOperands(b);

        // Boolean context check for ON/WHERE
        if ((_inWhere || _implicitOnContext) && IsArithmeticOnly(b))
        {
            AddError("WHERE/ON expression must be boolean", "error", "SQL010", b.Position);
        }
    }

    private bool _implicitOnContext;
    private static bool IsArithmeticOnly(BinaryExpression b)
    {
        var op = b.Operator;
        return op is BinaryOperator.Plus or BinaryOperator.Minus or BinaryOperator.Multiply
            or BinaryOperator.Divide or BinaryOperator.Modulo or BinaryOperator.Caret or BinaryOperator.Concat;
    }

    private void VisitFunction(FunctionCall fc)
    {
        var upper = fc.Name.ToUpperInvariant();

        // SQL021: Aggregate in WHERE clause
        if (_inStrictWhere && IsAggregateInWhere(fc))
        {
            AddError($"Aggregate function '{fc.Name}' cannot be used in WHERE clause", "error", "SQL021", fc.Position);
        }

        // Validate function name is known
        if (!KnownFunctions.Contains(upper) && fc.Name.Length > 0)
        {
            AddError($"Function '{fc.Name}' is not recognized", "error", "SQL011", fc.Position);
        }

        if (fc.Arguments is not null)
        {
            foreach (var arg in fc.Arguments) Visit(arg);
        }
        if (fc.Over?.PartitionBy is not null)
        {
            foreach (var pe in fc.Over.PartitionBy) Visit(pe);
        }
        if (fc.Over?.OrderBy is not null)
        {
            foreach (var o in fc.Over.OrderBy) Visit(o.Expression);
        }
        if (fc.WithinGroup?.OrderBy is not null)
        {
            foreach (var o in fc.WithinGroup.OrderBy) Visit(o.Expression);
        }

        if (upper is "PERCENTILE_CONT" or "PERCENTILE_DISC")
        {
            if (fc.WithinGroup is null)
            {
                AddError($"Ordered-set aggregate '{upper}' requires WITHIN GROUP (ORDER BY ...) clause",
                    "error", "SQL047", fc.Position);
            }
            else if (fc.Over is not null)
            {
                AddError($"Ordered-set aggregate '{upper}' cannot be used as a window aggregate",
                    "error", "SQL047", fc.Position);
            }
        }

        ValidateWindowFunction(fc);
    }

    private static readonly HashSet<string> WindowFunctionsRequiringOrderBy = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "LAG", "LEAD",
        "FIRST_VALUE", "LAST_VALUE", "PERCENT_RANK", "CUME_DIST", "NTH_VALUE",
    };

    private static readonly HashSet<string> WindowFunctionsWithoutFrame = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "PERCENT_RANK", "CUME_DIST",
    };

    private void ValidateWindowFunction(FunctionCall fc)
    {
        if (fc.Over is null) return;
        var upper = fc.Name.ToUpperInvariant();

        if (WindowFunctionsRequiringOrderBy.Contains(upper) &&
            (fc.Over.OrderBy is null || fc.Over.OrderBy.Count == 0))
        {
            AddError($"Window function '{upper}' requires ORDER BY in OVER() clause",
                "error", "SQL022", fc.Position);
        }

        if (WindowFunctionsWithoutFrame.Contains(upper) && fc.Over.Frame is not null)
        {
            AddError($"Window function '{upper}' cannot include a framing specification",
                "error", "SQL024", fc.Position);
        }
    }

    private static bool IsAggregateInWhere(FunctionCall fc)
    {
        var upper = fc.Name.ToUpperInvariant();

        // Always-aggregate functions (STRING_AGG, ARRAY_AGG, etc.) — always rejected in WHERE
        if (AlwaysAggregateFunctions.Contains(upper)) return true;

        // Functions that can be scalar or aggregate (MIN, MAX, SUM, etc.)
        // Only reject when used as true aggregate: DISTINCT, *, or single-arg
        if (!AggregateFunctions.Contains(upper)) return false;
        if (fc.Distinct || fc.StarArgument) return true;
        var argCount = fc.Arguments?.Count ?? 0;
        return argCount <= 1;
    }

    private void VisitCase(CaseExpression ce)
    {
        ValidateCaseStructure(ce);
        if (ce.Value is not null) Visit(ce.Value);
        foreach (var w in ce.WhenClauses)
        {
            Visit(w.When);
            Visit(w.Then);
        }
        if (ce.ElseClause is not null) Visit(ce.ElseClause);
    }

    private void CheckTypeLength(DataTypeInfo type, SourcePosition pos)
    {
        var upper = type.Name.ToUpperInvariant();

        // INTERVAL with qualifier (e.g. INTERVAL HOUR TO MINUTE) is always valid
        if (upper.StartsWith("INTERVAL ") && !upper.Equals("INTERVAL"))
        {
            return;
        }

        // SQL013: validate type name
        if (!string.IsNullOrEmpty(type.Name) && !KnownDataTypes.Contains(upper))
        {
            AddError($"Unrecognized data type '{type.Name}'", "error", "SQL013", pos);
            return;
        }

        // SQL014: validate type parameter counts
        var paramCount = type.Parameters?.Count ?? 0;
        var isVarLen = upper is "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" or "CHARACTER" or "CHAR VARYING" or "CHARACTER VARYING"
            or "NATIONAL CHARACTER" or "NATIONAL CHAR" or "NATIONAL CHARACTER VARYING" or "BINARY" or "VARBINARY";
        var isNumericWithScale = upper is "NUMERIC" or "DECIMAL";

        if (isVarLen)
        {
            if (paramCount == 0)
                AddError($"Character type '{type.Name}' used without length", "warning", "SQL012", pos);
            else if (paramCount > 1)
                AddError($"Character type '{type.Name}' accepts at most 1 parameter", "error", "SQL014", pos);
        }
        else if (isNumericWithScale)
        {
            if (paramCount > 2)
                AddError($"Numeric type '{type.Name}' accepts at most 2 parameters (precision, scale)", "error", "SQL014", pos);
        }
        else if (paramCount > 0 && upper is not "DOUBLE PRECISION" and not "VARRAY")
        {
            AddError($"Type '{type.Name}' does not accept parameters", "error", "SQL014", pos);
        }
    }
}
