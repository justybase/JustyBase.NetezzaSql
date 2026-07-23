using System.Text;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Formatter;

/// <summary>
/// Converts AST nodes back to SQL string. Used for pretty-printing and formatting.
/// </summary>
public sealed class NzSqlFormatter
{
    private const int IndentSize = 2;

    private readonly StringBuilder _sb = new();
    private int _indent;

    public static string Format(SelectStatement stmt) => new NzSqlFormatter().FormatSelect(stmt);

    public static string Format(Statement stmt) => new NzSqlFormatter().FormatStatement(stmt);

    public string FormatStatement(Statement stmt)
    {
        _sb.Clear();
        _indent = 0;
        FormatStatementCore(stmt);
        return _sb.ToString();
    }

    public string FormatSelect(SelectStatement stmt)
    {
        _sb.Clear();
        _indent = 0;
        FormatSelectCore(stmt);
        return _sb.ToString();
    }

    private void FormatStatementCore(Statement stmt)
    {
        switch (stmt)
        {
            case SelectStatement select:
                FormatSelectCore(select);
                break;
            case InsertStatement insert:
                FormatInsert(insert);
                break;
            case UpdateStatement update:
                FormatUpdate(update);
                break;
            case DeleteStatement delete:
                FormatDelete(delete);
                break;
            case MergeStatement merge:
                FormatMerge(merge);
                break;
            case CreateTableStatement createTable:
                FormatCreateTable(createTable);
                break;
            case CreateViewStatement createView:
                FormatCreateView(createView);
                break;
            case CreateProcedureStatement createProcedure:
                FormatCreateProcedure(createProcedure);
                break;
            case CreateExternalTableStatement createExternalTable:
                FormatCreateExternalTable(createExternalTable);
                break;
            case CreateSequenceStatement createSequence:
                FormatCreateSequence(createSequence);
                break;
            case DropStatement drop:
                FormatDrop(drop);
                break;
            case AlterTableStatement alterTable:
                FormatAlterTable(alterTable);
                break;
            case TruncateStatement truncate:
                FormatTruncate(truncate);
                break;
            case GroomStatement groom:
                FormatGroom(groom);
                break;
            case GenerateStatisticsStatement generateStatistics:
                FormatGenerateStatistics(generateStatistics);
                break;
            case CommentStatement comment:
                FormatComment(comment);
                break;
            case GrantStatement:
                Write("GRANT");
                break;
            case RevokeStatement:
                Write("REVOKE");
                break;
            case CallStatement call:
                FormatCall(call);
                break;
            case CommitStatement:
                Write("COMMIT");
                break;
            case RollbackStatement:
                Write("ROLLBACK");
                break;
            case BeginStatement:
                Write("BEGIN");
                break;
            case SetStatement:
                Write("SET");
                break;
            case VariableSetStatement variableSet:
                FormatVariableSet(variableSet);
                break;
            default:
                Write("?");
                break;
        }
    }

    private void FormatSelectCore(SelectStatement stmt)
    {
        if (stmt.With is not null)
        {
            FormatWithClause(stmt.With);
            NewLine();
        }

        Write("SELECT");
        if (stmt.Modifier is not null)
        {
            if (stmt.Modifier.Distinct)
                Write(" DISTINCT");
            else if (stmt.Modifier.All)
                Write(" ALL");
        }
        Write(" ");
        FormatSelectList(stmt.SelectList);

        if (stmt.From is { Count: > 0 })
        {
            NewLine();
            Write("FROM ");
            FormatFrom(stmt.From);
        }

        if (stmt.Where is not null)
        {
            NewLine();
            Write("WHERE ");
            FormatExpression(stmt.Where);
        }

        if (stmt.GroupBy is { Count: > 0 })
        {
            NewLine();
            Write("GROUP BY ");
            for (int i = 0; i < stmt.GroupBy.Count; i++)
            {
                if (i > 0)
                    Write(", ");
                FormatExpression(stmt.GroupBy[i]);
            }
        }

        if (stmt.Having is not null)
        {
            NewLine();
            Write("HAVING ");
            FormatExpression(stmt.Having);
        }

        if (stmt.OrderBy is { Count: > 0 })
        {
            NewLine();
            Write("ORDER BY ");
            for (int i = 0; i < stmt.OrderBy.Count; i++)
            {
                if (i > 0)
                    Write(", ");

                FormatExpression(stmt.OrderBy[i].Expression);
                if (stmt.OrderBy[i].Descending)
                    Write(" DESC");
                if (stmt.OrderBy[i].NullsFirst)
                    Write(" NULLS FIRST");
            }
        }

        if (stmt.Limit is not null)
        {
            NewLine();
            Write("LIMIT ");
            Write(stmt.Limit.Limit.ToString());
            if (stmt.Limit.Offset is not null)
            {
                Write(" OFFSET ");
                Write(stmt.Limit.Offset.Value.ToString());
            }
        }

        if (stmt.SetOperations is { Count: > 0 })
        {
            for (int i = 0; i < stmt.SetOperations.Count; i++)
            {
                var op = stmt.SetOperations[i];
                NewLine();
                Write(op.Type switch
                {
                    SetOperationType.Union => "UNION",
                    SetOperationType.Intersect => "INTERSECT",
                    SetOperationType.Except => "EXCEPT",
                    _ => "UNION"
                });
                if (op.All)
                    Write(" ALL");

                if (stmt.CompoundSelects is not null && stmt.CompoundSelects.Count > i)
                {
                    Write(" ");
                    FormatSelectCore(stmt.CompoundSelects[i]);
                }
            }
        }
    }

