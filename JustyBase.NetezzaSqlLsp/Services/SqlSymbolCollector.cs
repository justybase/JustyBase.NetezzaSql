using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using Superpower.Model;
using LspRange = JustyBase.NetezzaSqlLsp.Protocol.Range;

namespace JustyBase.NetezzaSqlLsp.Services;

internal enum SymbolResolutionKind
{
    Cte,
    Alias,
    GlobalObject
}

internal sealed record SymbolOccurrence(
    int Id,
    string Name,
    SymbolKind Kind,
    LspRange Range,
    int StartAbsolute,
    int EndAbsolute,
    bool IsDefinition,
    bool IsStatement,
    int? DefinitionId,
    string? ContainerName
);

internal sealed class SymbolIndex
{
    private readonly IReadOnlyList<SymbolOccurrence> _occurrences;
    private readonly Dictionary<int, SymbolOccurrence> _definitionsById;

    public SymbolIndex(IReadOnlyList<SymbolOccurrence> occurrences)
    {
        _occurrences = occurrences;
        _definitionsById = occurrences
            .Where(o => o.IsDefinition)
            .ToDictionary(o => o.Id, o => o);
    }

    public IReadOnlyList<SymbolOccurrence> Occurrences => _occurrences;

    public SymbolOccurrence? FindOccurrenceAt(int absolute)
    {
        SymbolOccurrence? best = null;
        var bestLength = int.MaxValue;

        foreach (var occ in _occurrences)
        {
            if (occ.IsStatement)
                continue;
            if (absolute < occ.StartAbsolute || absolute >= occ.EndAbsolute)
                continue;

            var length = Math.Max(1, occ.EndAbsolute - occ.StartAbsolute);
            if (length < bestLength)
            {
                best = occ;
                bestLength = length;
            }
        }
        return best;
    }

    public SymbolOccurrence? FindDefinition(int definitionId) =>
        _definitionsById.TryGetValue(definitionId, out var def) ? def : null;

    public IReadOnlyList<SymbolOccurrence> FindReferences(int definitionId, bool includeDeclaration) =>
        _occurrences
            .Where(o => o.DefinitionId == definitionId || (includeDeclaration && o.Id == definitionId))
            .OrderBy(o => o.Range.Start.Line)
            .ThenBy(o => o.Range.Start.Character)
            .ToList();
}

internal sealed class SymbolCollector
{
    private readonly List<SymbolOccurrence> _occurrences = new();
    private readonly Dictionary<(SymbolResolutionKind Kind, string Name), SymbolOccurrence> _globalDefinitions =
        new();
    private int _nextId = 1;

    private sealed class ScopeFrame
    {
        public ScopeFrame? Parent { get; }
        public Dictionary<(SymbolResolutionKind Kind, string Name), SymbolOccurrence> Definitions { get; } = new();

        public ScopeFrame(ScopeFrame? parent)
        {
            Parent = parent;
        }
    }

    public static SymbolIndex Collect(string text)
    {
        var collector = new SymbolCollector();
        collector.Analyze(text);
        return new SymbolIndex(collector._occurrences);
    }

    private void Analyze(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var lineStarts = LspTextUtilities.ComputeLineStarts(text);

        Token<NzToken>[] tokens;
        try
        {
            tokens = NzLexer.Tokenize(text).ToArray();
        }
        catch
        {
            return;
        }

        var parser = new NzSqlParser(tokens);
        var currentTokenIndex = 0;
        var globalScope = new ScopeFrame(null);

        while (currentTokenIndex < tokens.Length)
        {
            while (currentTokenIndex < tokens.Length && tokens[currentTokenIndex].Kind == NzToken.Semicolon)
                currentTokenIndex++;

            if (currentTokenIndex >= tokens.Length)
                break;

            var remaining = tokens.Skip(currentTokenIndex).ToArray();
            var subParser = new NzSqlParser(remaining);
            var stmt = subParser.Parse();
            if (stmt is null || subParser.Position <= 0)
                break;

            var stmtEndIndex = currentTokenIndex + subParser.Position;
            var stmtStartIndex = currentTokenIndex;
            var stmtScope = new ScopeFrame(globalScope);

            CollectStatement(stmt, tokens, stmtStartIndex, stmtEndIndex, stmtScope, lineStarts);

            currentTokenIndex = stmtEndIndex;
        }
    }

