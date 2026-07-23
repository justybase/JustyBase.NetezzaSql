using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

public partial class NzSqlVisitor
{
    public void Visit(InsertStatement stmt)
    {
        _scope.EnterScope();

        // Validate target table
        var source = new TableSource(stmt.Position, stmt.Target, null, null);
        Visit(source);

        if (stmt.Columns is { Count: > 0 }
            && _schema?.GetTable(stmt.Target.Database, stmt.Target.Schema, stmt.Target.Name)?.Columns is { } targetColumns)
        {
            foreach (var column in stmt.Columns)
            {
                if (!targetColumns.Any(candidate => candidate.Name.Equals(column, StringComparison.OrdinalIgnoreCase)))
                    AddError($"Column '{column}' does not exist on relation '{stmt.Target.Name}'", "error", "SQL030", stmt.Position);
            }
        }

        // Validate expressions in VALUES
        if (stmt.Values is not null)
        {
            if (stmt.Columns is { Count: > 0 })
            {
                var expectedCount = stmt.Columns.Count;
                foreach (var row in stmt.Values)
                {
                    if (row.Count != expectedCount)
                    {
                        AddError(
                            $"INSERT column count ({expectedCount}) does not match VALUES count ({row.Count})",
                            "error", "SQL029", stmt.Position);
                        break;
                    }
                }
            }

            foreach (var row in stmt.Values)
            {
                foreach (var val in row)
                    Visit(val);
            }
        }

        // Validate subquery
        if (stmt.SourceQuery is not null)
            Visit(stmt.SourceQuery);

        _scope.ExitScope();
    }

    public void Visit(UpdateStatement stmt)
    {
        ValidateUpdateStructure(stmt);
        _scope.EnterScope();

        // Register target table with alias
        var source = new TableSource(stmt.Position, stmt.Target, null, stmt.Alias);
        Visit(source);

        // Visit FROM clause first so its tables are in scope for SET/WHERE
        if (stmt.From is not null)
        {
            foreach (var tr in stmt.From)
                Visit(tr);
        }

        // Validate SET columns and expressions
        foreach (var item in stmt.SetItems)
        {
            Visit(item.Column);
            Visit(item.Value);
        }

        // Validate WHERE
        if (stmt.Where is not null)
            Visit(stmt.Where);

        _scope.ExitScope();
    }

    public void Visit(DeleteStatement stmt)
    {
        ValidateDeleteStructure(stmt);
        _scope.EnterScope();

        var source = new TableSource(stmt.Position, stmt.Target, null, stmt.Alias);
        Visit(source);

        // Validate WHERE
        if (stmt.Where is not null)
            Visit(stmt.Where);

        _scope.ExitScope();
    }

    public void Visit(MergeStatement stmt)
    {
        _scope.EnterScope();

        // Register target table
        var targetSource = new TableSource(stmt.Position, stmt.Target, null, stmt.TargetAlias);
        Visit(targetSource);

        // Register source table (USING)
        Visit(stmt.Source);

        // Visit ON condition
        Visit(stmt.OnCondition);

        // Visit clauses
        foreach (var clause in stmt.Clauses)
        {
            switch (clause)
            {
                case MergeMatchedUpdateClause u:
                    if (u.Condition is not null) Visit(u.Condition);
                    foreach (var item in u.SetItems)
                    {
                        Visit(item.Column);
                        Visit(item.Value);
                    }
                    if (u.Where is not null) Visit(u.Where);
                    break;
                case MergeMatchedDeleteClause d:
                    if (d.Condition is not null) Visit(d.Condition);
                    break;
                case MergeNotMatchedInsertClause i:
                    if (i.Condition is not null) Visit(i.Condition);
                    foreach (var val in i.Values)
                        Visit(val);
                    break;
            }
        }

        _scope.ExitScope();
    }
}
