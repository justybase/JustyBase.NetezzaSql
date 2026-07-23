using System.Diagnostics;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using System.Text.RegularExpressions;

namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>
/// Configuration for a single lint analysis run.
/// </summary>
public readonly record struct LintConfig(
    string Sql,
    ISchemaProvider? Schema = null,
    string? DocumentUri = null,
    int? MetadataEpoch = null,
    CancellationToken CancellationToken = default,
    IReadOnlyList<LintRule>? AdditionalRules = null,
    IDictionary<string, RuleSeverityConfig>? RuleSeverities = null
);

/// <summary>
/// Result of a lint analysis run.
/// </summary>
public readonly record struct LintResult(
    IReadOnlyList<LintIssue> Issues,
    int RuleCount,
    int ParserErrorCount,
    int VisitorErrorCount,
    bool UsedCache
);

/// <summary>
/// Pure lint engine — no UI dependencies. Separates lint logic from presentation.
/// Combines cheap (regex) rules with expensive (parser+visitor) semantic analysis.
/// Uses QualityRuleRegistry as the single source of truth for rule configuration.
/// Port of sqlQualityEngine.ts from the reference TypeScript project.
/// </summary>
public sealed class LintEngine : IDisposable
{
    private static readonly Regex ScriptScopeStatementPattern = new(
        @"^\s*(?:(?:CREATE\s+(?:(?:TEMP(?:ORARY)?\s+)?TABLE|EXTERNAL\s+TABLE|VIEW|OR\s+REPLACE\s+VIEW|PROCEDURE))|(?:DROP\s+(?:TABLE|VIEW|PROCEDURE))|(?:ALTER\s+TABLE))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly ParsingRuntime _parsingRuntime;
    private readonly bool _ownsParsingRuntime;
    private readonly DocumentValidationSession _validationSession = new();
    private readonly QualityRuleRegistry _registry;
    private readonly LintQueue _queue;
    private readonly LintEngineMetrics _metrics = new();

    /// <summary>
    /// Create engine with all built-in NZ and NZP rules.
    /// </summary>
    public LintEngine() : this(new QualityRuleRegistry(), null) { }

    /// <summary>
    /// Create engine with optional shared parse runtime (e.g. from DocumentParsingCoordinator).
    /// </summary>
    public LintEngine(ParsingRuntime? sharedParsingRuntime)
        : this(new QualityRuleRegistry(), sharedParsingRuntime) { }

    /// <summary>
    /// Create engine with a custom QualityRuleRegistry.
    /// </summary>
    public LintEngine(QualityRuleRegistry registry)
        : this(registry, null) { }

    /// <summary>
    /// Create engine with a custom set of rules (wraps them in a QualityRuleRegistry).
    /// </summary>
    public LintEngine(IEnumerable<LintRule> customRules)
        : this(new QualityRuleRegistry(customRules), null) { }

    private LintEngine(QualityRuleRegistry registry, ParsingRuntime? sharedParsingRuntime)
    {
        _parsingRuntime = sharedParsingRuntime ?? new ParsingRuntime();
        _ownsParsingRuntime = sharedParsingRuntime is null;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _queue = new LintQueue(_registry);
    }

    /// <summary>
    /// Get the underlying QualityRuleRegistry. Use this to configure rule severities.
    /// </summary>
    public QualityRuleRegistry Registry => _registry;

    /// <summary>
    /// Get the LintQueue that provides priority-sorted rule access.
    /// </summary>
    public LintQueue Queue => _queue;

    /// <summary>
    /// Get the cheap (regex-only) rules from the registry.
    /// </summary>
    public IReadOnlyList<LintRule> CheapRules => _registry.CheapRules;

    /// <summary>
    /// Get the expensive (parser+visitor) rules from the registry.
    /// </summary>
    public IReadOnlyList<LintRule> ExpensiveRules => _registry.ExpensiveRules;

    /// <summary>
    /// Get the current performance metrics snapshot.
    /// </summary>
    public LintMetricsSnapshot Metrics => _metrics.Snapshot();

    /// <summary>
    /// Reset all performance metrics counters.
    /// </summary>
    public void ResetMetrics() => _metrics.Reset();

    /// <summary>
    /// Run only cheap rules (regex), ordered by priority (highest first).
    /// Fast enough for every keystroke.
    /// Uses QualityRuleRegistry to resolve effective severities.
    /// Per-call severities override registry settings for this invocation only
    /// and can re-enable rules that are disabled (Off) in the registry.
    /// </summary>
    public List<LintIssue> RunCheapRules(string sql,
        IDictionary<string, RuleSeverityConfig>? perCallSeverities = null)
    {
        var sw = Stopwatch.StartNew();
        var issues = new List<LintIssue>();
        // Iterate cheap rules in priority order via LintQueue.
        // Rules disabled (Off) in the registry are excluded from the queue.
        // Per-call overrides can re-enable Off rules — iterate AllRules when present.
        var rulesToCheck = perCallSeverities is not null && perCallSeverities.Count > 0
            ? _registry.AllRules.Where(r => r.Cost == RuleCost.Cheap)
                .Select(r => (Rule: r, Priority: GetEffectivePriority(r.Id)))
                .OrderByDescending(x => x.Priority).ThenBy(x => x.Rule.Id)
                .Cast<(LintRule Rule, int Priority)>()
            : (IEnumerable<(LintRule Rule, int Priority)>)_queue.CheapRules;

        foreach (var (rule, _) in rulesToCheck)
        {
            var severity = ResolveSeverity(rule, perCallSeverities);
            if (severity == RuleSeverityConfig.Off) continue;
            var effectiveSeverity = NzLintRules.MapSeverity(severity);
            foreach (var issue in rule.Check(sql))
                issues.Add(issue with { Severity = effectiveSeverity });
        }

        sw.Stop();
        _metrics.RecordCheapRun(sw.Elapsed);
        return issues;
    }

    private int GetEffectivePriority(string ruleId) => _registry.GetEffectivePriority(ruleId);

    /// <summary>
    /// DDL changes the script catalog, so all following statements must be
    /// revalidated even when their own text is unchanged.
    /// </summary>
    private static IReadOnlyList<int> ExpandDirtyIndicesForScriptContext(DocumentValidationState state)
    {
        var dirty = state.Diff.DirtyIndices;
        if (dirty.Count == 0 || state.NextIndex.Statements.Count == 0)
            return dirty;

        var firstScopeChange = int.MaxValue;
        foreach (var index in dirty)
        {
            if (index < state.NextIndex.Statements.Count &&
                ScriptScopeStatementPattern.IsMatch(state.NextIndex.Statements[index].Sql))
            {
                firstScopeChange = Math.Min(firstScopeChange, index);
            }

            if (state.PreviousIndex is { } previous && index < previous.Statements.Count &&
                ScriptScopeStatementPattern.IsMatch(previous.Statements[index].Sql))
            {
                firstScopeChange = Math.Min(firstScopeChange, index);
            }
        }

        if (firstScopeChange == int.MaxValue)
            return dirty;

        return Enumerable.Range(firstScopeChange,
            state.NextIndex.Statements.Count - firstScopeChange).ToArray();
    }

    private RuleSeverityConfig ResolveSeverity(LintRule rule,
        IDictionary<string, RuleSeverityConfig>? perCallSeverities)
    {
        if (perCallSeverities is not null && perCallSeverities.TryGetValue(rule.Id, out var perCall))
            return perCall;
        return _registry.GetEffectiveSeverity(rule.Id);
    }

    /// <summary>
    /// Run expensive analysis: tokenize + parse + visitor.
    /// Tracks timing and cache hit/miss in LintEngineMetrics.
    /// Uses caches to avoid re-parsing unchanged content.
    /// Returns parser errors + visitor errors as LintIssues.
    /// </summary>
    public LintResult RunExpensiveAnalysis(LintConfig config)
    {
        var sw = Stopwatch.StartNew();
        var issues = new List<LintIssue>();
        var sql = config.Sql;
        var schema = config.Schema;
        var documentUri = config.DocumentUri ?? "default";
        var epoch = config.MetadataEpoch;

        if (string.IsNullOrEmpty(sql) || schema is null)
        {
            // Not a real analysis run — don't record metrics (would skew cache hit ratio)
            return new LintResult(issues, _registry.ExpensiveRules.Count, 0, 0, false);
        }

        var ct = config.CancellationToken;
        if (ct.IsCancellationRequested)
            return new LintResult(issues, _registry.ExpensiveRules.Count, 0, 0, false);

        // Step A: Statement index + diff
        var state = _validationSession.PrepareDocument(documentUri, sql);
        var effectiveDirtyIndices = ExpandDirtyIndicesForScriptContext(state);

        // Fast path: entire document unchanged
        if (effectiveDirtyIndices.Count == 0 && state.PreviousIndex is not null)
        {
            var allCached = new List<LintIssue>();
            var allStatementsCached = true;
            foreach (var stmt in state.NextIndex.Statements)
            {
                var cached = _validationSession.GetCachedDiagnostics(documentUri, stmt, epoch);
                if (cached is not null)
                    allCached.AddRange(cached);
                else
                    allStatementsCached = false;
            }
            if (allStatementsCached)
            {
                sw.Stop();
                _metrics.RecordExpensiveRun(sw.Elapsed, cacheHit: true);
                return new LintResult(allCached, _registry.ExpensiveRules.Count, 0, 0, true);
            }
        }

        if (ct.IsCancellationRequested)
            return new LintResult(issues, _registry.ExpensiveRules.Count, 0, 0, false);

        // Step B: Parse-level cache
        var parseResult = _parsingRuntime.Parse(sql);

        if (ct.IsCancellationRequested)
            return new LintResult(issues, _registry.ExpensiveRules.Count, 0, 0, false);

        // Step C: Only visit changed statements
        var dirtySet = new HashSet<int>(effectiveDirtyIndices);
        var stmtIndex = 0;
        var parserErrorCount = 0;
        var visitorErrorCount = 0;

        // Collect parser errors
        var seenErrors = new HashSet<(string message, int offset)>();
        foreach (var structural in NzSqlStructuralScanner.Scan(sql))
        {
            if (!seenErrors.Add((structural.Message, structural.Position.Absolute))) continue;
            issues.Add(new LintIssue(
                structural.Code, structural.Message,
                structural.Severity == "error" ? LintSeverity.Error : LintSeverity.Warning,
                structural.Position.Absolute,
                structural.Position.Absolute + 1,
                structural.Position.Line, structural.Position.Column,
                structural.EndLine, structural.EndColumn));
            parserErrorCount++;
        }

        foreach (var perr in parseResult.Errors)
        {
            if (perr.Position.Absolute >= sql.Length) continue;
            if (!seenErrors.Add((perr.Message, perr.Position.Absolute)))
                continue;

            var offset = perr.Position.Absolute;
            int perrEndOffset = perr.EndColumn > 0
                ? offset + Math.Max(perr.EndColumn - perr.Position.Column, 1)
                : offset + 1;

            issues.Add(new LintIssue(
                perr.Code, perr.Message,
                perr.Severity == "error" ? LintSeverity.Error : LintSeverity.Warning,
                offset, perrEndOffset,
                perr.Position.Line, perr.Position.Column,
                perr.EndLine, perr.EndColumn));
            parserErrorCount++;
        }

        // Step D: Visit each statement
        var scriptScope = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var stmt in parseResult.Statements)
        {
            ct.ThrowIfCancellationRequested();

            var visitor = new NzSqlVisitor(schema);
            visitor.SeedMultiStatementScope(scriptScope.Values);

            if (dirtySet.Count == 0 || dirtySet.Contains(stmtIndex))
            {
                // Dirty statement — run visitor + expensive rules
                visitor.Visit(stmt);

                var stmtIssues = new List<LintIssue>();

                // Visitor errors
                foreach (var err in visitor.Errors)
                {
                    if (err.Position.Absolute >= sql.Length) continue;

                    int endOffset = err.EndColumn > 0
                        ? err.Position.Absolute + Math.Max(err.EndColumn - err.Position.Column, 1)
                        : err.Position.Absolute + 1;

                    stmtIssues.Add(new LintIssue(
                        err.Code, err.Message,
                        err.Severity.ToLowerInvariant() switch
                        {
                            "error" => LintSeverity.Error,
                            "information" => LintSeverity.Information,
                            "hint" => LintSeverity.Hint,
                            _ => LintSeverity.Warning
                        },
                        err.Position.Absolute, endOffset,
                        err.Position.Line, err.Position.Column, err.EndLine, err.EndColumn, err.SuggestedFix));
                    visitorErrorCount++;
                }

                // Expensive rules (AST-based), in priority order with registry severity resolution
                foreach (var (rule, _) in _queue.ExpensiveRules)
                {
                    var severity = _registry.GetEffectiveSeverity(rule.Id);
                    if (severity == RuleSeverityConfig.Off) continue;
                    var effectiveSeverity = NzLintRules.MapSeverity(severity);
                    foreach (var issue in rule.CheckStatement(stmt))
                        stmtIssues.Add(issue with { Severity = effectiveSeverity });
                }

                issues.AddRange(stmtIssues);

                // Cache diagnostics
                if (stmtIndex < state.NextIndex.Statements.Count)
                {
                    var boundary = state.NextIndex.Statements[stmtIndex];
                    _validationSession.StoreStatementDiagnostics(
                        documentUri, boundary, stmtIssues, epoch);
                }
            }
            else
            {
                // Unchanged statement — use cached diagnostics
                if (stmtIndex < state.NextIndex.Statements.Count)
                {
                    var boundary = state.NextIndex.Statements[stmtIndex];
                    var cached = _validationSession.GetCachedDiagnostics(
                        documentUri, boundary, epoch);
                    if (cached is not null)
                        issues.AddRange(cached);
                }

                // Rebuild script scope even when diagnostics are reused. A
                // later statement must still see tables created by an
                // unchanged earlier statement.
                visitor.Visit(stmt);
            }

            scriptScope = visitor.GetMultiStatementScopeTables()
                .ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);

            if (stmt is DropStatement drop)
            {
                foreach (var target in drop.Targets)
                    scriptScope.Remove(target.Name);
            }

            stmtIndex++;
        }