    private void CollectStatement(
        Statement stmt,
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        ScopeFrame scope,
        int[] lineStarts)
    {
        switch (stmt)
        {
            case SelectStatement select:
                CollectSelect(select, tokens, startIndex, endIndex, scope, lineStarts);
                break;
            case InsertStatement insert:
                CollectInsert(insert, tokens, startIndex, endIndex, scope, lineStarts);
                break;
            case UpdateStatement update:
                CollectUpdate(update, tokens, startIndex, endIndex, scope, lineStarts);
                break;
            case DeleteStatement delete:
                CollectDelete(delete, tokens, startIndex, endIndex, scope, lineStarts);
                break;
            case MergeStatement merge:
                CollectMerge(merge, tokens, startIndex, endIndex, scope, lineStarts);
                break;
            case CreateTableStatement createTable:
                CollectCreateObject(createTable.Table.Name, SymbolKind.Struct, "CREATE TABLE", tokens, startIndex, endIndex, scope, lineStarts);
                if (createTable.AsSelect is not null)
                    CollectSelect(createTable.AsSelect, tokens, startIndex, endIndex, scope, lineStarts);
                break;
            case CreateViewStatement createView:
                CollectCreateObject(createView.View.Name, SymbolKind.Struct, "CREATE VIEW", tokens, startIndex, endIndex, scope, lineStarts);
                CollectSelect(createView.Query, tokens, startIndex, endIndex, scope, lineStarts);
                break;
            case CreateProcedureStatement createProc:
                CollectCreateObject(createProc.Procedure.Name, SymbolKind.Function, "CREATE PROCEDURE", tokens, startIndex, endIndex, scope, lineStarts);
                break;
        }
    }

    private void CollectSelect(
        SelectStatement stmt,
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        ScopeFrame scope,
        int[] lineStarts)
    {
        var selectSymbol = AddStatementSymbol("SELECT", SymbolKind.Namespace, tokens, startIndex, endIndex, lineStarts, "SELECT");
        var selectScope = new ScopeFrame(scope);

        // WITH CTEs
        if (stmt.With is not null)
        {
            CollectWithClause(stmt.With, tokens, startIndex, endIndex, selectScope, lineStarts, selectSymbol.Name);
        }

        // FROM/JOIN aliases and table references
        if (stmt.From is not null)
        {
            var fromIdx = FindTokenIndex(tokens, startIndex, endIndex, t => t.Kind == NzToken.From);
            var cursor = fromIdx >= 0 ? fromIdx + 1 : startIndex;
            foreach (var tr in stmt.From)
            {
                cursor = CollectTableReference(tr, tokens, cursor, endIndex, selectScope, lineStarts, selectSymbol.Name);
            }
        }

        // Expressions can reference aliases and CTEs.
        foreach (var item in stmt.SelectList)
            CollectExpression(item.Expression, tokens, selectScope, lineStarts);
        if (stmt.Where is not null)
            CollectExpression(stmt.Where, tokens, selectScope, lineStarts);
        if (stmt.GroupBy is not null)
        {
            foreach (var expr in stmt.GroupBy)
                CollectExpression(expr, tokens, selectScope, lineStarts);
        }
        if (stmt.Having is not null)
            CollectExpression(stmt.Having, tokens, selectScope, lineStarts);
        if (stmt.OrderBy is not null)
        {
            foreach (var order in stmt.OrderBy)
                CollectExpression(order.Expression, tokens, selectScope, lineStarts);
        }

    }

    private void CollectWithClause(
        WithClause withClause,
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        ScopeFrame scope,
        int[] lineStarts,
        string containerName)
    {
        var withIndex = FindTokenIndex(tokens, startIndex, endIndex, t => t.Kind == NzToken.With);
        if (withIndex < 0)
            return;

        var cursor = withIndex + 1;
        if (cursor < tokens.Length && tokens[cursor].Kind == NzToken.Recursive)
            cursor++;

        // Register all CTE names first so forward references can resolve.
        var cteBodies = new List<(CteDefinition Cte, int NameIndex, int BodyOpenIndex, int BodyCloseIndex)>();
        foreach (var cte in withClause.Ctes)
        {
            var nameIndex = FindIdentifierIndex(tokens, cursor, endIndex, cte.Name);
            if (nameIndex < 0)
                break;

            var nameToken = tokens[nameIndex];
            var def = AddDefinition(
                cte.Name,
                SymbolKind.Class,
                LspTextUtilities.ToRange(nameToken, lineStarts),
                nameToken.Span.Position.Absolute,
                nameToken.Span.Position.Absolute + nameToken.Span.Length,
                containerName);
            Register(scope, SymbolResolutionKind.Cte, cte.Name, def);
            cteBodies.Add((cte, nameIndex, -1, -1));

            cursor = nameIndex + 1;
            if (cte.Columns is { Count: > 0 } && cursor < tokens.Length && tokens[cursor].Kind == NzToken.LParen)
            {
                SkipBalancedParens(tokens, ref cursor);
            }
            if (cursor < tokens.Length && tokens[cursor].Kind == NzToken.As)
                cursor++;
            if (cursor < tokens.Length && tokens[cursor].Kind == NzToken.All)
                cursor++;
            if (cursor < tokens.Length && tokens[cursor].Kind == NzToken.LParen)
            {
                var bodyOpen = cursor;
                var bodyClose = FindMatchingParen(tokens, bodyOpen, endIndex);
                if (bodyClose > bodyOpen)
                {
                    cteBodies[^1] = (cte, nameIndex, bodyOpen, bodyClose);
                    cursor = bodyClose + 1;
                }
            }

            if (cursor < tokens.Length && tokens[cursor].Kind == NzToken.Comma)
                cursor++;
        }

        foreach (var (cte, _, bodyOpen, bodyClose) in cteBodies)
        {
            if (bodyOpen > 0 && bodyClose > bodyOpen)
            {
                var bodyStart = bodyOpen + 1;
                var bodyEnd = bodyClose;
                var childScope = new ScopeFrame(scope);
                // CTE body can see all CTEs in the same WITH clause and outer scope.
                CollectSelect(cte.Query, tokens, bodyStart, bodyEnd, childScope, lineStarts);
            }
        }
    }

