using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

/// <summary>
/// Walks the AST to build scopes and perform semantic validation.
/// Port of sqlVisitor.ts from the reference TypeScript project.
/// </summary>
public partial class NzSqlVisitor
{
    private readonly ScopeBuilder _scope;
    private readonly List<ValidationError> _errors = new();
    private readonly ISchemaProvider? _schema;

    // Context tracking
    private bool _inSelectList;
    private readonly HashSet<string> _selectListAliasesSoFar = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<HashSet<string>> _selectOutputAliasesStack = new();
    private bool _canReferenceSelectAliases;
    private bool _inOrderBy;
    private bool _inWhere;
    private bool _inProcedureContext;
    private ProcedureScopeBuilder? _procedureScope;

    // SQL018: Unused CTE tracking
    private readonly HashSet<string> _referencedCtes = new(StringComparer.OrdinalIgnoreCase);

    // SQL019: Unused table alias tracking
    private readonly Dictionary<string, SourcePosition> _tableAliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usedAliases = new(StringComparer.OrdinalIgnoreCase);

    // SQL021: Aggregate in WHERE clause tracking
    private bool _inStrictWhere;

    // CTE being validated (to exclude from unqualified column resolution)
    private string? _validatingCteName;

    // CTEs whose bodies have passed Pass 2 validation (columns are trustworthy)
    private readonly HashSet<string> _validatedCtes = new(StringComparer.OrdinalIgnoreCase);

    // SELECT depth counter for root-query-only checks
    private int _selectDepth;

    // Multi-statement scope: persists tables/views across statements in a script
    private Dictionary<string, TableInfo>? _multiStatementScope;

