using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

public partial class NzSqlVisitor
{
    private enum SqlTypeFamily { Unknown, String, Numeric, Boolean, DateTime }

    private void ValidateArithmeticOperands(Expression left, Expression right, SourcePosition pos)
    {
        var operands = new List<(SqlTypeFamily Family, SourcePosition Position)>();
        CollectArithmeticOperands(left, operands);
        CollectArithmeticOperands(right, operands);

        for (var i = 0; i < operands.Count; i++)
        {
            for (var j = i + 1; j < operands.Count; j++)
            {
                var first = operands[i];
                var second = operands[j];
                if (first.Family == SqlTypeFamily.String && second.Family == SqlTypeFamily.Numeric)
                {
                    AddError("Text value used in arithmetic expression with numeric value may cause implicit conversion",
                        "warning", "SQL025", second.Position);
                }
                else if (first.Family == SqlTypeFamily.Numeric && second.Family == SqlTypeFamily.String)
                {
                    AddError("Numeric value used in arithmetic expression with text value; implicit conversion may produce unexpected results",
                        "warning", "SQL025", second.Position);
                }
            }
        }
    }

    private void CollectArithmeticOperands(
        Expression expr,
        List<(SqlTypeFamily Family, SourcePosition Position)> operands)
    {
        switch (expr)
        {
            case ColumnReference cr:
            {
                var family = ClassifyColumnType(ResolveColumnDataType(cr));
                if (family != SqlTypeFamily.Unknown)
                    operands.Add((family, cr.Position));
                break;
            }
            case Literal l when l.Kind is not LiteralKind.Null:
                operands.Add((l.Kind == LiteralKind.Number ? SqlTypeFamily.Numeric : l.Kind == LiteralKind.String ? SqlTypeFamily.String : SqlTypeFamily.Unknown, l.Position));
                break;
            case BinaryExpression b when IsArithmeticOperator(b.Operator):
                CollectArithmeticOperands(b.Left, operands);
                CollectArithmeticOperands(b.Right, operands);
                break;
            case UnaryExpression u when u.Operator is UnaryOperator.Minus or UnaryOperator.Plus:
                CollectArithmeticOperands(u.Operand, operands);
                break;
            case CastExpression c:
                operands.Add((ClassifyColumnType(c.TargetType.Name), c.Position));
                break;
            case CastFunctionExpression cf:
                operands.Add((ClassifyColumnType(cf.TargetType.Name), cf.Position));
                break;
        }
    }

    private static bool IsArithmeticOperator(BinaryOperator op) =>
        op is BinaryOperator.Plus or BinaryOperator.Minus or BinaryOperator.Multiply
            or BinaryOperator.Divide or BinaryOperator.Modulo or BinaryOperator.Caret;

    private static bool IsComparisonOperator(BinaryOperator op) =>
        op is BinaryOperator.Equals or BinaryOperator.NotEquals
            or BinaryOperator.LessThan or BinaryOperator.GreaterThan
            or BinaryOperator.LessThanEquals or BinaryOperator.GreaterThanEquals;

    private void ValidateComparisonOperands(BinaryExpression expression)
    {
        var leftFamily = GetExpressionTypeFamily(expression.Left);
        var rightFamily = GetExpressionTypeFamily(expression.Right);
        if (leftFamily == SqlTypeFamily.Unknown || rightFamily == SqlTypeFamily.Unknown)
            return;

        var leftIsText = leftFamily == SqlTypeFamily.String;
        var rightIsText = rightFamily == SqlTypeFamily.String;
        var leftIsNumeric = leftFamily == SqlTypeFamily.Numeric;
        var rightIsNumeric = rightFamily == SqlTypeFamily.Numeric;
        if ((!leftIsText && !leftIsNumeric) || (!rightIsText && !rightIsNumeric) ||
            (leftIsText == rightIsText))
            return;

        var ordered = expression.Operator is not BinaryOperator.Equals and
            not BinaryOperator.NotEquals;
        var code = ordered && leftIsText ? "SQL026" : "SQL025";
        var message = code == "SQL026"
            ? "Text column compared to numeric value with ordered operator; use CAST for intentional comparison"
            : "Compared text and numeric values may cause implicit conversion";
        AddError(message, "warning", code, expression.Position);
    }

    private SqlTypeFamily GetExpressionTypeFamily(Expression expression) => expression switch
    {
        ColumnReference column => ClassifyColumnType(ResolveColumnDataType(column)),
        Literal literal when literal.Kind == LiteralKind.Number => SqlTypeFamily.Numeric,
        Literal literal when literal.Kind == LiteralKind.String => SqlTypeFamily.String,
        CastExpression cast => ClassifyColumnType(cast.TargetType.Name),
        CastFunctionExpression castFunction => ClassifyColumnType(castFunction.TargetType.Name),
        _ => SqlTypeFamily.Unknown
    };

    private string? ResolveColumnDataType(ColumnReference cr)
    {
        if (cr.Qualifier is not null)
        {
            var table = _scope.FindTable(cr.Qualifier);
            return table?.Columns?.FirstOrDefault(c =>
                c.Name.Equals(cr.Name, StringComparison.OrdinalIgnoreCase))?.DataType;
        }

        foreach (var table in _scope.GetAllVisibleTables())
        {
            var col = table.Columns?.FirstOrDefault(c =>
                c.Name.Equals(cr.Name, StringComparison.OrdinalIgnoreCase));
            if (col?.DataType is not null) return col.DataType;
        }

        return null;
    }

    private static SqlTypeFamily ClassifyColumnType(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType)) return SqlTypeFamily.Unknown;
        var upper = dataType.ToUpperInvariant();

        if (upper is "BOOLEAN" or "BOOL") return SqlTypeFamily.Boolean;
        if (upper.Contains("CHAR") || upper is "TEXT" or "CLOB" or "NCLOB" or "NCHAR" or "NVARCHAR")
            return SqlTypeFamily.String;
        if (upper is "DATE" or "TIME" or "TIMESTAMP" or "TIMESTAMPTZ" or "TIMETZ" or "INTERVAL"
            || upper.StartsWith("TIMESTAMP", StringComparison.Ordinal))
            return SqlTypeFamily.DateTime;
        if (upper is "INT" or "INT1" or "INT2" or "INT4" or "INT8" or "INTEGER" or "BIGINT"
            or "SMALLINT" or "BYTEINT" or "NUMERIC" or "DECIMAL" or "FLOAT" or "FLOAT4"
            or "FLOAT8" or "REAL" or "DOUBLE" or "DOUBLE PRECISION")
            return SqlTypeFamily.Numeric;

        return SqlTypeFamily.Unknown;
    }
}
