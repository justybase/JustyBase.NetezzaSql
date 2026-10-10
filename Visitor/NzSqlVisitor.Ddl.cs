using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

public partial class NzSqlVisitor
{
    public void Visit(CreateProcedureStatement stmt)
    {
        _scope.EnterScope();
        var previousProcedureScope = _procedureScope;
        _procedureScope = new ProcedureScopeBuilder();
        if (stmt.Parameters is not null)
        {
            foreach (var parameter in stmt.Parameters)
                _procedureScope.RegisterParameter(parameter);
        }

        // Validate variable declarations
        if (stmt.Body.Declarations is not null)
        {
            foreach (var decl in stmt.Body.Declarations)
            {
                _procedureScope.RegisterVariable(decl);
                CheckTypeLength(decl.Type, decl.Position);
            }
        }

        // Validate body statements with procedure context
        _inProcedureContext = true;
        if (stmt.Body.Statements is not null)
        {
            VisitProcedureStatements(stmt.Body.Statements);
        }

        // Validate exception handlers
        if (stmt.Body.ExceptionHandlers is not null)
        {
            foreach (var handler in stmt.Body.ExceptionHandlers)
            {
                VisitProcedureStatements(handler.Statements);
            }
        }
        _inProcedureContext = false;

        foreach (var diagnostic in _procedureScope.Finalize())
            AddError(diagnostic.Message, diagnostic.Severity, diagnostic.Code, diagnostic.Position,
                diagnostic.EndLine, diagnostic.EndColumn);
        _procedureScope = previousProcedureScope;

        _scope.ExitScope();
    }

    public void Visit(CreateTableStatement stmt)
    {
        ValidateCreateTableStructure(stmt);
        _scope.EnterScope();

        // Register table in multi-statement scope BEFORE validating the source
        _multiStatementScope ??= new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        var cols = stmt.Columns?.Select(c => new ColumnInfo(c.Name)).ToList() ?? new List<ColumnInfo>();
        if (stmt.AsSelect is not null)
        {
            var inferredCols = InferColumnsFromSelect(stmt.AsSelect);
            cols.AddRange(inferredCols);
        }
        _multiStatementScope[stmt.Table.Name.ToUpperInvariant()] = new TableInfo(
            stmt.Table.Name, stmt.Table.Schema, stmt.Table.Database,
            IsTempTable: stmt.Temporary, Columns: cols.Count > 0 ? cols : null);

        var source = new TableSource(stmt.Position, stmt.Table, null, null);
        Visit(source);

        // Validate column types
        if (stmt.Columns is not null)
        {
            foreach (var col in stmt.Columns)
            {
                CheckTypeLength(col.Type, col.Position);
            }
        }

        // Validate AS SELECT subquery
        if (stmt.AsSelect is not null)
            Visit(stmt.AsSelect);

        _scope.ExitScope();
    }

    public void Visit(CreateExternalTableStatement stmt)
    {
        _scope.EnterScope();

        // Validate column types
        if (stmt.Columns is not null)
        {
            foreach (var col in stmt.Columns)
            {
                CheckTypeLength(col.Type, col.Position);
            }
        }

        // Validate external table options
        if (stmt.Options is not null)
        {
            foreach (var opt in stmt.Options)
            {
                var upperName = opt.Name.ToUpperInvariant();
                if (!KnownExternalTableOptions.Contains(upperName))
                {
                    AddError(
                        $"External table option '{opt.Name}' is not supported",
                        "error", "SQL016", opt.Position);
                }
                else
                {
                    ValidateExternalOptionValue(upperName, opt);
                }
            }
        }

        _scope.ExitScope();
    }

    public void Visit(CreateViewStatement stmt)
    {
        _scope.EnterScope();

        // Register view name in multi-statement scope BEFORE visiting source
        var cols = stmt.Query.SelectList.Select(item =>
            new ColumnInfo(item.Alias ?? InferSelectItemName(item.Expression) ?? "?")).ToList();
        var viewInfo = new TableInfo(stmt.View.Name, stmt.View.Schema, stmt.View.Database,
            IsCte: false, IsTempTable: false, Columns: cols);

        // Register in multi-statement scope
        _multiStatementScope ??= new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        _multiStatementScope[stmt.View.Name.ToUpperInvariant()] = viewInfo;

        _scope.AddTable(viewInfo);

        // Validate the inner SELECT
        Visit(stmt.Query);
        _scope.ExitScope();
    }

    public void Visit(DropStatement stmt)
    {
        if (stmt.ObjectType.Equals("TABLE", StringComparison.OrdinalIgnoreCase) ||
            stmt.ObjectType.Equals("VIEW", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var target in stmt.Targets)
            {
                LookupTableOnly(target, stmt.Position);
                _multiStatementScope?.Remove(target.Name.ToUpperInvariant());
            }
        }
        // Procedures are intentionally not validated here because the schema
        // provider exposes relations, not routine metadata.
    }

    public void Visit(AlterTableStatement stmt)
    {
        LookupTableOnly(stmt.Table, stmt.Position);
        if (stmt.Actions is null || GetSchemaTable(stmt.Table)?.Columns is not { Count: > 0 } columns)
            return;
        foreach (var action in stmt.Actions)
            ValidateConstraintKeyColumns(action, stmt.Table.Name, columns);
    }

    /// <summary>
    /// <c>ADD CONSTRAINT n PRIMARY KEY|UNIQUE|FOREIGN KEY (cols)</c>: key columns must exist on the
    /// altered table. Columns after <c>REFERENCES</c> belong to another table and are not checked.
    /// </summary>
    private void ValidateConstraintKeyColumns(
        AlterTableAction action, string tableName, IReadOnlyList<ColumnInfo> columns)
    {
        if (action is not AddConstraintAlterAction { KeyColumns: { } keys })
            return;
        foreach (var key in keys)
        {
            if (columns.Any(column => column.Name.Equals(key.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            AddError($"Column '{key.Name}' does not exist on relation '{tableName}'", "error", "SQL004",
                key.Position, key.Position.Line, key.Position.Column + key.Length);
        }
    }

    public void Visit(TruncateStatement stmt)
    {
        LookupTableOnly(stmt.Table, stmt.Position);
    }

    public void Visit(LockStatement stmt)
    {
        LookupTableOnly(stmt.Table, stmt.Position);
    }

    public void Visit(CommentStatement stmt)
    {
        LookupTableOnly(stmt.Object, stmt.Position);
        if (stmt.Column is not null && GetSchemaTable(stmt.Object)?.Columns is { Count: > 0 } columns
            && !columns.Any(column => column.Name.Equals(stmt.Column, StringComparison.OrdinalIgnoreCase)))
        {
            var at = stmt.ColumnPosition ?? stmt.Position;
            AddError($"Column '{stmt.Column}' does not exist on relation '{stmt.Object.Name}'",
                "error", "SQL004", at, at.Line, at.Column + stmt.Column.Length);
        }
    }

    public void Visit(GroomStatement stmt)
    {
        LookupTableOnly(stmt.Table, stmt.Position);
    }

    public void Visit(GenerateStatisticsStatement stmt)
    {
        if (!string.IsNullOrWhiteSpace(stmt.Table.Name))
            LookupTableOnly(stmt.Table, stmt.Position);
    }
}
