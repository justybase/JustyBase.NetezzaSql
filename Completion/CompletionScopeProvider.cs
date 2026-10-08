using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Completion;

/// <summary>
/// Builds scope from the AST for completion.
/// Walks parsed statements with a <see cref="ScopeBuilder"/> to register
/// visible tables, CTEs, aliases, and their columns — enabling AST-powered
/// alias resolution and column suggestions without token-walking.
/// 
/// Scopes are entered but NOT exited, so <c>FindTable</c> / <c>GetAllVisibleTables</c>
/// return all tables visible at the cursor point (including parent scopes).
/// 
/// Returns null when parsing fails — caller falls back to token-based completion.
/// </summary>
public class CompletionScopeProvider
{
    private readonly ISchemaProvider? _schema;
    private readonly SqlDialect _dialect;

    public CompletionScopeProvider(ISchemaProvider? schema = null, SqlDialect dialect = SqlDialect.Netezza)
    {
        _schema = schema;
        _dialect = dialect;
    }

    /// <summary>Attempt to parse SQL and build completion scope. Null on failure or parser errors.</summary>
    public ScopeBuilder? TryBuild(string sql, int? cursor = null)
    {
        var tokens = Tokenize(sql, _dialect);
        if (tokens is null) return null;

        var parser = DialectRuntime.CreateParser(tokens, _dialect);
        var stmt = parser.Parse();
        if (stmt is null) return null;

        // Editor SQL is often incomplete at the caret. A recoverable partial
        // statement can still carry useful FROM/CTE scope, so use the AST when
        // the parser returned one and let the caller use token fallback only
        // when parsing could not produce a statement at all.

        var builder = new ScopeBuilder();
        var walker = new ScopeWalker(builder, _schema, _dialect, cursor);
        walker.Build(stmt);
        return builder;
    }

    internal static Token<NzToken>[]? Tokenize(string sql, SqlDialect dialect)
    {
        try { return DialectRuntime.Tokenize(sql, dialect).ToArray(); }
        catch { return null; }
    }
}

/// <summary>
/// Lightweight AST walker that builds scope for completion.
/// Does NOT validate — just registers tables, CTEs, aliases, columns.
/// Scopes are entered but NOT exited so the final scope state reflects
/// all visible tables at the innermost query level.
/// </summary>
internal class ScopeWalker
{
    private readonly ScopeBuilder _scope;
    private readonly ISchemaProvider? _schema;
    private readonly SqlDialect _dialect;
    private readonly int? _cursor;

    public ScopeWalker(ScopeBuilder scope, ISchemaProvider? schema, SqlDialect dialect, int? cursor = null)
    {
        _scope = scope;
        _schema = schema;
        _dialect = dialect;
        _cursor = cursor;
    }

    public void Build(Statement stmt)
    {
        switch (stmt)
        {
            case SelectStatement s: WalkSelect(s); break;
            case InsertStatement s: WalkInsert(s); break;
            case UpdateStatement s: WalkUpdate(s); break;
            case DeleteStatement s: WalkDelete(s); break;
            case MergeStatement s: WalkMerge(s); break;
            case CreateViewStatement s: WalkSelect(s.Query); break;
            case CreateTableStatement s: WalkCreateTable(s); break;
        }
    }

    // ====== SELECT ======

    private void WalkSelect(SelectStatement stmt)
    {
        _scope.EnterScope();
        var level = _scope.CurrentScope.Level;

        // Register CTEs first (scope-level, before FROM)
        if (stmt.With is not null)
            RegisterCtes(stmt.With);

        // Register FROM/JOIN tables
        if (stmt.From is not null)
        {
            foreach (var tr in stmt.From)
                WalkTableReference(tr, level);
        }

        // Subqueries in compound selects contribute their scope
        if (stmt.CompoundSelects is not null)
        {
            foreach (var cs in stmt.CompoundSelects)
                WalkSelect(cs);
        }

        // Expression subqueries (EXISTS / IN / scalar) contribute their own
        // scope at the cursor without leaking sibling scopes.
        foreach (var item in stmt.SelectList)
            WalkExpression(item.Expression, level);
        WalkExpression(stmt.Where, level);
        WalkExpression(stmt.Having, level);
        if (stmt.GroupBy is not null)
            foreach (var expression in stmt.GroupBy)
                WalkExpression(expression, level);

        // Scopes NOT exited — so the caller sees all tables registered
        // by this SELECT and its parent scopes
    }

    // ====== EXPRESSION SUBQUERIES ======

    private void WalkCursorSubquery(SelectStatement query, int level)
    {
        if (_cursor is null)
        {
            WalkSelect(query);
            return;
        }
        while (_scope.CurrentScope.Level > level)
            _scope.ExitScope();
        if (query.Position.Absolute <= _cursor)
            WalkSelect(query);
    }

