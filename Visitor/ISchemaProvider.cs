using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

/// <summary>
/// Provides table/column metadata for semantic validation and autocomplete.
/// </summary>
public interface ISchemaProvider
{
    bool TableExists(string? database, string? schema, string tableName);
    bool HasTables();
    TableInfo? GetTable(string? database, string? schema, string tableName);
    IReadOnlyList<(string Name, TableKind Kind)>? GetTableNames(string? database, string? schema);
    IReadOnlyList<string>? GetDatabases();
    IReadOnlyList<string>? GetSchemas(string? database);
    bool CanValidateUnqualifiedTableReferences();
    IReadOnlyList<TableQualificationProposal>? ProposeTableQualification(
        string? database, string? schema, string tableName) => null;
    void BumpMetadataEpoch();
    int MetadataEpoch { get; }
}

public record TableQualificationProposal(
    string Database,
    string Schema,
    string Name,
    string QualifiedText,
    bool IsPreferred = false
);

public enum TableKind { Table, View, Synonym }

/// <summary>
/// In-memory schema provider for testing.
/// </summary>
public class InMemorySchemaProvider : ISchemaProvider
{
    private readonly object _lock = new();
    private readonly Dictionary<string, TableInfo> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TableQualificationProposal>> _qualificationProposals = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _absentTables = new(StringComparer.OrdinalIgnoreCase);
    private int _metadataEpoch;

    public int MetadataEpoch
    {
        get { lock (_lock) return _metadataEpoch; }
    }

    public void BumpMetadataEpoch()
    {
        lock (_lock)
        {
            _metadataEpoch++;
            _absentTables.Clear();
        }
    }

    public void AddTable(TableInfo table)
    {
        lock (_lock)
        {
            var key = FormatKey(table.Database, table.Schema, table.Name);
            _tables[key] = table;
            _absentTables.Remove(key);
            _absentTables.Remove(FormatAbsentKey(table.Database, table.Schema, table.Name));

            // Also clear absent cache for database..table form
            if (table.Database is not null)
            {
                _absentTables.Remove(FormatAbsentKey(table.Database, null, table.Name));
            }

            _absentTables.Remove(FormatAbsentKey(null, null, table.Name));
        }
    }

    public bool TableExists(string? database, string? schema, string tableName)
    {
        return GetTable(database, schema, tableName) is not null;
    }

    public bool HasTables()
    {
        lock (_lock) return _tables.Count > 0;
    }

    public TableInfo? GetTable(string? database, string? schema, string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return null;

        lock (_lock)
        {
            var absentKey = FormatAbsentKey(database, schema, tableName);
            if (_absentTables.Contains(absentKey))
                return null;

            // Exact match first
            var exactKey = FormatKey(database, schema, tableName);
            if (_tables.TryGetValue(exactKey, out var t)) return t;

            // Double-dot: database..table means look across schemas
            if (database is not null && schema is null)
            {
                foreach (var (_, info) in _tables)
                {
                    if (IdentifiersEqual(info.Database, database) &&
                        IdentifiersEqual(info.Name, tableName))
                        return info;
                }
            }

            // Case-insensitive fallback
            foreach (var (key, info) in _tables)
            {
                var parts = key.Split('.');
                var keyTable = parts[^1];
                if (!IdentifiersEqual(keyTable, tableName)) continue;
                if (database is not null && !IdentifiersEqual(info.Database, database)) continue;
                if (schema is not null && !IdentifiersEqual(info.Schema, schema)) continue;
                return info;
            }

            if (_tables.Count > 0)
                _absentTables.Add(absentKey);

            return null;
        }
    }

    public IReadOnlyList<string>? GetDatabases()
    {
        lock (_lock)
        {
            return _tables.Values
                .Where(t => t.Database is not null)
                .Select(t => t.Database!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<string>? GetSchemas(string? database)
    {
        lock (_lock)
        {
            return _tables.Values
                .Where(t => database is null || IdentifiersEqual(t.Database, database))
                .Where(t => t.Schema is not null)
                .Select(t => t.Schema!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<(string Name, TableKind Kind)>? GetTableNames(string? database, string? schema)
    {
        lock (_lock)
        {
            var results = new List<(string Name, TableKind Kind)>();
            foreach (var info in _tables.Values)
            {
                if (database is not null && !IdentifiersEqual(info.Database, database))
                    continue;
                if (schema is not null && !IdentifiersEqual(info.Schema, schema))
                    continue;
                results.Add((info.Name, info.IsView ? TableKind.View : TableKind.Table));
            }
            return results
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>
    /// A host-neutral snapshot has no active database/search-path concept, so
    /// unqualified lookup remains intentionally conservative.
    /// </summary>
    public bool CanValidateUnqualifiedTableReferences() => false;

    public void SetTableQualificationProposals(IEnumerable<TableQualificationProposal> proposals)
    {
        lock (_lock)
        {
            _qualificationProposals.Clear();
            foreach (var proposal in proposals)
            {
                var normalizedName = NormalizeIdentifier(proposal.Name);
                if (!_qualificationProposals.TryGetValue(normalizedName, out var values))
                {
                    values = new List<TableQualificationProposal>();
                    _qualificationProposals[normalizedName] = values;
                }
                values.Add(proposal);
            }
        }
    }

    public IReadOnlyList<TableQualificationProposal>? ProposeTableQualification(
        string? database, string? schema, string tableName)
    {
        lock (_lock)
        {
            return _qualificationProposals.TryGetValue(NormalizeIdentifier(tableName), out var proposals)
                ? proposals.ToArray()
                : Array.Empty<TableQualificationProposal>();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _tables.Clear();
            _qualificationProposals.Clear();
            _absentTables.Clear();
            _metadataEpoch++;
        }
    }

    private static string FormatKey(string? db, string? schema, string name)
    {
        return $"{NormalizeIdentifier(db)}.{NormalizeIdentifier(schema)}.{NormalizeIdentifier(name)}";
    }

    private static string FormatAbsentKey(string? db, string? schema, string name) =>
        FormatKey(db, schema, name);

    private static bool IdentifiersEqual(string? left, string? right) =>
        string.Equals(NormalizeIdentifier(left), NormalizeIdentifier(right), StringComparison.Ordinal);

    /// <summary>
    /// Produces the comparison form used by Netezza catalog lookups. Quoting is
    /// syntactic at this boundary: callers can use either catalog names or SQL
    /// identifier text without creating duplicate metadata entries.
    /// </summary>
    private static string NormalizeIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return string.Empty;

        var value = identifier.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Replace("\"\"", "\"");

        return value.ToUpperInvariant();
    }
}
