using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Builds one column identity per column reference in a document. Definition
/// and References for columns are both answered from this single index.
/// </summary>
/// <remarks>
/// Resolution rules:
/// <list type="bullet">
/// <item>A qualified reference binds its qualifier to a visible relation (current
/// frame first, then correlated parents) and requires the column to exist when
/// the relation's columns are known.</item>
/// <item>An unqualified reference is resolved only when exactly one visible source
/// provides the column; two or more is ambiguous, and unknown metadata leaves it
/// unresolved instead of guessing.</item>
/// <item>ORDER BY binds an unqualified name to an explicit output alias first; an
/// aliased select item has one identity, which is the column of the CTE, derived
/// table or CTAS its select defines.</item>
/// <item>A projection carries the physical origin of a plain column reference.</item>
/// </list>
/// </remarks>
internal sealed class NzColumnIdentityCollector
{
    internal sealed record Occurrence(int Start, int End, string Key, bool IsDefinition);

    internal sealed class IdentityInfo
    {
        public required string Name { get; init; }
        public SqlColumnResolutionStatus Status { get; init; } = SqlColumnResolutionStatus.Resolved;
        public SqlColumnRelationKind? RelationKind { get; init; }
        public string? Relation { get; init; }
        public (int Start, int End)? Definition { get; init; }
        public SqlCatalogColumn? Catalog { get; init; }
        public SqlCatalogColumn? Origin { get; init; }
        public IReadOnlyList<string> Candidates { get; init; } = Array.Empty<string>();
    }

    private sealed record ProjectedColumn(string Name, string Norm, string Key, int DefStart, int DefEnd, SqlCatalogColumn? Origin);

    private sealed class Relation
    {
        public required string Name { get; init; }
        public required bool NameQuoted { get; init; }
        public string? Alias { get; init; }
        public bool AliasQuoted { get; init; }
        public required SqlColumnRelationKind Kind { get; init; }
        /// <summary>Local projected columns (CTE, derived table, script-local table).</summary>
        public IReadOnlyList<ProjectedColumn?>? Columns { get; init; }
        /// <summary>Physical catalog path and its column names; names are null when metadata is unknown.</summary>
        public (string? Database, string? Schema, string Table)? Physical { get; init; }
        public IReadOnlyList<string>? PhysicalColumns { get; init; }
        /// <summary>Metadata data types aligned with <see cref="PhysicalColumns"/>.</summary>
        public IReadOnlyList<string?>? PhysicalTypes { get; init; }

        public string ExposedNorm => Alias is not null ? Normalize(Alias, AliasQuoted) : Normalize(Name, NameQuoted);

        public bool HasKnownColumns => Physical is null || PhysicalColumns is not null;
    }

    private sealed class Frame
    {
        public Frame(Frame? parent, Dictionary<string, Relation>? ctes = null)
        {
            Parent = parent;
            Ctes = ctes ?? new Dictionary<string, Relation>(StringComparer.Ordinal);
        }

        public Frame? Parent { get; }
        public Dictionary<string, Relation> Ctes { get; }
        public List<Relation> Sources { get; } = new();
        public Dictionary<string, ProjectedColumn> OutputAliases { get; } = new(StringComparer.Ordinal);
    }

    private readonly ISchemaProvider? _schema;
    private readonly List<Occurrence> _occurrences = new();
    private readonly Dictionary<string, IdentityInfo> _identities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Relation> _scriptTables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _physicalTypes = new(StringComparer.Ordinal);
    private Token<NzToken>[] _tokens = Array.Empty<Token<NzToken>>();
    private int[] _tokenStarts = Array.Empty<int>();

    private NzColumnIdentityCollector(ISchemaProvider? schema) => _schema = schema;

    public IReadOnlyList<Occurrence> Occurrences => _occurrences;

    public IReadOnlyDictionary<string, IdentityInfo> Identities => _identities;

    /// <summary>Metadata data type of a physical column; null when metadata does not know it.</summary>
    public string? TypeOf(SqlCatalogColumn? column)
        => column is not null && _physicalTypes.TryGetValue(PhysicalKey(column), out var type) ? type : null;

