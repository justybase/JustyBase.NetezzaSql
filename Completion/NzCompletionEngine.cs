using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Completion;

public record CompletionItem(
    string Label,
    CompletionKind Kind,
    string? Detail = null,
    int Priority = 0
);

public enum CompletionKind
{
    Keyword,
    Table,
    View,
    Column,
    Function,
    Schema,
    Database,
    Alias,
    Cte,
    DataType,
    Snippet,
    Variable
}

/// <summary>
/// Context-aware SQL autocomplete engine.
/// </summary>
public class NzCompletionEngine
{
    private readonly ISchemaProvider? _schema;
    private readonly DocumentParsingCoordinator? _parsingCoordinator;
    private readonly CompletionWildcardResolver _wildcardResolver;
    private string? _documentUri;
    private int _cursorPosition;

    /// <summary>
    /// Position-aware scope collector for CTE/alias/temp-table resolution.
    /// Mirrors <c>ParserSqlContextCollector</c> from the reference Node project.
    /// Each scope records its <c>start</c>/<c>end</c> position and alias bindings.
    /// Only scopes containing the cursor contribute their bindings.
    /// </summary>
    private TokenScopeCollector? _lastScopeCollector;
    private Token<NzToken>[]? _lastFullTokens;

    public NzCompletionEngine(ISchemaProvider? schema = null, DocumentParsingCoordinator? parsingCoordinator = null)
    {
        _schema = schema;
        _parsingCoordinator = parsingCoordinator;
        _wildcardResolver = new CompletionWildcardResolver(schema);
    }

    public void SetDocumentUri(string? documentUri) => _documentUri = documentUri;