    private int CollectTableReference(
        TableReference reference,
        Token<NzToken>[] tokens,
        int cursor,
        int endIndex,
        ScopeFrame scope,
        int[] lineStarts,
        string containerName)
    {
        cursor = CollectTableSource(reference.Source, tokens, cursor, endIndex, scope, lineStarts, containerName);
        if (reference.Joins is not null)
        {
            foreach (var join in reference.Joins)
                cursor = CollectTableSource(join.Source, tokens, cursor, endIndex, scope, lineStarts, containerName);
        }
        return cursor;
    }

    private int CollectTableSource(
        TableSource source,
        Token<NzToken>[] tokens,
        int cursor,
        int endIndex,
        ScopeFrame scope,
        int[] lineStarts,
        string containerName)
    {
        if (source.Subquery is not null)
        {
            var openIndex = FindTokenIndex(tokens, cursor, endIndex, t => t.Kind == NzToken.LParen);
            var closeIndex = openIndex >= 0 ? FindMatchingParen(tokens, openIndex, endIndex) : -1;
            if (openIndex >= 0 && closeIndex > openIndex)
            {
                var childScope = new ScopeFrame(scope);
                CollectSelect(source.Subquery, tokens, openIndex + 1, closeIndex, childScope, lineStarts);
                cursor = closeIndex + 1;
            }

            if (!string.IsNullOrWhiteSpace(source.Alias))
            {
                var aliasIndex = FindIdentifierIndex(tokens, cursor, endIndex, source.Alias!);
                if (aliasIndex >= 0)
                {
                    var aliasToken = tokens[aliasIndex];
                    var def = AddDefinition(
                        source.Alias!,
                        SymbolKind.Variable,
                        LspTextUtilities.ToRange(aliasToken, lineStarts),
                        aliasToken.Span.Position.Absolute,
                        aliasToken.Span.Position.Absolute + aliasToken.Span.Length,
                        containerName);
                    Register(scope, SymbolResolutionKind.Alias, source.Alias!, def);
                    cursor = aliasIndex + 1;
                }
            }

            return cursor;
        }

        if (source.Table is null)
            return cursor;

        var tableRange = FindTableNameRange(tokens, cursor, endIndex, source.Table);
        if (tableRange is not null)
        {
            cursor = tableRange.Value.EndIndex + 1;
            var tableLocation = LspTextUtilities.ToRange(
                tokens[tableRange.Value.StartIndex].Span.Position.Absolute,
                tokens[tableRange.Value.EndIndex].Span.Position.Absolute + tokens[tableRange.Value.EndIndex].Span.Length,
                lineStarts);

            if (TryResolve(scope, source.Table.Name, out var resolved))
            {
                AddReferenceOccurrence(
                    source.Table.Name,
                    resolved.Kind,
                    tableLocation,
                    tokens[tableRange.Value.StartIndex].Span.Position.Absolute,
                    tokens[tableRange.Value.EndIndex].Span.Position.Absolute + tokens[tableRange.Value.EndIndex].Span.Length,
                    resolved.Id,
                    containerName);
            }
            else if (TryResolveGlobal(source.Table.Name, out var global))
            {
                AddReferenceOccurrence(
                    source.Table.Name,
                    global.Kind,
                    tableLocation,
                    tokens[tableRange.Value.StartIndex].Span.Position.Absolute,
                    tokens[tableRange.Value.EndIndex].Span.Position.Absolute + tokens[tableRange.Value.EndIndex].Span.Length,
                    global.Id,
                    containerName);
            }
        }

        if (!string.IsNullOrWhiteSpace(source.Alias))
        {
            var aliasIndex = FindIdentifierIndex(tokens, cursor, endIndex, source.Alias!);
            if (aliasIndex >= 0)
            {
                var aliasToken = tokens[aliasIndex];
                var def = AddDefinition(
                    source.Alias!,
                    SymbolKind.Variable,
                    LspTextUtilities.ToRange(aliasToken, lineStarts),
                    aliasToken.Span.Position.Absolute,
                    aliasToken.Span.Position.Absolute + aliasToken.Span.Length,
                    containerName);
                Register(scope, SymbolResolutionKind.Alias, source.Alias!, def);
                cursor = aliasIndex + 1;
            }
        }

        return cursor;
    }

