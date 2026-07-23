using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Ast;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Authoring;

internal sealed class NzSymbolCollector
{
    private readonly List<SymbolOccurrence> _occurrences = new();
    private readonly Dictionary<int, SymbolOccurrence> _definitionById = new();
    private int _nextId = 1;

    private sealed class ScopeFrame
    {
        public ScopeFrame? Parent { get; }
        public Dictionary<string, SymbolOccurrence> Ctes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SymbolOccurrence> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ScopeFrame(ScopeFrame? parent) => Parent = parent;
    }

    public static SymbolIndex Collect(string text)
    {
        var collector = new NzSymbolCollector();
        collector.Analyze(text);
        return new SymbolIndex(collector._occurrences, collector._definitionById);
    }

    private void Analyze(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Token<NzToken>[] tokens;
        try
        {
            tokens = NzLexer.Tokenize(text).ToArray();
        }
        catch
        {
            return;
        }

        if (tokens.Length == 0)
            return;

        var globalScope = new ScopeFrame(null);
        var currentTokenIndex = 0;

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
            var stmtScope = new ScopeFrame(globalScope);

            CollectStatement(stmt, tokens, currentTokenIndex, stmtEndIndex, stmtScope);
            currentTokenIndex = stmtEndIndex;
        }
    }

    private void CollectStatement(Statement stmt, Token<NzToken>[] tokens, int startIndex, int endIndex, ScopeFrame scope)
    {
        switch (stmt)
        {
            case SelectStatement select:
                CollectSelect(select, tokens, startIndex, endIndex, scope);
                break;
            case InsertStatement insert:
                CollectInsert(insert, tokens, startIndex, endIndex, scope);
                break;
            case UpdateStatement update:
                CollectUpdate(update, tokens, startIndex, endIndex, scope);
                break;
            case DeleteStatement delete:
                CollectDelete(delete, tokens, startIndex, endIndex, scope);
                break;
            case MergeStatement merge:
                CollectMerge(merge, tokens, startIndex, endIndex, scope);
                break;
        }
    }

    private void CollectSelect(SelectStatement stmt, Token<NzToken>[] tokens, int startIndex, int endIndex, ScopeFrame scope)
    {
        var selectScope = new ScopeFrame(scope);

        if (stmt.With is not null)
            CollectWithClause(stmt.With, tokens, startIndex, endIndex, selectScope);

        if (stmt.From is not null)
        {
            var fromIdx = FindTokenIndex(tokens, startIndex, endIndex, t => t.Kind == NzToken.From);
            var cursor = fromIdx >= 0 ? fromIdx + 1 : startIndex;
            foreach (var tr in stmt.From)
                cursor = CollectTableReference(tr, tokens, cursor, endIndex, selectScope);
        }

        foreach (var item in stmt.SelectList)
            CollectExpression(item.Expression, tokens, selectScope);
        if (stmt.Where is not null)
            CollectExpression(stmt.Where, tokens, selectScope);
        if (stmt.GroupBy is not null)
            foreach (var expr in stmt.GroupBy)
                CollectExpression(expr, tokens, selectScope);
        if (stmt.Having is not null)
            CollectExpression(stmt.Having, tokens, selectScope);
        if (stmt.OrderBy is not null)
            foreach (var order in stmt.OrderBy)
                CollectExpression(order.Expression, tokens, selectScope);
    }

    private void CollectWithClause(WithClause withClause, Token<NzToken>[] tokens, int startIndex, int endIndex, ScopeFrame scope)
    {
        var withIndex = FindTokenIndex(tokens, startIndex, endIndex, t => t.Kind == NzToken.With);
        if (withIndex < 0)
            return;

        var cursor = withIndex + 1;
        if (cursor < tokens.Length && tokens[cursor].Kind == NzToken.Recursive)
            cursor++;

        var cteBodies = new List<(CteDefinition Cte, int BodyOpen, int BodyClose)>();

        foreach (var cte in withClause.Ctes)
        {
            var nameIndex = FindIdentifierIndex(tokens, cursor, endIndex, cte.Name);
            if (nameIndex < 0)
                break;

            var nameToken = tokens[nameIndex];
            var def = AddDefinition(cte.Name, SqlSymbolKind.Cte,
                nameToken.Span.Position.Absolute,
                nameToken.Span.Position.Absolute + nameToken.Span.Length);
            scope.Ctes[cte.Name.ToUpperInvariant()] = def;

            cteBodies.Add((cte, -1, -1));
            cursor = nameIndex + 1;

            if (cte.Columns is { Count: > 0 } && cursor < tokens.Length && tokens[cursor].Kind == NzToken.LParen)
                SkipBalancedParens(tokens, ref cursor);

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
                    cteBodies[^1] = (cte, bodyOpen, bodyClose);
                    cursor = bodyClose + 1;
                }
            }

            if (cursor < tokens.Length && tokens[cursor].Kind == NzToken.Comma)
                cursor++;
        }

        foreach (var (cte, bodyOpen, bodyClose) in cteBodies)
        {
            if (bodyOpen > 0 && bodyClose > bodyOpen)
            {
                var childScope = new ScopeFrame(scope);
                CollectSelect(cte.Query, tokens, bodyOpen + 1, bodyClose, childScope);
            }
        }
    }

    private int CollectTableReference(TableReference reference, Token<NzToken>[] tokens, int cursor, int endIndex, ScopeFrame scope)
    {
        cursor = CollectTableSource(reference.Source, tokens, cursor, endIndex, scope);
        if (reference.Joins is not null)
        {
            foreach (var join in reference.Joins)
                cursor = CollectTableSource(join.Source, tokens, cursor, endIndex, scope);
        }
        return cursor;
    }

    private int CollectTableSource(TableSource source, Token<NzToken>[] tokens, int cursor, int endIndex, ScopeFrame scope)
    {
        if (source.Subquery is not null)
        {
            var openIndex = FindTokenIndex(tokens, cursor, endIndex, t => t.Kind == NzToken.LParen);
            var closeIndex = openIndex >= 0 ? FindMatchingParen(tokens, openIndex, endIndex) : -1;
            if (openIndex >= 0 && closeIndex > openIndex)
            {
                var childScope = new ScopeFrame(scope);
                CollectSelect(source.Subquery, tokens, openIndex + 1, closeIndex, childScope);
                cursor = closeIndex + 1;
            }

            if (!string.IsNullOrWhiteSpace(source.Alias))
            {
                var aliasIndex = FindIdentifierIndex(tokens, cursor, endIndex, source.Alias!);
                if (aliasIndex >= 0)
                {
                    var aliasToken = tokens[aliasIndex];
                    var def = AddDefinition(source.Alias!, SqlSymbolKind.Alias,
                        aliasToken.Span.Position.Absolute,
                        aliasToken.Span.Position.Absolute + aliasToken.Span.Length);
                    scope.Aliases[source.Alias!.ToUpperInvariant()] = def;
                    cursor = aliasIndex + 1;
                }
            }
            return cursor;
        }

        if (source.Table is null)
            return cursor;

        if (TryResolveTableInScope(scope, source.Table.Name, out var cteDef) && cteDef is not null)
        {
            var tableRange = FindTableNameRange(tokens, cursor, endIndex, source.Table);
            if (tableRange is not null)
            {
                AddReference(source.Table.Name, SqlSymbolKind.Cte,
                    tokens[tableRange.Value.StartIndex].Span.Position.Absolute,
                    tokens[tableRange.Value.EndIndex].Span.Position.Absolute + tokens[tableRange.Value.EndIndex].Span.Length,
                    cteDef.Id);
            }
        }

        cursor = SkipTableName(tokens, cursor, endIndex, source.Table);

        if (!string.IsNullOrWhiteSpace(source.Alias))
        {
            var aliasIndex = FindIdentifierIndex(tokens, cursor, endIndex, source.Alias!);
            if (aliasIndex >= 0)
            {
                var aliasToken = tokens[aliasIndex];
                var def = AddDefinition(source.Alias!, SqlSymbolKind.Alias,
                    aliasToken.Span.Position.Absolute,
                    aliasToken.Span.Position.Absolute + aliasToken.Span.Length);
                scope.Aliases[source.Alias!.ToUpperInvariant()] = def;
                cursor = aliasIndex + 1;
            }
        }

        return cursor;
    }

    private void CollectInsert(InsertStatement stmt, Token<NzToken>[] tokens, int startIndex, int endIndex, ScopeFrame scope)
    {
        if (stmt.SourceQuery is not null)
            CollectSelect(stmt.SourceQuery, tokens, startIndex, endIndex, new ScopeFrame(scope));
    }

    private void CollectUpdate(UpdateStatement stmt, Token<NzToken>[] tokens, int startIndex, int endIndex, ScopeFrame scope)
    {
        var updateScope = new ScopeFrame(scope);

        if (!string.IsNullOrWhiteSpace(stmt.Alias))
        {
            var aliasIndex = FindIdentifierIndex(tokens, startIndex, endIndex, stmt.Alias!);
            if (aliasIndex >= 0)
            {
                var aliasToken = tokens[aliasIndex];
                var def = AddDefinition(stmt.Alias!, SqlSymbolKind.Alias,
                    aliasToken.Span.Position.Absolute,
                    aliasToken.Span.Position.Absolute + aliasToken.Span.Length);
                updateScope.Aliases[stmt.Alias!.ToUpperInvariant()] = def;
            }
        }

        foreach (var setItem in stmt.SetItems)
            CollectExpression(setItem.Value, tokens, updateScope);
        if (stmt.From is not null)
        {
            var fromIdx = FindTokenIndex(tokens, startIndex, endIndex, t => t.Kind == NzToken.From);
            var cursor = fromIdx >= 0 ? fromIdx + 1 : startIndex;
            foreach (var tr in stmt.From)
                cursor = CollectTableReference(tr, tokens, cursor, endIndex, updateScope);
        }
        if (stmt.Where is not null)
            CollectExpression(stmt.Where, tokens, updateScope);
    }

    private void CollectDelete(DeleteStatement stmt, Token<NzToken>[] tokens, int startIndex, int endIndex, ScopeFrame scope)
    {
        var deleteScope = new ScopeFrame(scope);

        if (!string.IsNullOrWhiteSpace(stmt.Alias))
        {
            var aliasIndex = FindIdentifierIndex(tokens, startIndex, endIndex, stmt.Alias!);
            if (aliasIndex >= 0)
            {
                var aliasToken = tokens[aliasIndex];
                var def = AddDefinition(stmt.Alias!, SqlSymbolKind.Alias,
                    aliasToken.Span.Position.Absolute,
                    aliasToken.Span.Position.Absolute + aliasToken.Span.Length);
                deleteScope.Aliases[stmt.Alias!.ToUpperInvariant()] = def;
            }
        }

        if (stmt.Where is not null)
            CollectExpression(stmt.Where, tokens, deleteScope);
    }

    private void CollectMerge(MergeStatement stmt, Token<NzToken>[] tokens, int startIndex, int endIndex, ScopeFrame scope)
    {
        var mergeScope = new ScopeFrame(scope);

        if (!string.IsNullOrWhiteSpace(stmt.TargetAlias))
        {
            var aliasIndex = FindIdentifierIndex(tokens, startIndex, endIndex, stmt.TargetAlias!);
            if (aliasIndex >= 0)
            {
                var aliasToken = tokens[aliasIndex];
                var def = AddDefinition(stmt.TargetAlias!, SqlSymbolKind.Alias,
                    aliasToken.Span.Position.Absolute,
                    aliasToken.Span.Position.Absolute + aliasToken.Span.Length);
                mergeScope.Aliases[stmt.TargetAlias!.ToUpperInvariant()] = def;
            }
        }

        CollectExpression(stmt.OnCondition, tokens, mergeScope);

        foreach (var clause in stmt.Clauses)
        {
            switch (clause)
            {
                case MergeMatchedUpdateClause u:
                    if (u.Condition is not null) CollectExpression(u.Condition, tokens, mergeScope);
                    foreach (var item in u.SetItems)
                    {
                        CollectExpression(item.Column, tokens, mergeScope);
                        CollectExpression(item.Value, tokens, mergeScope);
                    }
                    if (u.Where is not null) CollectExpression(u.Where, tokens, mergeScope);
                    break;
                case MergeMatchedDeleteClause d:
                    if (d.Condition is not null) CollectExpression(d.Condition, tokens, mergeScope);
                    break;
                case MergeNotMatchedInsertClause i:
                    if (i.Condition is not null) CollectExpression(i.Condition, tokens, mergeScope);
                    foreach (var val in i.Values)
                        CollectExpression(val, tokens, mergeScope);
                    break;
            }
        }
    }

    private void CollectExpression(Expression expr, Token<NzToken>[] tokens, ScopeFrame scope)
    {
        switch (expr)
        {
            case ColumnReference cr when cr.Qualifier is not null:
            {
                var resolved = TryResolveAlias(scope, cr.Qualifier);
                if (resolved is not null)
                {
                    AddReference(cr.Qualifier, SqlSymbolKind.Alias,
                        expr.Position.Absolute,
                        expr.Position.Absolute + cr.Qualifier.Length,
                        resolved.Id);
                }
                break;
            }
            case BinaryExpression binary:
                CollectExpression(binary.Left, tokens, scope);
                CollectExpression(binary.Right, tokens, scope);
                break;
            case UnaryExpression unary:
                CollectExpression(unary.Operand, tokens, scope);
                break;
            case CastExpression castExpr:
                CollectExpression(castExpr.Expression, tokens, scope);
                break;
            case CastFunctionExpression castFn:
                CollectExpression(castFn.Expression, tokens, scope);
                break;
            case ExistsExpression exists:
                CollectNestedSelect(exists.Subquery, tokens, expr.Position.Absolute, scope);
                break;
            case SubqueryExpression subquery:
                CollectNestedSelect(subquery.Query, tokens, expr.Position.Absolute, scope);
                break;
            case InExpression inExpr:
                CollectExpression(inExpr.Left, tokens, scope);
                if (inExpr.Values is not null)
                    foreach (var value in inExpr.Values)
                        CollectExpression(value, tokens, scope);
                if (inExpr.Subquery is not null)
                    CollectNestedSelect(inExpr.Subquery, tokens, expr.Position.Absolute, scope);
                break;
            case BetweenExpression between:
                CollectExpression(between.Value, tokens, scope);
                CollectExpression(between.Low, tokens, scope);
                CollectExpression(between.High, tokens, scope);
                break;
            case IsExpression isExpr:
                CollectExpression(isExpr.Left, tokens, scope);
                break;
            case QuantifiedComparisonExpression quantified:
                CollectExpression(quantified.Left, tokens, scope);
                CollectExpression(quantified.Right, tokens, scope);
                break;
            case FunctionCall fn:
                if (fn.Arguments is not null)
                    foreach (var arg in fn.Arguments)
                        CollectExpression(arg, tokens, scope);
                if (fn.Filter is not null)
                    CollectExpression(fn.Filter.Condition, tokens, scope);
                if (fn.Over is not null)
                {
                    if (fn.Over.PartitionBy is not null)
                        foreach (var part in fn.Over.PartitionBy)
                            CollectExpression(part, tokens, scope);
                    if (fn.Over.OrderBy is not null)
                        foreach (var order in fn.Over.OrderBy)
                            CollectExpression(order.Expression, tokens, scope);
                }
                break;
            case CaseExpression caseExpr:
                if (caseExpr.Value is not null)
                    CollectExpression(caseExpr.Value, tokens, scope);
                foreach (var whenThen in caseExpr.WhenClauses)
                {
                    CollectExpression(whenThen.When, tokens, scope);
                    CollectExpression(whenThen.Then, tokens, scope);
                }
                if (caseExpr.ElseClause is not null)
                    CollectExpression(caseExpr.ElseClause, tokens, scope);
                break;
            case ExtractExpression extractExpr:
                CollectExpression(extractExpr.Source, tokens, scope);
                break;
        }
    }

    private void CollectNestedSelect(SelectStatement nested, Token<NzToken>[] tokens, int startAbsolute, ScopeFrame scope)
    {
        var openIndex = FindTokenIndex(tokens, 0, tokens.Length, t =>
            t.Kind == NzToken.LParen && t.Span.Position.Absolute == startAbsolute);
        if (openIndex < 0)
            return;

        var closeIndex = FindMatchingParen(tokens, openIndex, tokens.Length);
        if (closeIndex <= openIndex)
            return;

        var childScope = new ScopeFrame(scope);
        CollectSelect(nested, tokens, openIndex + 1, closeIndex, childScope);
    }

    private SymbolOccurrence AddDefinition(string name, SqlSymbolKind kind, int startAbsolute, int endAbsolute)
    {
        var occ = new SymbolOccurrence(_nextId++, name, kind, startAbsolute, endAbsolute, true, null);
        var def = occ with { DefinitionId = occ.Id };
        _occurrences.Add(def);
        _definitionById[def.Id] = def;
        return def;
    }

    private void AddReference(string name, SqlSymbolKind kind, int startAbsolute, int endAbsolute, int definitionId)
    {
        _occurrences.Add(new SymbolOccurrence(_nextId++, name, kind, startAbsolute, endAbsolute, false, definitionId));
    }

    private static bool TryResolveTableInScope(ScopeFrame scope, string name, out SymbolOccurrence? occurrence)
    {
        var current = scope;
        while (current is not null)
        {
            if (current.Ctes.TryGetValue(name.ToUpperInvariant(), out occurrence!))
                return true;
            current = current.Parent;
        }
        occurrence = null;
        return false;
    }

    private static SymbolOccurrence? TryResolveAlias(ScopeFrame scope, string name)
    {
        var current = scope;
        while (current is not null)
        {
            if (current.Aliases.TryGetValue(name.ToUpperInvariant(), out var occ))
                return occ;
            current = current.Parent;
        }
        return null;
    }

    private static int SkipTableName(Token<NzToken>[] tokens, int cursor, int endIndex, TableName tableName)
    {
        if (tableName.Database is not null && tableName.Schema is not null)
            return SkipQualified(tokens, cursor, endIndex, 3);
        if (tableName.Database is not null && tableName.Schema is null)
            return SkipQualified(tokens, cursor, endIndex, 2);
        if (tableName.Schema is not null)
            return SkipQualified(tokens, cursor, endIndex, 2);
        return SkipSimple(tokens, cursor, endIndex);
    }

    private static int SkipQualified(Token<NzToken>[] tokens, int cursor, int endIndex, int identCount)
    {
        var found = 0;
        while (cursor < tokens.Length && cursor < endIndex)
        {
            if (IsIdentifierToken(tokens[cursor]))
                found++;
            if (found >= identCount)
                return cursor + 1;
            if (tokens[cursor].Kind == NzToken.Dot)
            {
                cursor++;
                continue;
            }
            cursor++;
        }
        return cursor;
    }

    private static int SkipSimple(Token<NzToken>[] tokens, int cursor, int endIndex)
    {
        if (cursor < tokens.Length && cursor < endIndex && IsIdentifierToken(tokens[cursor]))
            return cursor + 1;
        return cursor;
    }

    private static (int StartIndex, int EndIndex)? FindTableNameRange(Token<NzToken>[] tokens, int startIndex, int endIndex, TableName tableName)
    {
        for (int i = Math.Max(0, startIndex); i < tokens.Length && i < endIndex; i++)
        {
            if (!IsIdentifierToken(tokens[i]))
                continue;

            if (tableName.Database is not null && tableName.Schema is not null)
            {
                if (MatchesQualified(tokens, i, endIndex, tableName.Database, tableName.Schema, tableName.Name))
                    return (i, FindQualifiedEnd(tokens, i, endIndex, 3));
            }
            else if (tableName.Database is not null)
            {
                if (MatchesDbDotTable(tokens, i, endIndex, tableName.Database, tableName.Name))
                    return (i, FindQualifiedEnd(tokens, i, endIndex, 2));
            }
            else if (tableName.Schema is not null)
            {
                if (MatchesSchemaTable(tokens, i, endIndex, tableName.Schema, tableName.Name))
                    return (i, FindQualifiedEnd(tokens, i, endIndex, 2));
            }
            else if (string.Equals(NormalizedTokenText(tokens[i]), tableName.Name, StringComparison.OrdinalIgnoreCase))
            {
                return (i, i);
            }
        }
        return null;
    }

    private static bool MatchesQualified(Token<NzToken>[] tokens, int index, int endIndex,
        string database, string schema, string table)
    {
        if (index + 4 >= tokens.Length || index + 4 >= endIndex) return false;
        return IsName(tokens[index], database)
            && tokens[index + 1].Kind == NzToken.Dot
            && IsName(tokens[index + 2], schema)
            && tokens[index + 3].Kind == NzToken.Dot
            && IsName(tokens[index + 4], table);
    }

    private static bool MatchesSchemaTable(Token<NzToken>[] tokens, int index, int endIndex,
        string schema, string table)
    {
        if (index + 2 >= tokens.Length || index + 2 >= endIndex) return false;
        return IsName(tokens[index], schema)
            && tokens[index + 1].Kind == NzToken.Dot
            && IsName(tokens[index + 2], table);
    }

    private static bool MatchesDbDotTable(Token<NzToken>[] tokens, int index, int endIndex,
        string database, string table)
    {
        if (index + 2 >= tokens.Length || index + 2 >= endIndex) return false;
        if (!IsName(tokens[index], database) || tokens[index + 1].Kind != NzToken.Dot) return false;
        if (tokens[index + 2].Kind == NzToken.Dot)
            return index + 3 < tokens.Length && index + 3 < endIndex && IsName(tokens[index + 3], table);
        return IsName(tokens[index + 2], table);
    }

    private static int FindQualifiedEnd(Token<NzToken>[] tokens, int index, int endIndex, int identCount)
    {
        var current = index;
        var foundIdents = 0;
        while (current < tokens.Length && current < endIndex)
        {
            if (IsIdentifierToken(tokens[current]))
                foundIdents++;
            if (foundIdents >= identCount)
                return current;
            current++;
        }
        return index;
    }

    private static bool IsName(Token<NzToken> token, string name) =>
        string.Equals(NormalizedTokenText(token), name, StringComparison.OrdinalIgnoreCase);

    private static bool IsIdentifierToken(Token<NzToken> token) =>
        token.Kind is NzToken.Identifier or NzToken.QuotedIdentifier;

    private static string NormalizedTokenText(Token<NzToken> token)
    {
        var val = token.ToStringValue();
        return val.Length >= 2 && val[0] == '"' && val[^1] == '"' ? val[1..^1] : val;
    }

    private static int FindTokenIndex(Token<NzToken>[] tokens, int startIndex, int endIndex,
        Func<Token<NzToken>, bool> predicate)
    {
        for (int i = Math.Max(0, startIndex); i < tokens.Length && i < endIndex; i++)
        {
            if (predicate(tokens[i]))
                return i;
        }
        return -1;
    }

    private static int FindIdentifierIndex(Token<NzToken>[] tokens, int startIndex, int endIndex, string name)
    {
        for (int i = Math.Max(0, startIndex); i < tokens.Length && i < endIndex; i++)
        {
            if (!IsIdentifierToken(tokens[i]))
                continue;
            if (string.Equals(NormalizedTokenText(tokens[i]), name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static int FindMatchingParen(Token<NzToken>[] tokens, int openIndex, int endIndex)
    {
        if (openIndex < 0 || openIndex >= tokens.Length || tokens[openIndex].Kind != NzToken.LParen)
            return -1;
        var depth = 0;
        for (int i = openIndex; i < tokens.Length && i < endIndex; i++)
        {
            if (tokens[i].Kind == NzToken.LParen) depth++;
            else if (tokens[i].Kind == NzToken.RParen)
            {
                depth--;
                if (depth == 0) return i;
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
            if (tokens[index].Kind == NzToken.LParen) depth++;
            else if (tokens[index].Kind == NzToken.RParen)
            {
                depth--;
                if (depth == 0) { index++; return; }
            }
            index++;
        }
    }
}

internal sealed class SymbolIndex
{
    private readonly IReadOnlyList<SymbolOccurrence> _occurrences;
    private readonly Dictionary<int, SymbolOccurrence> _definitionById;

    public SymbolIndex(IReadOnlyList<SymbolOccurrence> occurrences, Dictionary<int, SymbolOccurrence> definitionById)
    {
        _occurrences = occurrences;
        _definitionById = definitionById;
    }

    public IReadOnlyList<SymbolOccurrence> Occurrences => _occurrences;

    public SymbolOccurrence? FindOccurrenceAt(int absolute)
    {
        SymbolOccurrence? best = null;
        var bestLength = int.MaxValue;

        foreach (var occ in _occurrences)
        {
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
        _definitionById.TryGetValue(definitionId, out var def) ? def : null;

    public IReadOnlyList<SymbolOccurrence> FindReferences(int definitionId, bool includeDeclaration) =>
        _occurrences
            .Where(o => o.DefinitionId == definitionId || (includeDeclaration && o.Id == definitionId))
            .OrderBy(o => o.StartAbsolute)
            .ToList();
}
