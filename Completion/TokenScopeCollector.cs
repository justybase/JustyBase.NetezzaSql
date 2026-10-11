using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Completion;

/// <summary>
/// Position-aware scope collector for CTE/alias/temp-table resolution.
/// Mirrors <c>ParserSqlContextCollector._scopes</c> from the reference Node project.
///
/// Each scope records <c>Start</c>/<c>End</c> (absolute text positions) and
/// <c>Bindings</c> (name → columns). CTE definitions register their name in
/// the PARENT scope (the WITH statement scope), preventing them from leaking
/// past the WITH statement boundary. Temp tables are stored in a separate
/// global scope and are always visible regardless of cursor position.
/// 
/// Given a cursor position, only scopes containing that position contribute
/// their bindings — CTEs defined before a <c>;</c> are naturally excluded
/// because their WITH scope ends at the <c>;</c> boundary.
/// </summary>
public class TokenScopeCollector
{
    private readonly ISchemaProvider? _schema;
    private readonly SqlDialect _dialect;
    private readonly List<ScopeEntry> _scopes = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _globalEntries = new(StringComparer.OrdinalIgnoreCase);
    private int _sqlLength;

    private record ScopeEntry(int Start, int End, Dictionary<string, IReadOnlyList<string>> Bindings, bool OpenEnded);

    public TokenScopeCollector(
        ISchemaProvider? schema,
        SqlDialect dialect = SqlDialect.Netezza)
    {
        _schema = schema;
        _dialect = dialect;
    }

    public bool HasAny() => _scopes.Count > 0 || _globalEntries.Count > 0;