    private void CollectInsert(
        InsertStatement stmt,
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        ScopeFrame scope,
        int[] lineStarts)
    {
        AddStatementSymbol("INSERT", SymbolKind.Namespace, tokens, startIndex, endIndex, lineStarts, "INSERT");
        if (stmt.Target is not null)
        {
            var range = FindTableNameRange(tokens, startIndex, endIndex, stmt.Target);
            if (range is not null)
            {
                var loc = LspTextUtilities.ToRange(
                    tokens[range.Value.StartIndex].Span.Position.Absolute,
                    tokens[range.Value.EndIndex].Span.Position.Absolute + tokens[range.Value.EndIndex].Span.Length,
                    lineStarts);
                if (TryResolve(scope, stmt.Target.Name, out var resolved))
                    AddReferenceOccurrence(
                        stmt.Target.Name,
                        resolved.Kind,
                        loc,
                        tokens[range.Value.StartIndex].Span.Position.Absolute,
                        tokens[range.Value.EndIndex].Span.Position.Absolute + tokens[range.Value.EndIndex].Span.Length,
                        resolved.Id,
                        "INSERT");
                else if (TryResolveGlobal(stmt.Target.Name, out var global))
                    AddReferenceOccurrence(
                        stmt.Target.Name,
                        global.Kind,
                        loc,
                        tokens[range.Value.StartIndex].Span.Position.Absolute,
                        tokens[range.Value.EndIndex].Span.Position.Absolute + tokens[range.Value.EndIndex].Span.Length,
                        global.Id,
                        "INSERT");
            }
        }
        if (stmt.SourceQuery is not null)
            CollectSelect(stmt.SourceQuery, tokens, startIndex, endIndex, new ScopeFrame(scope), lineStarts);
    }

    private void CollectUpdate(
        UpdateStatement stmt,
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        ScopeFrame scope,
        int[] lineStarts)
    {
        AddStatementSymbol("UPDATE", SymbolKind.Namespace, tokens, startIndex, endIndex, lineStarts, "UPDATE");

        if (stmt.Target is not null)
        {
            var range = FindTableNameRange(tokens, startIndex, endIndex, stmt.Target);
            if (range is not null)
            {
                var loc = LspTextUtilities.ToRange(
                    tokens[range.Value.StartIndex].Span.Position.Absolute,
                    tokens[range.Value.EndIndex].Span.Position.Absolute + tokens[range.Value.EndIndex].Span.Length,
                    lineStarts);
                if (TryResolve(scope, stmt.Target.Name, out var resolved))
                    AddReferenceOccurrence(
                        stmt.Target.Name,
                        resolved.Kind,
                        loc,
                        tokens[range.Value.StartIndex].Span.Position.Absolute,
                        tokens[range.Value.EndIndex].Span.Position.Absolute + tokens[range.Value.EndIndex].Span.Length,
                        resolved.Id,
                        "UPDATE");
                else if (TryResolveGlobal(stmt.Target.Name, out var global))
                    AddReferenceOccurrence(
                        stmt.Target.Name,
                        global.Kind,
                        loc,
                        tokens[range.Value.StartIndex].Span.Position.Absolute,
                        tokens[range.Value.EndIndex].Span.Position.Absolute + tokens[range.Value.EndIndex].Span.Length,
                        global.Id,
                        "UPDATE");
            }
        }

        var updateScope = new ScopeFrame(scope);
        if (!string.IsNullOrWhiteSpace(stmt.Alias))
        {
            var aliasIndex = FindIdentifierIndex(tokens, startIndex, endIndex, stmt.Alias!);
            if (aliasIndex >= 0)
            {
                var aliasToken = tokens[aliasIndex];
                var def = AddDefinition(
                    stmt.Alias!,
                    SymbolKind.Variable,
                    LspTextUtilities.ToRange(aliasToken, lineStarts),
                    aliasToken.Span.Position.Absolute,
                    aliasToken.Span.Position.Absolute + aliasToken.Span.Length,
                    "UPDATE");
                Register(updateScope, SymbolResolutionKind.Alias, stmt.Alias!, def);
            }
        }

        foreach (var setItem in stmt.SetItems)
            CollectExpression(setItem.Value, tokens, updateScope, lineStarts);
        if (stmt.From is not null)
        {
            var fromIdx = FindTokenIndex(tokens, startIndex, endIndex, t => t.Kind == NzToken.From);
            var cursor = fromIdx >= 0 ? fromIdx + 1 : startIndex;
            foreach (var tr in stmt.From)
                cursor = CollectTableReference(tr, tokens, cursor, endIndex, updateScope, lineStarts, "UPDATE");
        }
        if (stmt.Where is not null)
            CollectExpression(stmt.Where, tokens, updateScope, lineStarts);
    }