    public static NzColumnIdentityCollector Collect(string text, ISchemaProvider? schema, SqlDialect dialect)
    {
        var collector = new NzColumnIdentityCollector(schema);
        collector.Analyze(text, dialect);
        return collector;
    }

    private void Analyze(string text, SqlDialect dialect)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            _tokens = DialectRuntime.Tokenize(text, dialect).ToArray();
        }
        catch
        {
            return;
        }
        _tokenStarts = _tokens.Select(token => token.Span.Position.Absolute).ToArray();

        var global = new Frame(null);
        var index = 0;
        while (index < _tokens.Length)
        {
            while (index < _tokens.Length && _tokens[index].Kind == NzToken.Semicolon) index++;
            if (index >= _tokens.Length) break;

            var parser = DialectRuntime.CreateParser(_tokens[index..], dialect);
            var statement = parser.Parse();
            if (statement is null || parser.Position <= 0) break;
            ProcessStatement(statement, global);
            index += parser.Position;
        }
    }

    private void ProcessStatement(Statement statement, Frame global)
    {
        switch (statement)
        {
            case SelectStatement select:
                ProcessSelect(select, global);
                break;
            case InsertStatement { SourceQuery: not null } insert:
                ProcessSelect(insert.SourceQuery, global);
                break;
            case CreateTableStatement { AsSelect: not null } create:
            {
                var columns = ProcessSelect(create.AsSelect, global);
                var relation = new Relation
                {
                    Name = create.Table.Name,
                    NameQuoted = create.Table.NameQuote is not null,
                    Kind = SqlColumnRelationKind.ScriptLocalTable,
                    Columns = columns,
                };
                RegisterLocalColumns(relation);
                _scriptTables[Normalize(create.Table.Name, create.Table.NameQuote is not null)] = relation;
                break;
            }
            case DropStatement drop when string.Equals(drop.ObjectType, "TABLE", StringComparison.OrdinalIgnoreCase):
                foreach (var target in drop.Targets)
                    _scriptTables.Remove(Normalize(target.Name, target.NameQuote is not null));
                break;
        }
    }

    /// <summary>Resolves one query block and returns its positional projection.</summary>
    private IReadOnlyList<ProjectedColumn?> ProcessSelect(SelectStatement select, Frame parent)
    {
        var frame = new Frame(parent);
        if (select.With is not null) ProcessWith(select.With, frame);

        if (select.From is not null)
        {
            foreach (var reference in select.From)
            {
                AddSource(reference.Source, frame);
                if (reference.Joins is null) continue;
                foreach (var join in reference.Joins) AddSource(join.Source, frame);
            }
            foreach (var reference in select.From)
            {
                if (reference.Joins is null) continue;
                foreach (var join in reference.Joins)
                    if (join.OnCondition is not null) Walk(join.OnCondition, frame);
            }
        }

        var projection = new List<ProjectedColumn?>();
        for (var i = 0; i < select.SelectList.Count; i++)
        {
            var item = select.SelectList[i];
            var key = Walk(item.Expression, frame);
            projection.AddRange(ProjectItem(select, i, key, frame));
        }

        if (select.DistinctOn is not null) foreach (var expression in select.DistinctOn) Walk(expression, frame);
        if (select.Where is not null) Walk(select.Where, frame);
        if (select.GroupBy is not null) foreach (var expression in select.GroupBy) Walk(expression, frame);
        if (select.Having is not null) Walk(select.Having, frame);
        if (select.OrderBy is not null) foreach (var order in select.OrderBy) Walk(order.Expression, frame, orderBy: true);
        if (select.CompoundSelects is not null)
            foreach (var branch in select.CompoundSelects) ProcessSelect(branch, parent);

        return projection;
    }

    private void ProcessWith(WithClause with, Frame frame)
    {
        foreach (var cte in with.Ctes)
        {
            var columns = ProcessSelect(cte.Query, frame);
            if (cte.Columns is { Count: > 0 })
                columns = RenameFromColumnList(cte, columns);

            var relation = new Relation
            {
                Name = cte.Name,
                NameQuoted = LocateCteHeader(cte).NameIndex is var nameIndex and >= 0
                             && _tokens[nameIndex].ToStringValue().StartsWith('"'),
                Kind = SqlColumnRelationKind.Cte,
                Columns = columns,
            };
            RegisterLocalColumns(relation);
            frame.Ctes[relation.ExposedNorm] = relation;
        }
    }

    /// <summary>Applies an explicit CTE column list; each list entry becomes the definition.</summary>
    private IReadOnlyList<ProjectedColumn?> RenameFromColumnList(CteDefinition cte, IReadOnlyList<ProjectedColumn?> columns)
    {
        var (_, listOpen, listClose) = LocateCteHeader(cte);
        if (listOpen < 0) return columns;

        var renamed = new List<ProjectedColumn?>();
        var position = 0;
        for (var i = listOpen + 1; i < listClose; i++)
        {
            if (_tokens[i].Kind == NzToken.Comma) continue;
            var token = _tokens[i];
            var quoted = token.ToStringValue().StartsWith('"');
            var name = token.ToIdentifierText();
            var start = token.Span.Position.Absolute;
            var end = start + token.Span.Length;
            var origin = position < columns.Count ? columns[position]?.Origin : null;
            var norm = Normalize(name, quoted);
            renamed.Add(new ProjectedColumn(name, norm, LocalKey(start, norm), start, end, origin));
            position++;
        }
        return renamed;
    }

    /// <summary>
    /// Finds the CTE name token and its optional column list by walking back from
    /// the body: <c>name [ ( columns ) ] AS ( body</c>. The parser positions a
    /// CteDefinition after its body, so the header is located from the query start.
    /// </summary>
    private (int NameIndex, int ListOpen, int ListClose) LocateCteHeader(CteDefinition cte)
    {
        var i = IndexAtOrAfter(cte.Query.Position.Absolute) - 1;
        if (i < 0) return (-1, -1, -1);
        while (i >= 0 && _tokens[i].Kind == NzToken.LParen) i--;
        if (i >= 0 && _tokens[i].Kind == NzToken.As) i--;
        if (i >= 0 && _tokens[i].Kind == NzToken.RParen)
        {
            var close = i;
            var depth = 0;
            for (; i >= 0; i--)
            {
                if (_tokens[i].Kind == NzToken.RParen) depth++;
                else if (_tokens[i].Kind == NzToken.LParen && --depth == 0) break;
            }
            return i > 0 ? (i - 1, i, close) : (-1, -1, -1);
        }
        return (i, -1, -1);
    }

    private void AddSource(TableSource source, Frame frame)
    {
        if (source.Subquery is not null)
        {
            // A derived table sees CTEs and correlation parents, not sibling FROM items.
            var derivedParent = new Frame(frame.Parent, frame.Ctes);
            var columns = ProcessSelect(source.Subquery, derivedParent);
            var relation = new Relation
            {
                Name = source.Alias ?? string.Empty,
                NameQuoted = source.AliasQuote is not null,
                Kind = SqlColumnRelationKind.DerivedTable,
                Columns = columns,
            };
            RegisterLocalColumns(relation);
            frame.Sources.Add(relation);
            return;
        }

        if (source.Table is null)
        {
            // Table functions and other opaque sources: columns are unknown and
            // there is no catalog relation (the alias is not a table name).
            frame.Sources.Add(new Relation
            {
                Name = source.Alias ?? string.Empty,
                NameQuoted = source.AliasQuote is not null,
                Kind = SqlColumnRelationKind.Table,
                Physical = (null, null, string.Empty),
            });
            return;
        }

        var table = source.Table;
        var nameQuoted = table.NameQuote is not null;
        var norm = Normalize(table.Name, nameQuoted);
        if (table.Database is null && table.Schema is null)
        {
            var cte = FindCte(frame, norm);
            if (cte is not null)
            {
                frame.Sources.Add(new Relation
                {
                    Name = cte.Name,
                    NameQuoted = cte.NameQuoted,
                    Alias = source.Alias,
                    AliasQuoted = source.AliasQuote is not null,
                    Kind = SqlColumnRelationKind.Cte,
                    Columns = cte.Columns,
                });
                return;
            }
            if (_scriptTables.TryGetValue(norm, out var local))
            {
                frame.Sources.Add(new Relation
                {
                    Name = local.Name,
                    NameQuoted = local.NameQuoted,
                    Alias = source.Alias,
                    AliasQuoted = source.AliasQuote is not null,
                    Kind = SqlColumnRelationKind.ScriptLocalTable,
                    Columns = local.Columns,
                });
                return;
            }
        }

        TableInfo? info = null;
        try { info = _schema?.GetTable(table.Database, table.Schema, table.Name); }
        catch { info = null; }
        frame.Sources.Add(new Relation
        {
            Name = info?.Name ?? table.Name,
            NameQuoted = nameQuoted,
            Alias = source.Alias,
            AliasQuoted = source.AliasQuote is not null,
            Kind = SqlColumnRelationKind.Table,
            Physical = (info?.Database ?? table.Database, info?.Schema ?? table.Schema, info?.Name ?? table.Name),
            PhysicalColumns = info?.Columns is { Count: > 0 } known ? known.Select(column => column.Name).ToArray() : null,
            PhysicalTypes = info?.Columns is { Count: > 0 } typed ? typed.Select(column => column.DataType).ToArray() : null,
        });
    }

    /// <summary>Walks an expression; returns the identity key when it is a single column reference.</summary>
    private string? Walk(Expression expression, Frame frame, bool orderBy = false)
    {
        switch (expression)
        {
            case ColumnReference column:
                return ResolveReference(column, frame, orderBy);
            case BinaryExpression binary:
                Walk(binary.Left, frame);
                Walk(binary.Right, frame);
                break;
            case UnaryExpression unary:
                Walk(unary.Operand, frame);
                break;
            case CastExpression cast:
                Walk(cast.Expression, frame);
                break;
            case CastFunctionExpression castFunction:
                Walk(castFunction.Expression, frame);
                break;
            case ArrayExpression array:
                foreach (var item in array.Items) Walk(item, frame);
                break;
            case ExistsExpression exists:
                ProcessSelect(exists.Subquery, frame);
                break;
            case SubqueryExpression subquery:
                ProcessSelect(subquery.Query, frame);
                break;
            case InExpression inExpression:
                Walk(inExpression.Left, frame);
                if (inExpression.Values is not null) foreach (var value in inExpression.Values) Walk(value, frame);
                if (inExpression.Subquery is not null) ProcessSelect(inExpression.Subquery, frame);
                break;
            case BetweenExpression between:
                Walk(between.Value, frame);
                Walk(between.Low, frame);
                Walk(between.High, frame);
                break;
            case IsExpression isExpression:
                Walk(isExpression.Left, frame);
                break;
            case QuantifiedComparisonExpression quantified:
                Walk(quantified.Left, frame);
                Walk(quantified.Right, frame);
                break;
            case FunctionCall function:
                if (function.Arguments is not null) foreach (var argument in function.Arguments) Walk(argument, frame);
                if (function.Filter is not null) Walk(function.Filter.Condition, frame);
                if (function.Over is not null)
                {
                    if (function.Over.PartitionBy is not null) foreach (var part in function.Over.PartitionBy) Walk(part, frame);
                    if (function.Over.OrderBy is not null) foreach (var order in function.Over.OrderBy) Walk(order.Expression, frame);
                }
                break;
            case CaseExpression caseExpression:
                if (caseExpression.Value is not null) Walk(caseExpression.Value, frame);
                foreach (var branch in caseExpression.WhenClauses)
                {
                    Walk(branch.When, frame);
                    Walk(branch.Then, frame);
                }
                if (caseExpression.ElseClause is not null) Walk(caseExpression.ElseClause, frame);
                break;
            case ExtractExpression extract:
                Walk(extract.Source, frame);
                break;
        }
        return null;
    }

    private string? ResolveReference(ColumnReference column, Frame frame, bool orderBy)
    {
        if (column.IsScriptVariable || column.Name == "*") return null;
        var nameIndex = FindColumnNameToken(column);
        if (nameIndex < 0) return null;
        var token = _tokens[nameIndex];
        var start = token.Span.Position.Absolute;
        var end = start + token.Span.Length;
        var norm = Normalize(column.Name, column.NameQuote is not null);

        string key;
        if (column.Qualifier is not null)
        {
            var relation = FindRelation(frame, Normalize(column.Qualifier, column.QualifierQuote is not null));
            key = relation is null
                ? Unresolved(column.Name, start)
                : ResolveInRelation(relation, norm, column.Name) ?? Unresolved(column.Name, start);
        }
        else if (orderBy && frame.OutputAliases.TryGetValue(norm, out var alias))
        {
            key = OutputAliasKey(alias);
        }
        else
        {
            key = ResolveUnqualified(frame, norm, column.Name, start);
        }

        _occurrences.Add(new Occurrence(start, end, key, IsDefinition: false));
        return key;
    }

    private string ResolveUnqualified(Frame frame, string norm, string name, int start)
    {
        for (var current = frame; current is not null; current = current.Parent)
        {
            if (current.Sources.Count == 0) continue;
            var matches = current.Sources.Where(relation => ProvidesColumn(relation, norm)).ToList();
            var unknown = current.Sources.Count(relation => !relation.HasKnownColumns);
            if (matches.Count > 1)
            {
                var keyName = $"A|{start}";
                _identities[keyName] = new IdentityInfo
                {
                    Name = name,
                    Status = SqlColumnResolutionStatus.Ambiguous,
                    Candidates = matches.Select(relation => relation.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                };
                return keyName;
            }
            if (matches.Count == 1)
                return unknown == 0
                    ? ResolveInRelation(matches[0], norm, name) ?? Unresolved(name, start)
                    : Unresolved(name, start);
            if (unknown > 0)
                return unknown == 1 && current.Sources.Count == 1
                    ? ResolveInRelation(current.Sources[0], norm, name) ?? Unresolved(name, start)
                    : Unresolved(name, start);
        }
        return Unresolved(name, start);
    }

    private static bool ProvidesColumn(Relation relation, string norm)
    {
        if (relation.Physical is not null)
            return relation.PhysicalColumns?.Any(column => string.Equals(Normalize(column, false), norm, StringComparison.Ordinal)) == true;
        return relation.Columns?.Any(column => column is not null && column.Norm == norm) == true;
    }

    private string? ResolveInRelation(Relation relation, string norm, string name)
    {
        if (relation.Physical is { } physical)
        {
            // An opaque or unfinished source has no catalog column to point at.
            if (physical.Table.Length == 0) return null;
            var columnName = name;
            string? type = null;
            if (relation.PhysicalColumns is not null)
            {
                var index = -1;
                for (var i = 0; i < relation.PhysicalColumns.Count && index < 0; i++)
                    if (string.Equals(Normalize(relation.PhysicalColumns[i], false), norm, StringComparison.Ordinal)) index = i;
                if (index < 0) return null;
                columnName = relation.PhysicalColumns[index];
                type = relation.PhysicalTypes is { } types && index < types.Count ? types[index] : null;
            }
            var catalog = new SqlCatalogColumn(physical.Database, physical.Schema, physical.Table, columnName);
            var key = PhysicalKey(catalog);
            if (!string.IsNullOrEmpty(type)) _physicalTypes[key] = type;
            if (!_identities.ContainsKey(key))
            {
                _identities[key] = new IdentityInfo
                {
                    Name = columnName,
                    RelationKind = SqlColumnRelationKind.Table,
                    Relation = physical.Table,
                    Catalog = catalog,
                };
            }
            return key;
        }

        var projected = relation.Columns?.FirstOrDefault(column => column is not null && column.Norm == norm);
        return projected?.Key;
    }

    /// <summary>Returns the projected columns that one select item contributes.</summary>
    private IEnumerable<ProjectedColumn?> ProjectItem(SelectStatement select, int itemIndex, string? key, Frame frame)
    {
        var item = select.SelectList[itemIndex];
        var star = item.Expression switch
        {
            StarExpression s => (true, s.Qualifier),
            ColumnReference { Name: "*" } c => (true, c.Qualifier),
            _ => (false, (string?)null),
        };
        if (star.Item1)
        {
            foreach (var column in ExpandStar(item, star.Item2, frame)) yield return column;
            yield break;
        }

        var origin = key is not null && _identities.TryGetValue(key, out var info) ? info.Catalog ?? info.Origin : null;
        if (item.Alias is not null)
        {
            var aliasIndex = FindAliasToken(select, itemIndex, item.Alias);
            if (aliasIndex < 0)
            {
                yield return null;
                yield break;
            }
            var token = _tokens[aliasIndex];
            var start = token.Span.Position.Absolute;
            var norm = Normalize(item.Alias, item.AliasQuote is not null);
            var projected = new ProjectedColumn(item.Alias, norm, LocalKey(start, norm), start, start + token.Span.Length, origin);
            frame.OutputAliases[norm] = projected;
            var aliasKey = OutputAliasKey(projected);
            if (_identities.TryAdd(aliasKey, new IdentityInfo
                {
                    Name = item.Alias,
                    RelationKind = SqlColumnRelationKind.OutputAlias,
                    Definition = (projected.DefStart, projected.DefEnd),
                    Origin = origin,
                }))
                _occurrences.Add(new Occurrence(projected.DefStart, projected.DefEnd, aliasKey, IsDefinition: true));
            yield return projected;
            yield break;
        }

        if (item.Expression is ColumnReference reference)
        {
            var nameIndex = FindColumnNameToken(reference);
            if (nameIndex < 0)
            {
                yield return null;
                yield break;
            }
            var token = _tokens[nameIndex];
            var start = token.Span.Position.Absolute;
            var norm = Normalize(reference.Name, reference.NameQuote is not null);
            yield return new ProjectedColumn(reference.Name, norm, LocalKey(start, norm), start, start + token.Span.Length, origin);
            yield break;
        }

        // An unnamed expression keeps its position for explicit CTE column lists.
        yield return null;
    }

    private IEnumerable<ProjectedColumn?> ExpandStar(SelectItem item, string? qualifier, Frame frame)
    {
        var starIndex = IndexAtOrAfter(item.Position.Absolute);
        while (starIndex >= 0 && starIndex < _tokens.Length && _tokens[starIndex].Kind != NzToken.Multiply) starIndex++;
        if (starIndex < 0 || starIndex >= _tokens.Length) yield break;
        var starToken = _tokens[starIndex];
        var start = starToken.Span.Position.Absolute;
        var end = start + starToken.Span.Length;

        var sources = qualifier is null
            ? frame.Sources
            : frame.Sources.Where(relation => relation.ExposedNorm == Normalize(qualifier, false)).ToList();
        foreach (var relation in sources)
        {
            if (relation.Physical is { } physical)
            {
                if (relation.PhysicalColumns is null) continue;
                for (var i = 0; i < relation.PhysicalColumns.Count; i++)
                {
                    var column = relation.PhysicalColumns[i];
                    var norm = Normalize(column, false);
                    var origin = new SqlCatalogColumn(physical.Database, physical.Schema, physical.Table, column);
                    if (relation.PhysicalTypes is { } types && i < types.Count && !string.IsNullOrEmpty(types[i]))
                        _physicalTypes[PhysicalKey(origin)] = types[i]!;
                    yield return new ProjectedColumn(column, norm, $"L|{start}|{relation.ExposedNorm}|{norm}", start, end, origin);
                }
                continue;
            }
            if (relation.Columns is null) continue;
            foreach (var column in relation.Columns)
            {
                if (column is null) continue;
                yield return column with { Key = $"L|{start}|{relation.ExposedNorm}|{column.Norm}", DefStart = start, DefEnd = end };
            }
        }
    }

    /// <summary>Records the local definition of every named projected column of a relation.</summary>
    private void RegisterLocalColumns(Relation relation)
    {
        if (relation.Columns is null) return;
        foreach (var column in relation.Columns)
        {
            if (column is null) continue;
            if (_identities.TryGetValue(column.Key, out var existing))
            {
                // An aliased select item has one identity: when its select
                // defines a relation, the alias (and the select's own ORDER BY
                // references to it) is that relation's column.
                if (existing.RelationKind == SqlColumnRelationKind.OutputAlias)
                    _identities[column.Key] = new IdentityInfo
                    {
                        Name = existing.Name,
                        RelationKind = relation.Kind,
                        Relation = relation.Name,
                        Definition = existing.Definition,
                        Origin = existing.Origin,
                    };
                continue;
            }
            _identities[column.Key] = new IdentityInfo
            {
                Name = column.Name,
                RelationKind = relation.Kind,
                Relation = relation.Name,
                Definition = (column.DefStart, column.DefEnd),
                Origin = column.Origin,
            };
            _occurrences.Add(new Occurrence(column.DefStart, column.DefEnd, column.Key, IsDefinition: true));
        }
    }

    private static Relation? FindRelation(Frame frame, string qualifierNorm)
    {
        for (var current = frame; current is not null; current = current.Parent)
        {
            var relation = current.Sources.LastOrDefault(candidate => candidate.ExposedNorm == qualifierNorm);
            if (relation is not null) return relation;
        }
        return null;
    }

    private static Relation? FindCte(Frame frame, string norm)
    {
        for (var current = frame; current is not null; current = current.Parent)
            if (current.Ctes.TryGetValue(norm, out var cte)) return cte;
        return null;
    }

    private string Unresolved(string name, int start)
    {
        var key = $"U|{start}";
        _identities[key] = new IdentityInfo { Name = name, Status = SqlColumnResolutionStatus.Unresolved };
        return key;
    }

    private static string LocalKey(int definitionStart, string norm) => $"L|{definitionStart}|{norm}";

    /// <summary>An output alias shares the identity of the projected column it names.</summary>
    private static string OutputAliasKey(ProjectedColumn alias) => alias.Key;

    private static string PhysicalKey(SqlCatalogColumn column) =>
        $"P|{column.Database?.ToUpperInvariant()}|{column.Schema?.ToUpperInvariant()}|{column.Relation.ToUpperInvariant()}|{column.Column.ToUpperInvariant()}";

    /// <summary>Unquoted identifiers fold to upper case; quoted identifiers keep their spelling.</summary>
    private static string Normalize(string name, bool quoted) => quoted ? name : name.ToUpperInvariant();

    private int FindColumnNameToken(ColumnReference column)
    {
        var index = IndexAtOrAfter(column.Position.Absolute);
        if (index < 0) return -1;
        if (column.Qualifier is not null)
        {
            // Skip `qualifier .` to reach the column name.
            for (var i = index; i < _tokens.Length && i < index + 6; i++)
            {
                if (_tokens[i].Kind != NzToken.Dot) continue;
                index = i + 1;
                break;
            }
        }
        for (var i = index; i < _tokens.Length && i < index + 4; i++)
            if (string.Equals(_tokens[i].ToIdentifierText(), column.Name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>Finds the alias token of a select item: the last matching identifier before the item ends.</summary>
    private int FindAliasToken(SelectStatement select, int itemIndex, string alias)
    {
        var start = IndexAtOrAfter(select.SelectList[itemIndex].Position.Absolute);
        if (start < 0) return -1;
        int end;
        if (itemIndex + 1 < select.SelectList.Count)
        {
            end = IndexAtOrAfter(select.SelectList[itemIndex + 1].Position.Absolute) - 1;
        }
        else
        {
            end = start;
            var depth = 0;
            for (; end < _tokens.Length; end++)
            {
                var kind = _tokens[end].Kind;
                if (kind == NzToken.LParen) depth++;
                else if (kind == NzToken.RParen)
                {
                    if (depth == 0) break;
                    depth--;
                }
                else if (depth == 0 && kind is NzToken.From or NzToken.Into or NzToken.Where or NzToken.GroupBy
                             or NzToken.OrderBy or NzToken.Having or NzToken.Limit or NzToken.Union or NzToken.Intersect
                             or NzToken.Except or NzToken.Minus or NzToken.Semicolon or NzToken.Offset or NzToken.Fetch
                             or NzToken.Distribute)
                    break;
            }
        }
        for (var i = Math.Min(end, _tokens.Length) - 1; i >= start; i--)
            if (string.Equals(_tokens[i].ToIdentifierText(), alias, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private int IndexAtOrAfter(int absolute)
    {
        var index = Array.BinarySearch(_tokenStarts, absolute);
        if (index < 0) index = ~index;
        return index < _tokens.Length ? index : -1;
    }
}
