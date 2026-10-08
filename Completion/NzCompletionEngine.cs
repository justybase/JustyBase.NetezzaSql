using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Completion;

public record CompletionItem(
    string Label,
    CompletionKind Kind,
    string? Detail = null,
    int Priority = 0,
    string? InsertText = null,
    string? Documentation = null
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
    Variable,
    ExternalTable,
    Reference
}

/// <summary>
/// Context-aware SQL autocomplete engine.
/// </summary>
public class NzCompletionEngine
{
    private readonly ISchemaProvider? _schema;
    private readonly DocumentParsingCoordinator? _parsingCoordinator;
    private readonly CompletionWildcardResolver _wildcardResolver;
    private readonly ISqlAuthoringCatalog _catalog;
    private readonly SqlDialect _dialect;
    private readonly string? _activeDatabase;
    private string? _documentUri;
    private int _cursorPosition;

    private TableInfo? LookupTable(string? database, string? schema, string tableName) =>
        _schema is null
            ? null
            : CompletionSchemaLookup.GetTable(
                _schema, _dialect, database ?? _activeDatabase, schema, tableName);

    /// <summary>
    /// Position-aware scope collector for CTE/alias/temp-table resolution.
    /// Mirrors <c>ParserSqlContextCollector</c> from the reference Node project.
    /// Each scope records its <c>start</c>/<c>end</c> position and alias bindings.
    /// Only scopes containing the cursor contribute their bindings.
    /// </summary>
    private TokenScopeCollector? _lastScopeCollector;
    private Token<NzToken>[]? _lastFullTokens;

    public NzCompletionEngine(
        ISchemaProvider? schema = null,
        DocumentParsingCoordinator? parsingCoordinator = null,
        ISqlAuthoringCatalog? catalog = null,
        SqlDialect dialect = SqlDialect.Netezza,
        string? activeDatabase = null)
    {
        _schema = schema;
        _parsingCoordinator = parsingCoordinator;
        _catalog = catalog ?? NetezzaSqlAuthoringCatalog.Instance;
        _wildcardResolver = new CompletionWildcardResolver(schema, dialect, activeDatabase);
        _dialect = dialect;
        _activeDatabase = string.IsNullOrWhiteSpace(activeDatabase) ? null : activeDatabase;
    }

    public void SetDocumentUri(string? documentUri) => _documentUri = documentUri;

    public IReadOnlyList<CompletionItem> GetCompletions(string sql, int cursorPosition)
    {
        if (cursorPosition > sql.Length) cursorPosition = sql.Length;
        _cursorPosition = cursorPosition;

        if (TryGetVariableCompletions(sql, cursorPosition) is { Count: > 0 } variableItems)
            return variableItems;

        if (cursorPosition > 0 && JustyBase.NetezzaSqlParser.Linter.LintHelpers.IsInsideStringOrComment(sql, cursorPosition - 1))
            return [];

        var fullTokens = TokenizePrefix(sql) ?? Array.Empty<Token<NzToken>>();
        _lastFullTokens = fullTokens;
        var astScope = new CompletionScopeProvider(_schema, _dialect).TryBuild(sql);

        _lastScopeCollector = new TokenScopeCollector(_schema, _dialect);
        _lastScopeCollector.Collect(fullTokens, sql.Length);

        if (_wildcardResolver.TryResolveWildcardSnippet(sql, cursorPosition, _lastScopeCollector, astScope, fullTokens) is { } wildcard)
            return new[] { wildcard };

        var statementPrefix = CompletionContextExtractor.GetStatementLocalPrefix(sql, cursorPosition);
        var (partialWord, partialStart) = ExtractPartialWord(statementPrefix, statementPrefix.Length);

        var contextTokens = TokenizePrefix(sql[..cursorPosition]);
        if (contextTokens is null) return Array.Empty<CompletionItem>();

        if (contextTokens.Length >= 3 && contextTokens[^1].Kind == NzToken.LParen
            && contextTokens[^2].Kind == NzToken.As && contextTokens.Any(token => token.Kind == NzToken.With))
            return [new CompletionItem("SELECT", CompletionKind.Keyword)];

        var filterPartial = partialWord;
        if (contextTokens.Length > 0 &&
            contextTokens[^1].ToStringValue().Equals(partialWord, StringComparison.OrdinalIgnoreCase) &&
            !contextTokens[^1].Kind.IsIdentifierLike())
        {
            filterPartial = string.Empty;
        }

        _parsingCoordinator?.GetOrCreate(_documentUri ?? "default", _dialect).Parse(sql);

        var trailingFromComma = cursorPosition > 0
            && sql[..cursorPosition].TrimEnd().EndsWith(",", StringComparison.Ordinal);
        var context = AnalyzeContext(
            contextTokens,
            IsTrailingWhitespace(sql, cursorPosition),
            trailingFromComma);

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
            {
                if (TryAddQualifiedPathCompletions(suggestions, statementPrefix, partialWord))
                    break;
                var fromScope = TryGetFromClauseObjectScope(contextTokens);
                AddTablesAndViews(suggestions, fromScope.Database ?? _activeDatabase, fromScope.Schema);
                AddCtes(suggestions, fullTokens, astScope);
                break;
            }

            case CompletionContext.FromClauseTail:
                AddKeywords(suggestions, _dialect == SqlDialect.Netezza
                    ? SqlContext.FromContinuationKeywords.Where(keyword => keyword != "FETCH").Concat(new[] { "LEFT", "RIGHT", "FULL", "INNER", "GROUP", "ORDER" }).ToArray()
                    : SqlContext.FromContinuationKeywords);
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
                AddColumnsFromScope(suggestions, fullTokens, astScope);
                AddFunctions(suggestions);
                AddKeywords(suggestions, SqlContext.WhereKeywords);
                break;

            case CompletionContext.WhereClause:
            case CompletionContext.AfterOn:
                if (IsWhereContinuation(contextTokens))
                {
                    AddKeywords(suggestions, new[] { "AND", "OR", "GROUP", "ORDER", "HAVING", "LIMIT" });
                }
                else
                {
                    if (context == CompletionContext.AfterOn)
                        TryAddJoinPredicates(suggestions, contextTokens);
                    AddColumnsFromScope(suggestions, fullTokens, astScope);
                    AddFunctions(suggestions);
                    AddKeywords(suggestions, SqlContext.WhereKeywords);
                }
                break;

            case CompletionContext.AfterJoin:
            {
                if (TryAddQualifiedPathCompletions(suggestions, statementPrefix, partialWord))
                    break;
                AddKeywords(suggestions, SqlContext.JoinKeywords);
                var joinScope = TryGetFromClauseObjectScope(contextTokens);
                var joinTargets = new List<CompletionItem>();
                if (joinScope.Schema is null && joinScope.Database is null && !partialWord.Contains('.'))
                    AddJoinTargetCompletions(joinTargets, contextTokens, partialWord);
                suggestions.AddRange(joinTargets);
                var relations = new List<CompletionItem>();
                AddTablesAndViews(relations, joinScope.Database ?? _activeDatabase, joinScope.Schema);
                suggestions.AddRange(relations.Where(item =>
                    joinTargets.All(target => !target.Label.Equals(item.Label, StringComparison.OrdinalIgnoreCase))));
                AddCtes(suggestions, fullTokens, astScope);
                break;
            }

            case CompletionContext.AfterGroupBy:
            case CompletionContext.GroupByList:
                AddKeywords(suggestions, new[] { "BY", "HAVING", "ORDER" });
                AddColumnsFromScope(suggestions, fullTokens, astScope);
                AddFunctions(suggestions);
                break;

            case CompletionContext.AfterOrderBy:
            case CompletionContext.OrderByList:
                AddColumnsFromScope(suggestions, fullTokens, astScope);
                AddFunctions(suggestions);
                AddKeywords(suggestions, _dialect == SqlDialect.Netezza
                    ? new[] { "BY", "ASC", "DESC", "NULLS", "FIRST", "LAST", "LIMIT", "OFFSET" }
                    : new[] { "BY", "ASC", "DESC", "NULLS", "FIRST", "LAST", "FETCH", "LIMIT", "OFFSET" });
                break;

            case CompletionContext.AfterHaving:
                if (IsWhereContinuation(contextTokens))
                {
                    AddKeywords(suggestions, new[] { "AND", "OR", "NOT", "IN", "BETWEEN", "LIKE", "ILIKE", "IS", "NULL" });
                }
                else
                {
                    AddColumnsFromScope(suggestions, fullTokens, astScope);
                    AddFunctions(suggestions);
                    AddKeywords(suggestions, new[] { "AND", "OR", "NOT", "IN", "BETWEEN", "LIKE", "ILIKE", "IS", "NULL" });
                }
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
                if (IsRelationQualifierContext(contextTokens)
                    && TryAddQualifiedPathCompletions(suggestions, statementPrefix, partialWord))
                    break;
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
                // Waiting for the synonym name â€” no schema suggestions here.
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
                AddKeywords(suggestions, SqlContext.TopLevelKeywords
                    .Concat(_catalog.CompletionKeywords)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
                break;
        }

        if (partialWord.Equals("FRO", StringComparison.OrdinalIgnoreCase))
            AddKeywords(suggestions, new[] { "FROM" });

        if (!string.IsNullOrEmpty(filterPartial))
        {
            suggestions = FilterAndRank(suggestions, filterPartial);
            return suggestions;
        }

        return suggestions.OrderBy(s => s.Priority).ThenBy(s => s.Label).ToList();
    }

