using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

/// <summary>
/// Manages scope stack for SQL validation: tracks visible tables, CTEs, and aliases.
/// Mirrors scopeBuilder.ts from the reference TypeScript project.
/// </summary>
public class ScopeBuilder
{
    private Scope _currentScope;

    public ScopeBuilder()
    {
        _currentScope = new Scope(
            new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, CteInfo>(StringComparer.OrdinalIgnoreCase),
            null, 0);
    }

    public void Reset()
    {
        _currentScope = new Scope(
            new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, CteInfo>(StringComparer.OrdinalIgnoreCase),
            null, 0);
    }

    public Scope CurrentScope => _currentScope;

    public void EnterScope()
    {
        _currentScope = new Scope(
            new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, CteInfo>(StringComparer.OrdinalIgnoreCase),
            _currentScope, _currentScope.Level + 1);
    }

    public void ExitScope()
    {
        if (_currentScope.Parent is not null)
            _currentScope = _currentScope.Parent;
    }

    /// <summary>Add table to current scope. Returns existing table with same alias/name if duplicate.</summary>
    public TableInfo? AddTable(TableInfo table)
    {
        var key = (table.Alias ?? table.Name).ToUpperInvariant();
        if (_currentScope.Tables.TryGetValue(key, out var existing))
            return existing;
        _currentScope.Tables[key] = table;
        return null;
    }

    public void AddCte(CteInfo cte)
    {
        _currentScope.Ctes[cte.Name.ToUpperInvariant()] = cte;
    }

    public TableInfo? FindTable(string nameOrAlias)
    {
        var key = nameOrAlias.ToUpperInvariant();
        var scope = _currentScope;
        while (scope is not null)
        {
            if (scope.Tables.TryGetValue(key, out var table))
                return table;
            if (scope.Ctes.TryGetValue(key, out var cte))
                return new TableInfo(cte.Name, IsCte: true, Columns: cte.Columns);
            scope = scope.Parent;
        }
        return null;
    }

    public IReadOnlyList<TableInfo> GetAllVisibleTables()
    {
        var all = new List<TableInfo>();
        var seenCteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scope = _currentScope;
        while (scope is not null)
        {
            all.AddRange(scope.Tables.Values);
            seenCteNames.UnionWith(scope.Tables.Values
                .Where(t => t.IsCte).Select(t => t.Name));
            foreach (var cte in scope.Ctes.Values)
            {
                if (!seenCteNames.Contains(cte.Name))
                    all.Add(new TableInfo(cte.Name, IsCte: true, Columns: cte.Columns));
            }
            scope = scope.Parent;
        }
        return all;
    }

    /// <summary>Get tables visible in the current scope only (not parent scopes).</summary>
    public IReadOnlyList<TableInfo> GetCurrentScopeTables()
    {
        var tables = new List<TableInfo>(_currentScope.Tables.Values);
        var tableKeys = new HashSet<string>(_currentScope.Tables.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var cte in _currentScope.Ctes.Values)
        {
            if (!tableKeys.Contains(cte.Name))
                tables.Add(new TableInfo(cte.Name, IsCte: true, Columns: cte.Columns));
        }
        return tables;
    }
}