        // Step E: Commit the new statement index
        _validationSession.CommitDocumentIndex(documentUri, state.NextIndex);

        sw.Stop();
        var cacheHit = parseResult.Valid && effectiveDirtyIndices.Count == 0;
        _metrics.RecordExpensiveRun(sw.Elapsed, cacheHit);
        return new LintResult(
            issues,
            _registry.ExpensiveRules.Count,
            parserErrorCount,
            visitorErrorCount,
            cacheHit
        );
    }

    /// <summary>
    /// Run full lint: cheap rules + expensive analysis.
    /// </summary>
    public LintResult RunFullLint(LintConfig config)
    {
        // 1. Cheap rules — always run (fast regex)
        var cheapIssues = RunCheapRules(config.Sql, config.RuleSeverities);

        // 2. Expensive analysis — only if schema is available
        if (config.Schema is not null)
        {
            var expensiveResult = RunExpensiveAnalysis(config);
            cheapIssues.AddRange(expensiveResult.Issues);
            return new LintResult(cheapIssues, _registry.CheapRules.Count + expensiveResult.RuleCount,
                expensiveResult.ParserErrorCount,
                expensiveResult.VisitorErrorCount,
                expensiveResult.UsedCache);
        }

        return new LintResult(cheapIssues, _registry.CheapRules.Count, 0, 0, false);
    }

    /// <summary>
    /// Invalidate cached data for a document.
    /// </summary>
    public void InvalidateDocument(string documentUri)
    {
        _validationSession.InvalidateDocument(documentUri);
    }

    /// <summary>
    /// Clear all caches.
    /// </summary>
    public void Clear()
    {
        _parsingRuntime.Clear();
        _validationSession.Clear();
    }

    public void Dispose()
    {
        if (_ownsParsingRuntime)
            _parsingRuntime.Dispose();
        _validationSession.Dispose();
    }
}