    private void FormatWithClause(WithClause withClause)
    {
        Write("WITH ");
        if (withClause.Recursive)
            Write("RECURSIVE ");

        for (int i = 0; i < withClause.Ctes.Count; i++)
        {
            if (i > 0)
                Write(", ");

            var cte = withClause.Ctes[i];
            Write(cte.Name);
            if (cte.Columns is { Count: > 0 })
            {
                Write(" (");
                for (int j = 0; j < cte.Columns.Count; j++)
                {
                    if (j > 0)
                        Write(", ");
                    Write(cte.Columns[j]);
                }
                Write(")");
            }

            Write(" AS (");
            FormatSelectCore(cte.Query);
            Write(")");
        }
    }

    private void FormatSelectList(IReadOnlyList<SelectItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
                Write(", ");

            FormatExpression(items[i].Expression);
            if (items[i].Alias is not null)
            {
                Write(" AS ");
                Write(items[i].Alias!);
            }
        }
    }

    private void FormatFrom(IReadOnlyList<TableReference> refs)
    {
        for (int i = 0; i < refs.Count; i++)
        {
            if (i > 0)
                Write(", ");

            FormatTableSource(refs[i].Source);
            var joins = refs[i].Joins;
            if (joins is not null)
            {
                foreach (var join in joins)
                    FormatJoin(join);
            }
        }
    }

    private void FormatTableSource(TableSource src)
    {
        if (src.FunctionSource)
        {
            Write("TABLE WITH FINAL");
            if (src.Alias is not null)
            {
                Write(" AS ");
                Write(src.Alias);
            }
            return;
        }

        if (src.Subquery is not null)
        {
            Write("(");
            FormatSelectCore(src.Subquery);
            Write(")");
        }
        else if (src.Table is not null)
        {
            Write(FormatTableName(src.Table));
        }

        if (src.Alias is not null)
        {
            Write(" AS ");
            Write(src.Alias);
        }
    }

    private void FormatJoin(JoinClause join)
    {
        var joinType = join.Type switch
        {
            JoinType.Inner => "INNER JOIN",
            JoinType.Left => "LEFT JOIN",
            JoinType.Right => "RIGHT JOIN",
            JoinType.Full => "FULL JOIN",
            JoinType.Cross => "CROSS JOIN",
            _ => "JOIN"
        };

        NewLine();
        Write(joinType);
        Write(" ");
        FormatTableSource(join.Source);

        if (join.UsingColumns is { Count: > 0 })
        {
            Write(" USING (");
            for (int i = 0; i < join.UsingColumns.Count; i++)
            {
                if (i > 0)
                    Write(", ");
                Write(join.UsingColumns[i]);
            }
            Write(")");
        }
        else if (join.OnCondition is not null)
        {
            Write(" ON ");
            FormatExpression(join.OnCondition);
        }
    }

    private void FormatInsert(InsertStatement stmt)
    {
        Write("INSERT INTO ");
        Write(FormatTableName(stmt.Target));

        if (stmt.Columns is { Count: > 0 })
        {
            Write(" (");
            for (int i = 0; i < stmt.Columns.Count; i++)
            {
                if (i > 0)
                    Write(", ");
                Write(stmt.Columns[i]);
            }
            Write(")");
        }

        if (stmt.Values is { Count: > 0 })
        {
            Write(" VALUES");
            for (int i = 0; i < stmt.Values.Count; i++)
            {
                if (i > 0)
                    Write(", ");
                Write(" (");
                for (int j = 0; j < stmt.Values[i].Count; j++)
                {
                    if (j > 0)
                        Write(", ");
                    FormatExpression(stmt.Values[i][j]);
                }
                Write(")");
            }
        }
        else if (stmt.SourceQuery is not null)
        {
            Write(" ");
            FormatSelectCore(stmt.SourceQuery);
        }
    }

    private void FormatUpdate(UpdateStatement stmt)
    {
        Write("UPDATE ");
        Write(FormatTableName(stmt.Target));
        if (stmt.Alias is not null)
        {
            Write(" ");
            Write(stmt.Alias);
        }

        Write(" SET ");
        for (int i = 0; i < stmt.SetItems.Count; i++)
        {
            if (i > 0)
                Write(", ");
            FormatUpdateSetItem(stmt.SetItems[i]);
        }

        if (stmt.From is { Count: > 0 })
        {
            NewLine();
            Write("FROM ");
            FormatFrom(stmt.From);
        }

        if (stmt.Where is not null)
        {
            NewLine();
            Write("WHERE ");
            FormatExpression(stmt.Where);
        }
    }

    private void FormatUpdateSetItem(UpdateSetItem item)
    {
        if (item.Column.Qualifier is not null)
        {
            Write(item.Column.Qualifier);
            Write(".");
        }

        Write(item.Column.Name);
        Write(" = ");
        FormatExpression(item.Value);
    }

    private void FormatDelete(DeleteStatement stmt)
    {
        Write("DELETE FROM ");
        Write(FormatTableName(stmt.Target));
        if (stmt.Alias is not null)
        {
            Write(" ");
            Write(stmt.Alias);
        }

        if (stmt.Where is not null)
        {
            NewLine();
            Write("WHERE ");
            FormatExpression(stmt.Where);
        }
    }

    private void FormatMerge(MergeStatement stmt)
    {
        Write("MERGE INTO ");
        Write(FormatTableName(stmt.Target));
        if (stmt.TargetAlias is not null)
        {
            Write(" ");
            Write(stmt.TargetAlias);
        }

        NewLine();
        Write("USING ");
        FormatTableSource(stmt.Source);
        NewLine();
        Write("ON ");
        FormatExpression(stmt.OnCondition);

        foreach (var clause in stmt.Clauses)
        {
            NewLine();
            FormatMergeClause(clause);
        }
    }

    private void FormatMergeClause(MergeClause clause)
    {
        switch (clause)
        {
            case MergeMatchedUpdateClause u:
                Write("WHEN MATCHED");
                if (u.Condition is not null)
                {
                    Write(" AND ");
                    FormatExpression(u.Condition);
                }
                Write(" THEN UPDATE SET ");
                for (int i = 0; i < u.SetItems.Count; i++)
                {
                    if (i > 0) Write(", ");
                    FormatUpdateSetItem(u.SetItems[i]);
                }
                if (u.Where is not null)
                {
                    Write(" WHERE ");
                    FormatExpression(u.Where);
                }
                break;

            case MergeMatchedDeleteClause d:
                Write("WHEN MATCHED");
                if (d.Condition is not null)
                {
                    Write(" AND ");
                    FormatExpression(d.Condition);
                }
                Write(" THEN DELETE");
                break;

            case MergeNotMatchedInsertClause i:
                Write("WHEN NOT MATCHED");
                if (i.Condition is not null)
                {
                    Write(" AND ");
                    FormatExpression(i.Condition);
                }
                Write(" THEN INSERT");
                if (i.Columns is { Count: > 0 })
                {
                    Write(" (");
                    for (int j = 0; j < i.Columns.Count; j++)
                    {
                        if (j > 0) Write(", ");
                        Write(i.Columns[j]);
                    }
                    Write(")");
                }
                Write(" VALUES (");
                for (int j = 0; j < i.Values.Count; j++)
                {
                    if (j > 0) Write(", ");
                    FormatExpression(i.Values[j]);
                }
                Write(")");
                break;
        }
    }

    private void FormatCreateTable(CreateTableStatement stmt)
    {
        Write("CREATE ");
        if (stmt.Global)
            Write("GLOBAL ");
        if (stmt.Temporary)
            Write("TEMPORARY ");
        Write("TABLE ");
        if (stmt.IfNotExists)
            Write("IF NOT EXISTS ");
        Write(FormatTableName(stmt.Table));

        if (stmt.Columns is { Count: > 0 } || stmt.Constraints is { Count: > 0 })
        {
            Write(" (");
            NewLine();
            _indent++;

            var entries = new List<Action>();
            if (stmt.Columns is { Count: > 0 })
            {
                foreach (var column in stmt.Columns)
                    entries.Add(() => FormatColumnDefinition(column));
            }

            if (stmt.Constraints is { Count: > 0 })
            {
                foreach (var constraint in stmt.Constraints)
                    entries.Add(() => FormatTableConstraint(constraint));
            }

            for (int i = 0; i < entries.Count; i++)
            {
                entries[i]();
                if (i < entries.Count - 1)
                    Write(",");
                NewLine();
            }

            _indent--;
            Write(")");
        }

        if (stmt.AsSelect is not null)
        {
            Write(" AS ");
            Write("(");
            FormatSelectCore(stmt.AsSelect);
            Write(")");
        }

        if (stmt.Distribute is not null)
        {
            NewLine();
            Write("DISTRIBUTE ON ");
            if (stmt.Distribute.Random)
            {
                Write("RANDOM");
            }
            else if (stmt.Distribute.Columns is { Count: > 0 })
            {
                Write("(");
                for (int i = 0; i < stmt.Distribute.Columns.Count; i++)
                {
                    if (i > 0)
                        Write(", ");
                    Write(stmt.Distribute.Columns[i]);
                }
                Write(")");
            }
        }

        if (stmt.Organize is not null)
        {
            NewLine();
            Write("ORGANIZE ON ");
            if (stmt.Organize.Columns.Count == 0)
            {
                Write("NONE");
            }
            else
            {
                Write("(");
                for (int i = 0; i < stmt.Organize.Columns.Count; i++)
                {
                    if (i > 0)
                        Write(", ");
                    Write(stmt.Organize.Columns[i]);
                }
                Write(")");
            }
        }
    }

    private void FormatCreateView(CreateViewStatement stmt)
    {
        Write("CREATE ");
        if (stmt.OrReplace)
            Write("OR REPLACE ");
        Write("VIEW ");
        Write(FormatTableName(stmt.View));

        if (stmt.ColumnAliases is { Count: > 0 })
        {
            Write(" (");
            for (int i = 0; i < stmt.ColumnAliases.Count; i++)
            {
                if (i > 0)
                    Write(", ");
                Write(stmt.ColumnAliases[i]);
            }
            Write(")");
        }

        Write(" AS ");
        Write("(");
        FormatSelectCore(stmt.Query);
        Write(")");
    }

    private void FormatCreateProcedure(CreateProcedureStatement stmt)
    {
        Write("CREATE ");
        if (stmt.OrReplace)
            Write("OR REPLACE ");
        Write("PROCEDURE ");
        Write(FormatTableName(stmt.Procedure));
        Write(" ()");

        if (!string.IsNullOrWhiteSpace(stmt.Returns))
        {
            Write(" RETURNS ");
            Write(stmt.Returns!);
        }

        if (stmt.ExecuteAs is not null)
        {
            Write(" EXECUTE AS ");
            Write(stmt.ExecuteAs.Value == ExecuteAs.Owner ? "OWNER" : "CALLER");
        }

        Write(" LANGUAGE ");
        Write(stmt.Language);
        Write(" AS");
        NewLine();
        FormatProcedureBody(stmt.Body);
    }

    private void FormatCreateExternalTable(CreateExternalTableStatement stmt)
    {
        Write("CREATE EXTERNAL TABLE ");
        Write(FormatTableName(stmt.Table));

        if (stmt.Columns is { Count: > 0 })
        {
            Write(" (");
            for (int i = 0; i < stmt.Columns.Count; i++)
            {
                if (i > 0)
                    Write(", ");
                FormatColumnDefinition(stmt.Columns[i]);
            }
            Write(")");
        }

        if (stmt.SameAs is not null)
        {
            Write(" SAME AS ");
            Write(FormatTableName(stmt.SameAs));
        }

        if (stmt.Options is { Count: > 0 })
        {
            Write(" USING (");
            for (int i = 0; i < stmt.Options.Count; i++)
            {
                if (i > 0)
                    Write(", ");
                FormatExternalTableOption(stmt.Options[i]);
            }
            Write(")");
        }
    }

    private void FormatCreateSequence(CreateSequenceStatement stmt)
    {
        Write("CREATE SEQUENCE ");
        Write(FormatTableName(stmt.Sequence));
    }

    private void FormatDrop(DropStatement stmt)
    {
        Write("DROP ");
        Write(stmt.ObjectType);
        if (stmt.IfExists)
            Write(" IF EXISTS");
        if (stmt.Targets.Count > 0)
        {
            Write(" ");
            for (int i = 0; i < stmt.Targets.Count; i++)
            {
                if (i > 0)
                    Write(", ");
                Write(FormatTableName(stmt.Targets[i]));
            }
        }
    }

    private void FormatAlterTable(AlterTableStatement stmt)
    {
        Write("ALTER TABLE");
        if (!string.IsNullOrWhiteSpace(stmt.Table.Name))
        {
            Write(" ");
            Write(FormatTableName(stmt.Table));
        }
        if (stmt.Actions is not null)
            foreach (var action in stmt.Actions)
            {
                if (string.IsNullOrWhiteSpace(action.RawSql)) continue;
                Write(" ");
                Write(action.RawSql);
            }
    }

    private void FormatTruncate(TruncateStatement stmt)
    {
        Write("TRUNCATE TABLE ");
        Write(FormatTableName(stmt.Table));
    }

    private void FormatGroom(GroomStatement stmt)
    {
        Write("GROOM TABLE ");
        Write(FormatTableName(stmt.Table));

        if (stmt.Mode is not null)
        {
            Write(" ");
            Write(stmt.Mode.Kind switch
            {
                GroomModeKind.Versions => "VERSIONS",
                GroomModeKind.Records => "RECORDS",
                GroomModeKind.Pages => "PAGES",
                GroomModeKind.All => "ALL",
                _ => "ALL"
            });
        }

        if (stmt.Reclaim is not null)
        {
            Write(" RECLAIM BACKUPSET");
            if (stmt.Reclaim.None)
                Write(" NONE");
        }
    }

    private void FormatGenerateStatistics(GenerateStatisticsStatement stmt)
    {
        Write("GENERATE ");
        if (stmt.Express)
            Write("EXPRESS ");
        Write("STATISTICS");

        if (!string.IsNullOrWhiteSpace(stmt.Table.Name))
        {
            Write(" ON ");
            Write(FormatTableName(stmt.Table));
            if (stmt.Columns is { Count: > 0 })
            {
                Write(" (");
                for (int i = 0; i < stmt.Columns.Count; i++)
                {
                    if (i > 0)
                        Write(", ");
                    Write(stmt.Columns[i]);
                }
                Write(")");
            }
        }
    }

    private void FormatComment(CommentStatement stmt)
    {
        Write("COMMENT ON ");
        Write(stmt.ObjectType);
        if (stmt.ObjectType.Equals("COLUMN", StringComparison.OrdinalIgnoreCase))
        {
            Write(" ");
            Write(FormatTableName(stmt.Object));
            if (!string.IsNullOrWhiteSpace(stmt.Column))
            {
                Write(".");
                Write(stmt.Column!);
            }
        }
        else
        {
            Write(" ");
            Write(FormatTableName(stmt.Object));
        }

        Write(" IS '");
        Write(StripQuotes(stmt.Comment).Replace("'", "''", StringComparison.Ordinal));
        Write("'");
    }

    private void FormatCall(CallStatement stmt)
    {
        Write("CALL ");
        Write(FormatTableName(stmt.Procedure));
    }

    private void FormatVariableSet(VariableSetStatement stmt)
    {
        Write("SET ");
        FormatExpression(stmt.Value);
    }

    private void FormatProcedureBody(ProcedureBody body)
    {
        Write("BEGIN_PROC");
        NewLine();
        _indent++;

        if (body.Declarations is { Count: > 0 })
        {
            Write("DECLARE");
            NewLine();
            _indent++;
            for (int i = 0; i < body.Declarations.Count; i++)
            {
                FormatVariableDeclaration(body.Declarations[i]);
                Write(";");
                NewLine();
            }
            _indent--;
        }

        Write("BEGIN");
        NewLine();
        _indent++;
        FormatProcedureStatements(body.Statements);

        if (body.ExceptionHandlers is { Count: > 0 })
        {
            Write("EXCEPTION");
            NewLine();
            _indent++;
            for (int i = 0; i < body.ExceptionHandlers.Count; i++)
            {
                FormatExceptionHandler(body.ExceptionHandlers[i]);
            }
            _indent--;
        }

        _indent--;
        Write("END");
        _indent--;
        NewLine();
        Write("END_PROC");
    }

    private void FormatProcedureStatements(IReadOnlyList<ProcedureStatement> statements)
    {
        for (int i = 0; i < statements.Count; i++)
        {
            FormatProcedureStatement(statements[i]);
            Write(";");
            if (i < statements.Count - 1)
                NewLine();
        }
        if (statements.Count > 0)
            NewLine();
    }

    private void FormatExceptionHandler(ExceptionHandler handler)
    {
        Write("WHEN ");
        Write(handler.Condition ?? "OTHERS");
        Write(" THEN");
        NewLine();
        _indent++;
        FormatProcedureStatements(handler.Statements);
        _indent--;
    }

    private void FormatProcedureStatement(ProcedureStatement stmt)
    {
        switch (stmt)
        {
            case AssignmentStatement assignment:
                Write(assignment.Variable);
                Write(" := ");
                FormatExpression(assignment.Value);
                break;
            case ProcedureReturnStatement procedureReturn:
                Write("RETURN");
                if (procedureReturn.Value is not null)
                {
                    Write(" ");
                    FormatExpression(procedureReturn.Value);
                }
                break;
            case ProcedureIfStatement procedureIf:
                FormatProcedureIf(procedureIf);
                break;
            case ProcedureLoopStatement procedureLoop:
                Write("LOOP");
                NewLine();
                _indent++;
                FormatProcedureStatements(procedureLoop.Statements);
                _indent--;
                Write("END LOOP");
                break;
            case ProcedureWhileStatement procedureWhile:
                Write("WHILE ");
                FormatExpression(procedureWhile.Condition);
                Write(" LOOP");
                NewLine();
                _indent++;
                FormatProcedureStatements(procedureWhile.Statements);
                _indent--;
                Write("END LOOP");
                break;
            case ProcedureForStatement procedureFor:
                FormatProcedureFor(procedureFor);
                break;
            case ProcedureExitStatement procedureExit:
                Write("EXIT");
                if (procedureExit.When is not null)
                {
                    Write(" WHEN ");
                    FormatExpression(procedureExit.When);
                }
                break;
            case ProcedureRaiseStatement procedureRaise:
                Write("RAISE ");
                Write(procedureRaise.Level switch
                {
                    RaiseLevel.Exception => "EXCEPTION",
                    RaiseLevel.Notice => "NOTICE",
                    RaiseLevel.Debug => "DEBUG",
                    RaiseLevel.Error => "ERROR",
                    _ => "NOTICE"
                });
                Write(" ");
                FormatExpression(procedureRaise.Message);
                break;
            case ProcedureRollbackStatement:
                Write("ROLLBACK");
                break;
            case ProcedureCommitStatement:
                Write("COMMIT");
                break;
            case ProcedureCallStatement procedureCall:
                Write("CALL ");
                Write(FormatTableName(procedureCall.Procedure));
                if (procedureCall.Arguments is { Count: > 0 })
                {
                    Write(" (");
                    for (int i = 0; i < procedureCall.Arguments.Count; i++)
                    {
                        if (i > 0)
                            Write(", ");
                        FormatExpression(procedureCall.Arguments[i]);
                    }
                    Write(")");
                }
                break;
            case ProcedureExecuteImmediateStatement executeImmediate:
                Write("EXECUTE IMMEDIATE ");
                FormatExpression(executeImmediate.Sql);
                if (executeImmediate.Using is { Count: > 0 })
                {
                    Write(" USING ");
                    for (int i = 0; i < executeImmediate.Using.Count; i++)
                    {
                        if (i > 0)
                            Write(", ");
                        Write(executeImmediate.Using[i]);
                    }
                }
                break;
            case ProcedureSqlStatement procedureSql:
                FormatStatementCore(procedureSql.Sql);
                break;
            case ProcedureBlockStatement procedureBlock:
                FormatProcedureBlock(procedureBlock);
                break;
            default:
                Write("?");
                break;
        }
    }

    private void FormatProcedureIf(ProcedureIfStatement stmt)
    {
        Write("IF ");
        FormatExpression(stmt.Condition);
        Write(" THEN");
        NewLine();
        _indent++;
        FormatProcedureStatements(stmt.ThenStatements);
        _indent--;

        if (stmt.ElsifClauses is { Count: > 0 })
        {
            foreach (var elsif in stmt.ElsifClauses)
            {
                Write("ELSIF ");
                FormatExpression(elsif.Condition);
                Write(" THEN");
                NewLine();
                _indent++;
                FormatProcedureStatements(elsif.Statements);
                _indent--;
            }
        }

        if (stmt.ElseStatements is { Count: > 0 })
        {
            Write("ELSE");
            NewLine();
            _indent++;
            FormatProcedureStatements(stmt.ElseStatements);
            _indent--;
        }

        Write("END IF");
    }

    private void FormatProcedureFor(ProcedureForStatement stmt)
    {
        Write("FOR ");
        Write(stmt.Variable);
        Write(" IN ");

        if (stmt.ForQuery is not null)
        {
            FormatSelectCore(stmt.ForQuery);
            Write(" LOOP");
        }
        else if (stmt.ExecuteSql is not null)
        {
            Write("EXECUTE ");
            FormatExpression(stmt.ExecuteSql);
            Write(" LOOP");
        }
        else
        {
            if (stmt.From is not null)
                FormatExpression(stmt.From);
            Write("..");
            if (stmt.To is not null)
                FormatExpression(stmt.To);
            Write(" LOOP");
        }

        NewLine();
        _indent++;
        FormatProcedureStatements(stmt.Statements);
        _indent--;
        Write("END LOOP");
    }

    private void FormatProcedureBlock(ProcedureBlockStatement stmt)
    {
        Write("BEGIN");
        NewLine();
        _indent++;

        if (stmt.Body.Declarations is { Count: > 0 })
        {
            Write("DECLARE");
            NewLine();
            _indent++;
            for (int i = 0; i < stmt.Body.Declarations.Count; i++)
            {
                FormatVariableDeclaration(stmt.Body.Declarations[i]);
                Write(";");
                NewLine();
            }
            _indent--;
        }

        FormatProcedureStatements(stmt.Body.Statements);
        if (stmt.Body.ExceptionHandlers is { Count: > 0 })
        {
            Write("EXCEPTION");
            NewLine();
            _indent++;
            for (int i = 0; i < stmt.Body.ExceptionHandlers.Count; i++)
                FormatExceptionHandler(stmt.Body.ExceptionHandlers[i]);
            _indent--;
        }

        _indent--;
        Write("END");
    }

    private void FormatVariableDeclaration(VariableDeclaration decl)
    {
        Write(decl.Name);
        Write(" ");
        Write(decl.Type.Name);
        if (decl.Type.Parameters is { Count: > 0 })
        {
            Write("(");
            for (int i = 0; i < decl.Type.Parameters.Count; i++)
            {
                if (i > 0)
                    Write(", ");
                Write(decl.Type.Parameters[i]);
            }
            Write(")");
        }

        if (decl.Alias)
        {
            Write(" ALIAS FOR ");
            Write(decl.AliasFor ?? "");
        }
        else if (decl.Constant)
        {
            Write(" CONSTANT");
        }
    }

    private void FormatExpression(Expression expr)
    {
        switch (expr)
        {
            case Literal literal:
                FormatLiteral(literal);
                break;
            case TypeLiteral typeLiteral:
                Write(typeLiteral.TypeName);
                if (!string.IsNullOrWhiteSpace(typeLiteral.Value))
                {
                    Write(" ");
                    Write("'");
                    Write(typeLiteral.Value.Replace("'", "''", StringComparison.Ordinal));
                    Write("'");
                }
                break;
            case ColumnReference columnReference:
                if (columnReference.Qualifier is not null)
                {
                    Write(columnReference.Qualifier);
                    Write(".");
                }
                Write(columnReference.Name);
                break;
            case StarExpression star:
                if (star.Qualifier is not null)
                {
                    Write(star.Qualifier);
                    Write(".");
                }
                Write("*");
                break;
            case FunctionCall functionCall:
                if (functionCall.Schema is not null)
                {
                    Write(functionCall.Schema);
                    Write(".");
                }
                Write(functionCall.Name);
                Write("(");
                if (functionCall.Distinct)
                    Write("DISTINCT ");
                if (functionCall.StarArgument)
                {
                    Write("*");
                }
                else if (functionCall.Arguments is { Count: > 0 })
                {
                    for (int i = 0; i < functionCall.Arguments.Count; i++)
                    {
                        if (i > 0)
                            Write(", ");
                        FormatExpression(functionCall.Arguments[i]);
                    }
                }
                Write(")");
                if (functionCall.Filter is not null)
                {
                    Write(" FILTER (WHERE ");
                    FormatExpression(functionCall.Filter.Condition);
                    Write(")");
                }
                if (functionCall.Over is not null)
                {
                    Write(" OVER (");
                    FormatOverClause(functionCall.Over);
                    Write(")");
                }
                break;
            case BinaryExpression binary:
                FormatExpression(binary.Left);
                Write(" ");
                Write(OpString(binary.Operator));
                Write(" ");
                FormatExpression(binary.Right);
                break;
            case UnaryExpression unary:
                Write(UnaryOpString(unary.Operator));
                Write(" ");
                FormatExpression(unary.Operand);
                break;
            case CastExpression castExpression:
                Write("CAST(");
                FormatExpression(castExpression.Expression);
                Write(" AS ");
                FormatDataType(castExpression.TargetType);
                Write(")");
                break;
            case CastFunctionExpression castFunctionExpression:
                FormatExpression(castFunctionExpression.Expression);
                Write("::");
                FormatDataType(castFunctionExpression.TargetType);
                break;
            case SequenceValueExpression sequenceValue:
                Write(sequenceValue.NextVal ? "NEXT VALUE FOR " : "CURRENT VALUE FOR ");
                Write(FormatTableName(sequenceValue.Sequence));
                break;
            case ParameterExpression:
                Write("?");
                break;
            case CaseExpression caseExpression:
                Write("CASE");
                if (caseExpression.Value is not null)
                {
                    Write(" ");
                    FormatExpression(caseExpression.Value);
                }
                foreach (var whenThen in caseExpression.WhenClauses)
                {
                    Write(" WHEN ");
                    FormatExpression(whenThen.When);
                    Write(" THEN ");
                    FormatExpression(whenThen.Then);
                }
                if (caseExpression.ElseClause is not null)
                {
                    Write(" ELSE ");
                    FormatExpression(caseExpression.ElseClause);
                }
                Write(" END");
                break;
            case InExpression inExpression:
                FormatExpression(inExpression.Left);
                Write(inExpression.Not ? " NOT IN (" : " IN (");
                if (inExpression.Values is { Count: > 0 })
                {
                    for (int i = 0; i < inExpression.Values.Count; i++)
                    {
                        if (i > 0)
                            Write(", ");
                        FormatExpression(inExpression.Values[i]);
                    }
                }
                else if (inExpression.Subquery is not null)
                {
                    FormatSelectCore(inExpression.Subquery);
                }
                Write(")");
                break;
            case BetweenExpression betweenExpression:
                FormatExpression(betweenExpression.Value);
                Write(betweenExpression.Not ? " NOT BETWEEN " : " BETWEEN ");
                FormatExpression(betweenExpression.Low);
                Write(" AND ");
                FormatExpression(betweenExpression.High);
                break;
            case IsExpression isExpression:
                FormatExpression(isExpression.Left);
                Write(isExpression.Not ? " IS NOT " : " IS ");
                if (isExpression.Null)
                    Write("NULL");
                else if (isExpression.Boolean)
                    Write("BOOLEAN");
                else if (isExpression.Unknown)
                    Write("UNKNOWN");
                break;
            case ExistsExpression existsExpression:
                Write("EXISTS (");
                FormatSelectCore(existsExpression.Subquery);
                Write(")");
                break;
            case QuantifiedComparisonExpression quantifiedComparisonExpression:
                FormatExpression(quantifiedComparisonExpression.Left);
                Write(" ");
                Write(OpString(quantifiedComparisonExpression.Operator));
                Write(" ");
                Write(quantifiedComparisonExpression.Quantifier switch
                {
                    QuantifierKind.Any => "ANY",
                    QuantifierKind.Some => "SOME",
                    QuantifierKind.All => "ALL",
                    _ => "ALL"
                });
                Write(" (");
                FormatExpression(quantifiedComparisonExpression.Right);
                Write(")");
                break;
            case SubqueryExpression subqueryExpression:
                Write("(");
                FormatSelectCore(subqueryExpression.Query);
                Write(")");
                break;
            case ExtractExpression extractExpression:
                Write("EXTRACT(");
                Write(extractExpression.Field);
                Write(" FROM ");
                FormatExpression(extractExpression.Source);
                Write(")");
                break;
            default:
                Write("?");
                break;
        }
    }

    private void FormatOverClause(OverClause overClause)
    {
        var parts = new List<string>();

        if (overClause.PartitionBy is { Count: > 0 })
        {
            parts.Add("PARTITION BY " + JoinExpressions(overClause.PartitionBy));
        }

        if (overClause.OrderBy is { Count: > 0 })
        {
            var orderParts = new List<string>();
            foreach (var item in overClause.OrderBy)
            {
                var text = FormatExpressionToString(item.Expression);
                if (item.Descending)
                    text += " DESC";
                else
                    text += " ASC";
                if (item.NullsFirst)
                    text += " NULLS FIRST";
                orderParts.Add(text);
            }
            parts.Add("ORDER BY " + string.Join(", ", orderParts));
        }

        if (overClause.Frame is not null)
            parts.Add(FormatWindowFrameToString(overClause.Frame));

        Write(string.Join(" ", parts));
    }

    private string FormatWindowFrameToString(WindowFrame frame)
    {
        var builder = new StringBuilder();
        builder.Append(frame.Unit switch
        {
            WindowFrameUnit.Rows => "ROWS",
            WindowFrameUnit.Range => "RANGE",
            WindowFrameUnit.Groups => "GROUPS",
            _ => "ROWS"
        });
        builder.Append(' ');
        if (frame.Start is not null && frame.End is not null)
        {
            builder.Append("BETWEEN ");
            builder.Append(FormatFrameBoundToString(frame.Start));
            builder.Append(" AND ");
            builder.Append(FormatFrameBoundToString(frame.End));
        }
        else if (frame.Start is not null)
        {
            builder.Append(FormatFrameBoundToString(frame.Start));
        }

        if (frame.Exclude is not null)
        {
            builder.Append(" EXCLUDE ");
            builder.Append(frame.Exclude.Kind switch
            {
                ExcludeKind.CurrentRow => "CURRENT ROW",
                ExcludeKind.Group => "GROUP",
                ExcludeKind.Ties => "TIES",
                ExcludeKind.NoOthers => "NO OTHERS",
                _ => "NO OTHERS"
            });
        }

        return builder.ToString();
    }

    private static string FormatFrameBoundToString(FrameBound bound)
    {
        return bound.Kind switch
        {
            FrameBoundKind.CurrentRow => "CURRENT ROW",
            FrameBoundKind.UnboundedPreceding => "UNBOUNDED PRECEDING",
            FrameBoundKind.UnboundedFollowing => "UNBOUNDED FOLLOWING",
            FrameBoundKind.Preceding => bound.Value is not null ? $"{bound.Value} PRECEDING" : "PRECEDING",
            FrameBoundKind.Following => bound.Value is not null ? $"{bound.Value} FOLLOWING" : "FOLLOWING",
            _ => "CURRENT ROW"
        };
    }

    private string JoinExpressions(IReadOnlyList<Expression> expressions)
    {
        var parts = new string[expressions.Count];
        for (int i = 0; i < expressions.Count; i++)
            parts[i] = FormatExpressionToString(expressions[i]);
        return string.Join(", ", parts);
    }

    private string FormatExpressionToString(Expression expression)
    {
        var formatter = new NzSqlFormatter();
        formatter.FormatExpression(expression);
        return formatter._sb.ToString();
    }

    private void FormatLiteral(Literal literal)
    {
        switch (literal.Kind)
        {
            case LiteralKind.String:
                var unescaped = StripQuotes(literal.Value);
                Write("'");
                Write(unescaped.Replace("'", "''", StringComparison.Ordinal));
                Write("'");
                break;
            case LiteralKind.BooleanTrue:
                Write("TRUE");
                break;
            case LiteralKind.BooleanFalse:
                Write("FALSE");
                break;
            case LiteralKind.Null:
                Write("NULL");
                break;
            default:
                Write(literal.Value);
                break;
        }
    }

    private static string StripQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1];
        return value;
    }

    private void FormatDataType(DataTypeInfo dataType)
    {
        Write(dataType.Name);
        if (dataType.Parameters is { Count: > 0 })
        {
            Write("(");
            for (int i = 0; i < dataType.Parameters.Count; i++)
            {
                if (i > 0)
                    Write(", ");
                Write(dataType.Parameters[i]);
            }
            Write(")");
        }
    }

    private void FormatColumnDefinition(ColumnDefinition columnDefinition)
    {
        Write(columnDefinition.Name);
        Write(" ");
        FormatDataType(columnDefinition.Type);

        var constraints = columnDefinition.Constraints ?? [];
        if (columnDefinition.NotNull || constraints.OfType<NotNullConstraint>().Any())
            Write(" NOT NULL");
        else if (constraints.OfType<NullConstraint>().Any())
            Write(" NULL");

        if (columnDefinition.DefaultValue is not null)
        {
            Write(" DEFAULT ");
            FormatExpression(columnDefinition.DefaultValue);
        }

        foreach (var constraint in constraints)
        {
            switch (constraint)
            {
                case NotNullConstraint or NullConstraint or DefaultConstraint:
                    continue;
                default:
                    Write(" ");
                    FormatColumnConstraint(constraint);
                    break;
            }
        }
    }

    private void FormatColumnConstraint(ColumnConstraint constraint)
    {
        switch (constraint)
        {
            case PrimaryKeyColumnConstraint:
                Write("PRIMARY KEY");
                break;
            case UniqueColumnConstraint:
                Write("UNIQUE");
                break;
            case ReferencesConstraint referencesConstraint:
                Write("REFERENCES ");
                Write(FormatTableName(referencesConstraint.ReferencedTable));
                if (referencesConstraint.Columns is { Count: > 0 })
                {
                    Write(" (");
                    for (int i = 0; i < referencesConstraint.Columns.Count; i++)
                    {
                        if (i > 0)
                            Write(", ");
                        Write(referencesConstraint.Columns[i]);
                    }
                    Write(")");
                }
                break;
            case NamedColumnConstraint namedColumnConstraint:
                Write("CONSTRAINT ");
                Write(namedColumnConstraint.Name);
                Write(" ");
                FormatColumnConstraint(namedColumnConstraint.Constraint);
                break;
            case DefaultConstraint defaultConstraint:
                Write("DEFAULT ");
                FormatExpression(defaultConstraint.Value);
                break;
            case NotNullConstraint:
                Write("NOT NULL");
                break;
            case NullConstraint:
                Write("NULL");
                break;
            default:
                Write("?");
                break;
        }
    }

    private void FormatTableConstraint(TableConstraint constraint)
    {
        switch (constraint)
        {
            case PrimaryKeyConstraint primaryKeyConstraint:
                if (primaryKeyConstraint.Name is not null)
                {
                    Write("CONSTRAINT ");
                    Write(primaryKeyConstraint.Name);
                    Write(" ");
                }
                Write("PRIMARY KEY");
                if (primaryKeyConstraint.Columns is { Count: > 0 })
                    Write(" (" + string.Join(", ", primaryKeyConstraint.Columns) + ")");
                break;
            case UniqueConstraint uniqueConstraint:
                if (uniqueConstraint.Name is not null)
                {
                    Write("CONSTRAINT ");
                    Write(uniqueConstraint.Name);
                    Write(" ");
                }
                Write("UNIQUE");
                if (uniqueConstraint.Columns is { Count: > 0 })
                    Write(" (" + string.Join(", ", uniqueConstraint.Columns) + ")");
                break;
            case ForeignKeyConstraint foreignKeyConstraint:
                if (foreignKeyConstraint.Name is not null)
                {
                    Write("CONSTRAINT ");
                    Write(foreignKeyConstraint.Name);
                    Write(" ");
                }
                Write("FOREIGN KEY");
                if (foreignKeyConstraint.Columns is { Count: > 0 })
                    Write(" (" + string.Join(", ", foreignKeyConstraint.Columns) + ")");
                Write(" REFERENCES ");
                Write(FormatTableName(foreignKeyConstraint.ReferencedTable));
                if (foreignKeyConstraint.ReferencedColumns is { Count: > 0 })
                    Write(" (" + string.Join(", ", foreignKeyConstraint.ReferencedColumns) + ")");
                break;
            case CheckConstraint checkConstraint:
                Write("CHECK (");
                FormatExpression(checkConstraint.Condition);
                Write(")");
                break;
            default:
                Write("?");
                break;
        }
    }

    private void FormatExternalTableOption(ExternalTableOption option)
    {
        Write(option.Name);
        if (option.Value is null)
            return;

        Write(" ");
        switch (option.Value)
        {
            case ExternalStringValue stringValue:
                Write("'");
                Write(stringValue.Value.Replace("'", "''", StringComparison.Ordinal));
                Write("'");
                break;
            case ExternalNumberValue numberValue:
                Write(numberValue.Value.ToString());
                break;
            case ExternalIdentifierValue identifierValue:
                Write(identifierValue.Value);
                break;
            default:
                Write("?");
                break;
        }
    }

    private static string FormatTableName(TableName table)
    {
        if (table.Database is not null && table.Schema is not null)
            return $"{table.Database}.{table.Schema}.{table.Name}";
        if (table.Database is not null)
            return $"{table.Database}..{table.Name}";
        if (table.Schema is not null)
            return $"{table.Schema}.{table.Name}";
        return table.Name;
    }

    private static string OpString(BinaryOperator op) => op switch
    {
        BinaryOperator.And => "AND",
        BinaryOperator.Or => "OR",
        BinaryOperator.Equals => "=",
        BinaryOperator.NotEquals => "<>",
        BinaryOperator.LessThan => "<",
        BinaryOperator.GreaterThan => ">",
        BinaryOperator.LessThanEquals => "<=",
        BinaryOperator.GreaterThanEquals => ">=",
        BinaryOperator.Like => "LIKE",
        BinaryOperator.Ilike => "ILIKE",
        BinaryOperator.NotLike => "NOT LIKE",
        BinaryOperator.NotIlike => "NOT ILIKE",
        BinaryOperator.In => "IN",
        BinaryOperator.NotIn => "NOT IN",
        BinaryOperator.Between => "BETWEEN",
        BinaryOperator.NotBetween => "NOT BETWEEN",
        BinaryOperator.Is => "IS",
        BinaryOperator.IsNot => "IS NOT",
        BinaryOperator.Plus => "+",
        BinaryOperator.Minus => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Modulo => "%",
        BinaryOperator.Caret => "^",
        BinaryOperator.Concat => "||",
        _ => "?"
    };

    private static string UnaryOpString(UnaryOperator op) => op switch
    {
        UnaryOperator.Not => "NOT",
        UnaryOperator.Minus => "-",
        UnaryOperator.Plus => "+",
        UnaryOperator.Exists => "EXISTS",
        _ => "?"
    };

    private void Write(string s) => _sb.Append(s);

    private void NewLine()
    {
        _sb.AppendLine();
        if (_indent > 0)
            _sb.Append(' ', _indent * IndentSize);
    }
}
