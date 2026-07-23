using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

/// <summary>
/// Tracks NZPLSQL procedure symbols and the procedure-level diagnostics that
/// cannot be derived from the ordinary SQL relation scope.
/// </summary>
internal sealed class ProcedureScopeBuilder
{
    private sealed class ParameterState(ProcedureParameterMode mode, SourcePosition position)
    {
        public ProcedureParameterMode Mode { get; } = mode;
        public SourcePosition Position { get; } = position;
        public bool Assigned { get; set; } = mode == ProcedureParameterMode.In;
    }

    private sealed class VariableState(SourcePosition position)
    {
        public SourcePosition Position { get; } = position;
        public bool Used { get; set; }
    }

    private readonly Dictionary<string, ParameterState> _parameters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VariableState> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ValidationError> _diagnostics = new();
    private bool _hasReturns;
    private SourcePosition? _returnsPosition;
    private bool _hasReturn;

    public void RegisterParameter(ProcedureParameter parameter) =>
        _parameters[parameter.Name] = new ParameterState(parameter.Mode, parameter.Position);

    public void RegisterVariable(VariableDeclaration declaration)
    {
        if (!_variables.ContainsKey(declaration.Name))
            _variables[declaration.Name] = new VariableState(declaration.Position);
    }

    public void SetHasReturns(SourcePosition position)
    {
        _hasReturns = true;
        _returnsPosition = position;
    }

    public void MarkReturn() => _hasReturn = true;

    public void MarkNameUsed(string name)
    {
        if (_variables.TryGetValue(name, out var variable))
        {
            variable.Used = true;
            return;
        }

        if (_parameters.TryGetValue(name, out var parameter))
            parameter.Assigned = true;
    }

    public void MarkExpressionUsed(Expression expression)
    {
        switch (expression)
        {
            case ColumnReference column:
                MarkNameUsed(column.Name);
                break;
            case BinaryExpression binary:
                MarkExpressionUsed(binary.Left);
                MarkExpressionUsed(binary.Right);
                break;
            case UnaryExpression unary:
                MarkExpressionUsed(unary.Operand);
                break;
            case FunctionCall function when function.Arguments is not null:
                foreach (var argument in function.Arguments) MarkExpressionUsed(argument);
                break;
            case CaseExpression caseExpression:
                if (caseExpression.Value is not null) MarkExpressionUsed(caseExpression.Value);
                foreach (var clause in caseExpression.WhenClauses)
                {
                    MarkExpressionUsed(clause.When);
                    MarkExpressionUsed(clause.Then);
                }
                if (caseExpression.ElseClause is not null) MarkExpressionUsed(caseExpression.ElseClause);
                break;
            case CastExpression cast:
                MarkExpressionUsed(cast.Expression);
                break;
            case CastFunctionExpression castFunction:
                MarkExpressionUsed(castFunction.Expression);
                break;
            case InExpression inExpression:
                MarkExpressionUsed(inExpression.Left);
                if (inExpression.Values is not null)
                    foreach (var value in inExpression.Values) MarkExpressionUsed(value);
                break;
            case BetweenExpression between:
                MarkExpressionUsed(between.Value);
                MarkExpressionUsed(between.Low);
                MarkExpressionUsed(between.High);
                break;
            case IsExpression isExpression:
                MarkExpressionUsed(isExpression.Left);
                break;
            case ExistsExpression exists:
                MarkSelectUsed(exists.Subquery);
                break;
            case SubqueryExpression subquery:
                MarkSelectUsed(subquery.Query);
                break;
            case ExtractExpression extract:
                MarkExpressionUsed(extract.Source);
                break;
            case QuantifiedComparisonExpression quantified:
                MarkExpressionUsed(quantified.Left);
                MarkExpressionUsed(quantified.Right);
                break;
        }
    }

    public void MarkSelectUsed(SelectStatement select)
    {
        foreach (var item in select.SelectList) MarkExpressionUsed(item.Expression);
        if (select.Where is not null) MarkExpressionUsed(select.Where);
        if (select.GroupBy is not null)
            foreach (var item in select.GroupBy) MarkExpressionUsed(item);
        if (select.Having is not null) MarkExpressionUsed(select.Having);
        if (select.OrderBy is not null)
            foreach (var item in select.OrderBy) MarkExpressionUsed(item.Expression);
    }

    public void CheckStandaloneSelect(SelectStatement select)
    {
        if (!select.HasInto)
        {
            _diagnostics.Add(new ValidationError(
                "Possibly standalone SELECT in procedure should use INTO or PERFORM if the result is expected to be consumed",
                "information", select.Position, "SQL037"));
        }
    }

    public IReadOnlyList<ValidationError> Finalize()
    {
        if (_hasReturns && !_hasReturn && _returnsPosition is not null)
        {
            _diagnostics.Add(new ValidationError(
                "Procedure declares RETURNS but has no RETURN statement",
                "warning", _returnsPosition, "SQL038"));
        }

        foreach (var (name, variable) in _variables)
        {
            if (!variable.Used)
            {
                _diagnostics.Add(new ValidationError(
                    $"Variable '{name.ToUpperInvariant()}' is declared but never used",
                    "information", variable.Position, "SQL039"));
            }
        }

        foreach (var (name, parameter) in _parameters)
        {
            if (parameter.Mode is ProcedureParameterMode.Out or ProcedureParameterMode.InOut &&
                !parameter.Assigned)
            {
                _diagnostics.Add(new ValidationError(
                    $"OUT/INOUT parameter '{name.ToUpperInvariant()}' is possibly not assigned a value",
                    "warning", parameter.Position, "SQL040"));
            }
        }

        return _diagnostics.ToArray();
    }
}
