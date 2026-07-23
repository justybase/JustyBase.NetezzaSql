using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
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

    public CompletionScopeProvider(ISchemaProvider? schema = null)
    {
        _schema = schema;
    }

    /// <summary>Attempt to parse SQL and build completion scope. Null on failure or parser errors.</summary>
    public ScopeBuilder? TryBuild(string sql)
    {
        var tokens = Tokenize(sql);
        if (tokens is null) return null;

        var parser = new NzSqlParser(tokens);
        var stmt = parser.Parse();
        if (stmt is null) return null;

        // Only use AST scope when parsing produced no errors — otherwise
        // the scope may have incomplete or incorrect registrations
        // (e.g., WITH ... AS (...) ... WHERE alias. with trailing dot)
        if (parser.Errors.Count > 0) return null;

        var builder = new ScopeBuilder();
        var walker = new ScopeWalker(builder, _schema);
        walker.Build(stmt);
        return builder;
    }

    private static Token<NzToken>[]? Tokenize(string sql)
    {
        try { return NzLexer.Tokenize(sql).ToArray(); }
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

    public ScopeWalker(ScopeBuilder scope, ISchemaProvider? schema)
    {
        _scope = scope;
        _schema = schema;
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

        // Register CTEs first (scope-level, before FROM)
        if (stmt.With is not null)
            RegisterCtes(stmt.With);

        // Register FROM/JOIN tables
        if (stmt.From is not null)
        {
            foreach (var tr in stmt.From)
                WalkTableReference(tr);
        }

        // Subqueries in compound selects contribute their scope
        if (stmt.CompoundSelects is not null)
        {
            foreach (var cs in stmt.CompoundSelects)
                WalkSelect(cs);
        }

        // Scopes NOT exited — so the caller sees all tables registered
        // by this SELECT and its parent scopes
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
            var info = _schema.GetTable(source.Table.Database, source.Table.Schema, source.Table.Name);
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

    private void WalkTableReference(TableReference tr)
    {
        WalkTableSource(tr.Source);
        if (tr.Joins is not null)
            foreach (var join in tr.Joins)
                WalkJoinClause(join);
    }

    private void WalkTableSource(TableSource source)
    {
        if (source.Table is not null)
        {
            var table = BuildTableInfo(source.Table, source.Alias);
            _scope.AddTable(table);
        }

        if (source.Subquery is not null)
        {
            WalkSelect(source.Subquery);
            if (source.Alias is not null)
            {
                var subCols = InferSubqueryColumns(source.Subquery);
                _scope.AddTable(new TableInfo(source.Alias, IsCte: false, IsTempTable: false,
                    Columns: subCols.Count > 0 ? subCols : null));
            }
        }

        if (source.FunctionSource && source.Alias is not null)
        {
            _scope.AddTable(new TableInfo(source.Alias, IsCte: false, IsTempTable: false));
        }
    }

    private void WalkJoinClause(JoinClause join)
    {
        WalkTableSource(join.Source);
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
        _scope.AddTable(BuildTableInfo(stmt.Target, stmt.Alias));

        if (stmt.From is not null)
            foreach (var tr in stmt.From)
                WalkTableReference(tr);
    }

    private void WalkDelete(DeleteStatement stmt)
    {
        _scope.EnterScope();
        if (stmt.Target is not null)
            _scope.AddTable(BuildTableInfo(stmt.Target, stmt.Alias));
    }

    private void WalkMerge(MergeStatement stmt)
    {
        _scope.EnterScope();
        _scope.AddTable(BuildTableInfo(stmt.Target, stmt.TargetAlias));
        WalkTableSource(stmt.Source);
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
            var info = _schema.GetTable(name.Database, name.Schema, name.Name);
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
        var info = _schema.GetTable(null, null, name);
        return info?.Columns;
    }

    private static List<ColumnInfo> InferSubqueryColumns(SelectStatement stmt)
    {
        var cols = new List<ColumnInfo>();
        foreach (var item in stmt.SelectList)
        {
            var name = item.Alias;
            if (name is null && item.Expression is ColumnReference cr)
                name = cr.Name;
            if (name is not null)
                cols.Add(new ColumnInfo(name));
        }
        return cols;
    }
}
