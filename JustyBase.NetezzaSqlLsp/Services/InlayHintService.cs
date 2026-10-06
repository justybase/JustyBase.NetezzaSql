using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;
using LspRange = JustyBase.NetezzaSqlLsp.Protocol.Range;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>
/// Produces type inlay hints for projected columns of a parsed SELECT statement.
/// Hints require a schema provider with column data types; anything unresolved is skipped.
/// </summary>
public static class InlayHintService
{
    /// <summary>Returns type inlay hints for every SELECT statement intersecting the requested range.</summary>
    public static InlayHint[] GetInlayHints(string sql, ISchemaProvider? schema, SqlDialect dialect, LspRange? requestedRange = null)
    {
        if (string.IsNullOrEmpty(sql) || schema is null)
            return Array.Empty<InlayHint>();

        try
        {
            var lineStarts = LspTextUtilities.ComputeLineStarts(sql);
            var hints = new List<InlayHint>();
            var statementIndex = StatementIndexBuilder.BuildIndex(sql);
            foreach (var boundary in statementIndex.Statements)
            {
                var tokens = DialectRuntime.Tokenize(boundary.Sql, dialect).ToArray();
                var parser = DialectRuntime.CreateParser(tokens, dialect);
                if (parser.Parse() is not SelectStatement select || parser.Errors.Count > 0)
                    continue;

                if (select.From is null || select.From.Count == 0)
                    continue;

                var scope = BuildTableScope(select, schema);
                if (scope.Count == 0)
                    continue;

                foreach (var item in select.SelectList)
                {
                    if (item.Expression is not ColumnReference column)
                        continue;

                    var columnInfo = ResolveColumn(column, scope);
                    if (columnInfo?.DataType is not { Length: > 0 })
                        continue;

                    var end = boundary.StartOffset + ColumnEndOffset(column);
                    var position = LspTextUtilities.OffsetToPosition(end, lineStarts);
                    if (requestedRange is not null && !Contains(requestedRange, position))
                        continue;

                    hints.Add(new InlayHint(position, ": " + columnInfo.DataType, InlayHintKind.Type));
                }
            }

            return hints.ToArray();
        }
        catch
        {
            // Inlay hints are best-effort and must never break a request.
            return Array.Empty<InlayHint>();
        }
    }

    private static Dictionary<string, TableInfo> BuildTableScope(SelectStatement select, ISchemaProvider schema)
    {
        var scope = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in select.From!)
        {
            AddSource(reference.Source, scope, schema);
            if (reference.Joins is null)
                continue;

            foreach (var join in reference.Joins)
                AddSource(join.Source, scope, schema);
        }
        return scope;
    }

    private static void AddSource(TableSource source, Dictionary<string, TableInfo> scope, ISchemaProvider schema)
    {
        if (source.Table is null)
            return;

        var info = schema.GetTable(source.Table.Database, source.Table.Schema, source.Table.Name);
        if (info is null)
            return;

        var key = source.Alias ?? source.Table.Name;
        if (!string.IsNullOrEmpty(key))
            scope.TryAdd(key, info);
    }

    private static ColumnInfo? ResolveColumn(ColumnReference column, Dictionary<string, TableInfo> scope)
    {
        if (!string.IsNullOrEmpty(column.Qualifier))
        {
            return scope.TryGetValue(column.Qualifier, out var qualified)
                ? FindColumn(qualified, column.Name)
                : null;
        }

        ColumnInfo? match = null;
        foreach (var table in scope.Values)
        {
            var candidate = FindColumn(table, column.Name);
            if (candidate is null)
                continue;

            if (match is not null)
                return null; // ambiguous unqualified column

            match = candidate;
        }
        return match;
    }

    private static ColumnInfo? FindColumn(TableInfo table, string name) =>
        table.Columns?.FirstOrDefault(column => column.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static int ColumnEndOffset(ColumnReference column)
    {
        var length = 0;
        if (!string.IsNullOrEmpty(column.Qualifier))
        {
            length += column.Qualifier.Length + 1;
            if (column.QualifierQuote is not null)
                length += 2;
        }

        length += column.Name.Length;
        if (column.NameQuote is not null)
            length += 2;

        return column.Position.Absolute + length;
    }

    private static bool Contains(LspRange range, Position position)
    {
        if (position.Line < range.Start.Line || position.Line > range.End.Line)
            return false;
        if (position.Line == range.Start.Line && position.Character < range.Start.Character)
            return false;
        if (position.Line == range.End.Line && position.Character > range.End.Character)
            return false;
        return true;
    }
}