    private void CollectDelete(
        DeleteStatement stmt,
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        ScopeFrame scope,
        int[] lineStarts)
    {
        AddStatementSymbol("DELETE", SymbolKind.Namespace, tokens, startIndex, endIndex, lineStarts, "DELETE");

        var deleteScope = new ScopeFrame(scope);
        if (!string.IsNullOrWhiteSpace(stmt.Alias))
        {
            var aliasIndex = FindIdentifierIndex(tokens, startIndex, endIndex, stmt.Alias!);
            if (aliasIndex >= 0)
            {
                var aliasToken = tokens[aliasIndex];
                var def = AddDefinition(
                    stmt.Alias!,
                    SymbolKind.Variable,
                    LspTextUtilities.ToRange(aliasToken, lineStarts),
                    aliasToken.Span.Position.Absolute,
                    aliasToken.Span.Position.Absolute + aliasToken.Span.Length,
                    "DELETE");
                Register(deleteScope, SymbolResolutionKind.Alias, stmt.Alias!, def);
            }
        }

        if (stmt.Where is not null)
            CollectExpression(stmt.Where, tokens, deleteScope, lineStarts);
    }

    private void CollectMerge(
        MergeStatement stmt,
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        ScopeFrame scope,
        int[] lineStarts)
    {
        AddStatementSymbol("MERGE", SymbolKind.Namespace, tokens, startIndex, endIndex, lineStarts, "MERGE");

        var mergeScope = new ScopeFrame(scope);
        if (!string.IsNullOrWhiteSpace(stmt.TargetAlias))
        {
            var aliasIndex = FindIdentifierIndex(tokens, startIndex, endIndex, stmt.TargetAlias!);
            if (aliasIndex >= 0)
            {
                var aliasToken = tokens[aliasIndex];
                var def = AddDefinition(
                    stmt.TargetAlias!,
                    SymbolKind.Variable,
                    LspTextUtilities.ToRange(aliasToken, lineStarts),
                    aliasToken.Span.Position.Absolute,
                    aliasToken.Span.Position.Absolute + aliasToken.Span.Length,
                    "MERGE");
                Register(mergeScope, SymbolResolutionKind.Alias, stmt.TargetAlias!, def);
            }
        }

        if (stmt.Source.Table is not null)
        {
            var sourceRange = FindTableNameRange(tokens, startIndex, endIndex, stmt.Source.Table);
            if (sourceRange is not null)
            {
                var loc = LspTextUtilities.ToRange(
                    tokens[sourceRange.Value.StartIndex].Span.Position.Absolute,
                    tokens[sourceRange.Value.EndIndex].Span.Position.Absolute + tokens[sourceRange.Value.EndIndex].Span.Length,
                    lineStarts);
                if (TryResolve(mergeScope, stmt.Source.Table.Name, out var resolved))
                    AddReferenceOccurrence(
                        stmt.Source.Table.Name,
                        resolved.Kind,
                        loc,
                        tokens[sourceRange.Value.StartIndex].Span.Position.Absolute,
                        tokens[sourceRange.Value.EndIndex].Span.Position.Absolute + tokens[sourceRange.Value.EndIndex].Span.Length,
                        resolved.Id,
                        "MERGE");
                else if (TryResolveGlobal(stmt.Source.Table.Name, out var global))
                    AddReferenceOccurrence(
                        stmt.Source.Table.Name,
                        global.Kind,
                        loc,
                        tokens[sourceRange.Value.StartIndex].Span.Position.Absolute,
                        tokens[sourceRange.Value.EndIndex].Span.Position.Absolute + tokens[sourceRange.Value.EndIndex].Span.Length,
                        global.Id,
                        "MERGE");
            }
        }

        CollectExpression(stmt.OnCondition, tokens, mergeScope, lineStarts);

        foreach (var clause in stmt.Clauses)
        {
            switch (clause)
            {
                case MergeMatchedUpdateClause u:
                    if (u.Condition is not null) CollectExpression(u.Condition, tokens, mergeScope, lineStarts);
                    foreach (var item in u.SetItems)
                    {
                        CollectExpression(item.Column, tokens, mergeScope, lineStarts);
                        CollectExpression(item.Value, tokens, mergeScope, lineStarts);
                    }
                    if (u.Where is not null) CollectExpression(u.Where, tokens, mergeScope, lineStarts);
                    break;
                case MergeMatchedDeleteClause d:
                    if (d.Condition is not null) CollectExpression(d.Condition, tokens, mergeScope, lineStarts);
                    break;
                case MergeNotMatchedInsertClause i:
                    if (i.Condition is not null) CollectExpression(i.Condition, tokens, mergeScope, lineStarts);
                    foreach (var val in i.Values)
                        CollectExpression(val, tokens, mergeScope, lineStarts);
                    break;
            }
        }
    }