    /// <summary>
    /// Walk tokens to build scopes and collect CTE/temp-table column info.
    /// Must be called before any query methods.
    /// </summary>
    public void Collect(Token<NzToken>[] tokens, int sqlLength, int? cursorPosition = null)
    {
        _scopes.Clear();
        _globalEntries.Clear();
        _sqlLength = sqlLength;

        // First pass: find WITH + CREATE TEMP TABLE patterns and collect names/positions
        var cteEntries = new List<(string Name, int TokenIndex, bool IsCte)>();

        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Kind == NzToken.With)
            {
                // Determine the scope of this WITH clause: from WITH to the end of
                // the main SELECT/INSERT/UPDATE/DELETE (track balanced parens past body)
                int scopeStart = tokens[i].Span.Position.Absolute;
                int scopeEnd = FindWithScopeEnd(tokens, i);

                var scopeBindings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

                // Register CTE names in this scope
                int j = i + 1;
                if (j < tokens.Length && tokens[j].Kind == NzToken.Recursive)
                    j++;

                while (j < tokens.Length)
                {
                    if (tokens[j].Kind.IsIdentifierLike())
                    {
                        var cteName = tokens[j].ToIdentifierText();
                        int cteNamePos = j;

                        // Extract columns for this CTE
                        var columns = ExtractCteColumnsForEntry(tokens, cteNamePos);
                        var starCols = ResolveStarFromBody(tokens, cteNamePos, scopeBindings);
                        if (starCols.Count > 0)
                        {
                            var existing = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
                            foreach (var sc in starCols)
                                if (existing.Add(sc)) columns.Add(sc);
                        }

                        // Store in the WITH scope bindings (not the CTE's own scope)
                        scopeBindings[cteName] = columns;

                        // Skip past CTE definition
                        j++;
                        if (j < tokens.Length && tokens[j].Kind == NzToken.LParen
                            && IsColumnListStart(tokens, j))
                            SkipBalancedParens(tokens, ref j);
                        if (j < tokens.Length && tokens[j].Kind == NzToken.As)
                        {
                            j++;
                            SkipBalancedParens(tokens, ref j);
                            if (j < tokens.Length && tokens[j].Kind == NzToken.Comma)
                                j++;
                            else
                                break;
                        }
                        else break;
                    }
                    else j++;
                }

                _scopes.Add(new ScopeEntry(scopeStart, scopeEnd, scopeBindings, false));
            }
            else if (tokens[i].Kind == NzToken.Create && StatementEnd(tokens, i, sqlLength) <= (cursorPosition ?? sqlLength))
            {
                int j = i + 1;
                bool isTemp = false;
                if (j < tokens.Length && (tokens[j].Kind == NzToken.Temp || tokens[j].Kind == NzToken.Temporary))
                { isTemp = true; j++; }
                if (j >= tokens.Length || tokens[j].Kind != NzToken.Table) continue;
                j++;
                if (j >= tokens.Length || !tokens[j].Kind.IsIdentifierLike()) continue;

                var tableName = tokens[j].ToIdentifierText();
                if (!isTemp && !IsCreateTableAsSelect(tokens, j))
                    continue;
                var columns = ExtractCreateTableColumns(tokens, j);
                var starCols = ResolveStarFromBody(tokens, j);
                if (starCols.Count > 0)
                {
                    var existing = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
                    foreach (var sc in starCols)
                        if (existing.Add(sc)) columns.Add(sc);
                }
                // Script effects become visible after their statement ends.
                _globalEntries[tableName] = columns;
            }
            else if (tokens[i].Kind == NzToken.Drop && StatementEnd(tokens, i, sqlLength) <= (cursorPosition ?? sqlLength))
            {
                int j = i + 1;
                if (j >= tokens.Length || tokens[j].Kind != NzToken.Table) continue;
                j++;
                if (j < tokens.Length && tokens[j].Kind == NzToken.If)
                {
                    j++;
                    if (j < tokens.Length && tokens[j].Kind == NzToken.Exists) j++;
                }
                if (j >= tokens.Length || !tokens[j].Kind.IsIdentifierLike()) continue;
                _globalEntries.Remove(tokens[j].ToIdentifierText());
            }
        }

        // Cross-CTE dependencies within one WITH list are resolved during the
        // entry walk above: earlier CTE names are already present in the
        // in-progress bindings when a later CTE's `*` is expanded.
    }

    /// <summary>
    /// Get columns for a CTE/temp-table name at the given cursor position.
    /// CTEs are only returned if their scope contains the cursor.
    /// Temp tables are always returned regardless of cursor position.
    /// </summary>
    public IReadOnlyList<string>? GetCteColumns(string name, int cursorPos)
    {
        // Check scoped entries (CTEs — only if cursor is inside their scope)
        foreach (var scope in _scopes.OrderByDescending(scope => scope.Start))
        {
            bool cursorInScope = scope.OpenEnded
                ? cursorPos >= scope.Start
                : cursorPos >= scope.Start && cursorPos <= scope.End;
            if (cursorInScope)
            {
                if (scope.Bindings.TryGetValue(name, out var cols))
                    return cols;
            }
        }

        return _globalEntries.TryGetValue(name, out var globalCols) ? globalCols : null;
    }

    /// <summary>
    /// Returns script-local temp table names created anywhere in the script.
    /// </summary>
    public IEnumerable<string> GetTempTableNames() => _globalEntries.Keys;

    /// <summary>
    /// Returns CTE names that are visible at the given cursor position.
    /// </summary>
    public IEnumerable<string> GetCteNamesInScope(int cursorPos)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scope in _scopes)
        {
            bool cursorInScope = scope.OpenEnded
                ? cursorPos >= scope.Start
                : cursorPos >= scope.Start && cursorPos <= scope.End;
            if (cursorInScope)
            {
                foreach (var name in scope.Bindings.Keys)
                {
                    if (seen.Add(name))
                        yield return name;
                }
            }
        }

        // Also yield temp tables (always visible)
        foreach (var name in _globalEntries.Keys)
        {
            if (seen.Add(name))
                yield return name;
        }
    }

    /// <summary>
    /// Resolve an alias/name to its underlying table name.
    /// </summary>
    public string? FindTable(string name, int cursorPos)
    {
        foreach (var scope in _scopes)
        {
            bool cursorInScope = scope.OpenEnded
                ? cursorPos >= scope.Start
                : cursorPos >= scope.Start && cursorPos <= scope.End;
            if (cursorInScope)
            {
                if (scope.Bindings.ContainsKey(name))
                    return name;
            }
        }
        return null;
    }

    /// <summary>
    /// Find the end position of a WITH clause scope.
    /// Ends at: ; or EOF; or at a statement boundary keyword (SELECT/INSERT/etc
    /// at depth 0) that is NOT part of the WITH's main query.
    /// Simple heuristic: find the matching end of the WITH's statement.
    /// </summary>
    private static int StatementEnd(Token<NzToken>[] tokens, int start, int sqlLength)
    {
        int depth = 0;
        for (int i = start; i < tokens.Length; i++)
        {
            if (tokens[i].Kind == NzToken.LParen) depth++;
            else if (tokens[i].Kind == NzToken.RParen) depth--;
            else if (depth == 0 && tokens[i].Kind == NzToken.Semicolon)
                return tokens[i].Span.Position.Absolute + tokens[i].Span.Length;
        }
        return sqlLength;
    }

    private static int FindWithScopeEnd(Token<NzToken>[] tokens, int withIndex)
    {
        // Find the SELECT that follows the WITH clause's CTE bodies
        int depth = 0;
        int lastSelect = withIndex;

        for (int i = withIndex + 1; i < tokens.Length; i++)
        {
            var k = tokens[i].Kind;
            if (k == NzToken.LParen) depth++;
            if (k == NzToken.RParen) depth--;
            if (depth < 0) return tokens[i].Span.Position.Absolute;

            if (depth == 0 && k == NzToken.Semicolon)
                return tokens[i].Span.Position.Absolute;
        }

        // Fallback: include trailing space after last token
        return tokens.Length > 0
            ? tokens[^1].Span.Position.Absolute + tokens[^1].Span.Length
            : 0;
    }

    // ====== Column extraction helpers ======

    private static bool IsCreateTableAsSelect(Token<NzToken>[] tokens, int nameIndex)
    {
        int depth = 0;
        for (int i = nameIndex + 1; i < tokens.Length; i++)
        {
            var kind = tokens[i].Kind;
            if (kind == NzToken.LParen)
            {
                depth++;
            }
            else if (kind == NzToken.RParen)
            {
                if (depth == 0) return false;
                depth--;
            }
            else if (depth == 0 && (kind == NzToken.Semicolon || kind == NzToken.Create || kind == NzToken.Drop))
            {
                return false;
            }
            else if (depth == 0 && kind == NzToken.As)
            {
                int next = i + 1;
                return next < tokens.Length
                    && (tokens[next].Kind == NzToken.Select || tokens[next].Kind == NzToken.With);
            }
        }
        return false;
    }

    /// <summary>
    /// Columns of <c>CREATE [TEMP] TABLE name ...</c>, bounded to that statement:
    /// column definitions, an explicit CTAS column list, <c>AS (SELECT ...)</c>
    /// or <c>AS SELECT ...</c>. Never reads tokens of a later statement.
    /// </summary>
    private static List<string> ExtractCreateTableColumns(Token<NzToken>[] tokens, int nameIndex)
    {
        var end = nameIndex + 1;
        var depth = 0;
        for (; end < tokens.Length; end++)
        {
            if (tokens[end].Kind == NzToken.LParen) depth++;
            else if (tokens[end].Kind == NzToken.RParen) depth--;
            else if (depth == 0 && tokens[end].Kind == NzToken.Semicolon) break;
        }

        var j = nameIndex + 1;
        // Skip the rest of a qualified name (db.schema.name).
        while (j + 1 < end && tokens[j].Kind == NzToken.Dot && tokens[j + 1].Kind.IsIdentifierLike()) j += 2;

        var listed = new List<string>();
        if (j < end && tokens[j].Kind == NzToken.LParen)
        {
            // Column definitions or an explicit CTAS column list: the first
            // identifier of each top-level comma-separated item.
            var itemStart = true;
            depth = 0;
            for (j++; j < end; j++)
            {
                var kind = tokens[j].Kind;
                if (kind == NzToken.LParen) { depth++; continue; }
                if (kind == NzToken.RParen)
                {
                    if (depth == 0) { j++; break; }
                    depth--;
                    continue;
                }
                if (depth > 0) continue;
                if (kind == NzToken.Comma) { itemStart = true; continue; }
                if (itemStart && kind.IsIdentifierLike()) listed.Add(tokens[j].ToIdentifierText());
                itemStart = false;
            }
        }

        while (j < end && tokens[j].Kind != NzToken.As) j++;
        if (j >= end) return listed;
        j++;
        if (listed.Count > 0) return listed;
        if (j < end && tokens[j].Kind == NzToken.LParen)
            return ExtractCteColumnsForEntry(tokens[..end], nameIndex);

        // AS SELECT ... : the projection of the top-level SELECT.
        depth = 0;
        for (; j < end; j++)
        {
            if (tokens[j].Kind == NzToken.LParen) depth++;
            else if (tokens[j].Kind == NzToken.RParen) depth--;
            else if (depth == 0 && tokens[j].Kind == NzToken.Select)
                return ExtractSelectColumnNames(tokens, j + 1, end);
        }
        return new List<string>();
    }

    private static List<string> ExtractCteColumnsForEntry(Token<NzToken>[] tokens, int cteNameIndex)
    {
        int j = cteNameIndex + 1;
        if (j >= tokens.Length) return new List<string>();

        // Try explicit column list: cte (col1, col2) AS
        if (tokens[j].Kind == NzToken.LParen && IsColumnListStart(tokens, j))
        {
            var cols = new List<string>();
            j++;
            bool valid = true;
            while (j < tokens.Length && valid)
            {
                if (tokens[j].Kind.IsIdentifierLike())
                {
                    cols.Add(tokens[j].ToIdentifierText());
                    j++;
                    if (j < tokens.Length && tokens[j].Kind == NzToken.Comma) j++;
                    else if (j < tokens.Length && tokens[j].Kind == NzToken.RParen) return cols;
                    else valid = false;
                }
                else valid = false;
            }
        }

        // Infer columns from SELECT list inside the CTE body
        j = cteNameIndex + 1;
        while (j < tokens.Length && tokens[j].Kind != NzToken.As) j++;
        if (j >= tokens.Length) return new List<string>();
        j++;
        if (j < tokens.Length && tokens[j].Kind == NzToken.All) j++;

        while (j < tokens.Length && tokens[j].Kind != NzToken.LParen) j++;
        if (j >= tokens.Length) return new List<string>();

        int bodyStart = j, depth = 1;
        j++;
        while (j < tokens.Length && depth > 0)
        {
            if (tokens[j].Kind == NzToken.LParen) depth++;
            else if (tokens[j].Kind == NzToken.RParen) depth--;
            j++;
        }
        int bodyEnd = j;

        int selectStart = -1;
        for (int k = bodyStart + 1; k < bodyEnd && k < tokens.Length; k++)
        {
            if (tokens[k].Kind == NzToken.With)
            {
                int wk = k + 1;
                if (wk < bodyEnd && tokens[wk].Kind == NzToken.Recursive) wk++;
                while (wk < bodyEnd && wk < tokens.Length)
                {
                    if (tokens[wk].Kind.IsIdentifierLike())
                    {
                        wk++;
                        if (wk < bodyEnd && tokens[wk].Kind == NzToken.LParen && IsColumnListStart(tokens, wk))
                            SkipBalancedParens(tokens, ref wk);
                        if (wk < bodyEnd && tokens[wk].Kind == NzToken.As)
                        {
                            wk++;
                            SkipBalancedParens(tokens, ref wk);
                            if (wk < bodyEnd && tokens[wk].Kind == NzToken.Comma) wk++;
                            else break;
                        }
                        else break;
                    }
                    else wk++;
                }
                k = wk - 1;
                continue;
            }
            if (tokens[k].Kind == NzToken.Select) { selectStart = k + 1; break; }
        }

        if (selectStart > 0)
            return ExtractSelectColumnNames(tokens, selectStart, bodyEnd);

        return new List<string>();
    }

    private static List<string> ExtractSelectColumnNames(Token<NzToken>[] tokens, int start, int end)
    {
        var columns = new List<string>();
        int lastItemStart = start;
        var listEnded = false;

        for (int i = start; i < end; i++)
        {
            var k = tokens[i].Kind;
            if (k == NzToken.Comma || k == NzToken.From || k == NzToken.Into
                || k == NzToken.Where || k == NzToken.GroupBy || k == NzToken.Having
                || k == NzToken.OrderBy || k == NzToken.Limit
                || k == NzToken.Union || k == NzToken.Intersect || k == NzToken.Except)
            {
                var colName = ExtractColumnAlias(tokens, lastItemStart, i);
                if (colName is not null) columns.Add(colName);
                lastItemStart = i + 1;
                if (k != NzToken.Comma)
                {
                    // The select list ended at a clause keyword; what follows
                    // (FROM sources, predicates) is not a projected column.
                    listEnded = true;
                    break;
                }
            }
        }

        if (!listEnded && lastItemStart < end)
        {
            var colName = ExtractColumnAlias(tokens, lastItemStart, end);
            if (colName is not null) columns.Add(colName);
        }

        return columns;
    }

    private static string? ExtractColumnAlias(Token<NzToken>[] tokens, int start, int end)
    {
        if (start >= end) return null;
        if (start < end && tokens[start].Kind is NzToken.Distinct or NzToken.All) start++;

        for (int i = end - 1; i > start; i--)
        {
            if (tokens[i].Kind == NzToken.As && i + 1 < end && tokens[i + 1].Kind.IsIdentifierLike())
                return tokens[i + 1].ToIdentifierText();
            if (tokens[i].Kind == NzToken.As) break;
        }

        if (start == end - 1 && tokens[start].Kind.IsIdentifierLike())
            return tokens[start].ToIdentifierText();

        if (end - start >= 2 && tokens[end - 1].Kind.IsIdentifierLike())
        {
            bool hasDot = false, allOk = true;
            for (int i = start; i < end; i++)
            {
                if (tokens[i].Kind == NzToken.Dot) hasDot = true;
                else if (!tokens[i].Kind.IsIdentifierLike()) { allOk = false; break; }
            }
            if (allOk && hasDot)
            {
                for (int i = end - 1; i >= start; i--)
                    if (tokens[i].Kind.IsIdentifierLike()) return tokens[i].ToIdentifierText();
            }
        }
        return null;
    }