    private void WalkExpression(Expression? expression, int level)
    {
        switch (expression)
        {
            case null:
                return;
            case SubqueryExpression subquery:
                WalkCursorSubquery(subquery.Query, level);
                return;
            case ExistsExpression exists:
                WalkCursorSubquery(exists.Subquery, level);
                return;
            case InExpression inExpression:
                WalkExpression(inExpression.Left, level);
                if (inExpression.Values is not null)
                    foreach (var value in inExpression.Values)
                        WalkExpression(value, level);
                if (inExpression.Subquery is not null)
                    WalkCursorSubquery(inExpression.Subquery, level);
                return;
            case BinaryExpression binary:
                WalkExpression(binary.Left, level);
                WalkExpression(binary.Right, level);
                return;
            case UnaryExpression unary:
                WalkExpression(unary.Operand, level);
                return;
            case BetweenExpression between:
                WalkExpression(between.Value, level);
                WalkExpression(between.Low, level);
                WalkExpression(between.High, level);
                return;
            case IsExpression isExpression:
                WalkExpression(isExpression.Left, level);
                return;
            case QuantifiedComparisonExpression quantified:
                WalkExpression(quantified.Left, level);
                WalkExpression(quantified.Right, level);
                return;
            case CaseExpression caseExpression:
                WalkExpression(caseExpression.Value, level);
                foreach (var clause in caseExpression.WhenClauses)
                {
                    WalkExpression(clause.When, level);
                    WalkExpression(clause.Then, level);
                }
                WalkExpression(caseExpression.ElseClause, level);
                return;
            case CastExpression cast:
                WalkExpression(cast.Expression, level);
                return;
            case CastFunctionExpression castFunction:
                WalkExpression(castFunction.Expression, level);
                return;
            case ExtractExpression extract:
                WalkExpression(extract.Source, level);
                return;
            case FilterClause filter:
                WalkExpression(filter.Condition, level);
                return;
            case FunctionCall function:
                if (function.Arguments is not null)
                    foreach (var argument in function.Arguments)
                        WalkExpression(argument, level);
                if (function.Filter is not null)
                    WalkExpression(function.Filter.Condition, level);
                if (function.Over?.PartitionBy is not null)
                    foreach (var partition in function.Over.PartitionBy)
                        WalkExpression(partition, level);
                return;
            case ArrayExpression array:
                foreach (var item in array.Items)
                    WalkExpression(item, level);
                return;
        }
    }

    // ====== CTE Registration ======

    private void RegisterCtes(WithClause with)
    {
        // Register ALL CTE names first (forward references)
        foreach (var cte in with.Ctes)
        {
            _scope.AddCte(new CteInfo(cte.Name, with.Recursive, null));
            _scope.AddTable(new TableInfo(cte.Name, IsCte: true, IsTempTable: false));
        }

        // Resolve columns (two passes for cross-CTE references)
        foreach (var cte in with.Ctes) ResolveCteColumns(cte);
        foreach (var cte in with.Ctes) ResolveCteColumns(cte);
    }

    private void ResolveCteColumns(CteDefinition cte)
    {
        var existing = _scope.FindTable(cte.Name);
        if (existing?.Columns is { Count: > 0 }) return;

        var cols = CollectCteColumns(cte);
        if (cols.Count == 0) return;

        var colInfos = cols.Select(c => new ColumnInfo(c)).ToList();
        _scope.AddCte(new CteInfo(cte.Name, cte.Query.With?.Recursive ?? false, colInfos));

        var schemaCols = TryGetSchemaColumns(cte.Name);
        _scope.AddTable(new TableInfo(cte.Name, IsCte: true, IsTempTable: false,
            Columns: colInfos));
    }

    private List<string> CollectCteColumns(CteDefinition cte)
    {
        if (cte.Columns is { Count: > 0 })
            return new List<string>(cte.Columns);

        var cols = new List<string>();
        foreach (var item in cte.Query.SelectList)
        {
            var name = item.Alias;
            if (name is null && item.Expression is ColumnReference cr)
                name = cr.Name;
            if (name is not null)
                cols.Add(name);
            else if (item.Expression is StarExpression star)
            {
                var expanded = ExpandStarColumns(cte.Query.From, star.Qualifier);
                cols.AddRange(expanded);
            }
        }
        return cols;
    }

    private List<string> ExpandStarColumns(IReadOnlyList<TableReference>? from, string? qualifier)
    {
        var result = new List<string>();
        if (from is null) return result;
        foreach (var tr in from)
            ExpandSource(tr.Source, result, qualifier);
        return result;
    }