    /// <summary>
    /// Ranks candidate labels by match quality. Direct prefix matches keep the
    /// historical prefix-only behavior; fuzzy tiers (compact spelling, word
    /// starts, initials, fragments) only surface when no direct prefix exists.
    /// </summary>
    private static List<CompletionItem> FilterAndRank(List<CompletionItem> suggestions, string partial)
    {
        var scored = new List<(CompletionItem Item, int Score)>(suggestions.Count);
        foreach (var suggestion in suggestions)
        {
            var score = CompletionMatchScorer.Compute(suggestion.Label, partial);
            var qualifierSeparator = suggestion.Label.LastIndexOf('.');
            if (qualifierSeparator >= 0 && qualifierSeparator + 1 < suggestion.Label.Length)
            {
                score = Math.Max(score, CompletionMatchScorer.Compute(
                    suggestion.Label[(qualifierSeparator + 1)..], partial));
            }
            if (score > 0)
                scored.Add((suggestion, score));
        }

        if (scored.Any(entry => entry.Score >= CompletionMatchScorer.Prefix))
            scored = scored.Where(entry => entry.Score >= CompletionMatchScorer.Prefix).ToList();

        return scored
            .OrderBy(entry => entry.Item.Priority)
            .ThenByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Item.Label, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Item)
            .ToList();
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
        AfterFrom, FromList, FromClauseTail,
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

