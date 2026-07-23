using System.Text.RegularExpressions;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Context-aware semantic token classifier shared by the Avalonia editor and LSP.
/// </summary>
public sealed class NzSemanticTokenClassifier
{
    private static readonly Regex LineComment = new(@"--[^\n]*", RegexOptions.Compiled);
    private static readonly Regex BlockComment = new(@"/\*[\s\S]*?\*/", RegexOptions.Compiled);

    private readonly ISchemaProvider? _schema;
    private readonly DocumentParsingCoordinator? _coordinator;
    private readonly object _cacheLock = new();
    private string? _cachedKey;
    private SemanticTokenSpan[] _cachedSpans = [];

    public static readonly string[] TokenTypesLegend =
    [
        "comment", "string", "number", "keyword", "type", "function", "operator",
        "parameter", "variable", "table", "column", "cte", "alias", "identifier"
    ];

    public static readonly string[] TokenModifiersLegend =
    [
        "deprecated", "definition", "defaultLibrary"
    ];

    public NzSemanticTokenClassifier(ISchemaProvider? schema = null, DocumentParsingCoordinator? coordinator = null)
    {
        _schema = schema;
        _coordinator = coordinator;
    }

    public IReadOnlyList<SemanticTokenSpan> Classify(string sql, string? documentUri = null)
    {
        if (string.IsNullOrEmpty(sql) || sql.Length > NzSemanticTokenKnown.LargeDocumentCharLimit)
            return Array.Empty<SemanticTokenSpan>();

        var cacheKey = BuildCacheKey(documentUri, sql);
        lock (_cacheLock)
        {
            if (_cachedKey == cacheKey)
                return _cachedSpans;
        }

        var spans = BuildSpans(sql, documentUri);
        lock (_cacheLock)
        {
            _cachedKey = cacheKey;
            _cachedSpans = spans;
        }

        return spans;
    }

    private static string BuildCacheKey(string? documentUri, string sql) =>
        $"{documentUri ?? "default"}:{StatementIndexBuilder.SimpleHash(sql)}:{sql.Length}";

    private SemanticTokenSpan[] BuildSpans(string sql, string? documentUri)
    {
        var items = new List<SemanticTokenSpan>();

        foreach (Match m in LineComment.Matches(sql))
        {
            var modifier = ContainsTodoMarker(m.Value) ? SemanticTokenModifiers.Deprecated : SemanticTokenModifiers.None;
            items.Add(new SemanticTokenSpan(m.Index, m.Length, SemanticTokenKind.Comment, modifier));
        }

        foreach (Match m in BlockComment.Matches(sql))
        {
            var modifier = ContainsTodoMarker(m.Value) ? SemanticTokenModifiers.Deprecated : SemanticTokenModifiers.None;
            items.Add(new SemanticTokenSpan(m.Index, m.Length, SemanticTokenKind.Comment, modifier));
        }

        Token<NzToken>[] tokens;
        try
        {
            tokens = NzLexer.Tokenize(sql).ToArray();
            if (_coordinator is not null)
                _ = _coordinator.GetOrCreate(documentUri ?? "semantic-default").Parse(sql);
        }
        catch
        {
            return items.ToArray();
        }

        var useScope = sql.Length <= NzSemanticTokenKnown.LargeDocumentCharLimit;
        TokenScopeCollector? scopeCollector = null;
        HashSet<string>? aliasNames = null;
        HashSet<string>? tableNames = null;

        if (useScope)
        {
            scopeCollector = new TokenScopeCollector(_schema);
            scopeCollector.Collect(tokens, sql.Length);
            aliasNames = BuildAliasNames(tokens);
            tableNames = BuildTableNames(tokens);
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var (kind, modifiers) = MapToken(token, tokens, i, scopeCollector, aliasNames, tableNames, _schema);
            if (kind == SemanticTokenKind.Operator)
                continue;

            items.Add(new SemanticTokenSpan(
                token.Span.Position.Absolute,
                token.Span.Length,
                kind,
                modifiers));
        }

        items.Sort((a, b) => a.Start.CompareTo(b.Start));
        return items.ToArray();
    }