    private void ExpandSource(TableSource source, List<string> result, string? qualifier)
    {
        if (source.Table is null) return;

        // If qualifier specified, only match that table/alias
        if (qualifier is not null)
        {
            var matchAlias = source.Alias is not null &&
                string.Equals(source.Alias, qualifier, StringComparison.OrdinalIgnoreCase);
            var matchName = string.Equals(source.Table.Name, qualifier, StringComparison.OrdinalIgnoreCase);
            if (!matchAlias && !matchName) return;
        }

        // Schema columns
        if (_schema is not null)
        {
            var info = CompletionSchemaLookup.GetTable(
                _schema, _dialect, source.Table.Database, source.Table.Schema, source.Table.Name);
            if (info?.Columns is { Count: > 0 })
            {
                result.AddRange(info.Columns.Select(c => c.Name));
                return;
            }
        }

        // CTE columns
        var scopeTable = _scope.FindTable(source.Table.Name);
        if (scopeTable?.Columns is { Count: > 0 })
            result.AddRange(scopeTable.Columns.Select(c => c.Name));
    }

    // ====== FROM / JOIN / TABLE SOURCE ======

    private void WalkTableReference(TableReference tr, int level)
    {
        WalkTableSource(tr.Source, level);
        if (tr.Joins is not null)
            foreach (var join in tr.Joins)
                WalkJoinClause(join, level);
    }

    private void WalkTableSource(TableSource source, int level)
    {
        if (source.Table is not null)
        {
            var table = BuildTableInfo(source.Table, source.Alias);
            if (_scope.FindTable(source.Table.Name) is { IsCte: true })
                table = table with { IsCte = true };
            _scope.AddTable(table);
        }

        if (source.Subquery is not null)
        {
            WalkCursorSubquery(source.Subquery, level);
            if (source.Alias is not null)
            {
                var subCols = InferSubqueryColumns(source.Subquery);
                _scope.AddTable(new TableInfo(source.Alias, IsCte: false, IsTempTable: false,
                    Columns: subCols.Count > 0 ? subCols : null, IsDerived: true));
            }
        }

        if (source.FunctionSource && source.Alias is not null)
        {
            _scope.AddTable(new TableInfo(source.Alias, IsCte: false, IsTempTable: false, IsDerived: true));
        }
    }

    private void WalkJoinClause(JoinClause join, int level)
    {
        WalkTableSource(join.Source, level);
    }

    // ====== INSERT / UPDATE / DELETE / MERGE ======

    private void WalkInsert(InsertStatement stmt)
    {
        _scope.EnterScope();
        _scope.AddTable(BuildTableInfo(stmt.Target, null));

        if (stmt.SourceQuery is not null)
            WalkSelect(stmt.SourceQuery);
    }

    private void WalkUpdate(UpdateStatement stmt)
    {
        _scope.EnterScope();
        var level = _scope.CurrentScope.Level;
        _scope.AddTable(BuildTableInfo(stmt.Target, stmt.Alias));

        if (stmt.From is not null)
            foreach (var tr in stmt.From)
                WalkTableReference(tr, level);
    }

    private void WalkDelete(DeleteStatement stmt)
    {
        _scope.EnterScope();
        var level = _scope.CurrentScope.Level;
        if (stmt.Target is not null)
            _scope.AddTable(BuildTableInfo(stmt.Target, stmt.Alias));
        if (stmt.From is not null)
        {
            foreach (var tr in stmt.From)
                WalkTableReference(tr, level);
        }
    }

    private void WalkMerge(MergeStatement stmt)
    {
        _scope.EnterScope();
        var level = _scope.CurrentScope.Level;
        _scope.AddTable(BuildTableInfo(stmt.Target, stmt.TargetAlias));
        WalkTableSource(stmt.Source, level);
    }

    private void WalkCreateTable(CreateTableStatement stmt)
    {
        _scope.EnterScope();
        if (stmt.AsSelect is not null)
            WalkSelect(stmt.AsSelect);
    }

    // ====== Helpers ======

    private TableInfo BuildTableInfo(TableName name, string? alias)
    {
        IReadOnlyList<ColumnInfo>? columns = null;
        if (_schema is not null)
        {
            var info = CompletionSchemaLookup.GetTable(_schema, _dialect, name.Database, name.Schema, name.Name);
            if (info?.Columns is { Count: > 0 })
                columns = info.Columns;
        }

        return new TableInfo(name.Name, name.Schema, name.Database,
            IsCte: false, IsTempTable: false,
            Alias: alias ?? name.Name,
            Columns: columns);
    }

    private IReadOnlyList<ColumnInfo>? TryGetSchemaColumns(string name)
    {
        if (_schema is null) return null;
        var info = CompletionSchemaLookup.GetTable(_schema, _dialect, null, null, name);
        return info?.Columns;
    }

