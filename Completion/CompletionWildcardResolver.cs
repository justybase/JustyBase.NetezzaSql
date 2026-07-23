using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Completion;

/// <summary>
/// Wildcard column expansion: alias.* → snippet of qualified columns.
/// Supports aliases, CTEs, subqueries, schema-qualified tables, and db..table paths.
/// </summary>
public sealed class CompletionWildcardResolver
{
    private readonly ISchemaProvider? _schema;

    public CompletionWildcardResolver(ISchemaProvider? schema = null)
    {
        _schema = schema;
    }

    public CompletionItem? TryResolveWildcardSnippet(
        string sql,
        int cursorPosition,
        TokenScopeCollector? scopeCollector,
        ScopeBuilder? astScope,
        Token<NzToken>[]? tokens)
    {
        if (cursorPosition <= 0 || cursorPosition > sql.Length) return null;
        if (cursorPosition < 2 || sql[cursorPosition - 1] != '*' || sql[cursorPosition - 2] != '.')
            return null;

        var before = sql[..cursorPosition];
        var qualifier = ExtractQualifierBeforeDot(before, before.Length - 2);
        if (string.IsNullOrEmpty(qualifier)) return null;

        var columns = ResolveColumns(qualifier, cursorPosition, scopeCollector, astScope, tokens);
        if (columns.Count == 0) return null;

        var displayQualifier = qualifier.Contains('.') ? qualifier.Split('.')[^1] : qualifier;
        var snippet = string.Join(", ", columns.Select(c => $"{displayQualifier}.{c}"));
        return new CompletionItem(snippet, CompletionKind.Snippet,
            Detail: $"Expand {qualifier}.* ({columns.Count} columns)", Priority: 100);
    }

    private static string ExtractQualifierBeforeDot(string before, int dotIndex)
    {
        int end = dotIndex;
        var segments = new List<string>();

        while (end > 0)
        {
            int start = end - 1;
            while (start >= 0)
            {
                var ch = before[start];
                if (char.IsLetterOrDigit(ch) || ch is '_' or '"' or ']') { start--; continue; }
                break;
            }
            start++;

            if (start >= end) break;
            segments.Insert(0, before[start..end].Trim('"', '[', ']'));

            end = start;
            if (end > 0 && before[end - 1] == '.')
            {
                end--;
                if (end > 0 && before[end - 1] == '.')
                    end--;
                continue;
            }
            break;
        }

        return segments.Count == 0 ? string.Empty : string.Join('.', segments);
    }

    private List<string> ResolveColumns(
        string qualifier,
        int cursorPosition,
        TokenScopeCollector? scopeCollector,
        ScopeBuilder? astScope,
        Token<NzToken>[]? tokens)
    {
        var result = new List<string>();
        var path = CompletionAliasResolver.ParseQualifierPath(qualifier);

        // AST scope: alias, CTE, or subquery alias
        if (astScope is not null)
        {
            foreach (var key in new[] { path.Name, qualifier })
            {
                var table = astScope.FindTable(key);
                if (table?.Columns is { Count: > 0 })
                {
                    result.AddRange(table.Columns.Select(c => c.Name));
                    return result;
                }
            }

            foreach (var visible in astScope.GetAllVisibleTables())
            {
                if (!MatchesQualifier(visible, path, qualifier)) continue;
                if (visible.Columns is { Count: > 0 })
                {
                    result.AddRange(visible.Columns.Select(c => c.Name));
                    return result;
                }
            }
        }

        // Token-based CTE / temp table scope
        if (scopeCollector is not null)
        {
            foreach (var key in new[] { path.Name, qualifier })
            {
                var scopedCols = scopeCollector.GetCteColumns(key, cursorPosition);
                if (scopedCols is { Count: > 0 })
                {
                    result.AddRange(scopedCols);
                    return result;
                }
            }
        }

        // Resolve alias → table path from tokens, then schema lookup
        if (tokens is not null)
        {
            var tablePath = CompletionAliasResolver.ResolveTablePath(tokens, path.Name)
                            ?? CompletionAliasResolver.ResolveTablePath(tokens, qualifier);
            if (tablePath is not null && TrySchemaColumns(tablePath.Value, result))
                return result;

            var resolvedName = CompletionAliasResolver.ResolveAlias(tokens, path.Name)
                               ?? CompletionAliasResolver.ResolveAlias(tokens, qualifier);
            if (resolvedName is not null)
            {
                if (scopeCollector?.GetCteColumns(resolvedName, cursorPosition) is { Count: > 0 } cteCols)
                {
                    result.AddRange(cteCols);
                    return result;
                }

                var resolvedPath = CompletionAliasResolver.ResolveTablePath(tokens, qualifier)
                                   ?? CompletionAliasResolver.ResolveTablePath(tokens, resolvedName);
                if (resolvedPath is not null && TrySchemaColumns(resolvedPath.Value, result))
                    return result;

                if (_schema?.GetTable(null, null, resolvedName)?.Columns is { } aliasCols)
                {
                    result.AddRange(aliasCols.Select(c => c.Name));
                    return result;
                }
            }
        }

        // Direct schema lookup (qualified or unqualified)
        if (TrySchemaColumns(path, result))
            return result;

        if (_schema is not null && path.Schema is null && path.Database is null)
        {
            var table = _schema.GetTable(null, null, path.Name);
            if (table?.Columns is not null)
                result.AddRange(table.Columns.Select(c => c.Name));
        }

        return result;
    }

    private bool TrySchemaColumns(CompletionAliasResolver.TablePath path, List<string> result)
    {
        if (_schema is null) return false;

        var table = _schema.GetTable(path.Database, path.Schema, path.Name);
        if (table?.Columns is not { Count: > 0 }) return false;

        result.AddRange(table.Columns.Select(c => c.Name));
        return true;
    }

    private static bool MatchesQualifier(TableInfo table, CompletionAliasResolver.TablePath path, string qualifier)
    {
        if (string.Equals(table.Alias, path.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(table.Alias, qualifier, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(table.Name, path.Name, StringComparison.OrdinalIgnoreCase))
        {
            if (path.Schema is null || string.Equals(table.Schema, path.Schema, StringComparison.OrdinalIgnoreCase))
                return path.Database is null || string.Equals(table.Database, path.Database, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