    private void CollectCreateObject(
        string name,
        SymbolKind kind,
        string statementName,
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        ScopeFrame scope,
        int[] lineStarts)
    {
        AddStatementSymbol(statementName, SymbolKind.Namespace, tokens, startIndex, endIndex, lineStarts, statementName);
        var nameIndex = FindIdentifierIndex(tokens, startIndex, endIndex, name);
        if (nameIndex < 0)
            return;

        var token = tokens[nameIndex];
        var def = AddDefinition(
            name,
            kind,
            LspTextUtilities.ToRange(token, lineStarts),
            token.Span.Position.Absolute,
            token.Span.Position.Absolute + token.Span.Length,
            statementName);
        Register(scope, SymbolResolutionKind.GlobalObject, name, def);
        _globalDefinitions[(SymbolResolutionKind.GlobalObject, name.ToUpperInvariant())] = def;
    }

    private void CollectExpression(Expression expr, Token<NzToken>[] tokens, ScopeFrame scope, int[] lineStarts)
    {
        switch (expr)
        {
            case ColumnReference cr when cr.Qualifier is not null:
            {
                var qualified = TryResolve(scope, cr.Qualifier, out var def)
                    ? def
                    : TryResolveGlobal(cr.Qualifier, out var global) ? global : null;
                if (qualified is not null)
                {
                    var range = LspTextUtilities.ToRange(
                        expr.Position.Absolute,
                        expr.Position.Absolute + cr.Qualifier.Length,
                        lineStarts);
                    AddReferenceOccurrence(
                        cr.Qualifier,
                        qualified.Kind,
                        range,
                        expr.Position.Absolute,
                        expr.Position.Absolute + cr.Qualifier.Length,
                        qualified.Id,
                        null);
                }
                break;
            }
            case UnaryExpression unary:
                CollectExpression(unary.Operand, tokens, scope, lineStarts);
                break;
            case BinaryExpression binary:
                CollectExpression(binary.Left, tokens, scope, lineStarts);
                CollectExpression(binary.Right, tokens, scope, lineStarts);
                break;
            case CastExpression castExpr:
                CollectExpression(castExpr.Expression, tokens, scope, lineStarts);
                break;
            case CastFunctionExpression castFn:
                CollectExpression(castFn.Expression, tokens, scope, lineStarts);
                break;
            case ExistsExpression exists:
                CollectNestedSelect(exists.Subquery, tokens, expr.Position.Absolute, scope, lineStarts);
                break;
            case SubqueryExpression subquery:
                CollectNestedSelect(subquery.Query, tokens, expr.Position.Absolute, scope, lineStarts);
                break;
            case InExpression inExpr:
                CollectExpression(inExpr.Left, tokens, scope, lineStarts);
                if (inExpr.Values is not null)
                {
                    foreach (var value in inExpr.Values)
                        CollectExpression(value, tokens, scope, lineStarts);
                }
                if (inExpr.Subquery is not null)
                    CollectNestedSelect(inExpr.Subquery, tokens, expr.Position.Absolute, scope, lineStarts);
                break;
            case BetweenExpression between:
                CollectExpression(between.Value, tokens, scope, lineStarts);
                CollectExpression(between.Low, tokens, scope, lineStarts);
                CollectExpression(between.High, tokens, scope, lineStarts);
                break;
            case IsExpression isExpr:
                CollectExpression(isExpr.Left, tokens, scope, lineStarts);
                break;
            case QuantifiedComparisonExpression quantified:
                CollectExpression(quantified.Left, tokens, scope, lineStarts);
                CollectExpression(quantified.Right, tokens, scope, lineStarts);
                break;
            case FunctionCall fn:
                if (fn.Arguments is not null)
                {
                    foreach (var arg in fn.Arguments)
                        CollectExpression(arg, tokens, scope, lineStarts);
                }
                if (fn.Filter is not null)
                    CollectExpression(fn.Filter.Condition, tokens, scope, lineStarts);
                if (fn.Over is not null)
                {
                    if (fn.Over.PartitionBy is not null)
                    {
                        foreach (var part in fn.Over.PartitionBy)
                            CollectExpression(part, tokens, scope, lineStarts);
                    }
                    if (fn.Over.OrderBy is not null)
                    {
                        foreach (var order in fn.Over.OrderBy)
                            CollectExpression(order.Expression, tokens, scope, lineStarts);
                    }
                }
                break;
            case CaseExpression caseExpr:
                if (caseExpr.Value is not null)
                    CollectExpression(caseExpr.Value, tokens, scope, lineStarts);
                foreach (var whenThen in caseExpr.WhenClauses)
                {
                    CollectExpression(whenThen.When, tokens, scope, lineStarts);
                    CollectExpression(whenThen.Then, tokens, scope, lineStarts);
                }
                if (caseExpr.ElseClause is not null)
                    CollectExpression(caseExpr.ElseClause, tokens, scope, lineStarts);
                break;
            case ExtractExpression extractExpr:
                CollectExpression(extractExpr.Source, tokens, scope, lineStarts);
                break;
        }
    }