    private List<ColumnInfo> InferSubqueryColumns(SelectStatement stmt)
    {
        var cols = new List<ColumnInfo>();
        foreach (var item in stmt.SelectList)
        {
            var name = item.Alias;
            if (name is null && item.Expression is ColumnReference cr)
                name = cr.Name;
            if (name is not null)
                cols.Add(new ColumnInfo(name));
            else if (item.Expression is StarExpression star)
            {
                foreach (var reference in stmt.From ?? Array.Empty<TableReference>())
                {
                    AddExpandedSourceColumns(reference.Source, star.Qualifier, cols);
                    foreach (var join in reference.Joins ?? Array.Empty<JoinClause>())
                        AddExpandedSourceColumns(join.Source, star.Qualifier, cols);
                }
            }
        }
        return cols;
    }

    private void AddExpandedSourceColumns(TableSource source, string? qualifier, List<ColumnInfo> columns)
    {
        if (qualifier is not null
            && !string.Equals(source.Alias, qualifier, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(source.Table?.Name, qualifier, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (source.Table is not null && _schema is not null)
        {
            var table = CompletionSchemaLookup.GetTable(
                _schema, _dialect, source.Table.Database, source.Table.Schema, source.Table.Name);
            if (table?.Columns is { Count: > 0 })
            {
                columns.AddRange(table.Columns.Select(column => new ColumnInfo(
                    column.Name, DataType: column.DataType, Description: column.Description)));
                return;
            }
        }

        if (source.Subquery is not null)
            columns.AddRange(InferSubqueryColumns(source.Subquery));
    }
}

public enum SqlScopeRelationKind
{
    Table,
    Cte,
    DerivedTable,
    ScriptLocalTable,
}

public sealed record SqlScopeRelation(string Name, string Alias, SqlScopeRelationKind Kind);

public sealed record SqlScopeAtCursor(
    IReadOnlyList<SqlScopeRelation> VisibleRelations,
    IReadOnlyList<string> VisibleCtes,
    IReadOnlyList<string> VisibleAliases);

/// <summary>
/// Direct semantic scope primitive for editor contracts: the relations, CTEs
/// and reference names visible at one cursor offset. It deliberately exposes
/// no scope ids, parents, depths, AST nodes or shadowing data.
/// </summary>
public static class SqlScopeAtCursorResolver
{
    public static SqlScopeAtCursor? Resolve(
        string sql,
        int cursorPosition,
        ISchemaProvider? schema = null,
        SqlDialect dialect = SqlDialect.Netezza)
    {
        var relations = new List<SqlScopeRelation>();
        var ctes = new List<string>();
        var aliases = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string alias, SqlScopeRelationKind kind)
        {
            var key = $"{alias}\u0000{name}\u0000{kind}";
            if (!seen.Add(key)) return;
            if (seenAliases.Add(alias)) aliases.Add(alias);
            relations.Add(new SqlScopeRelation(name, alias, kind));
        }

        var tokens = CompletionScopeProvider.Tokenize(sql, dialect);
        TokenScopeCollector? collector = null;
        if (tokens is not null)
        {
            collector = new TokenScopeCollector(schema, dialect);
            collector.Collect(tokens, sql.Length);
            foreach (var temp in collector.GetTempTableNames())
                Add(temp, temp, SqlScopeRelationKind.ScriptLocalTable);
        }

        // The AST walker builds scope for a single statement; multi-statement
        // scripts rely on the token collector for script-local relations.
        if (!sql.Contains(';'))
        {
            var builder = new CompletionScopeProvider(schema, dialect).TryBuild(sql, cursorPosition);
            if (builder is not null)
            {
                foreach (var table in builder.GetAllVisibleTables())
                {
                    var kind = table.IsCte
                        ? SqlScopeRelationKind.Cte
                        : table.IsTempTable
                            ? SqlScopeRelationKind.ScriptLocalTable
                            : table.IsDerived
                                ? SqlScopeRelationKind.DerivedTable
                                : SqlScopeRelationKind.Table;
                    Add(table.Name, table.Alias ?? table.Name, kind);
                }
                var scope = builder.CurrentScope;
                while (scope is not null)
                {
                    foreach (var cte in scope.Ctes.Values)
                    {
                        if (!ctes.Contains(cte.Name, StringComparer.OrdinalIgnoreCase))
                            ctes.Add(cte.Name);
                    }
                    scope = scope.Parent;
                }
            }
        }

        if (collector is not null)
        {
            var tempNames = collector.GetTempTableNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in collector.GetCteNamesInScope(cursorPosition))
            {
                if (tempNames.Contains(name)) continue;
                if (!ctes.Contains(name, StringComparer.OrdinalIgnoreCase))
                    ctes.Add(name);
                if (!relations.Any(relation =>
                        relation.Kind == SqlScopeRelationKind.Cte &&
                        string.Equals(relation.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    Add(name, name, SqlScopeRelationKind.Cte);
                }
            }
        }

        return new SqlScopeAtCursor(relations, ctes, aliases);
    }
}