private List<string> ResolveStarFromBody(
    Token<NzToken>[] tokens,
    int cteNameIndex,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? siblingBindings = null)
{
    int j = cteNameIndex + 1;
    while (j < tokens.Length && tokens[j].Kind != NzToken.As) j++;
    if (j >= tokens.Length) return new List<string>();
    j++;
    if (j < tokens.Length && tokens[j].Kind == NzToken.All) j++;

    int bodyEnd;
    int scanStart;
    if (j < tokens.Length && tokens[j].Kind is NzToken.Select or NzToken.With)
    {
        // CREATE TABLE ... AS SELECT: the unparenthesized body runs to the
        // end of the statement. The scan starts at the body's first token.
        bodyEnd = j;
        while (bodyEnd < tokens.Length && tokens[bodyEnd].Kind != NzToken.Semicolon) bodyEnd++;
        scanStart = j;
    }
    else
    {
        while (j < tokens.Length && tokens[j].Kind != NzToken.LParen) j++;
        if (j >= tokens.Length) return new List<string>();

        bodyEnd = j + 1;
        var depth = 1;
        while (bodyEnd < tokens.Length && depth > 0)
        {
            if (tokens[bodyEnd].Kind == NzToken.LParen) depth++;
            else if (tokens[bodyEnd].Kind == NzToken.RParen) depth--;
            bodyEnd++;
        }
        scanStart = j + 1;
    }

    // Nested CTEs inside the body resolve before the outer SELECT's star.
    var nestedCtes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
    BuildNestedCtes(tokens, bodyStart: scanStart, bodyEnd, nestedCtes);

    return ResolveStarColumnsInBody(tokens, scanStart, bodyEnd, nestedCtes, siblingBindings);
}