    private void CollectNestedSelect(
        SelectStatement nested,
        Token<NzToken>[] tokens,
        int startAbsolute,
        ScopeFrame scope,
        int[] lineStarts)
    {
        var openIndex = FindTokenIndex(tokens, 0, tokens.Length, t =>
            t.Kind == NzToken.LParen && t.Span.Position.Absolute == startAbsolute);
        if (openIndex < 0)
            return;

        var closeIndex = FindMatchingParen(tokens, openIndex, tokens.Length);
        if (closeIndex <= openIndex)
            return;

        CollectSelect(nested, tokens, openIndex + 1, closeIndex, new ScopeFrame(scope), lineStarts);
    }

    private SymbolOccurrence AddStatementSymbol(
        string name,
        SymbolKind kind,
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        int[] lineStarts,
        string? containerName)
    {
        var startToken = tokens[startIndex];
        var endToken = tokens[Math.Max(startIndex, endIndex - 1)];
        var occurrence = new SymbolOccurrence(
            _nextId++,
            name,
            kind,
            LspTextUtilities.ToRange(
                startToken.Span.Position.Absolute,
                endToken.Span.Position.Absolute + endToken.Span.Length,
                lineStarts),
            startToken.Span.Position.Absolute,
            endToken.Span.Position.Absolute + endToken.Span.Length,
            true,
            true,
            null,
            containerName);
        _occurrences.Add(occurrence);
        return occurrence;
    }

    private SymbolOccurrence AddDefinition(
        string name,
        SymbolKind kind,
        LspRange range,
        int startAbsolute,
        int endAbsolute,
        string? containerName)
    {
        var occurrence = new SymbolOccurrence(
            _nextId++,
            name,
            kind,
            range,
            startAbsolute,
            endAbsolute,
            true,
            false,
            null,
            containerName);
        var def = occurrence with { DefinitionId = occurrence.Id };
        _occurrences.Add(def);
        return def;
    }

    private void AddReferenceOccurrence(
        string name,
        SymbolKind kind,
        LspRange range,
        int startAbsolute,
        int endAbsolute,
        int definitionId,
        string? containerName)
    {
        var occurrence = new SymbolOccurrence(
            _nextId++,
            name,
            kind,
            range,
            startAbsolute,
            endAbsolute,
            false,
            false,
            definitionId,
            containerName);
        _occurrences.Add(occurrence);
    }

    private static void Register(ScopeFrame scope, SymbolResolutionKind kind, string name, SymbolOccurrence definition)
    {
        scope.Definitions[(kind, name.ToUpperInvariant())] = definition;
    }

    private bool TryResolve(ScopeFrame scope, string name, out SymbolOccurrence occurrence)
    {
        var upper = name.ToUpperInvariant();
        var current = scope;
        while (current is not null)
        {
            if (current.Definitions.TryGetValue((SymbolResolutionKind.Alias, upper), out occurrence!))
                return true;
            if (current.Definitions.TryGetValue((SymbolResolutionKind.Cte, upper), out occurrence!))
                return true;
            current = current.Parent;
        }

        return TryResolveGlobal(name, out occurrence);
    }

    private bool TryResolveGlobal(string name, out SymbolOccurrence occurrence)
    {
        var upper = name.ToUpperInvariant();
        if (_globalDefinitions.TryGetValue((SymbolResolutionKind.GlobalObject, upper), out occurrence!))
            return true;

        occurrence = null!;
        return false;
    }

    private static int FindTokenIndex(
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        Func<Token<NzToken>, bool> predicate)
    {
        for (int i = Math.Max(0, startIndex); i < tokens.Length && i < endIndex; i++)
        {
            if (predicate(tokens[i]))
                return i;
        }
        return -1;
    }