    public NzSqlVisitor(ISchemaProvider? schema = null)
    {
        _scope = new ScopeBuilder();
        _schema = schema;
        _selectOutputAliasesStack.Push(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ValidationError> Errors => _errors;
    public Scope CurrentScope => _scope.CurrentScope;

    public void Reset()
    {
        _scope.Reset();
        _errors.Clear();
        _inSelectList = false;
        _selectListAliasesSoFar.Clear();
        _selectOutputAliasesStack.Clear();
        _selectOutputAliasesStack.Push(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        _canReferenceSelectAliases = false;
        _inOrderBy = false;
        _inWhere = false;
        _referencedCtes.Clear();
        _tableAliases.Clear();
        _usedAliases.Clear();
        _inStrictWhere = false;
        _validatingCteName = null;
        _validatedCtes.Clear();
        _selectDepth = 0;
        _procedureScope = null;
    }

    public void ResetKeepMultiStatementScope()
    {
        Reset();
        _multiStatementScope ??= new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
    }

    public void ClearMultiStatementScope()
    {
        _multiStatementScope = null;
    }

    /// <summary>
    /// Seeds the relations created by earlier statements in the same SQL
    /// document. The linter validates statements independently so it can keep
    /// statement-level diagnostics cached, but DDL still has script scope.
    /// </summary>
    public void SeedMultiStatementScope(IEnumerable<TableInfo> tables)
    {
        _multiStatementScope = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
            _multiStatementScope[table.Name] = table;
    }

    /// <summary>Returns the current script-scope relations after visiting a statement.</summary>
    public IReadOnlyList<TableInfo> GetMultiStatementScopeTables() =>
        _multiStatementScope is null
            ? Array.Empty<TableInfo>()
            : _multiStatementScope.Values.ToList();

    // ====== Statement Visiting ======

    public void Visit(Statement stmt)
    {
        switch (stmt)
        {
            case SelectStatement s: Visit(s); break;
            case InsertStatement s: Visit(s); break;
            case UpdateStatement s: Visit(s); break;
            case DeleteStatement s: Visit(s); break;
            case CreateProcedureStatement s: Visit(s); break;
            case MergeStatement s: Visit(s); break;
            case CreateTableStatement s: Visit(s); break;
            case CreateExternalTableStatement s: Visit(s); break;
            case CreateViewStatement s: Visit(s); break;
            case AlterTableStatement s: Visit(s); break;
            case DropStatement s: Visit(s); break;
            case TruncateStatement s: Visit(s); break;
            case CommentStatement s: Visit(s); break;
            case GroomStatement s: Visit(s); break;
            case GenerateStatisticsStatement s: Visit(s); break;
        }
    }

    // ====== Helper Methods ======

    private void VisitProcedureStatements(IReadOnlyList<ProcedureStatement> stmts)
    {
        foreach (var stmt in stmts)
        {
            VisitProcedureStatement(stmt);
        }
    }

    public void VisitProcedureStatement(ProcedureStatement stmt)
    {
        switch (stmt)
        {
            case AssignmentStatement a:
                _procedureScope?.MarkNameUsed(a.Variable);
                _procedureScope?.MarkExpressionUsed(a.Value);
                Visit(a.Value);
                break;
            case ProcedureReturnStatement r:
                _procedureScope?.MarkReturn();
                if (r.Value is not null) _procedureScope?.MarkExpressionUsed(r.Value);
                if (r.Value is not null) Visit(r.Value);
                break;
            case ProcedureIfStatement i:
                _procedureScope?.MarkExpressionUsed(i.Condition);
                Visit(i.Condition);
                VisitProcedureStatements(i.ThenStatements);
                if (i.ElsifClauses is not null)
                {
                    foreach (var e in i.ElsifClauses)
                    {
                        _procedureScope?.MarkExpressionUsed(e.Condition);
                        Visit(e.Condition);
                        VisitProcedureStatements(e.Statements);
                    }
                }
                if (i.ElseStatements is not null)
                    VisitProcedureStatements(i.ElseStatements);
                break;
            case ProcedureLoopStatement l:
                VisitProcedureStatements(l.Statements);
                break;
            case ProcedureWhileStatement w:
                _procedureScope?.MarkExpressionUsed(w.Condition);
                Visit(w.Condition);
                VisitProcedureStatements(w.Statements);
                break;
            case ProcedureForStatement f:
                if (f.From is not null) _procedureScope?.MarkExpressionUsed(f.From);
                if (f.To is not null) _procedureScope?.MarkExpressionUsed(f.To);
                if (f.By is not null) _procedureScope?.MarkExpressionUsed(f.By);
                if (f.From is not null) Visit(f.From);
                if (f.To is not null) Visit(f.To);
                if (f.By is not null) Visit(f.By);
                if (f.ForQuery is not null) Visit(f.ForQuery);
                if (f.ExecuteSql is not null) Visit(f.ExecuteSql);
                VisitProcedureStatements(f.Statements);
                break;
            case ProcedureExitStatement x:
                if (x.When is not null) _procedureScope?.MarkExpressionUsed(x.When);
                if (x.When is not null) Visit(x.When);
                break;
            case ProcedureRaiseStatement r:
                _procedureScope?.MarkExpressionUsed(r.Message);
                Visit(r.Message);
                break;
            case ProcedureCallStatement c:
                if (c.Arguments is not null)
                {
                    foreach (var arg in c.Arguments)
                    {
                        _procedureScope?.MarkExpressionUsed(arg);
                        Visit(arg);
                    }
                }
                break;
            case ProcedureExecuteImmediateStatement ei:
                _procedureScope?.MarkExpressionUsed(ei.Sql);
                Visit(ei.Sql);
                break;
            case ProcedureSqlStatement sql:
                if (sql.Sql is SelectStatement select)
                {
                    _procedureScope?.CheckStandaloneSelect(select);
                    _procedureScope?.MarkSelectUsed(select);
                }
                Visit(sql.Sql);
                break;
            case ProcedureBlockStatement b:
                if (b.Body.Declarations is not null)
                {
                    foreach (var decl in b.Body.Declarations)
                    {
                        _procedureScope?.RegisterVariable(decl);
                        CheckTypeLength(decl.Type, decl.Position);
                    }
                }
                VisitProcedureStatements(b.Body.Statements);
                if (b.Body.ExceptionHandlers is not null)
                {
                    foreach (var h in b.Body.ExceptionHandlers)
                        VisitProcedureStatements(h.Statements);
                }
                break;
            case ProcedureCommitStatement:
            case ProcedureRollbackStatement:
                break;
        }
    }

    private void AddError(string message, string severity, string code, SourcePosition pos,
        int endLine = 0, int endColumn = 0, string? suggestedFix = null)
    {
        _errors.Add(new ValidationError(message, severity, pos, code, endLine, endColumn, suggestedFix));
    }

    private HashSet<string>? GetCurrentSelectOutputAliases()
    {
        return _selectOutputAliasesStack.Count > 0 ? _selectOutputAliasesStack.Peek() : null;
    }

    private static string? InferSelectItemName(Expression expr)
    {
        if (expr is ColumnReference cr)
            return cr.Name;
        return null;
    }

    private static List<ColumnInfo> InferColumnsFromSelect(SelectStatement stmt)
    {
        var cols = new List<ColumnInfo>();
        foreach (var item in stmt.SelectList)
        {
            var name = item.Alias ?? InferSelectItemName(item.Expression);
            if (name is not null) cols.Add(new ColumnInfo(name));
        }
        return cols;
    }

    private static string FormatName(string? db, string? schema, string name)
    {
        if (db is not null && schema is not null) return $"{db}.{schema}.{name}";
        if (db is not null) return $"{db}..{name}";
        if (schema is not null) return $"{schema}.{name}";
        return name;
    }

    private static bool IsReservedKeyword(string name)
    {
        var u = name.ToUpperInvariant();
        return u is "FROM" or "WHERE" or "JOIN" or "ON" or "SELECT" or "INSERT" or "UPDATE"
            or "DELETE" or "CREATE" or "DROP" or "ALTER" or "WITH" or "GROUP" or "ORDER"
            or "HAVING" or "LIMIT" or "UNION" or "INTERSECT" or "EXCEPT" or "AS"
            or "AND" or "OR" or "NOT" or "IN" or "BETWEEN" or "LIKE" or "IS" or "NULL"
            or "CASE" or "WHEN" or "THEN" or "ELSE" or "END" or "TABLE" or "VIEW";
    }
}