/// <summary>
/// Finds the top-level SELECT of a body and expands its <c>*</c> /
/// <c>T.*</c> projection over every FROM/JOIN source of that query.
/// </summary>
private List<string> ResolveStarColumnsInBody(
    Token<NzToken>[] tokens,
    int scanStart,
    int bodyEnd,
    IReadOnlyDictionary<string, IReadOnlyList<string>> nestedCtes,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? siblingBindings)
{
    for (int k = scanStart; k < bodyEnd && k < tokens.Length; k++)
    {
        if (tokens[k].Kind == NzToken.With)
        {
            int wk = k + 1;
            if (wk < bodyEnd && tokens[wk].Kind == NzToken.Recursive) wk++;
            while (wk < bodyEnd && wk < tokens.Length)
            {
                if (tokens[wk].Kind.IsIdentifierLike())
                {
                    wk++;
                    if (wk < bodyEnd && tokens[wk].Kind == NzToken.LParen && IsColumnListStart(tokens, wk))
                        SkipBalancedParens(tokens, ref wk);
                    if (wk < bodyEnd && tokens[wk].Kind == NzToken.As)
                    {
                        wk++;
                        SkipBalancedParens(tokens, ref wk);
                        if (wk < bodyEnd && tokens[wk].Kind == NzToken.Comma) wk++;
                        else break;
                    }
                    else break;
                }
                else wk++;
            }
            k = wk - 1;
            continue;
        }

        if (tokens[k].Kind != NzToken.Select) continue;

        int afterSelect = k + 1;
        string? starQualifier = null;
        bool hasStar = false;

        if (afterSelect < bodyEnd && tokens[afterSelect].Kind is NzToken.Distinct or NzToken.All)
            afterSelect++;

        int parenDepth = 0;
        for (int s = afterSelect; s < bodyEnd; s++)
        {
            var sk = tokens[s].Kind;
            if (sk == NzToken.LParen) { parenDepth++; continue; }
            if (sk == NzToken.RParen) { parenDepth--; continue; }
            if (parenDepth > 0) continue;

            if (sk is NzToken.From or NzToken.Into or NzToken.Where
                or NzToken.GroupBy or NzToken.Having or NzToken.OrderBy
                or NzToken.Limit or NzToken.Union or NzToken.Intersect or NzToken.Except)
                break;

            if (sk == NzToken.Multiply) { hasStar = true; break; }

            if (sk.IsIdentifierLike() && s + 2 < bodyEnd
                && tokens[s + 1].Kind == NzToken.Dot
                && tokens[s + 2].Kind == NzToken.Multiply)
            {
                hasStar = true;
                starQualifier = tokens[s].ToIdentifierText();
                break;
            }
        }

        if (!hasStar) continue;

        return CollectStarSourceColumns(tokens, afterSelect, bodyEnd, starQualifier,
            nestedCtes, siblingBindings);
    }

    return new List<string>();
}