    private static CompletionContext AnalyzeContext(
        Token<NzToken>[] tokens,
        bool lastTokenComplete = false,
        bool trailingFromComma = false)
    {
        var ctx = CompletionContext.TopLevel;
        bool sawSelect = false;
        var parentContexts = new Stack<(CompletionContext Context, bool SawSelect)>();

        if (tokens.Length >= 2 &&
            tokens[^1].Kind == NzToken.Dot &&
            (tokens[^2].Kind.IsIdentifierLike() || IsKeywordUsableAsName(tokens[^2].Kind)))
        {
            return CompletionContext.QualifiedReference;
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i].Kind;

            if (t == NzToken.LParen)
            {
                parentContexts.Push((ctx, sawSelect));
                continue;
            }
            if (t == NzToken.RParen)
            {
                if (parentContexts.TryPop(out var parent))
                {
                    ctx = parent.Context;
                    sawSelect = parent.SawSelect;
                }
                continue;
            }

            if (IsSelect(t)) { ctx = CompletionContext.AfterSelect; sawSelect = true; }
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
                parentContexts.Clear();
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
            else if (t.IsIdentifierLike() || t is NzToken.NumberLiteral
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
                    // Synonym name seen â†’ waiting for FOR keyword
                    CompletionContext.AfterCreateSynonym => CompletionContext.AfterCreateSynonymName,
                    _ => ctx
                };
            }
        }

        // A completed (space-terminated) table reference or alias at the end of a
        // SELECT FROM clause expects clause-continuation keywords (JOIN/WHERE/...).
        // When the caret is on an in-progress word, the completed reference is the
        // token before it.
        if (sawSelect &&
            ctx is CompletionContext.AfterFrom or CompletionContext.FromList &&
            tokens.Length > 0)
        {
            if (trailingFromComma)
            {
                return CompletionContext.FromList;
            }

            int completedIndex = tokens.Length - 1;
            if (!lastTokenComplete)
            {
                completedIndex--;
            }

            if (completedIndex >= 0 &&
                tokens[completedIndex].Kind.IsIdentifierLike() &&
                (completedIndex == 0 || tokens[completedIndex - 1].Kind != NzToken.Comma))
            {
                return CompletionContext.FromClauseTail;
            }
        }

        return ctx;
    }

    private static bool IsTrailingWhitespace(string sql, int cursorPosition)
        => cursorPosition > 0 && cursorPosition <= sql.Length && char.IsWhiteSpace(sql[cursorPosition - 1]);

    private static bool IsRelationQualifierContext(Token<NzToken>[] tokens)
    {
        bool inRelation = false;
        foreach (var token in tokens)
        {
            var kind = token.Kind;
            if (kind is NzToken.From or NzToken.Join or NzToken.Update)
            {
                inRelation = true;
                continue;
            }

            if (kind is NzToken.Where or NzToken.On or NzToken.GroupBy
                or NzToken.OrderBy or NzToken.Having or NzToken.Set)
                inRelation = false;
        }

        return inRelation;
    }

    private static bool IsSelect(NzToken t) => t == NzToken.Select;
    private static bool IsFrom(NzToken t) => t == NzToken.From;
    private static bool IsWhere(NzToken t) => t == NzToken.Where;
    private static bool IsJoin(NzToken t) => t is NzToken.Join;
    private static bool IsGroupBy(NzToken t) => t == NzToken.GroupBy;
    private static bool IsOrderBy(NzToken t) => t == NzToken.OrderBy;
    private static bool IsHaving(NzToken t) => t == NzToken.Having;

    /// <summary>
    /// True when the WHERE/ON predicate already contains a comparison operator since
    /// the last logical boundary (AND/OR/WHERE/start), i.e. the caret is positioned
    /// after a complete predicate and only continuation keywords make sense.
    /// </summary>
    private static bool IsWhereContinuation(Token<NzToken>[] tokens)
    {
        int parenDepth = 0;
        bool hasRightHandOperand = false;
        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            var t = tokens[i].Kind;
            if (t == NzToken.RParen)
            {
                parenDepth++;
                hasRightHandOperand = true;
                continue;
            }
            if (t == NzToken.LParen)
            {
                if (parenDepth > 0) { parenDepth--; continue; }
                return false;
            }
            if (parenDepth > 0) continue;

            if (t is NzToken.And or NzToken.Or) return false;
            if (t == NzToken.Where) return false;
            if (t is NzToken.EqualsOp or NzToken.NotEquals or NzToken.LessThanEquals or NzToken.GreaterThanEquals
                or NzToken.LessThan or NzToken.GreaterThan or NzToken.Like or NzToken.Ilike
                or NzToken.Is or NzToken.In or NzToken.Between)
            {
                // An operator at the caret is an unfinished predicate. Only an operator
                // encountered after a RHS operand means the comparison is complete.
                return hasRightHandOperand;
            }

            if (t.IsIdentifierLike() || t is NzToken.NumberLiteral
                or NzToken.StringLiteral or NzToken.Null or NzToken.Multiply)
            {
                hasRightHandOperand = true;
            }
        }

        return false;
    }

    // ====== Suggestion Generators ======

    private void AddKeywords(List<CompletionItem> list, string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (kw.Length == 0) continue;
            list.Add(new CompletionItem(kw, CompletionKind.Keyword, Priority: 20));
        }
    }

    private void AddTablesAndViews(List<CompletionItem> list, string? database = null, string? schema = null)
    {
        if (_schema is null) return;
        database ??= _activeDatabase;
        var names = _schema.GetTableNames(database, schema);
        if (names is null) return;
        foreach (var (name, kind) in names)
        {
            list.Add(new CompletionItem(name, ToRelationCompletionKind(kind), Priority: 3));
        }
    }

    private void AddTables(List<CompletionItem> list, string? database = null, string? schema = null)
    {
        if (_schema is null) return;
        database ??= _activeDatabase;
        var names = _schema.GetTableNames(database, schema);
        if (names is null) return;
        foreach (var (name, kind) in names)
        {
            if (kind is TableKind.Table or TableKind.External)
                list.Add(new CompletionItem(name, ToRelationCompletionKind(kind), Priority: 3));
        }
    }

    private void AddViews(List<CompletionItem> list, string? database = null, string? schema = null)
    {
        if (_schema is null) return;
        database ??= _activeDatabase;
        var names = _schema.GetTableNames(database, schema);
        if (names is null) return;
        foreach (var (name, kind) in names)
        {
            if (kind == TableKind.View)
                list.Add(new CompletionItem(name, CompletionKind.View, Priority: 3));
        }
    }

    private static CompletionKind ToRelationCompletionKind(TableKind kind) => kind switch
    {
        TableKind.View => CompletionKind.View,
        TableKind.External => CompletionKind.ExternalTable,
        _ => CompletionKind.Table
    };

    /// <summary>
    /// Resolves database/schema prefix for the table name currently being typed in FROM/JOIN.
    /// Supports <c>db..table</c>, <c>schema.table</c>, and <c>db.schema.table</c> prefixes.
    /// </summary>
    private static (string? Database, string? Schema) TryGetFromClauseObjectScope(Token<NzToken>[] tokens)
    {
        if (tokens.Length == 0)
            return (null, null);

        int i = tokens.Length - 1;
        if (tokens[i].Kind.IsIdentifierLike())
            i--;

        if (i >= 1 &&
            tokens[i].Kind == NzToken.Dot &&
            tokens[i - 1].Kind == NzToken.Dot &&
            i - 2 >= 0 &&
            tokens[i - 2].Kind.IsIdentifierLike())
        {
            return (tokens[i - 2].ToIdentifierText(), null);
        }

        if (i >= 2 &&
            tokens[i].Kind == NzToken.Dot &&
            tokens[i - 1].Kind.IsIdentifierLike() &&
            tokens[i - 2].Kind == NzToken.Dot &&
            i - 3 >= 0 &&
            tokens[i - 3].Kind.IsIdentifierLike())
        {
            return (tokens[i - 3].ToIdentifierText(), tokens[i - 1].ToIdentifierText());
        }

        if (i >= 1 &&
            tokens[i].Kind == NzToken.Dot &&
            tokens[i - 1].Kind.IsIdentifierLike() &&
            (i < 2 || tokens[i - 2].Kind != NzToken.Dot))
        {
            return (null, tokens[i - 1].ToIdentifierText());
        }

        return (null, null);
    }

    /// <summary>
    /// Handles qualified path completions: DB. â†’ schemas, SCHEMA. â†’ tables/views.
    /// Returns true when objects were added (caller should skip further resolution).
    /// </summary>
    private bool AddObjectsForQualifier(List<CompletionItem> list, string qualifier)
    {
        if (_schema is null || string.IsNullOrEmpty(qualifier)) return false;

        // Check if qualifier is a known database name â†’ suggest its schemas.
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

        // Check if qualifier is a known schema name in the active database.
        var tables = _schema.GetTableNames(_activeDatabase, qualifier);
        if (tables is { Count: > 0 })
        {
            foreach (var (name, kind) in tables)
                list.Add(new CompletionItem(name, ToRelationCompletionKind(kind), Priority: 3));
            return true;
        }

        return false;
    }

    private bool TryAddQualifiedPathCompletions(
        List<CompletionItem> list,
        string statementPrefix,
        string partialWord)
    {
        if (_schema is null)
            return false;

        string? fragment = CompletionFragment.GetLastWordFromText(statementPrefix, statementPrefix.Length);
        if (string.IsNullOrWhiteSpace(fragment) || !fragment.Contains('.'))
            return false;

        bool endsWithDot = fragment.EndsWith(".", StringComparison.Ordinal);
        bool isDoubleDotPath = fragment.Contains("..", StringComparison.Ordinal);
        string pathText = endsWithDot ? fragment[..^1] : fragment;
        var rawParts = pathText.Split('.', StringSplitOptions.None)
            .Select(part => part.Trim())
            .ToArray();
        if (rawParts.Length == 0)
            return false;

        string prefix = endsWithDot ? string.Empty : Unquote(rawParts[^1]);
        string? database = null;
        string? schema = null;

        if (rawParts.Length == 1)
        {
            string first = Unquote(rawParts[0]);
            if (_schema.GetDatabases()?.Contains(first, StringComparer.OrdinalIgnoreCase) == true)
                database = first;
            else
                schema = first;
        }
        else if (rawParts.Length >= 3 && rawParts[1].Length == 0)
        {
            database = Unquote(rawParts[0]);
        }
        else if (rawParts.Length >= 3)
        {
            database = Unquote(rawParts[0]);
            schema = Unquote(rawParts[1]);
        }
        else
        {
            string first = Unquote(rawParts[0]);
            // X.Y is schema.table first. A known database is used as the
            // database path only when X is not a schema in the active DB.
            if (_schema.GetDatabases()?.Contains(first, StringComparer.OrdinalIgnoreCase) == true)
                database = first;
            else
                schema = first;

            if (database is not null && rawParts.Length == 2 && endsWithDot
                && rawParts[1].Length > 0)
                schema = Unquote(rawParts[1]);
        }

        if (!endsWithDot)
        {
            if (database is not null && schema is null)
            {
                foreach (string schemaName in _schema.GetSchemas(database)
                             ?.Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                         ?? [])
                    list.Add(new CompletionItem(schemaName, CompletionKind.Schema, Priority: 2));
                return list.Count > 0;
            }

            return false;
        }

        if (database is not null && schema is not null)
        {
            AddTablesAndViews(list, database, schema);
            return list.Count > 0;
        }

        if (database is not null)
        {
            if (isDoubleDotPath)
            {
                AddTablesAndViews(list, database, null);
                return list.Count > 0;
            }

            foreach (string schemaName in _schema.GetSchemas(database) ?? [])
                list.Add(new CompletionItem(schemaName, CompletionKind.Schema, Priority: 2));
            return list.Count > 0;
        }

        if (schema is not null)
        {
            AddTablesAndViews(list, _activeDatabase, schema);
            return list.Count > 0;
        }

        return false;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1].Replace("\"\"", "\"");
        if (value.Length >= 2 && value[0] == '[' && value[^1] == ']')
            return value[1..^1].Replace("]]", "]");
        return value;
    }

    private void AddFunctions(List<CompletionItem> list)
    {
        var catalogFunctions = _catalog.BuiltinFunctions
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
        var positionVisibleCtes = _lastScopeCollector?.GetCteNamesInScope(_cursorPosition)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (astScope is not null)
        {
            // AST-based: CTE names from scope builder
            var visible = astScope.GetAllVisibleTables();
            bool hasCtes = false;
            foreach (var table in visible)
            {
                if (table.IsCte || astScope.CurrentScope.Ctes.ContainsKey(table.Name.ToUpperInvariant()))
                {
                    if (positionVisibleCtes is { Count: > 0 }
                        && !positionVisibleCtes.Contains(table.Name))
                        continue;
                    list.Add(new CompletionItem(table.Name, CompletionKind.Cte, Priority: -5));
                    hasCtes = true;
                }
            }
            if (hasCtes) return;
        }

        // Token-based: use scope collector for position-aware CTE visibility
        if (positionVisibleCtes is not null)
        {
            foreach (var name in positionVisibleCtes)
            {
                list.Add(new CompletionItem(name, CompletionKind.Cte, Priority: -5));
            }
        }
    }

    /// <summary>
    /// Suggests ON predicates from declared foreign keys between the last two
    /// table sources in scope. Requires an <see cref="IForeignKeyProvider"/> schema.
    /// </summary>
    private void TryAddJoinPredicates(List<CompletionItem> list, Token<NzToken>[] tokens)
    {
        if (_schema is not IForeignKeyProvider provider)
            return;

        var candidates = GetJoinTableCandidates(tokens);
        if (candidates.Count < 2)
            return;

        var left = candidates[^2];
        var right = candidates[^1];
        if (left.TableName.Equals(right.TableName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Schema, right.Schema, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AddForeignKeyPredicates(list, provider, left, right, fromIsLeft: true);
        AddForeignKeyPredicates(list, provider, right, left, fromIsLeft: false);
        if (list.Any(item => item.Kind == CompletionKind.Reference)) return;
        var leftColumns = LookupTable(left.Database, left.Schema, left.TableName)?.Columns;
        var rightColumns = LookupTable(right.Database, right.Schema, right.TableName)?.Columns;
        if (leftColumns is null || rightColumns is null) return;
        foreach (var column in leftColumns)
        {
            if (!rightColumns.Any(other => other.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            var label = $"{left.Qualifier}.{column.Name} = {right.Qualifier}.{column.Name}";
            list.Add(new CompletionItem(label, CompletionKind.Reference,
                "Suggested join using matching column names", Priority: -10, InsertText: label));
        }
    }

    private List<(string TableName, string? Schema, string? Database, string Qualifier)> GetJoinTableCandidates(
        Token<NzToken>[] tokens)
    {
        var queryTokens = CurrentQueryTokens(tokens);
        return ExtractTableReferences(queryTokens)
            .Select(reference => (reference.TableName, reference.Schema, reference.Database, reference.Alias ?? reference.TableName))
            .ToList();
    }

    private static Token<NzToken>[] CurrentQueryTokens(Token<NzToken>[] tokens)
    {
        var depth = 0;
        var lastSelectByDepth = new Dictionary<int, int>();

        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Kind == NzToken.LParen)
            {
                depth++;
                continue;
            }
            if (tokens[i].Kind == NzToken.RParen)
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }
            if (tokens[i].Kind == NzToken.Select)
                lastSelectByDepth[depth] = i;
        }

        var targetDepth = depth;
        if (!lastSelectByDepth.TryGetValue(targetDepth, out var lastSelectIndex))
            return Array.Empty<Token<NzToken>>();

        depth = targetDepth;
        var result = new List<Token<NzToken>>();
        for (var i = lastSelectIndex; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Kind == NzToken.LParen)
            {
                depth++;
                continue;
            }
            if (token.Kind == NzToken.RParen)
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }
            if (depth == targetDepth)
                result.Add(token);
        }

        return result.ToArray();
    }

    private void AddForeignKeyPredicates(
        List<CompletionItem> list,
        IForeignKeyProvider provider,
        (string TableName, string? Schema, string? Database, string Qualifier) from,
        (string TableName, string? Schema, string? Database, string Qualifier) to,
        bool fromIsLeft)
    {
        var fromInfo = _schema?.GetTable(from.Database, from.Schema, from.TableName);
        var toInfo = _schema?.GetTable(to.Database, to.Schema, to.TableName);
        var fromDatabase = from.Database ?? fromInfo?.Database;
        var fromSchema = from.Schema ?? fromInfo?.Schema;
        var toDatabase = to.Database ?? toInfo?.Database;
        var toSchema = to.Schema ?? toInfo?.Schema;

        var relations = provider.GetForeignKeys(fromDatabase, fromSchema, from.TableName);
        if (relations is null)
            return;

        foreach (var relation in relations)
        {
            if (!relation.ReferencedTable.Equals(to.TableName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (relation.ReferencedSchema is { Length: > 0 }
                && !relation.ReferencedSchema.Equals(toSchema, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (relation.ReferencedDatabase is { Length: > 0 }
                && !relation.ReferencedDatabase.Equals(toDatabase, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (relation.Columns.Count == 0 || relation.Columns.Count != relation.ReferencedColumns.Count)
                continue;

            // Predicates always read left-to-right in FROM order, even when the
            // foreign key is declared on the right-hand table.
            var predicates = relation.Columns
                .Zip(relation.ReferencedColumns, (local, referenced) => fromIsLeft
                    ? $"{from.Qualifier}.{local} = {to.Qualifier}.{referenced}"
                    : $"{to.Qualifier}.{referenced} = {from.Qualifier}.{local}");
            var label = string.Join(" AND ", predicates);

            if (list.All(item => !item.Label.Equals(label, StringComparison.OrdinalIgnoreCase)))
                list.Add(new CompletionItem(label, CompletionKind.Reference, Detail: "foreign key", Priority: -30, InsertText: label));
        }
    }

    /// <summary>
    /// Suggests "target alias ON predicate" snippets after JOIN for tables related
    /// to a visible source by declared foreign keys (either direction, same or
    /// other schema), falling back to same-schema key-named columns.
    /// </summary>
    private void AddJoinTargetCompletions(List<CompletionItem> list, Token<NzToken>[] contextTokens, string partialWord)
    {
        if (_schema is not ISchemaProvider schemaProvider || _schema is not IForeignKeyProvider provider)
            return;

        var sourceTokens = partialWord.Length > 0 && contextTokens.Length > 0
            ? contextTokens[..^1]
            : contextTokens;
        var sources = ExtractTableReferences(CurrentQueryTokens(sourceTokens)).ToList();
        if (sources.Count == 0)
            return;

        var visibleAliases = sources.Select(source => source.Alias ?? source.TableName).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            var sourceInfo = schemaProvider.GetTable(source.Database, source.Schema, source.TableName);
            var database = sourceInfo?.Database ?? source.Database ?? _activeDatabase;
            var schema = sourceInfo?.Schema ?? source.Schema;
            var sourceQualifier = source.Alias ?? source.TableName;
            var keyColumns = CollectJoinKeyColumns(schemaProvider, provider, database, schema);
            var outgoing = provider.GetForeignKeys(database, schema, source.TableName) ?? Array.Empty<ForeignKeyRelation>();

            foreach (var (candidateSchema, candidateName) in EnumerateJoinTargets(schemaProvider, database))
            {
                if (string.Equals(candidateSchema, schema, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidateName, source.TableName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (partialWord.Length > 0 && !candidateName.StartsWith(partialWord, StringComparison.OrdinalIgnoreCase))
                    continue;

                var sameSchema = string.Equals(candidateSchema, schema, StringComparison.OrdinalIgnoreCase);
                var candidateInfo = schemaProvider.GetTable(database, candidateSchema, candidateName);
                var incoming = provider.GetForeignKeys(database, candidateSchema, candidateName) ?? Array.Empty<ForeignKeyRelation>();

                var groups = new List<(string Predicates, int Priority, string Detail)>();
                foreach (var relation in outgoing)
                {
                    if (!relation.ReferencedTable.Equals(candidateName, StringComparison.OrdinalIgnoreCase)
                        || !IsReferencedRelation(relation.ReferencedSchema, candidateSchema)
                        || relation.Columns.Count == 0
                        || relation.Columns.Count != relation.ReferencedColumns.Count)
                        continue;
                    var predicates = relation.Columns.Zip(relation.ReferencedColumns,
                        (local, referenced) => $"{sourceQualifier}.{local} = {{TARGET}}.{referenced}");
                    groups.Add((string.Join(" AND ", predicates), sameSchema ? -30 : -20, "JOIN with declared foreign key"));
                }
                foreach (var relation in incoming)
                {
                    if (!relation.ReferencedTable.Equals(source.TableName, StringComparison.OrdinalIgnoreCase)
                        || !IsReferencedRelation(relation.ReferencedSchema, schema)
                        || relation.Columns.Count == 0
                        || relation.Columns.Count != relation.ReferencedColumns.Count)
                        continue;
                    var predicates = relation.Columns.Zip(relation.ReferencedColumns,
                        (local, referenced) => $"{sourceQualifier}.{referenced} = {{TARGET}}.{local}");
                    groups.Add((string.Join(" AND ", predicates), sameSchema ? -30 : -20, "JOIN with declared foreign key"));
                }
                if (groups.Count == 0 && sameSchema && candidateInfo is not null && sourceInfo is not null)
                {
                    var pairs = new List<string>();
                    foreach (var sourceColumn in sourceInfo.Columns ?? Array.Empty<ColumnInfo>())
                    {
                        foreach (var candidateColumn in candidateInfo.Columns ?? Array.Empty<ColumnInfo>())
                        {
                            if (!sourceColumn.Name.Equals(candidateColumn.Name, StringComparison.OrdinalIgnoreCase))
                                continue;
                            var sourceKey = keyColumns.Contains($"{schema}.{source.TableName}.{sourceColumn.Name}");
                            var candidateKey = keyColumns.Contains($"{candidateSchema}.{candidateName}.{candidateColumn.Name}");
                            if (!sourceKey && !candidateKey)
                                continue;
                            pairs.Add($"{sourceQualifier}.{sourceColumn.Name} = {{TARGET}}.{candidateColumn.Name}");
                        }
                    }
                    if (pairs.Count > 0)
                        groups.Add((string.Join(" AND ", pairs), -10, "JOIN with name match"));
                }

                foreach (var (predicates, priority, detail) in groups)
                {
                    var alias = ChooseJoinAlias(candidateName, visibleAliases);
                    var targetPath = sameSchema || candidateSchema is null ? candidateName : $"{candidateSchema}.{candidateName}";
                    var insertText = $"{targetPath} {alias} ON {predicates.Replace("{TARGET}", alias)}";
                    if (!seen.Add(insertText))
                        continue;
                    list.Add(new CompletionItem(candidateName, CompletionKind.Table, detail, priority, insertText));
                }
            }
        }
    }

    private static bool IsReferencedRelation(string? referencedSchema, string? relationSchema) =>
        referencedSchema is not { Length: > 0 }
        || string.Equals(referencedSchema, relationSchema, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(string? Schema, string Name)> EnumerateJoinTargets(ISchemaProvider provider, string? database)
    {
        foreach (var schemaName in provider.GetSchemas(database) ?? Array.Empty<string>())
        {
            foreach (var table in provider.GetTableNames(database, schemaName) ?? Array.Empty<(string Name, TableKind Kind)>())
            {
                if (table.Kind == TableKind.Table)
                    yield return (schemaName, table.Name);
            }
        }
    }

    /// <summary>Columns that act as keys: declared FK columns on either side of a relation.</summary>
    private static HashSet<string> CollectJoinKeyColumns(
        ISchemaProvider schemaProvider, IForeignKeyProvider provider, string? database, string? schema)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in schemaProvider.GetTableNames(database, schema) ?? Array.Empty<(string Name, TableKind Kind)>())
        {
            foreach (var relation in provider.GetForeignKeys(database, schema, table.Name) ?? Array.Empty<ForeignKeyRelation>())
            {
                foreach (var column in relation.Columns)
                    keys.Add($"{schema}.{table.Name}.{column}");
                var referencedSchema = relation.ReferencedSchema ?? schema;
                foreach (var column in relation.ReferencedColumns)
                    keys.Add($"{referencedSchema}.{relation.ReferencedTable}.{column}");
            }
        }
        return keys;
    }

    private static string ChooseJoinAlias(string tableName, IReadOnlyCollection<string> visibleAliases)
    {
        var initials = new string(tableName
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => char.IsLetter(part[0]))
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray());
        var baseAlias = initials.Length > 0 ? initials : "T";
        var alias = baseAlias;
        var suffix = 2;
        while (visibleAliases.Any(existing => existing.Equals(alias, StringComparison.OrdinalIgnoreCase)))
            alias = $"{baseAlias}{suffix++}";
        return alias;
    }

    private void AddColumnsFromScope(List<CompletionItem> list, Token<NzToken>[] tokens, ScopeBuilder? astScope = null)
    {
        if (astScope is not null)
        {
            // AST-based: use ScopeBuilder's visible relations, but exclude CTE
            // definitions that are not referenced by any query scope containing
            // the cursor. ScopeBuilder exposes the whole WITH scope, including
            // sibling CTEs that are not part of the current FROM clause.
            var referencedRelations = GetRelationNamesAtCursor(tokens, _cursorPosition);
            var visibleTables = astScope.GetAllVisibleTables()
                .Where(table => !table.IsCte
                    || referencedRelations.Contains(table.Name)
                    || (table.Alias is not null && referencedRelations.Contains(table.Alias)))
                .ToArray();
            bool hasTables = false;
            foreach (var table in visibleTables)
            {
                if (table.Columns is null || table.Columns.Count == 0) continue;
                hasTables = true;
                var displayName = table.Alias ?? table.Name;
                foreach (var col in table.Columns)
                    list.Add(CreateColumnItem(col, displayName));
            }
            // If an unqualified column occurs on multiple visible relations,
            // offer qualified labels as well so the user can disambiguate it.
            AddAmbiguousQualifiedColumns(list, visibleTables
                .Where(table => table.Columns is { Count: > 0 })
                .SelectMany(table => table.Columns!.Select(column =>
                    (Qualifier: table.Alias ?? table.Name, Column: column))));
            // If AST found tables, use it exclusively (it's more accurate)
            if (hasTables) return;
            // Otherwise fall through to token-based
        }

        // Fallback: token-based table reference extraction
        var tableRefs = ExtractTableReferences(tokens);
        var columnCandidates = new List<(string Qualifier, ColumnInfo Column)>();
        foreach (var (tableName, schema, database, alias) in tableRefs)
        {
            var displayName = alias ?? tableName;
            // Try schema provider first
            if (_schema is not null)
            {
                var info = LookupTable(database, schema, tableName);
                if (info?.Columns is { Count: > 0 })
                {
                    foreach (var col in info.Columns)
                    {
                        list.Add(CreateColumnItem(col, displayName));
                        columnCandidates.Add((displayName, col));
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
                    var column = new ColumnInfo(col);
                    list.Add(CreateColumnItem(column, displayName));
                    columnCandidates.Add((displayName, column));
                }
            }
        }
        AddAmbiguousQualifiedColumns(list, columnCandidates);
    }

    private static HashSet<string> GetRelationNamesAtCursor(Token<NzToken>[] tokens, int cursorPosition)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokenDepths = new int[tokens.Length];
        var lastSelectByDepth = new Dictionary<int, int>();
        var depth = 0;
        var tokenCount = 0;

        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Span.Position.Absolute >= cursorPosition)
                break;

            var kind = tokens[i].Kind;
            if (kind == NzToken.LParen)
            {
                tokenDepths[i] = depth;
                depth++;
            }
            else if (kind == NzToken.RParen)
            {
                depth = Math.Max(0, depth - 1);
                tokenDepths[i] = depth;
            }
            else
            {
                tokenDepths[i] = depth;
                if (kind == NzToken.Select)
                    lastSelectByDepth[depth] = i;
            }

            tokenCount = i + 1;
        }

        // Include the current query and enclosing query scopes so correlated
        // subqueries can still complete columns from their outer FROM clauses.
        for (var queryDepth = 0; queryDepth <= depth; queryDepth++)
        {
            if (!lastSelectByDepth.TryGetValue(queryDepth, out var selectIndex))
                continue;

            var queryTokens = new List<Token<NzToken>>();
            for (var i = selectIndex; i < tokenCount; i++)
            {
                if (tokenDepths[i] != queryDepth)
                    continue;
                if (tokens[i].Kind == NzToken.Semicolon)
                    break;
                queryTokens.Add(tokens[i]);
            }

            foreach (var reference in ExtractTableReferences(queryTokens.ToArray()))
            {
                names.Add(reference.TableName);
                if (reference.Alias is not null)
                    names.Add(reference.Alias);
            }
        }

        return names;
    }

    private static void AddAmbiguousQualifiedColumns(
        List<CompletionItem> list,
        IEnumerable<(string Qualifier, ColumnInfo Column)> candidates)
    {
        foreach (var group in candidates
                     .GroupBy(entry => entry.Column.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Select(entry => entry.Qualifier)
                         .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            foreach (var entry in group)
            {
                var qualified = $"{entry.Qualifier}.{entry.Column.Name}";
                list.Add(new CompletionItem(
                    qualified,
                    CompletionKind.Column,
                    Detail: entry.Column.DataType,
                    Priority: 3,
                    InsertText: qualified,
                    Documentation: entry.Column.Description));
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
                    list.Add(CreateColumnItem(col, qualifier));
                return;
            }

            // Try as direct table name
            if (_schema is not null)
            {
                var directInfo = LookupTable(null, null, qualifier);
                if (directInfo?.Columns is { Count: > 0 })
                {
                    foreach (var col in directInfo.Columns)
                        list.Add(CreateColumnItem(col, qualifier));
                    return;
                }
            }

            // AST didn't find the alias â€” fall through to token-based
        }

        // Fallback: token-based alias resolution
        // CTEs shadow real tables â€” check scope collector first
        var cteColumns = _lastScopeCollector?.GetCteColumns(qualifier, _cursorPosition);
        if (cteColumns is { Count: > 0 })
        {
            foreach (var col in cteColumns)
                list.Add(CreateColumnItem(col, qualifier));
            return;
        }

        // Resolve alias to full table path (database..table, db.schema.table, etc.)
        if (TryAddColumnsFromResolvedTablePath(list, tokens, qualifier))
            return;

        // Try alias resolution first: FROM t alias â†’ alias resolves to t
        var resolvedName = CompletionAliasResolver.ResolveAlias(tokens, qualifier);
        if (resolvedName is not null)
        {
            // Check if resolved name is a CTE/temp-table in scope
            var resolvedCteCols = _lastScopeCollector?.GetCteColumns(resolvedName, _cursorPosition);
            if (resolvedCteCols is { Count: > 0 })
            {
                foreach (var col in resolvedCteCols)
                    list.Add(CreateColumnItem(col, qualifier));
                return;
            }

            // Check schema provider for resolved name (qualified path first)
            if (_schema is not null)
            {
                var resolvedPath = CompletionAliasResolver.ResolveTablePath(tokens, qualifier)
                                   ?? CompletionAliasResolver.ResolveTablePath(tokens, resolvedName);
                if (resolvedPath is { } path)
                {
                    var pathInfo = LookupTable(path.Database, path.Schema, path.Name);
                    if (pathInfo?.Columns is { Count: > 0 })
                    {
                        foreach (var col in pathInfo.Columns)
                            list.Add(CreateColumnItem(col, qualifier));
                        return;
                    }
                }

                var info = LookupTable(null, null, resolvedName);
                if (info?.Columns is { Count: > 0 })
                {
                    foreach (var col in info.Columns)
                        list.Add(CreateColumnItem(col, qualifier));
                    return;
                }
            }
        }

        // Try as a direct table name
        if (_schema is not null)
        {
            var directInfo = LookupTable(null, null, qualifier);
            if (directInfo?.Columns is { Count: > 0 })
            {
                foreach (var col in directInfo.Columns)
                    list.Add(CreateColumnItem(col, qualifier));
            }
        }
    }

    private bool TryAddColumnsFromResolvedTablePath(List<CompletionItem> list, Token<NzToken>[] tokens, string qualifier)
    {
        if (_schema is null) return false;

        var tablePath = CompletionAliasResolver.ResolveTablePath(tokens, qualifier);
        if (tablePath is not { } path) return false;

        var info = CompletionSchemaLookup.GetTable(_schema, _dialect, path.Database, path.Schema, path.Name);
        // Empty column list means deferred hydration â€” treat as miss so the host can lazy-load.
        if (info?.Columns is not { Count: > 0 }) return false;

        foreach (var col in info.Columns)
            list.Add(CreateColumnItem(col, qualifier));

        return true;
    }

    // ====== Table Reference Extraction ======

    private static string ExtractQualifier(Token<NzToken>[] tokens)
    {
        for (int i = tokens.Length - 1; i > 0; i--)
        {
            if (tokens[i].Kind == NzToken.Dot &&
                (tokens[i - 1].Kind.IsIdentifierLike()
                 || IsKeywordUsableAsName(tokens[i - 1].Kind)))
            {
                return tokens[i - 1].ToIdentifierText();
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

            if (k.IsIdentifierLike())
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
        while (i < tokens.Length && tokens[i].Kind.IsIdentifierLike())
        {
            if (firstIdent is null)
                firstIdent = tokens[i].ToIdentifierText();
            else if (secondIdent is null)
                secondIdent = tokens[i].ToIdentifierText();
            else
                thirdIdent = tokens[i].ToIdentifierText();
            i++;

            bool consumed = false;

            // Single dot: schema.table or db.schema.table part separator
            if (i < tokens.Length && tokens[i].Kind == NzToken.Dot &&
                i + 1 < tokens.Length && tokens[i + 1].Kind.IsIdentifierLike())
            {
                i++;
                consumed = true;
                continue;
            }

            // Double dot (db..table)
            if (i < tokens.Length && tokens[i].Kind == NzToken.Dot &&
                i + 1 < tokens.Length && tokens[i + 1].Kind == NzToken.Dot &&
                i + 2 < tokens.Length && tokens[i + 2].Kind.IsIdentifierLike())
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

        if (i < tokens.Length && tokens[i].Kind.IsIdentifierLike())
        {
            var candidate = tokens[i].ToIdentifierText();
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
        CompletionAliasResolver.TablePath? path = null;
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Kind == NzToken.Into)
            {
                var parsed = CompletionAliasResolver.ParseTablePathAt(tokens, i + 1);
                if (parsed.Consumed > 0) path = parsed.Path;
                break;
            }
        }
        if (path is null) return;
        var tableName = path.Value.Name;
        var table = LookupTable(path.Value.Database, path.Value.Schema, tableName);
        if (table?.Columns is null) return;

        foreach (var col in table.Columns)
            list.Add(CreateColumnItem(col, tableName, priority: 5));
    }

    private void AddAlterTablePhaseCompletions(List<CompletionItem> list, Token<NzToken>[] tokens)
    {
        var phase = AlterTableCompletion.AnalyzePhase(tokens);
        if (AlterTableCompletion.PhaseNeedsTableColumns(phase))
        {
            var tableName = ExtractAlterTableName(tokens);
            if (_schema is not null
                && tableName is not null
                && LookupTable(null, null, tableName)?.Columns is { } cols)
            {
                foreach (var col in cols)
                    list.Add(CreateColumnItem(col, tableName, priority: 5));
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
        while (i < tokens.Length && tokens[i].Kind.IsIdentifierLike())
        {
            if (first is null) first = tokens[i].ToIdentifierText();
            else if (second is null) second = tokens[i].ToIdentifierText();
            else { third = tokens[i].ToIdentifierText(); i++; break; }
            i++;
        }

        if (first is null) return (new CompletionAliasResolver.TablePath(string.Empty, null, null), 0);

        if (i < tokens.Length && tokens[i].Kind == NzToken.Dot &&
            i + 1 < tokens.Length && tokens[i + 1].Kind.IsIdentifierLike())
        {
            second ??= tokens[i + 1].ToIdentifierText();
            i += 2;
        }

        if (i < tokens.Length && tokens[i].Kind == NzToken.Dot &&
            i + 1 < tokens.Length && tokens[i + 1].Kind == NzToken.Dot &&
            i + 2 < tokens.Length && tokens[i + 2].Kind.IsIdentifierLike())
        {
            third = tokens[i + 2].ToIdentifierText();
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

    private static readonly string[] BuiltInVariableNames =
    {
        "ROWCOUNT", "SQLCODE", "SQLSTATE", "ERROR", "MESSAGE"
    };

    private static readonly HashSet<string> VariableTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "INT", "INTEGER", "INT2", "INT4", "INT8", "BIGINT", "SMALLINT", "BYTEINT",
        "FLOAT", "FLOAT4", "FLOAT8", "REAL", "DOUBLE", "NUMERIC", "DECIMAL",
        "VARCHAR", "CHAR", "NCHAR", "NVARCHAR", "BPCHAR", "CHARACTER", "TEXT",
        "DATE", "TIME", "TIMESTAMP", "BOOLEAN", "BOOL", "VARRAY", "RECORD", "ALIAS"
    };

    private List<CompletionItem> TryGetVariableCompletions(string sql, int cursorPosition)
    {
        var (sigil, prefix, valid) = ResolveVariableSigil(sql, cursorPosition);
        if (!valid)
            return [];

        var closing = sigil is "{" or "${" ? "}" : string.Empty;
        var items = new List<CompletionItem>();

        foreach (var name in BuiltInVariableNames)
        {
            if (prefix.Length == 0 || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                items.Add(new CompletionItem($"{sigil}{name}{closing}", CompletionKind.Variable, Priority: 15));
        }

        foreach (var declared in GetDeclaredVariables(sql, cursorPosition))
        {
            if (prefix.Length > 0 && !declared.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var label = $"{sigil}{declared.Label}{closing}";
            if (items.All(item => !item.Label.Equals(label, StringComparison.OrdinalIgnoreCase)))
                items.Add(new CompletionItem(label, CompletionKind.Variable, Priority: 15));
        }

        return items;
    }

    /// <summary>
    /// Resolves the sigil and typed prefix for script/procedure variable forms:
    /// <c>&amp;name</c>, <c>$name</c>, <c>{name}</c> and <c>${name}</c>.
    /// </summary>
    private static (string Sigil, string Prefix, bool Valid) ResolveVariableSigil(string sql, int cursorPosition)
    {
        var start = cursorPosition;
        while (start > 0 && (char.IsLetterOrDigit(sql[start - 1]) || sql[start - 1] == '_'))
            start--;
        var prefix = sql[start..cursorPosition];

        if (start > 0)
        {
            var before = sql[start - 1];
            if (before is '&' or '$')
                return (before.ToString(), prefix, true);
            if (before == '{')
                return start >= 2 && sql[start - 2] == '$' ? ("${", prefix, true) : ("{", prefix, true);
        }

        if (cursorPosition > 0)
        {
            var last = sql[cursorPosition - 1];
            if (last is '&' or '$')
                return (last.ToString(), string.Empty, true);
            if (last == '{')
                return cursorPosition >= 2 && sql[cursorPosition - 2] == '$'
                    ? ("${", string.Empty, true)
                    : ("{", string.Empty, true);
        }

        return (string.Empty, prefix, false);
    }

    /// <summary>Collects declared variables by pairing identifiers with known type names.</summary>
    private List<CompletionItem> GetDeclaredVariables(string sql, int cursorPosition)
    {
        var names = new List<CompletionItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var tokens = DialectRuntime.Tokenize(sql, _dialect).ToArray();
            var procedureStartIndex = -1;
            var declareIndex = -1;
            var bodyBeginIndex = -1;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Span.Position.Absolute >= cursorPosition)
                    break;
                var tokenText = tokens[i].ToStringValue();
                if (tokenText.Equals("PROCEDURE", StringComparison.OrdinalIgnoreCase))
                {
                    procedureStartIndex = i;
                    declareIndex = -1;
                    bodyBeginIndex = -1;
                }
                else if (procedureStartIndex >= 0 && tokenText.Equals("END_PROC", StringComparison.OrdinalIgnoreCase))
                {
                    procedureStartIndex = -1;
                    declareIndex = -1;
                    bodyBeginIndex = -1;
                }
                else if (procedureStartIndex >= 0 && tokenText.Equals("DECLARE", StringComparison.OrdinalIgnoreCase))
                {
                    declareIndex = i;
                    bodyBeginIndex = -1;
                }
                else if (declareIndex >= 0 && bodyBeginIndex < 0
                         && tokenText.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
                {
                    bodyBeginIndex = i;
                }
            }

            // Only expose declarations from a PL/SQL DECLARE section while the
            // cursor is in its executable body. This avoids treating DDL columns
            // such as CREATE TABLE t (id INT4) as variables.
            if (procedureStartIndex < 0 || declareIndex < procedureStartIndex || bodyBeginIndex <= declareIndex)
                return names;

            for (var i = declareIndex + 1; i + 1 < bodyBeginIndex; i++)
            {
                if (!tokens[i].Kind.IsIdentifierLike())
                    continue;
                if (!VariableTypeNames.Contains(tokens[i + 1].ToStringValue()))
                    continue;

                var name = tokens[i].ToIdentifierText();
                if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    names.Add(new CompletionItem(name, CompletionKind.Variable, Priority: 15));
            }
        }
        catch
        {
            // Declaration scanning is best-effort and must never break completion.
        }

        return names;
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

    private Token<NzToken>[]? TokenizePrefix(string prefix)
    {
        try
        {
            var result = DialectRuntime.Tokenize(prefix, _dialect);
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

    /// <summary>
    /// Column completion: Detail prefers data type; falls back to qualifier.Name when type is unknown.
    /// </summary>
    private static CompletionItem CreateColumnItem(ColumnInfo col, string qualifier, int priority = 1)
    {
        var detail = string.IsNullOrWhiteSpace(col.DataType)
            ? $"{qualifier}.{col.Name}"
            : col.DataType;
        return new CompletionItem(
            col.Name,
            CompletionKind.Column,
            Detail: detail,
            Documentation: col.Description,
            Priority: priority);
    }

    private static CompletionItem CreateColumnItem(string columnName, string qualifier, int priority = 1)
        => new(columnName, CompletionKind.Column, Detail: $"{qualifier}.{columnName}", Priority: priority);
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

    /// <summary>
    /// Keywords valid after a completed table reference / alias in a SELECT FROM clause.
    /// </summary>
    public static readonly string[] FromContinuationKeywords =
    {
        "JOIN", "INNER JOIN", "LEFT JOIN", "LEFT OUTER JOIN",
        "RIGHT JOIN", "RIGHT OUTER JOIN", "FULL JOIN", "FULL OUTER JOIN",
        "CROSS JOIN", "NATURAL JOIN",
        "WHERE", "GROUP BY", "HAVING", "ORDER BY", "LIMIT", "OFFSET", "FETCH"
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