    private static int FindIdentifierIndex(
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        string name)
    {
        for (int i = Math.Max(0, startIndex); i < tokens.Length && i < endIndex; i++)
        {
            if (!LspTextUtilities.IsIdentifierToken(tokens[i]))
                continue;
            if (string.Equals(LspTextUtilities.NormalizedTokenText(tokens[i]), name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static (int StartIndex, int EndIndex)? FindTableNameRange(
        Token<NzToken>[] tokens,
        int startIndex,
        int endIndex,
        TableName tableName)
    {
        for (int i = Math.Max(0, startIndex); i < tokens.Length && i < endIndex; i++)
        {
            if (!LspTextUtilities.IsIdentifierToken(tokens[i]))
                continue;

            var first = LspTextUtilities.NormalizedTokenText(tokens[i]);
            if (tableName.Database is not null && tableName.Schema is not null)
            {
                if (!MatchesQualified(tokens, i, endIndex, tableName.Database, tableName.Schema, tableName.Name))
                    continue;
                var end = FindQualifiedEnd(tokens, i, endIndex, 3);
                return (i, end);
            }

            if (tableName.Database is not null && tableName.Schema is null)
            {
                if (!MatchesDbDotTable(tokens, i, endIndex, tableName.Database, tableName.Name))
                    continue;
                var end = FindQualifiedEnd(tokens, i, endIndex, 2);
                return (i, end);
            }

            if (tableName.Schema is not null)
            {
                if (!MatchesSchemaTable(tokens, i, endIndex, tableName.Schema, tableName.Name))
                    continue;
                var end = FindQualifiedEnd(tokens, i, endIndex, 2);
                return (i, end);
            }

            if (string.Equals(first, tableName.Name, StringComparison.OrdinalIgnoreCase))
                return (i, i);
        }
        return null;
    }

    private static bool MatchesQualified(
        Token<NzToken>[] tokens,
        int index,
        int endIndex,
        string database,
        string schema,
        string table)
    {
        if (index + 4 >= tokens.Length || index + 4 >= endIndex)
            return false;
        return IsName(tokens[index], database)
            && tokens[index + 1].Kind == NzToken.Dot
            && IsName(tokens[index + 2], schema)
            && tokens[index + 3].Kind == NzToken.Dot
            && IsName(tokens[index + 4], table);
    }

    private static bool MatchesSchemaTable(
        Token<NzToken>[] tokens,
        int index,
        int endIndex,
        string schema,
        string table)
    {
        if (index + 2 >= tokens.Length || index + 2 >= endIndex)
            return false;
        return IsName(tokens[index], schema)
            && tokens[index + 1].Kind == NzToken.Dot
            && IsName(tokens[index + 2], table);
    }

    private static bool MatchesDbDotTable(
        Token<NzToken>[] tokens,
        int index,
        int endIndex,
        string database,
        string table)
    {
        if (index + 2 >= tokens.Length || index + 2 >= endIndex)
            return false;
        if (!IsName(tokens[index], database))
            return false;
        if (tokens[index + 1].Kind != NzToken.Dot)
            return false;
        if (tokens[index + 2].Kind == NzToken.Dot)
        {
            return index + 3 < tokens.Length && index + 3 < endIndex && IsName(tokens[index + 3], table);
        }
        return IsName(tokens[index + 2], table);
    }

    private static int FindQualifiedEnd(Token<NzToken>[] tokens, int index, int endIndex, int identCount)
    {
        var current = index;
        var foundIdents = 0;
        while (current < tokens.Length && current < endIndex)
        {
            if (LspTextUtilities.IsIdentifierToken(tokens[current]))
                foundIdents++;
            if (foundIdents >= identCount)
                return current;
            current++;
        }
        return index;
    }

    private static bool IsName(Token<NzToken> token, string name) =>
        string.Equals(LspTextUtilities.NormalizedTokenText(token), name, StringComparison.OrdinalIgnoreCase);

    private static int FindMatchingParen(Token<NzToken>[] tokens, int openIndex, int endIndex)
    {
        if (openIndex < 0 || openIndex >= tokens.Length || tokens[openIndex].Kind != NzToken.LParen)
            return -1;

        var depth = 0;
        for (int i = openIndex; i < tokens.Length && i < endIndex; i++)
        {
            if (tokens[i].Kind == NzToken.LParen)
                depth++;
            else if (tokens[i].Kind == NzToken.RParen)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static void SkipBalancedParens(Token<NzToken>[] tokens, ref int index)
    {
        if (index < 0 || index >= tokens.Length || tokens[index].Kind != NzToken.LParen)
            return;

        var depth = 0;
        while (index < tokens.Length)
        {
            if (tokens[index].Kind == NzToken.LParen)
                depth++;
            else if (tokens[index].Kind == NzToken.RParen)
            {
                depth--;
                if (depth == 0)
                {
                    index++;
                    return;
                }
            }
            index++;
        }
    }

    private static int FindExpressionEnd(Token<NzToken>[] tokens, int absoluteStart)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Span.Position.Absolute > absoluteStart)
                return tokens[i].Span.Position.Absolute + tokens[i].Span.Length;
        }
        return absoluteStart;
    }
}