/// <summary>
/// Columns of every top-level FROM/JOIN source visible to a star projection.
/// <c>T.*</c> restricts the expansion to the matching source, and
/// <c>database..table</c> paths keep their database for metadata lookup.
/// </summary>
private List<string> CollectStarSourceColumns(
    Token<NzToken>[] tokens,
    int selectListStart,
    int end,
    string? qualifier,
    IReadOnlyDictionary<string, IReadOnlyList<string>> nestedCtes,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? siblingBindings)
{
    var columns = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void AddRange(IEnumerable<string> names)
    {
        foreach (var name in names)
            if (seen.Add(name))
                columns.Add(name);
    }

    void AddSourceColumns(CompletionAliasResolver.TablePath path, string? alias)
    {
        if (qualifier is not null
            && !string.Equals(alias, qualifier, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path.Name, qualifier, StringComparison.OrdinalIgnoreCase))
            return;

        if (nestedCtes.TryGetValue(path.Name, out var nestedCols) && nestedCols.Count > 0)
        {
            AddRange(nestedCols);
            return;
        }
        if (siblingBindings is not null
            && siblingBindings.TryGetValue(path.Name, out var siblingCols) && siblingCols.Count > 0)
        {
            AddRange(siblingCols);
            return;
        }
        foreach (var scope in _scopes)
        {
            if (scope.Bindings.TryGetValue(path.Name, out var cteCols) && cteCols.Count > 0)
            {
                AddRange(cteCols);
                return;
            }
        }
        if (_globalEntries.TryGetValue(path.Name, out var globalCols) && globalCols.Count > 0)
        {
            AddRange(globalCols);
            return;
        }

        if (_schema is null) return;
        var info = CompletionSchemaLookup.GetTable(_schema, _dialect, path.Database, path.Schema, path.Name);
        if (info?.Columns is { Count: > 0 })
            AddRange(info.Columns.Select(c => c.Name));
    }

    // Locate the top-level FROM of this query.
    int parenDepth = 0;
    int fromPos = -1;
    for (int i = selectListStart; i < end && i < tokens.Length; i++)
    {
        var kind = tokens[i].Kind;
        if (kind == NzToken.LParen) { parenDepth++; continue; }
        if (kind == NzToken.RParen) { if (parenDepth == 0) break; parenDepth--; continue; }
        if (parenDepth > 0) continue;
        if (kind == NzToken.From) { fromPos = i; break; }
        if (kind is NzToken.Into or NzToken.Where or NzToken.GroupBy or NzToken.Having
            or NzToken.OrderBy or NzToken.Limit or NzToken.Union or NzToken.Intersect
            or NzToken.Except or NzToken.Semicolon)
            break;
    }
    if (fromPos < 0) return columns;

    // Walk FROM/JOIN sources at this query's depth.
    bool expectSource = true;
    bool inCondition = false;
    parenDepth = 0;
    for (int i = fromPos + 1; i < end && i < tokens.Length; i++)
    {
        var kind = tokens[i].Kind;
        if (kind == NzToken.LParen)
        {
            if (parenDepth == 0 && expectSource && !inCondition)
            {
                // Derived table source; its alias is not a relation name.
                SkipBalancedParens(tokens, ref i);
                i--;
                if (i + 1 < end && tokens[i + 1].Kind == NzToken.As) i++;
                if (i + 1 < end && tokens[i + 1].Kind.IsIdentifierLike()) i++;
                expectSource = false;
                continue;
            }
            parenDepth++;
            continue;
        }
        if (kind == NzToken.RParen)
        {
            if (parenDepth == 0) break;
            parenDepth--;
            continue;
        }
        if (parenDepth > 0) continue;

        if (kind is NzToken.Where or NzToken.GroupBy or NzToken.Having or NzToken.OrderBy
            or NzToken.Limit or NzToken.Union or NzToken.Intersect or NzToken.Except
            or NzToken.Semicolon)
            break;
        if (kind is NzToken.From or NzToken.Join)
        {
            expectSource = true;
            inCondition = false;
            continue;
        }
        if (kind == NzToken.Comma)
        {
            expectSource = true;
            inCondition = false;
            continue;
        }
        if (kind is NzToken.On or NzToken.Using)
        {
            inCondition = true;
            expectSource = false;
            continue;
        }

        if (expectSource && !inCondition && kind.IsIdentifierLike())
        {
            var (path, alias, consumed) = CompletionAliasResolver.ParseTablePathAt(tokens, i);
            if (consumed > 0 && path.Name.Length > 0)
                AddSourceColumns(path, alias);
            i += Math.Max(consumed, 1) - 1;
            expectSource = false;
        }
    }

    return columns;
}