    private static (SemanticTokenKind Kind, SemanticTokenModifiers Modifiers) MapToken(
        Token<NzToken> token,
        Token<NzToken>[] allTokens,
        int index,
        TokenScopeCollector? scopeCollector,
        HashSet<string>? aliasNames,
        HashSet<string>? tableNames,
        ISchemaProvider? schema)
    {
        switch (token.Kind)
        {
            case NzToken.StringLiteral:
                return (SemanticTokenKind.String, SemanticTokenModifiers.None);
            case NzToken.NumberLiteral:
                return (SemanticTokenKind.Number, SemanticTokenModifiers.None);
            case NzToken.Parameter:
            case NzToken.DollarNumber:
                return (SemanticTokenKind.Parameter, SemanticTokenModifiers.None);
            case NzToken.BracedVariable:
            case NzToken.BracesOnlyVariable:
            case NzToken.DollarIdentifier:
                return (SemanticTokenKind.Variable, SemanticTokenModifiers.None);
            case NzToken.QuotedIdentifier:
                return (SemanticTokenKind.String, SemanticTokenModifiers.None);
            case NzToken.Identifier:
                return ClassifyIdentifier(token, allTokens, index, scopeCollector, aliasNames, tableNames, schema);
            default:
                if (IsKeyword(token.Kind))
                    return (SemanticTokenKind.Keyword, SemanticTokenModifiers.None);
                if (IsOperator(token.Kind))
                    return (SemanticTokenKind.Operator, SemanticTokenModifiers.None);
                return (SemanticTokenKind.Identifier, SemanticTokenModifiers.None);
        }
    }