    public IReadOnlyList<CompletionItem> GetCompletions(string sql, int cursorPosition)
    {
        if (cursorPosition > sql.Length) cursorPosition = sql.Length;
        _cursorPosition = cursorPosition;

        if (TryGetVariableCompletions(sql, cursorPosition) is { Count: > 0 } variableItems)
            return variableItems;

        var fullTokens = TokenizePrefix(sql) ?? Array.Empty<Token<NzToken>>();
        _lastFullTokens = fullTokens;
        var astScope = new CompletionScopeProvider(_schema).TryBuild(sql);

        _lastScopeCollector = new TokenScopeCollector(_schema);
        _lastScopeCollector.Collect(fullTokens, sql.Length);

        if (_wildcardResolver.TryResolveWildcardSnippet(sql, cursorPosition, _lastScopeCollector, astScope, fullTokens) is { } wildcard)
            return new[] { wildcard };

        var statementPrefix = CompletionContextExtractor.GetStatementLocalPrefix(sql, cursorPosition);
        var (partialWord, partialStart) = ExtractPartialWord(statementPrefix, statementPrefix.Length);

        var contextTokens = TokenizePrefix(sql[..cursorPosition]);
        if (contextTokens is null) return Array.Empty<CompletionItem>();

        var filterPartial = partialWord;
        if (contextTokens.Length > 0 &&
            contextTokens[^1].ToStringValue().Equals(partialWord, StringComparison.OrdinalIgnoreCase) &&
            contextTokens[^1].Kind is not NzToken.Identifier and not NzToken.QuotedIdentifier)
        {
            filterPartial = string.Empty;
        }

        _parsingCoordinator?.GetOrCreate(_documentUri ?? "default").Parse(sql);

        var context = AnalyzeContext(contextTokens);

        var suggestions = new List<CompletionItem>();

        switch (context)
        {
            case CompletionContext.AfterSelect:
            case CompletionContext.SelectList:
                AddKeywords(suggestions, SqlContext.SelectListKeywords);
                AddFunctions(suggestions);
                AddColumnsFromScope(suggestions, fullTokens, astScope);
                AddCtes(suggestions, fullTokens, astScope);
                break;

            case CompletionContext.AfterFrom:
            case CompletionContext.FromList:
                AddTablesAndViews(suggestions);
                AddCtes(suggestions, fullTokens, astScope);
                break;

            case CompletionContext.AfterUpdate:
                AddTables(suggestions);
                AddKeywords(suggestions, new[] { "SET" });
                AddCtes(suggestions, fullTokens, astScope);
                break;

            case CompletionContext.AfterSet:
            case CompletionContext.UpdateSetList:
                AddColumnsFromScope(suggestions, fullTokens, astScope);
                AddFunctions(suggestions);
                AddKeywords(suggestions, new[] { "WHERE" });
                break;

            case CompletionContext.AfterDelete:
                AddKeywords(suggestions, new[] { "FROM" });
                break;

            case CompletionContext.AfterWhere:
            case CompletionContext.WhereClause:
            case CompletionContext.AfterOn:
                AddColumnsFromScope(suggestions, fullTokens, astScope);
                AddFunctions(suggestions);
                AddKeywords(suggestions, SqlContext.WhereKeywords);
                break;

            case CompletionContext.AfterJoin:
                AddKeywords(suggestions, SqlContext.JoinKeywords);
                AddTablesAndViews(suggestions);
                AddCtes(suggestions, fullTokens, astScope);
                break;

            case CompletionContext.AfterGroupBy:
            case CompletionContext.GroupByList:
                AddColumnsFromScope(suggestions, fullTokens, astScope);
                AddFunctions(suggestions);
                break;

            case CompletionContext.AfterOrderBy:
            case CompletionContext.OrderByList:
                AddColumnsFromScope(suggestions, fullTokens, astScope);
                AddFunctions(suggestions);
                AddKeywords(suggestions, new[] { "ASC", "DESC", "NULLS", "FETCH", "LIMIT", "OFFSET" });
                break;

            case CompletionContext.AfterHaving:
                AddColumnsFromScope(suggestions, fullTokens, astScope);
                AddFunctions(suggestions);
                AddKeywords(suggestions, new[] { "AND", "OR", "NOT", "IN", "BETWEEN", "LIKE", "ILIKE", "IS", "NULL" });
                break;

            case CompletionContext.AfterAs:
                break;

            case CompletionContext.AfterInsert:
                AddKeywords(suggestions, new[] { "INTO" });
                break;

            case CompletionContext.AfterInsertInto:
                AddTables(suggestions);
                break;

            case CompletionContext.InsertColumns:
                AddColumnsForInsertTarget(suggestions, contextTokens);
                AddKeywords(suggestions, new[] { "VALUES" });
                break;

            case CompletionContext.AfterValues:
                AddKeywords(suggestions, new[] { "NULL", "DEFAULT" });
                AddFunctions(suggestions);
                break;

            case CompletionContext.AfterMerge:
                AddKeywords(suggestions, new[] { "INTO" });
                break;

            case CompletionContext.AfterMergeInto:
                AddTables(suggestions);
                break;

            case CompletionContext.AfterGenerate:
                AddKeywords(suggestions, new[] { "STATISTICS" });
                break;

            case CompletionContext.AfterGenerateStatistics:
                AddKeywords(suggestions, new[] { "ON" });
                break;

            case CompletionContext.AfterGenerateStatisticsOn:
                AddTables(suggestions);
                AddKeywords(suggestions, new[] { "EXPRESS" });
                break;

            case CompletionContext.AfterAlterTableAction:
                AddAlterTablePhaseCompletions(suggestions, contextTokens);
                break;

            case CompletionContext.QualifiedReference:
                var qualifier = ExtractQualifier(contextTokens);
                // Prefer schema/table path completion when qualifier is a known database or schema;
                // otherwise fall back to alias/column resolution.
                if (!AddObjectsForQualifier(suggestions, qualifier))
                    AddColumnsForAlias(suggestions, fullTokens, qualifier, astScope);
                break;

            case CompletionContext.AfterCreate:
                AddKeywords(suggestions, SqlContext.CreateKeywords);
                break;

            case CompletionContext.AfterDrop:
                AddKeywords(suggestions, SqlContext.DropKeywords);
                AddTablesAndViews(suggestions);
                break;

            case CompletionContext.AfterDropTable:
                AddTables(suggestions);
                break;

            case CompletionContext.AfterDropView:
                AddViews(suggestions);
                break;

            case CompletionContext.AfterDropProcedure:
                // No procedure registry in schema yet; fall back to tables+views.
                AddTablesAndViews(suggestions);
                break;

            case CompletionContext.AfterAlter:
                AddKeywords(suggestions, SqlContext.AlterKeywords);
                break;

            case CompletionContext.AfterAlterTable:
                AddTables(suggestions);
                break;

            case CompletionContext.AfterAlterView:
            case CompletionContext.AfterAlterProcedure:
                AddKeywords(suggestions, new[] { "ADD", "DROP", "COLUMN", "RENAME", "OWNER" });
                break;

            case CompletionContext.AfterTruncate:
                AddKeywords(suggestions, new[] { "TABLE" });
                AddTables(suggestions);
                break;

            case CompletionContext.AfterTruncateTable:
                AddTables(suggestions);
                break;

            case CompletionContext.AfterGroom:
                AddKeywords(suggestions, new[] { "TABLE" });
                AddTables(suggestions);
                break;

            case CompletionContext.AfterGroomTable:
                AddTables(suggestions);
                break;

            case CompletionContext.AfterCreateSynonym:
                // Waiting for the synonym name — no schema suggestions here.
                break;

            case CompletionContext.AfterCreateSynonymName:
                AddKeywords(suggestions, new[] { "FOR" });
                break;

            case CompletionContext.AfterCreateSynonymFor:
                AddTablesAndViews(suggestions);
                break;

            case CompletionContext.AfterExplain:
                AddKeywords(suggestions, SqlContext.TopLevelKeywords);
                AddKeywords(suggestions, new[] { "VERBOSE", "DISTRIBUTION", "PLANTEXT", "PLANGRAPH" });
                break;

            case CompletionContext.TopLevel:
            default:
                AddKeywords(suggestions, SqlContext.TopLevelKeywords);
                break;
        }

        if (!string.IsNullOrEmpty(filterPartial))
        {
            suggestions = suggestions
                .Where(s => s.Label.StartsWith(filterPartial, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return suggestions.OrderBy(s => s.Priority).ThenBy(s => s.Label).ToList();
    }

    /// <summary>
    /// Returns scope hints collected during the last GetCompletions call.
    /// Used by the legacy completion path as hints for CTE/temp-table columns and aliases.
    /// </summary>
    public (Dictionary<string, List<string>> WithHints,
            Dictionary<string, List<string>> TempTableHints,
            Dictionary<string, List<string>> AliasDbTable) GetScopeHints()
    {
        var withHints = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var tempTableHints = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var aliasDbTable = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (_lastScopeCollector is not null)
        {
            foreach (var cteName in _lastScopeCollector.GetCteNamesInScope(_cursorPosition))
            {
                var cols = _lastScopeCollector.GetCteColumns(cteName, _cursorPosition);
                if (cols is { Count: > 0 })
                    withHints[cteName] = cols.ToList();
            }
        }

        if (_lastFullTokens is { Length: > 0 })
        {
            foreach (var (tableName, schema, database, alias) in ExtractTableReferences(_lastFullTokens))
            {
                if (alias is null) continue;
                var key = database is not null && schema is not null ? $"{database}.{schema}.{tableName}"
                    : database is not null && schema is null ? $"{database}..{tableName}"
                    : schema is not null ? $"{schema}.{tableName}"
                    : tableName;
                if (!aliasDbTable.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    aliasDbTable[key] = list;
                }
                if (!list.Contains(alias, StringComparer.OrdinalIgnoreCase))
                    list.Add(alias);
            }
        }

        return (withHints, tempTableHints, aliasDbTable);
    }

    // ====== Context Detection ======

    private enum CompletionContext
    {
        TopLevel,
        AfterSelect, SelectList,
        AfterFrom, FromList,
        AfterWhere, WhereClause,
        AfterJoin,
        AfterOn,
        AfterGroupBy, GroupByList,
        AfterOrderBy, OrderByList,
        AfterHaving,
        AfterAs,
        AfterInsert, AfterInsertInto, InsertColumns, AfterValues,
        AfterMerge, AfterMergeInto,
        AfterGenerate, AfterGenerateStatistics, AfterGenerateStatisticsOn,
        AfterUpdate, AfterSet, UpdateSetList,
        AfterDelete,
        AfterCreate, AfterDrop, AfterAlter,
        AfterTruncate, AfterGroom, AfterExplain,
        // Target-specific DROP sub-contexts
        AfterDropTable, AfterDropView, AfterDropProcedure,
        // Target-specific TRUNCATE sub-context
        AfterTruncateTable,
        // Target-specific ALTER sub-contexts
        AfterAlterTable, AfterAlterView, AfterAlterProcedure, AfterAlterTableAction,
        // Target-specific GROOM sub-context
        AfterGroomTable,
        // CREATE SYNONYM ... FOR sub-contexts
        AfterCreateSynonym, AfterCreateSynonymName, AfterCreateSynonymFor,
        QualifiedReference,
    }

    private static CompletionContext AnalyzeContext(Token<NzToken>[] tokens)
    {
        var ctx = CompletionContext.TopLevel;
        int parenDepth = 0;

        if (tokens.Length >= 2 &&
            tokens[^1].Kind == NzToken.Dot &&
            (tokens[^2].Kind == NzToken.Identifier || tokens[^2].Kind == NzToken.QuotedIdentifier || IsKeywordUsableAsName(tokens[^2].Kind)))
        {
            return CompletionContext.QualifiedReference;
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i].Kind;

            if (t == NzToken.LParen) parenDepth++;
            if (t == NzToken.RParen) parenDepth--;

            if (parenDepth > 0) continue;

            if (IsSelect(t)) ctx = CompletionContext.AfterSelect;
            else if (IsFrom(t)) ctx = CompletionContext.AfterFrom;
            else if (IsWhere(t)) ctx = CompletionContext.AfterWhere;
            else if (IsJoin(t)) ctx = CompletionContext.AfterJoin;
            else if (t == NzToken.On && ctx == CompletionContext.AfterGenerateStatistics)
                ctx = CompletionContext.AfterGenerateStatisticsOn;
            else if (t == NzToken.On) ctx = CompletionContext.AfterOn;
            else if (IsGroupBy(t)) ctx = CompletionContext.AfterGroupBy;
            else if (IsOrderBy(t)) ctx = CompletionContext.AfterOrderBy;
            else if (IsHaving(t)) ctx = CompletionContext.AfterHaving;
            else if (t == NzToken.As) ctx = CompletionContext.AfterAs;
            else if (t == NzToken.Update)
            {
                ctx = CompletionContext.AfterUpdate;
                parenDepth = 0;
            }
            else if (t == NzToken.Set && ctx == CompletionContext.AfterUpdate)
                ctx = CompletionContext.AfterSet;
            else if (t == NzToken.Delete) ctx = CompletionContext.AfterDelete;
            else if (t == NzToken.Insert) ctx = CompletionContext.AfterInsert;
            else if (t == NzToken.Into && ctx == CompletionContext.AfterInsert)
                ctx = CompletionContext.AfterInsertInto;
            else if (t == NzToken.Merge) ctx = CompletionContext.AfterMerge;
            else if (t == NzToken.Into && ctx == CompletionContext.AfterMerge)
                ctx = CompletionContext.AfterMergeInto;
            else if (t == NzToken.Generate) ctx = CompletionContext.AfterGenerate;
            else if (t == NzToken.Statistics && ctx == CompletionContext.AfterGenerate)
                ctx = CompletionContext.AfterGenerateStatistics;
            else if (t == NzToken.Values && ctx == CompletionContext.InsertColumns)
                ctx = CompletionContext.AfterValues;
            else if (t == NzToken.Create) ctx = CompletionContext.AfterCreate;
            else if (t == NzToken.Drop && ctx is not (CompletionContext.AfterAlterTable or CompletionContext.AfterAlterTableAction))
                ctx = CompletionContext.AfterDrop;
            else if (t == NzToken.Alter && ctx is not (CompletionContext.AfterAlterTable or CompletionContext.AfterAlterTableAction))
                ctx = CompletionContext.AfterAlter;
            else if (t == NzToken.Truncate) ctx = CompletionContext.AfterTruncate;
            else if (t == NzToken.Groom) ctx = CompletionContext.AfterGroom;
            else if (t == NzToken.Explain) ctx = CompletionContext.AfterExplain;
            // Target-specific DROP: DROP TABLE / VIEW / PROCEDURE
            else if (t == NzToken.Table)
            {
                ctx = ctx switch
                {
                    CompletionContext.AfterDrop => CompletionContext.AfterDropTable,
                    CompletionContext.AfterTruncate => CompletionContext.AfterTruncateTable,
                    CompletionContext.AfterAlter => CompletionContext.AfterAlterTable,
                    CompletionContext.AfterGroom => CompletionContext.AfterGroomTable,
                    _ => ctx
                };
            }
            else if (t == NzToken.View)
            {
                ctx = ctx switch
                {
                    CompletionContext.AfterDrop => CompletionContext.AfterDropView,
                    CompletionContext.AfterAlter => CompletionContext.AfterAlterView,
                    _ => ctx
                };
            }
            else if (t == NzToken.Procedure)
            {
                ctx = ctx switch
                {
                    CompletionContext.AfterDrop => CompletionContext.AfterDropProcedure,
                    CompletionContext.AfterAlter => CompletionContext.AfterAlterProcedure,
                    _ => ctx
                };
            }
            // CREATE SYNONYM <name> FOR
            else if (t == NzToken.Synonym && ctx == CompletionContext.AfterCreate)
                ctx = CompletionContext.AfterCreateSynonym;
            else if (t == NzToken.For && ctx == CompletionContext.AfterCreateSynonymName)
                ctx = CompletionContext.AfterCreateSynonymFor;
            else if (t == NzToken.Semicolon)
            {
                ctx = CompletionContext.TopLevel;
                parenDepth = 0;
            }
            else if (t == NzToken.Comma)
            {
                ctx = ctx switch
                {
                    CompletionContext.SelectList or CompletionContext.AfterSelect => CompletionContext.SelectList,
                    CompletionContext.FromList or CompletionContext.AfterFrom => CompletionContext.FromList,
                    CompletionContext.AfterJoin => CompletionContext.FromList,
                    CompletionContext.GroupByList or CompletionContext.AfterGroupBy => CompletionContext.GroupByList,
                    CompletionContext.OrderByList or CompletionContext.AfterOrderBy => CompletionContext.OrderByList,
                    CompletionContext.UpdateSetList or CompletionContext.AfterSet => CompletionContext.UpdateSetList,
                    CompletionContext.InsertColumns or CompletionContext.AfterInsertInto => CompletionContext.InsertColumns,
                    _ => ctx
                };
            }
            else if (t is NzToken.Identifier or NzToken.QuotedIdentifier or NzToken.NumberLiteral
                      or NzToken.StringLiteral or NzToken.Null or NzToken.Multiply)
            {
                ctx = ctx switch
                {
                    CompletionContext.AfterSelect => CompletionContext.SelectList,
                    CompletionContext.AfterFrom => CompletionContext.FromList,
                    CompletionContext.AfterWhere => CompletionContext.WhereClause,
                    CompletionContext.AfterGroupBy => CompletionContext.GroupByList,
                    CompletionContext.AfterOrderBy => CompletionContext.OrderByList,
                    CompletionContext.AfterHaving => CompletionContext.WhereClause,
                    CompletionContext.AfterOn => CompletionContext.WhereClause,
                    CompletionContext.AfterSet => CompletionContext.UpdateSetList,
                    CompletionContext.AfterUpdate => CompletionContext.AfterUpdate,
                    CompletionContext.AfterDelete => CompletionContext.AfterFrom,
                    CompletionContext.AfterInsertInto => CompletionContext.InsertColumns,
                    CompletionContext.AfterAlterTable => CompletionContext.AfterAlterTableAction,
                    // Synonym name seen → waiting for FOR keyword
                    CompletionContext.AfterCreateSynonym => CompletionContext.AfterCreateSynonymName,
                    _ => ctx
                };
            }
        }

        return ctx;
    }

    private static bool IsSelect(NzToken t) => t == NzToken.Select;
    private static bool IsFrom(NzToken t) => t == NzToken.From;
    private static bool IsWhere(NzToken t) => t == NzToken.Where;
    private static bool IsJoin(NzToken t) => t is NzToken.Join;
    private static bool IsGroupBy(NzToken t) => t == NzToken.GroupBy;
    private static bool IsOrderBy(NzToken t) => t == NzToken.OrderBy;
    private static bool IsHaving(NzToken t) => t == NzToken.Having;

    // ====== Suggestion Generators ======

    private void AddKeywords(List<CompletionItem> list, string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (kw.Length == 0) continue;
            list.Add(new CompletionItem(kw, CompletionKind.Keyword, Priority: 20));
        }
    }

    private void AddTablesAndViews(List<CompletionItem> list)
    {
        if (_schema is null) return;
        var names = _schema.GetTableNames(null, null);
        if (names is null) return;
        foreach (var (name, kind) in names)
        {
            list.Add(new CompletionItem(name,
                kind == TableKind.View ? CompletionKind.View : CompletionKind.Table,
                Priority: 3));
        }
    }

    private void AddTables(List<CompletionItem> list)
    {
        if (_schema is null) return;
        var names = _schema.GetTableNames(null, null);
        if (names is null) return;
        foreach (var (name, kind) in names)
        {
            if (kind != TableKind.View)
                list.Add(new CompletionItem(name, CompletionKind.Table, Priority: 3));
        }
    }

    private void AddViews(List<CompletionItem> list)
    {
        if (_schema is null) return;
        var names = _schema.GetTableNames(null, null);
        if (names is null) return;
        foreach (var (name, kind) in names)
        {
            if (kind == TableKind.View)
                list.Add(new CompletionItem(name, CompletionKind.View, Priority: 3));
        }
    }

    /// <summary>
    /// Handles qualified path completions: DB. → schemas, SCHEMA. → tables/views.
    /// Returns true when objects were added (caller should skip further resolution).
    /// </summary>
    private bool AddObjectsForQualifier(List<CompletionItem> list, string qualifier)
    {
        if (_schema is null || string.IsNullOrEmpty(qualifier)) return false;

        // Check if qualifier is a known database name → suggest its schemas.
        var databases = _schema.GetDatabases();
        if (databases?.Any(d => string.Equals(d, qualifier, StringComparison.OrdinalIgnoreCase)) == true)
        {
            var schemas = _schema.GetSchemas(qualifier);
            if (schemas is { Count: > 0 })
            {
                foreach (var schemaName in schemas)
                    list.Add(new CompletionItem(schemaName, CompletionKind.Schema, Priority: 2));
                return true;
            }
        }

        // Check if qualifier is a known schema name → suggest tables/views in that schema.
        var tables = _schema.GetTableNames(null, qualifier);
        if (tables is { Count: > 0 })
        {
            foreach (var (name, kind) in tables)
                list.Add(new CompletionItem(name,
                    kind == TableKind.View ? CompletionKind.View : CompletionKind.Table,
                    Priority: 3));
            return true;
        }

        return false;
    }

    private static void AddFunctions(List<CompletionItem> list)
    {
        var catalogFunctions = NetezzaSqlCatalog.BuiltinFunctions
            .Select(f => (f.Name, Detail: f.Signatures.FirstOrDefault()?.Label));
        var legacyFunctions = SqlContext.BuiltinFunctions
            .Select(name => (Name: name, Detail: (string?)null));

        foreach (var fn in catalogFunctions.Concat(legacyFunctions).DistinctBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(new CompletionItem(fn.Name, CompletionKind.Function, fn.Detail, Priority: 10));
        }
    }

    /// <summary>
    /// Extracts CTE names and their inferred columns from the WITH clause.
    /// Uses position-aware scope collector for CTE visibility.
    /// </summary>
    private void AddCtes(List<CompletionItem> list, Token<NzToken>[] tokens, ScopeBuilder? astScope = null)
    {
        if (astScope is not null)
        {
            // AST-based: CTE names from scope builder
            var visible = astScope.GetAllVisibleTables();
            bool hasCtes = false;
            foreach (var table in visible)
            {
                if (table.IsCte || astScope.CurrentScope.Ctes.ContainsKey(table.Name.ToUpperInvariant()))
                {
                    list.Add(new CompletionItem(table.Name, CompletionKind.Cte, Priority: 5));
                    hasCtes = true;
                }
            }
            if (hasCtes) return;
        }

        // Token-based: use scope collector for position-aware CTE visibility
        if (_lastScopeCollector is not null)
        {
            foreach (var name in _lastScopeCollector.GetCteNamesInScope(_cursorPosition))
            {
                list.Add(new CompletionItem(name, CompletionKind.Cte, Priority: 5));
            }
        }
    }

    private void AddColumnsFromScope(List<CompletionItem> list, Token<NzToken>[] tokens, ScopeBuilder? astScope = null)
    {
        if (astScope is not null)
        {
            // AST-based: use ScopeBuilder's visible tables (handles CTEs, subquery aliases, etc.)
            var visibleTables = astScope.GetAllVisibleTables();
            bool hasTables = false;
            foreach (var table in visibleTables)
            {
                if (table.Columns is null || table.Columns.Count == 0) continue;
                hasTables = true;
                var displayName = table.Alias ?? table.Name;
                foreach (var col in table.Columns)
                {
                    list.Add(new CompletionItem(col.Name, CompletionKind.Column,
                        Detail: $"{displayName}.{col.Name}", Priority: 1));
                }
            }
            // If AST found tables, use it exclusively (it's more accurate)
            if (hasTables) return;
            // Otherwise fall through to token-based
        }

        // Fallback: token-based table reference extraction
        var tableRefs = ExtractTableReferences(tokens);
        foreach (var (tableName, schema, database, _) in tableRefs)
        {
            // Try schema provider first
            if (_schema is not null)
            {
                var info = _schema.GetTable(database, schema, tableName);
                if (info?.Columns is not null)
                {
                    foreach (var col in info.Columns)
                    {
                        list.Add(new CompletionItem(col.Name, CompletionKind.Column,
                            Detail: $"{tableName}.{col.Name}", Priority: 1));
                    }
                    continue;
                }
            }

            // Fallback: check scope collector for CTE/temp-table columns
            var cteCols = _lastScopeCollector?.GetCteColumns(tableName, _cursorPosition);
            if (cteCols is not null && cteCols.Count > 0)
            {
                foreach (var col in cteCols)
                {
                    list.Add(new CompletionItem(col, CompletionKind.Column,
                        Detail: $"{tableName}.{col}", Priority: 1));
                }
            }
        }
    }

    private void AddColumnsForAlias(List<CompletionItem> list, Token<NzToken>[] tokens, string qualifier, ScopeBuilder? astScope = null)
    {
        if (_schema is null && (_lastScopeCollector is null || !_lastScopeCollector.HasAny())) return;

        if (astScope is not null)
        {
            // AST-based alias resolution via ScopeBuilder
            var table = astScope.FindTable(qualifier);
            if (table?.Columns is { Count: > 0 })
            {
                foreach (var col in table.Columns)
                {
                    list.Add(new CompletionItem(col.Name, CompletionKind.Column,
                        Detail: $"{qualifier}.{col.Name}", Priority: 1));
                }
                return;
            }

            // Try as direct table name
            if (_schema is not null)
            {
                var directInfo = _schema.GetTable(null, null, qualifier);
                if (directInfo?.Columns is not null)
                {
                    foreach (var col in directInfo.Columns)
                    {
                        list.Add(new CompletionItem(col.Name, CompletionKind.Column,
                            Detail: $"{qualifier}.{col.Name}", Priority: 1));
                    }
                    return;
                }
            }

            // AST didn't find the alias — fall through to token-based
        }

        // Fallback: token-based alias resolution
        // CTEs shadow real tables — check scope collector first
        var cteColumns = _lastScopeCollector?.GetCteColumns(qualifier, _cursorPosition);
        if (cteColumns is { Count: > 0 })
        {
            foreach (var col in cteColumns)
            {
                list.Add(new CompletionItem(col, CompletionKind.Column,
                    Detail: $"{qualifier}.{col}", Priority: 1));
            }
            return;
        }

        // Resolve alias to full table path (database..table, db.schema.table, etc.)
        if (TryAddColumnsFromResolvedTablePath(list, tokens, qualifier))
            return;

        // Try alias resolution first: FROM t alias → alias resolves to t
        var resolvedName = CompletionAliasResolver.ResolveAlias(tokens, qualifier);
        if (resolvedName is not null)
        {
            // Check if resolved name is a CTE/temp-table in scope
            var resolvedCteCols = _lastScopeCollector?.GetCteColumns(resolvedName, _cursorPosition);
            if (resolvedCteCols is { Count: > 0 })
            {
                foreach (var col in resolvedCteCols)
                {
                    list.Add(new CompletionItem(col, CompletionKind.Column,
                        Detail: $"{qualifier}.{col}", Priority: 1));
                }
                return;
            }

            // Check schema provider for resolved name (qualified path first)
            if (_schema is not null)
            {
                var resolvedPath = CompletionAliasResolver.ResolveTablePath(tokens, qualifier)
                                   ?? CompletionAliasResolver.ResolveTablePath(tokens, resolvedName);
                if (resolvedPath is { } path)
                {
                    var pathInfo = _schema.GetTable(path.Database, path.Schema, path.Name);
                    if (pathInfo?.Columns is not null)
                    {
                        foreach (var col in pathInfo.Columns)
                        {
                            list.Add(new CompletionItem(col.Name, CompletionKind.Column,
                                Detail: $"{qualifier}.{col.Name}", Priority: 1));
                        }
                        return;
                    }
                }

                var info = _schema.GetTable(null, null, resolvedName);
                if (info?.Columns is not null)
                {
                    foreach (var col in info.Columns)
                    {
                        list.Add(new CompletionItem(col.Name, CompletionKind.Column,
                            Detail: $"{qualifier}.{col.Name}", Priority: 1));
                    }
                    return;
                }
            }
        }

        // Try as a direct table name
        if (_schema is not null)
        {
            var directInfo = _schema.GetTable(null, null, qualifier);
            if (directInfo?.Columns is not null)
            {
                foreach (var col in directInfo.Columns)
                {
                    list.Add(new CompletionItem(col.Name, CompletionKind.Column,
                        Detail: $"{qualifier}.{col.Name}", Priority: 1));
                }
            }
        }
    }

    private bool TryAddColumnsFromResolvedTablePath(List<CompletionItem> list, Token<NzToken>[] tokens, string qualifier)
    {
        if (_schema is null) return false;

        var tablePath = CompletionAliasResolver.ResolveTablePath(tokens, qualifier);
        if (tablePath is not { } path) return false;

        var info = _schema.GetTable(path.Database, path.Schema, path.Name);
        if (info?.Columns is null) return false;

        foreach (var col in info.Columns)
        {
            list.Add(new CompletionItem(col.Name, CompletionKind.Column,
                Detail: $"{qualifier}.{col.Name}", Priority: 1));
        }

        return true;
    }

    // ====== Table Reference Extraction ======

    private static string ExtractQualifier(Token<NzToken>[] tokens)
    {
        for (int i = tokens.Length - 1; i > 0; i--)
        {
            if (tokens[i].Kind == NzToken.Dot &&
                (tokens[i - 1].Kind is NzToken.Identifier or NzToken.QuotedIdentifier
                 || IsKeywordUsableAsName(tokens[i - 1].Kind)))
            {
                return tokens[i - 1].ToStringValue();
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// Returns true for keyword tokens that are commonly used as object names
    /// (e.g. PUBLIC, ADMIN, SALES can be schema names).
    /// </summary>
    private static bool IsKeywordUsableAsName(NzToken kind) =>
        kind is NzToken.Public
            or NzToken.Schema
            or NzToken.Session
            or NzToken.Database
            or NzToken.User
            or NzToken.Table
            or NzToken.View
            or NzToken.Procedure
            or NzToken.Synonym
            or NzToken.Sequence;

    private static List<(string TableName, string? Schema, string? Database, string? Alias)> ExtractTableReferences(Token<NzToken>[] tokens)
    {
        var refs = new List<(string TableName, string? Schema, string? Database, string? Alias)>();
        bool inFromOrJoin = false;
        bool afterUpdate = false;
        int parenDepth = 0;

        for (int i = 0; i < tokens.Length; i++)
        {
            var k = tokens[i].Kind;

            if (k == NzToken.LParen) parenDepth++;
            if (k == NzToken.RParen) parenDepth--;

            // Only process FROM/JOIN at top-level paren depth to avoid picking up
            // table references from inside CTE body definitions (e.g., WITH cte AS (SELECT * FROM t))
            if (parenDepth > 0)
                continue;

            if (k == NzToken.Update)
            {
                afterUpdate = true;
                inFromOrJoin = false;
                continue;
            }
            if (k == NzToken.Set && afterUpdate)
            {
                afterUpdate = false;
                continue;
            }
            if (IsFrom(k) || IsJoin(k))
            {
                inFromOrJoin = true;
                afterUpdate = false;
                continue;
            }
            if (IsWhere(k) || IsGroupBy(k) || IsOrderBy(k) || IsHaving(k) || k == NzToken.On)
            {
                inFromOrJoin = false;
                afterUpdate = false;
                continue;
            }

            if (!inFromOrJoin && !afterUpdate)
                continue;

            if (k == NzToken.Identifier)
            {
                var (tableName, schema, database, alias, consumed) = ParseTableReference(tokens, i);
                if (tableName is not null)
                {
                    refs.Add((tableName, schema, database, alias));
                    i += consumed - 1;
                }
            }
        }
        return refs;
    }

    /// <summary>
    /// Parses a table reference starting at position <c>start</c>.
    /// Handles qualified paths (schema.table, db..table) and optional alias.
    /// Returns (unqualified table name, alias, tokens consumed).
    /// </summary>
    private static (string? TableName, string? Schema, string? Database, string? Alias, int Consumed) ParseTableReference(
        Token<NzToken>[] tokens, int start)
    {
        int i = start;
        string? firstIdent = null;
        string? secondIdent = null;
        string? thirdIdent = null;
        bool afterDoubleDot = false;

        // Consume up to 3 dot-separated path parts: table, schema.table, db.schema.table, db..table
        while (i < tokens.Length && tokens[i].Kind == NzToken.Identifier)
        {
            if (firstIdent is null)
                firstIdent = tokens[i].ToStringValue();
            else if (secondIdent is null)
                secondIdent = tokens[i].ToStringValue();
            else
                thirdIdent = tokens[i].ToStringValue();
            i++;

            bool consumed = false;

            // Single dot: schema.table or db.schema.table part separator
            if (i < tokens.Length && tokens[i].Kind == NzToken.Dot &&
                i + 1 < tokens.Length && tokens[i + 1].Kind == NzToken.Identifier)
            {
                i++;
                consumed = true;
                continue;
            }

            // Double dot (db..table)
            if (i < tokens.Length && tokens[i].Kind == NzToken.Dot &&
                i + 1 < tokens.Length && tokens[i + 1].Kind == NzToken.Dot &&
                i + 2 < tokens.Length && tokens[i + 2].Kind == NzToken.Identifier)
            {
                afterDoubleDot = true;
                secondIdent = null;
                thirdIdent = null;
                i += 2;
                consumed = true;
                continue;
            }

            if (!consumed) break;
        }

        // Map parts to (table, schema, database)
        string? tableName;
        string? schema = null;
        string? database = null;

        if (afterDoubleDot && secondIdent is not null)
        {
            database = firstIdent;
            tableName = secondIdent;
        }
        else if (thirdIdent is not null)
        {
            database = firstIdent;
            schema = secondIdent;
            tableName = thirdIdent;
        }
        else if (secondIdent is not null)
        {
            schema = firstIdent;
            tableName = secondIdent;
        }
        else
        {
            tableName = firstIdent;
        }

        if (tableName is null)
            return (null, null, null, null, i - start);

        // Check for alias: optional AS + identifier
        string? alias = null;
        if (i < tokens.Length && tokens[i].Kind == NzToken.As)
            i++;

        if (i < tokens.Length && tokens[i].Kind == NzToken.Identifier)
        {
            var candidate = tokens[i].ToStringValue();
            if (!IsClauseKeyword(candidate))
            {
                alias = candidate;
                i++;
            }
        }

        return (tableName, schema, database, alias, i - start);
    }

    private void AddColumnsForInsertTarget(List<CompletionItem> list, Token<NzToken>[] tokens)
    {
        if (_schema is null) return;
        string? tableName = null;
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Kind == NzToken.Into &&
                tokens[i + 1].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
            {
                tableName = tokens[i + 1].ToStringValue();
                break;
            }
        }

        if (tableName is null) return;
        var table = _schema.GetTable(null, null, tableName);
        if (table?.Columns is null) return;

        foreach (var col in table.Columns)
            list.Add(new CompletionItem(col.Name, CompletionKind.Column,
                Detail: col.DataType, Priority: 5));
    }

    private void AddAlterTablePhaseCompletions(List<CompletionItem> list, Token<NzToken>[] tokens)
    {
        var phase = AlterTableCompletion.AnalyzePhase(tokens);
        if (AlterTableCompletion.PhaseNeedsTableColumns(phase))
        {
            var tableName = ExtractAlterTableName(tokens);
            if (tableName is not null && _schema?.GetTable(null, null, tableName)?.Columns is { } cols)
            {
                foreach (var col in cols)
                    list.Add(new CompletionItem(col.Name, CompletionKind.Column,
                        Detail: col.DataType, Priority: 5));
            }
        }

        foreach (var kw in AlterTableCompletion.GetKeywordsForPhase(phase))
            list.Add(new CompletionItem(kw, CompletionKind.Keyword, Priority: 20));
    }

    private static string? ExtractAlterTableName(Token<NzToken>[] tokens)
    {
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Kind != NzToken.Table) continue;

            var (path, consumed) = ParseAlterTablePathAt(tokens, i + 1);
            if (consumed > 0 && !string.IsNullOrEmpty(path.Name))
                return path.Name;
        }
        return null;
    }

    private static (CompletionAliasResolver.TablePath Path, int Consumed) ParseAlterTablePathAt(
        Token<NzToken>[] tokens, int start)
    {
        string? first = null, second = null, third = null;
        int i = start;
        while (i < tokens.Length && tokens[i].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
        {
            if (first is null) first = tokens[i].ToStringValue();
            else if (second is null) second = tokens[i].ToStringValue();
            else { third = tokens[i].ToStringValue(); i++; break; }
            i++;
        }

        if (first is null) return (new CompletionAliasResolver.TablePath(string.Empty, null, null), 0);

        if (i < tokens.Length && tokens[i].Kind == NzToken.Dot &&
            i + 1 < tokens.Length && tokens[i + 1].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
        {
            second ??= tokens[i + 1].ToStringValue();
            i += 2;
        }

        if (i < tokens.Length && tokens[i].Kind == NzToken.Dot &&
            i + 1 < tokens.Length && tokens[i + 1].Kind == NzToken.Dot &&
            i + 2 < tokens.Length && tokens[i + 2].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
        {
            third = tokens[i + 2].ToStringValue();
            i += 3;
        }

        string name, schema, database;
        if (third is not null) { database = first; schema = second ?? string.Empty; name = third; }
        else if (second is not null) { database = string.Empty; schema = first; name = second; }
        else { database = string.Empty; schema = string.Empty; name = first; }

        return (new CompletionAliasResolver.TablePath(name,
            string.IsNullOrEmpty(schema) ? null : schema,
            string.IsNullOrEmpty(database) ? null : database), i - start);
    }

    private static List<CompletionItem> TryGetVariableCompletions(string sql, int cursorPosition)
    {
        var items = new List<CompletionItem>();
        if (cursorPosition <= 0) return items;

        var start = cursorPosition - 1;
        while (start >= 0 && (char.IsLetterOrDigit(sql[start]) || sql[start] is '_' or '&'))
            start--;
        start++;

        var word = sql[start..cursorPosition];
        if (!word.StartsWith("&", StringComparison.Ordinal)) return items;

        var prefix = word[1..];
        foreach (var name in new[] { "ROWCOUNT", "SQLCODE", "SQLSTATE", "ERROR", "MESSAGE" })
        {
            if (prefix.Length == 0 || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                items.Add(new CompletionItem($"&{name}", CompletionKind.Variable, Priority: 15));
        }

        return items;
    }

    private static bool IsClauseKeyword(string word)
    {
        return word.Equals("WHERE", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("SET", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("GROUP", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("ORDER", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("HAVING", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("LIMIT", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("FETCH", StringComparison.OrdinalIgnoreCase);
    }

    // ====== Helpers ======

    private static Token<NzToken>[]? TokenizePrefix(string prefix)
    {
        try
        {
            var result = NzLexer.Tokenize(prefix);
            return result.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static (string word, int start) ExtractPartialWord(string sql, int cursor)
    {
        int start = cursor;
        while (start > 0 && IsWordChar(sql[start - 1]))
            start--;
        return (sql[start..cursor], start);
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '#';
}

internal static class SqlContext
{
    public static readonly string[] TopLevelKeywords =
    {
        "SELECT", "WITH", "INSERT", "UPDATE", "DELETE", "CREATE",
        "DROP", "ALTER", "TRUNCATE", "GRANT", "REVOKE", "GROOM",
        "GENERATE", "EXPLAIN", "SHOW", "SET", "BEGIN", "COMMIT", "ROLLBACK",
        "BEGIN_PROC", "END_PROC", "DISTRIBUTE", "ORGANIZE", "REFTABLE", "VARARGS",
        "NZPLSQL", "RECLAIM", "BACKUPSET", "EXPRESS"
    };

    public static readonly string[] SelectListKeywords =
    {
        "AS", "CASE", "CAST", "EXTRACT", "DISTINCT"
    };

    public static readonly string[] JoinKeywords =
    {
        "JOIN", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN",
        "CROSS JOIN", "NATURAL JOIN", "ON"
    };

    public static readonly string[] WhereKeywords =
    {
        "=", "<>", "<", ">", "<=", ">=", "AND", "OR", "NOT", "IN", "BETWEEN",
        "LIKE", "ILIKE", "IS", "NULL", "EXISTS", "CASE", "CAST"
    };

    public static readonly string[] CreateKeywords =
    {
        "TABLE", "VIEW", "PROCEDURE", "EXTERNAL TABLE", "SEQUENCE",
        "DATABASE", "GROUP", "SCHEMA", "SYNONYM", "USER",
        "TEMP", "TEMPORARY", "OR REPLACE"
    };

    public static readonly string[] DropKeywords =
    {
        "TABLE", "VIEW", "PROCEDURE", "DATABASE", "GROUP", "SCHEMA",
        "SEQUENCE", "SYNONYM", "USER", "EXTERNAL TABLE"
    };

    public static readonly string[] AlterKeywords =
    {
        "TABLE", "VIEW", "DATABASE", "SEQUENCE", "USER", "SCHEMA", "PROCEDURE"
    };

    public static readonly string[] BuiltinFunctions =
    {
        "ABS", "ADD_MONTHS", "AGE", "AVG", "BIGINT", "BITAND", "BITNOT", "BITOR", "BITXOR",
        "BTRIM", "CEIL", "CEILING", "COALESCE", "CONCAT", "CONVERT", "COUNT",
        "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
        "DATE_PART", "DATE_TRUNC", "DAY", "DAYS_BETWEEN", "DCEIL", "DECODE", "DENSE_RANK", "DFLOOR",
        "DURATION_ADD", "DURATION_SUBTRACT",
        "EXTRACT", "FIRST_DAY", "FIRST_VALUE", "FLOOR", "FORMAT", "FPOW",
        "GET_VIEWDEF", "GREATER", "GREATEST",
        "HASH", "HASH4", "HASH8", "HEX_TO_BINARY", "HEX_TO_GEOMETRY", "HOUR", "HOURS_BETWEEN",
        "INSTR", "INT_TO_STRING",
        "INT1AND", "INT1OR", "INT1XOR", "INT1NOT",
        "INT1INCR", "INT2INCR", "INT4INCR", "INT8INCR",
        "INT1DECR", "INT2DECR", "INT4DECR", "INT8DECR",
        "INT1SHL", "INT1SHR", "INT2SHL", "INT2SHR", "INT4SHL", "INT4SHR", "INT8SHL", "INT8SHR",
        "INT2AND", "INT2OR", "INT2XOR", "INT2NOT",
        "INT4AND", "INT4OR", "INT4XOR", "INT4NOT",
        "INT8AND", "INT8OR", "INT8XOR", "INT8NOT",
        "ISFALSE", "ISNOTFALSE", "ISNOTTRUE", "ISTRUE",
        "LAG", "LAST_DAY", "LAST_VALUE", "LEAD", "LEAST", "LENGTH",
        "LISTAGG", "LOWER", "MAX", "MEDIAN", "MIN", "MINUTES_BETWEEN", "MOD", "MONTH",
        "MONTHS_BETWEEN", "NEXT_MONTH", "NEXT_QUARTER", "NEXT_WEEK", "NEXT_YEAR",
        "NTH_VALUE", "NTILE", "NULLIF", "NUMERIC_SQRT", "NVL", "NVL2", "NOW",
        "OVERLAPS", "POW", "POWER", "RANDOM", "RANK",
        "REGEXP_LIKE", "REGEXP_REPLACE", "REGEXP_SUBSTR",
        "REPLACE", "ROUND", "ROW_NUMBER",
        "SECONDS_BETWEEN", "SETSEED", "SQRT", "STDDEV", "STDDEV_POP", "STDDEV_SAMP", "STRING_AGG",
        "STRING_TO_INT", "STRPOS", "SUBSTR", "SUBSTRING", "SUM",
        "THIS_MONTH", "THIS_QUARTER", "THIS_WEEK", "THIS_YEAR",
        "TIMEOFDAY", "TIMEZONE", "TO_CHAR", "TO_DATE", "TO_NUMBER", "TO_TIMESTAMP",
        "TRANSLATE", "TRIM", "TRUNC", "UNICHR", "UNICODE", "UNICODES",
        "UPPER", "VARIANCE", "VAR_POP", "VAR_SAMP", "VERSION", "WEEKS_BETWEEN", "WIDTH_BUCKET", "YEAR", "YEARS_BETWEEN",
    };
}