/// <summary>
/// Scans a token range for nested WITH definitions and builds their columns.
/// Resolves <c>*</c> against the schema or other CTEs.
/// </summary>
private void BuildNestedCtes(Token<NzToken>[] tokens, int bodyStart, int bodyEnd,
    Dictionary<string, IReadOnlyList<string>> result)
{
    // First pass: register all nested CTE names
    for (int i = bodyStart; i < bodyEnd && i < tokens.Length; i++)
    {
        if (tokens[i].Kind != NzToken.With) continue;
        int j = i + 1;
        if (j < bodyEnd && tokens[j].Kind == NzToken.Recursive) j++;
        while (j < bodyEnd && j < tokens.Length)
        {
            if (tokens[j].Kind.IsIdentifierLike())
            {
                var name = tokens[j].ToIdentifierText();
                if (!result.ContainsKey(name))
                    result[name] = Array.Empty<string>();
                j++;
                if (j < bodyEnd && tokens[j].Kind == NzToken.LParen && IsColumnListStart(tokens, j))
                    SkipBalancedParens(tokens, ref j);
                if (j < bodyEnd && tokens[j].Kind == NzToken.As)
                {
                    j++;
                    SkipBalancedParens(tokens, ref j);
                    if (j < bodyEnd && tokens[j].Kind == NzToken.Comma) j++;
                    else break;
                }
                else break;
            }
            else j++;
        }
    }
    // Second pass: resolve columns (resolve * in nested body context)
    for (int i = bodyStart; i < bodyEnd && i < tokens.Length; i++)
    {
        if (tokens[i].Kind != NzToken.With) continue;
        int j = i + 1;
        if (j < bodyEnd && tokens[j].Kind == NzToken.Recursive) j++;
        while (j < bodyEnd && j < tokens.Length)
        {
            if (tokens[j].Kind.IsIdentifierLike())
            {
                var name = tokens[j].ToIdentifierText();
                var cols = ExtractCteColumnsForEntry(tokens, j);
                // Resolve * in this nested CTE by scanning its body
                var starCols = ResolveStarInNestedBody(tokens, j, result);
                if (starCols.Count > 0)
                {
                    var existing = new HashSet<string>(cols, StringComparer.OrdinalIgnoreCase);
                    foreach (var sc in starCols)
                        if (existing.Add(sc)) cols.Add(sc);
                }
                result[name] = cols;
                j++;
                if (j < bodyEnd && tokens[j].Kind == NzToken.LParen && IsColumnListStart(tokens, j))
                    SkipBalancedParens(tokens, ref j);
                if (j < bodyEnd && tokens[j].Kind == NzToken.As)
                {
                    j++;
                    SkipBalancedParens(tokens, ref j);
                    if (j < bodyEnd && tokens[j].Kind == NzToken.Comma) j++;
                    else break;
                }
                else break;
            }
            else j++;
        }
    }
}