    private static (SemanticTokenKind Kind, SemanticTokenModifiers Modifiers) ClassifyIdentifier(
        Token<NzToken> token,
        Token<NzToken>[] allTokens,
        int index,
        TokenScopeCollector? scopeCollector,
        HashSet<string>? aliasNames,
        HashSet<string>? tableNames,
        ISchemaProvider? schema)
    {
        var name = token.ToStringValue();
        var pos = token.Span.Position.Absolute;

        if (NzSemanticTokenKnown.DataTypes.Contains(name))
            return (SemanticTokenKind.Type, SemanticTokenModifiers.None);

        if (string.Equals(name, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "FALSE", StringComparison.OrdinalIgnoreCase))
            return (SemanticTokenKind.Keyword, SemanticTokenModifiers.DefaultLibrary);

        if (IsSpecialValue(name))
            return (SemanticTokenKind.Function, SemanticTokenModifiers.None);

        if (index + 1 < allTokens.Length && allTokens[index + 1].Kind == NzToken.LParen
            && !IsTableContext(allTokens, index))
            return (SemanticTokenKind.Function, SemanticTokenModifiers.None);

        if (name.Length > 1 && NzSemanticTokenKnown.FunctionNames.Contains(name.ToUpperInvariant()))
            return (SemanticTokenKind.Function, SemanticTokenModifiers.None);

        if (scopeCollector is not null)
        {
            if (index > 0 && allTokens[index - 1].Kind == NzToken.Dot)
            {
                if (IsQualifiedTablePath(allTokens, index))
                    return (SemanticTokenKind.Table, SemanticTokenModifiers.None);
                return (SemanticTokenKind.Column, SemanticTokenModifiers.None);
            }

            if (IsTableContext(allTokens, index))
            {
                if (tableNames?.Contains(name) == true)
                    return (SemanticTokenKind.Table, SemanticTokenModifiers.None);
                return (SemanticTokenKind.Table, SemanticTokenModifiers.None);
            }

            if (aliasNames?.Contains(name) == true)
                return (SemanticTokenKind.Alias, SemanticTokenModifiers.None);

            if (scopeCollector.GetCteNamesInScope(pos).Any(n =>
                    string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                return (SemanticTokenKind.Cte, SemanticTokenModifiers.None);

            if (tableNames?.Contains(name) == true && IsTableReferenceContext(allTokens, index))
                return (SemanticTokenKind.Table, SemanticTokenModifiers.None);

            if (IsColumnContext(allTokens, index) &&
                IsKnownColumnAtPosition(name, pos, allTokens, scopeCollector, schema))
                return (SemanticTokenKind.Column, SemanticTokenModifiers.None);
        }
        else if (index > 0)
        {
            var prev = allTokens[index - 1].Kind;
            if (prev is NzToken.From or NzToken.Join or NzToken.Update or NzToken.Into
                or NzToken.Table or NzToken.View or NzToken.Merge or NzToken.Dot)
                return (SemanticTokenKind.Table, SemanticTokenModifiers.None);
        }

        return (SemanticTokenKind.Identifier, SemanticTokenModifiers.None);
    }

    private static bool IsSpecialValue(string name) =>
        string.Equals(name, "CURRENT_DATE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "CURRENT_TIME", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "NOW", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "TODAY", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "TOMORROW", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "YESTERDAY", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> BuildAliasNames(Token<NzToken>[] tokens)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? previousIdentifier = null;
        bool inFromOrJoin = false;
        bool afterUpdate = false;

        for (int i = 0; i < tokens.Length; i++)
        {
            var k = tokens[i].Kind;
            if (k == NzToken.Update)
            {
                afterUpdate = true;
                inFromOrJoin = false;
                previousIdentifier = null;
                continue;
            }

            if (k == NzToken.Set && afterUpdate)
            {
                afterUpdate = false;
                previousIdentifier = null;
                continue;
            }

            if (IsFromOrJoin(k))
            {
                inFromOrJoin = true;
                afterUpdate = false;
                previousIdentifier = null;
                continue;
            }

            if (IsClauseBoundary(k))
            {
                inFromOrJoin = false;
                afterUpdate = false;
                previousIdentifier = null;
                continue;
            }

            if (k == NzToken.As)
                continue;

            if (k == NzToken.Comma)
            {
                previousIdentifier = null;
                continue;
            }

            if (k == NzToken.Dot)
                continue;

            if (k is NzToken.Identifier or NzToken.QuotedIdentifier)
            {
                var name = tokens[i].ToStringValue();
                if (previousIdentifier is not null && (inFromOrJoin || afterUpdate))
                    aliases.Add(name);

                if (i > 0 && tokens[i - 1].Kind == NzToken.RParen)
                    aliases.Add(name);

                if (inFromOrJoin || afterUpdate)
                    previousIdentifier = name;
            }
            else
            {
                previousIdentifier = null;
            }
        }

        return aliases;
    }

    private static HashSet<string> BuildTableNames(Token<NzToken>[] tokens)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < tokens.Length; i++)
        {
            if (!IsFromOrJoin(tokens[i].Kind) && tokens[i].Kind is not (NzToken.Update or NzToken.Into or NzToken.Merge))
                continue;

            int j = i + 1;
            if (tokens[i].Kind == NzToken.Merge && j < tokens.Length && tokens[j].Kind == NzToken.Into)
                j++;

            while (j < tokens.Length && tokens[j].Kind is not (NzToken.Identifier or NzToken.QuotedIdentifier))
            {
                if (tokens[j].Kind == NzToken.Semicolon)
                    break;
                j++;
            }

            while (j < tokens.Length && tokens[j].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
            {
                tables.Add(tokens[j].ToStringValue());
                j++;
                if (j < tokens.Length && tokens[j].Kind == NzToken.Dot)
                {
                    j++;
                    continue;
                }
                break;
            }
        }

        return tables;
    }

    private static bool IsKnownColumnAtPosition(
        string name,
        int cursorPos,
        Token<NzToken>[] tokens,
        TokenScopeCollector scopeCollector,
        ISchemaProvider? schema)
    {
        foreach (var path in CollectVisibleTablePathsAt(tokens, cursorPos))
        {
            if (TableHasColumn(path, name, cursorPos, scopeCollector, schema))
                return true;
        }

        return false;
    }

    private static bool TableHasColumn(
        CompletionAliasResolver.TablePath path,
        string columnName,
        int cursorPos,
        TokenScopeCollector scopeCollector,
        ISchemaProvider? schema)
    {
        var cteCols = scopeCollector.GetCteColumns(path.Name, cursorPos);
        if (cteCols?.Any(c => string.Equals(c, columnName, StringComparison.OrdinalIgnoreCase)) == true)
            return true;

        return schema?.GetTable(path.Database, path.Schema, path.Name) is { Columns: { } cols }
            && cols.Any(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<CompletionAliasResolver.TablePath> CollectVisibleTablePathsAt(
        Token<NzToken>[] tokens,
        int cursorPos)
    {
        var paths = new List<CompletionAliasResolver.TablePath>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int stmtStart = 0;
        int stmtEnd = tokens.Length;

        for (int i = 0; i < tokens.Length; i++)
        {
            var pos = tokens[i].Span.Position.Absolute;
            if (pos <= cursorPos && tokens[i].Kind == NzToken.Semicolon)
                stmtStart = i + 1;
        }

        for (int i = stmtStart; i < tokens.Length; i++)
        {
            if (tokens[i].Kind == NzToken.Semicolon && tokens[i].Span.Position.Absolute > cursorPos)
            {
                stmtEnd = i;
                break;
            }
        }

        for (int i = stmtStart; i < stmtEnd; i++)
        {
            var k = tokens[i].Kind;
            if (!IsFromOrJoin(k) && k is not (NzToken.Update or NzToken.Into or NzToken.Merge))
                continue;

            int start = i + 1;
            if (k == NzToken.Merge && start < stmtEnd && tokens[start].Kind == NzToken.Into)
                start++;

            if (start >= stmtEnd)
                continue;

            int pos = start;
            while (pos < stmtEnd)
            {
                if (tokens[pos].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
                {
                    var (path, _, consumed) = CompletionAliasResolver.ParseTablePathAt(tokens, pos);
                    if (consumed > 0)
                    {
                        AddTablePath(path, paths, seen);
                        pos += consumed;
                        continue;
                    }
                }

                if (tokens[pos].Kind == NzToken.Comma)
                {
                    pos++;
                    continue;
                }

                break;
            }
        }

        return paths;
    }

    private static void AddTablePath(
        CompletionAliasResolver.TablePath path,
        List<CompletionAliasResolver.TablePath> paths,
        HashSet<string> seen)
    {
        var key = $"{path.Database}|{path.Schema}|{path.Name}";
        if (seen.Add(key))
            paths.Add(path);
    }

    private static bool IsTableContext(Token<NzToken>[] tokens, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            var k = tokens[i].Kind;
            if (k == NzToken.As || k == NzToken.Dot || k == NzToken.Comma)
                continue;
            return k is NzToken.From or NzToken.Join or NzToken.Inner or NzToken.Left or NzToken.Right
                or NzToken.Full or NzToken.Cross or NzToken.Update or NzToken.Into or NzToken.Merge
                or NzToken.Table or NzToken.View;
        }

        return false;
    }

    private static bool IsTableReferenceContext(Token<NzToken>[] tokens, int index)
    {
        if (IsTableContext(tokens, index))
            return true;

        for (int i = index - 1; i >= 0; i--)
        {
            var k = tokens[i].Kind;
            if (k == NzToken.Dot)
                return true;
            if (k is NzToken.Identifier or NzToken.QuotedIdentifier)
                continue;
            break;
        }

        return false;
    }

    private static bool IsQualifiedTablePath(Token<NzToken>[] tokens, int index)
    {
        if (index < 2 || tokens[index - 1].Kind != NzToken.Dot)
            return false;

        if (index >= 2 && tokens[index - 2].Kind == NzToken.Dot)
            return true;

        for (int i = index - 2; i >= 0; i--)
        {
            if (tokens[i].Kind is NzToken.From or NzToken.Join or NzToken.Into or NzToken.Update or NzToken.Merge)
                return true;
            if (tokens[i].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
                continue;
            break;
        }

        return false;
    }

    private static bool IsColumnContext(Token<NzToken>[] tokens, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            var k = tokens[i].Kind;
            if (k == NzToken.Dot || k == NzToken.Comma)
                continue;
            return k is NzToken.Select or NzToken.Where or NzToken.Set or NzToken.On
                or NzToken.Having or NzToken.GroupBy or NzToken.OrderBy
                or NzToken.And or NzToken.Or;
        }

        return false;
    }

    private static bool IsFromOrJoin(NzToken t) =>
        t is NzToken.From or NzToken.Join or NzToken.Inner or NzToken.Left or NzToken.Right
            or NzToken.Full or NzToken.Cross;

    private static bool IsClauseBoundary(NzToken t) =>
        t is NzToken.Where or NzToken.GroupBy or NzToken.OrderBy or NzToken.Having
            or NzToken.On or NzToken.Set or NzToken.Limit or NzToken.Offset;

    private static bool ContainsTodoMarker(string comment)
    {
        var upper = comment.ToUpperInvariant();
        return upper.Contains("TODO") || upper.Contains("FIXME")
            || upper.Contains("HACK") || upper.Contains("UNDONE");
    }

    private static bool IsKeyword(NzToken kind) => kind switch
    {
        NzToken.Select or NzToken.From or NzToken.Where or NzToken.Insert or NzToken.Into
            or NzToken.Values or NzToken.Value or NzToken.Update or NzToken.Set or NzToken.Delete
            or NzToken.Join or NzToken.Inner or NzToken.Left or NzToken.Right or NzToken.Full
            or NzToken.Outer or NzToken.Cross or NzToken.Natural or NzToken.Only or NzToken.On
            or NzToken.And or NzToken.Or or NzToken.Not or NzToken.As or NzToken.Distinct or NzToken.All
            or NzToken.Union or NzToken.Intersect or NzToken.Except
            or NzToken.Having or NzToken.Limit or NzToken.Offset
            or NzToken.Nulls or NzToken.Null or NzToken.Is
            or NzToken.Ilike or NzToken.Like or NzToken.Escape or NzToken.In
            or NzToken.Between or NzToken.Exists
            or NzToken.Case or NzToken.When or NzToken.Then or NzToken.Elsif or NzToken.If
            or NzToken.Else or NzToken.End
            or NzToken.Nzplsql or NzToken.BeginProc or NzToken.EndProc or NzToken.Begin
            or NzToken.Declare or NzToken.Exception or NzToken.Return or NzToken.Alias
            or NzToken.Constant or NzToken.Loop or NzToken.While or NzToken.Exit
            or NzToken.Raise or NzToken.Notice or NzToken.Debug or NzToken.Error1
            or NzToken.Rollback or NzToken.Commit or NzToken.Call or NzToken.Immediate or NzToken.Using
            or NzToken.Grant or NzToken.Revoke or NzToken.To or NzToken.Public or NzToken.Type
            or NzToken.Cascade or NzToken.Restrict or NzToken.SameAs or NzToken.Hash
            or NzToken.Deferrable or NzToken.Initially
            or NzToken.Create or NzToken.Replace or NzToken.Database or NzToken.Schema
            or NzToken.Table or NzToken.Sequence or NzToken.Session or NzToken.Synonym
            or NzToken.User or NzToken.Procedure or NzToken.Temporary or NzToken.Temp
            or NzToken.Drop or NzToken.Truncate or NzToken.Explain or NzToken.Verbose
            or NzToken.Distribution or NzToken.Plantext or NzToken.Plangraph
            or NzToken.Alter or NzToken.Show or NzToken.Copy or NzToken.Lock or NzToken.Merge
            or NzToken.Reindex or NzToken.Reset or NzToken.External or NzToken.Views or NzToken.View
            or NzToken.Comment or NzToken.Column or NzToken.Add or NzToken.Constraint
            or NzToken.Primary or NzToken.Key or NzToken.Foreign or NzToken.References
            or NzToken.Unique or NzToken.Check or NzToken.Global
            or NzToken.Returns or NzToken.Language or NzToken.Execute or NzToken.Exec
            or NzToken.Owner or NzToken.Caller or NzToken.RefTable or NzToken.Varargs
            or NzToken.Varray or NzToken.Autocommit
            or NzToken.With or NzToken.Final or NzToken.Recursive
            or NzToken.Distribute or NzToken.Random or NzToken.Organize or NzToken.Groom
            or NzToken.Versions or NzToken.Records or NzToken.Pages or NzToken.Ready or NzToken.Start
            or NzToken.Reclaim or NzToken.Backupset or NzToken.Default or NzToken.None
            or NzToken.Generate or NzToken.Next or NzToken.Express or NzToken.Statistics
            or NzToken.For or NzToken.Of
            or NzToken.Asc or NzToken.Desc or NzToken.Fetch or NzToken.First
            or NzToken.Any or NzToken.Some
            or NzToken.Over or NzToken.Rows or NzToken.Range or NzToken.Groups
            or NzToken.Current or NzToken.Row or NzToken.Unbounded
            or NzToken.Preceding or NzToken.Following or NzToken.Filter or NzToken.Exclude
            or NzToken.Ties or NzToken.AtSet
            or NzToken.GroupBy or NzToken.OrderBy or NzToken.PartitionBy => true,
        _ => false,
    };

    private static bool IsOperator(NzToken kind) => kind switch
    {
        NzToken.NotEquals or NzToken.LessThanEquals or NzToken.GreaterThanEquals
            or NzToken.Concat or NzToken.DoubleColon or NzToken.Assign
            or NzToken.EqualsOp or NzToken.LessThan or NzToken.GreaterThan
            or NzToken.Plus or NzToken.Minus or NzToken.Multiply
            or NzToken.Divide or NzToken.Modulo or NzToken.Caret
            or NzToken.Dot or NzToken.Comma or NzToken.Semicolon => true,
        _ => false,
    };
}