/// <summary>
/// Resolves <c>*</c> in a nested CTE's SELECT list, using sibling nested
/// CTEs and schema.
/// </summary>
private List<string> ResolveStarInNestedBody(Token<NzToken>[] tokens, int cteNamePos,
    Dictionary<string, IReadOnlyList<string>> nestedCtes)
{
    int j = cteNamePos + 1;
    while (j < tokens.Length && tokens[j].Kind != NzToken.As) j++;
    if (j >= tokens.Length) return new List<string>();
    j++;
    if (j < tokens.Length && tokens[j].Kind == NzToken.All) j++;
    while (j < tokens.Length && tokens[j].Kind != NzToken.LParen) j++;
    if (j >= tokens.Length) return new List<string>();

    int bodyEnd = j, depth = 1;
    bodyEnd++;
    while (bodyEnd < tokens.Length && depth > 0)
    {
        if (tokens[bodyEnd].Kind == NzToken.LParen) depth++;
        else if (tokens[bodyEnd].Kind == NzToken.RParen) depth--;
        bodyEnd++;
    }

    return ResolveStarColumnsInBody(tokens, j + 1, bodyEnd, nestedCtes, siblingBindings: null);
}

    private static bool IsColumnListStart(Token<NzToken>[] tokens, int pos)
    {
        if (pos >= tokens.Length || tokens[pos].Kind != NzToken.LParen) return false;
        return pos + 1 < tokens.Length && tokens[pos + 1].Kind.IsIdentifierLike();
    }

    private static void SkipBalancedParens(Token<NzToken>[] tokens, ref int pos)
    {
        if (pos >= tokens.Length || tokens[pos].Kind != NzToken.LParen) return;
        int depth = 0;
        while (pos < tokens.Length)
        {
            if (tokens[pos].Kind == NzToken.LParen) depth++;
            else if (tokens[pos].Kind == NzToken.RParen) { depth--; if (depth == 0) { pos++; return; } }
            pos++;
        }
    }
}
