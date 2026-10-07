using System.Text.Json;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;
using Xunit;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Shared non-parser behavior contracts consumed by the .NET SQL implementation.
/// Implementation AST and service types are reduced to the language-neutral corpus
/// assertions here; no production parser code is shared with the corpus.
/// </summary>
public sealed class SharedSqlConformanceAuthoringTests
{
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "completion", "diagnostics", "semantic", "quick-fixes", "signature",
        "formatting", "hover", "recovery"
    };

    public static IEnumerable<object[]> SharedCases()
    {
        foreach (var (json, dialect) in LoadCases())
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var category = GetString(root, "category");
            if (!Categories.Contains(category)
                || GetString(root.GetProperty("verification"), "status", "source-backed") == "needsVerification")
            {
                continue;
            }

            yield return [GetString(root, "id"), json, dialect];
        }
    }

    [Theory]
    [MemberData(nameof(SharedCases))]
    public async Task Shared_authoring_behavior_matches_the_contract(
        string id,
        string caseJson,
        SqlDialect dialect)
    {
        using var document = JsonDocument.Parse(caseJson);
        var root = document.RootElement;
        var category = GetString(root, "category");
        var sql = RenderSql(GetString(root, "sql"));
        var cursor = RemoveCursorMarker(root, ref sql);
        var schema = LoadSchema(root);

        try
        {
            switch (category)
            {
                case "completion":
                    await AssertCompletion(root, sql, cursor, schema, dialect, id);
                    break;
                case "diagnostics":
                    AssertDiagnostics(root, sql, schema, id);
                    break;
                case "semantic":
                    AssertSemantic(root, sql, schema, dialect, id);
                    break;
                case "quick-fixes":
                    AssertQuickFix(root, sql, schema, id);
                    break;
                case "signature":
                    AssertSignature(root, sql, cursor, dialect, id);
                    break;
                case "formatting":
                    AssertFormatting(root, sql, dialect, id);
                    break;
                case "hover":
                    AssertHover(root, sql, cursor, schema, dialect, id);
                    break;
                case "recovery":
                    await AssertRecovery(root, sql, cursor, schema, dialect, id);
                    break;
                default:
                    throw new InvalidOperationException($"No .NET adapter for {category}.");
            }
        }
        catch (Xunit.Sdk.XunitException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new Xunit.Sdk.XunitException($"[{id}] .NET conformance adapter threw {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static async Task AssertCompletion(
        JsonElement root,
        string sql,
        int cursor,
        ISchemaProvider? schema,
        SqlDialect dialect,
        string id)
    {
        var result = await CompletionOrchestrator.GetCompletions(
            sql,
            cursor,
            schema,
            dialect,
            options: new CompletionOrchestrationOptions { ForcedAutocomplete = true });
        var items = result.EngineItems
            .Select(item => new CompletionContractItem(
                item.Label, NormalizeKind(item.Kind), item.Detail, item.Documentation))
            .Concat(result.WordListItems.Select(item => new CompletionContractItem(
                item.Label, NormalizeKind(item.Kind), item.Detail, item.Description)))
            .ToArray();
        var expected = root.GetProperty("expect");

        AssertContainsItems(expected, "contains", items, id);
        AssertAbsentItems(expected, "notContains", items, id);
        AssertOrderedItems(expected, "top", items, id);
        AssertTopN(expected, items, id);
        AssertExactItems(expected, items, id);
    }

    private static string NormalizeKind(CompletionKind kind) => kind switch
    {
        CompletionKind.View => "view",
        CompletionKind.Column => "column",
        CompletionKind.Function => "function",
        CompletionKind.Cte => "table",
        CompletionKind.Table or CompletionKind.ExternalTable => "table",
        CompletionKind.Schema => "schema",
        CompletionKind.Database => "database",
        CompletionKind.Alias => "alias",
        CompletionKind.DataType => "datatype",
        CompletionKind.Variable => "variable",
        CompletionKind.Snippet => "snippet",
        _ => "keyword"
    };

    private static string NormalizeKind(JustyBase.Core.Database.SqlWordListKind kind) => kind switch
    {
        JustyBase.Core.Database.SqlWordListKind.View => "view",
        JustyBase.Core.Database.SqlWordListKind.Column => "column",
        JustyBase.Core.Database.SqlWordListKind.Function or JustyBase.Core.Database.SqlWordListKind.Procedure => "function",
        JustyBase.Core.Database.SqlWordListKind.With or JustyBase.Core.Database.SqlWordListKind.Subquery => "table",
        JustyBase.Core.Database.SqlWordListKind.Table or JustyBase.Core.Database.SqlWordListKind.TempTable
            or JustyBase.Core.Database.SqlWordListKind.ExternalTable => "table",
        JustyBase.Core.Database.SqlWordListKind.Schema => "schema",
        JustyBase.Core.Database.SqlWordListKind.Database => "database",
        JustyBase.Core.Database.SqlWordListKind.Alias => "alias",
        JustyBase.Core.Database.SqlWordListKind.DataType => "datatype",
        JustyBase.Core.Database.SqlWordListKind.Variable => "variable",
        JustyBase.Core.Database.SqlWordListKind.Snippet => "snippet",
        _ => "keyword"
    };

    private static void AssertContainsItems(JsonElement expected, string property, CompletionContractItem[] actual, string id)
    {
        if (!expected.TryGetProperty(property, out var items))
            return;
        foreach (var item in items.EnumerateArray())
        {
            var label = GetString(item, "label");
            var kind = GetString(item, "kind");
            var found = actual.Any(candidate =>
                SameIdentifier(candidate.Label, label)
                && (kind.Length == 0 || candidate.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                && OptionalMatches(item, "detail", candidate.Detail)
                && OptionalMatches(item, "documentation", candidate.Documentation));
            Assert.True(found,
                $"[{id}] missing completion {label} ({kind}) from [{string.Join(", ", actual.Select(candidate => candidate.Label))}].");
        }
    }

    private static void AssertAbsentItems(JsonElement expected, string property, CompletionContractItem[] actual, string id)
    {
        if (!expected.TryGetProperty(property, out var items))
            return;
        foreach (var item in items.EnumerateArray())
        {
            var label = GetString(item, "label");
            var kind = GetString(item, "kind");
            Assert.DoesNotContain(actual, candidate =>
                SameIdentifier(candidate.Label, label)
                && (kind.Length == 0 || candidate.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static void AssertOrderedItems(JsonElement expected, string property, CompletionContractItem[] actual, string id)
    {
        if (!expected.TryGetProperty(property, out var items))
            return;
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var label = item.ValueKind == JsonValueKind.String ? item.GetString()! : GetString(item, "label");
            var found = Array.FindIndex(actual, index, candidate => SameIdentifier(candidate.Label, label));
            Assert.True(found >= index, $"[{id}] expected {label} in the requested ranking order.");
            index = found + 1;
        }
    }

    private static void AssertTopN(JsonElement expected, CompletionContractItem[] actual, string id)
    {
        if (!expected.TryGetProperty("topN", out var topN))
            return;
        var count = topN.ValueKind == JsonValueKind.Number
            ? topN.GetInt32()
            : topN.TryGetProperty("count", out var countElement) ? countElement.GetInt32() : 0;
        if (topN.ValueKind == JsonValueKind.Array)
            AssertOrderedItems(JsonDocument.Parse(JsonSerializer.Serialize(new { top = topN })).RootElement, "top", actual, id);
        else if (topN.TryGetProperty("contains", out _))
            AssertContainsItems(topN, "contains", actual.Take(count > 0 ? count : actual.Length).ToArray(), id);
        Assert.True(actual.Length >= count, $"[{id}] expected at least {count} ranked completion items, received {actual.Length}.");
    }

    private static void AssertExactItems(JsonElement expected, CompletionContractItem[] actual, string id)
    {
        if (!expected.TryGetProperty("exact", out var exact))
            return;
        Assert.Equal(exact.GetArrayLength(), actual.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            var item = exact[index];
            Assert.True(SameIdentifier(actual[index].Label, GetString(item, "label")), $"[{id}] completion order differs at index {index}.");
            var kind = GetString(item, "kind");
            if (kind.Length > 0)
                Assert.Equal(kind, actual[index].Kind, ignoreCase: true);
        }
    }

    private static void AssertDiagnostics(JsonElement root, string sql, ISchemaProvider? schema, string id)
    {
        var expected = root.GetProperty("expect");
        var pipeline = GetString(expected, "pipeline", "full");
        var actual = CollectLintIssues(sql, schema, pipeline)
            .Select(issue => issue.RuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AssertDiagnosticExpectation(root.GetProperty("expect"), actual, id);
    }

    private static void AssertDiagnosticExpectation(JsonElement expected, HashSet<string> actual, string id)
    {
        if (!expected.TryGetProperty("diagnostics", out var diagnostics))
            return;
        foreach (var property in new[] { "anyOfCodes", "containsCodes" })
        {
            if (!diagnostics.TryGetProperty(property, out var codes))
                continue;
            var expectedCodes = codes.EnumerateArray().Select(code => code.GetString()!).ToArray();
            if (property == "anyOfCodes")
            {
                if (expectedCodes.Length > 0)
                    Assert.True(expectedCodes.Any(actual.Contains),
                        $"[{id}] expected any of [{string.Join(", ", expectedCodes)}], received [{string.Join(", ", actual)}].");
            }
            else
            {
                foreach (var code in expectedCodes)
                    Assert.True(actual.Contains(code), $"[{id}] expected diagnostic {code}, received [{string.Join(", ", actual)}].");
            }
        }
        if (diagnostics.TryGetProperty("notContainsCodes", out var excluded))
            foreach (var code in excluded.EnumerateArray().Select(item => item.GetString()!))
                Assert.DoesNotContain(code, actual);
    }

    private static void AssertSemantic(JsonElement root, string sql, ISchemaProvider? schema, SqlDialect dialect, string id)
    {
        var expected = root.GetProperty("expect");
        if (expected.TryGetProperty("diagnostics", out _))
        {
            var codes = CollectLintIssues(sql, schema, GetString(expected, "pipeline", "full"))
                .Select(issue => issue.RuleId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            AssertDiagnosticExpectation(expected, codes, id);
            return;
        }

        using var runtime = new ParsingRuntime(dialect);
        var parsed = runtime.Parse(sql);
        Assert.True(parsed.Valid, $"[{id}] expected a valid semantic-model input: {string.Join(" | ", parsed.Errors.Select(error => error.Code + ": " + error.Message))}");
        var statement = parsed.Statements.FirstOrDefault();
        Assert.NotNull(statement);
        if (expected.TryGetProperty("statementType", out var statementType))
            Assert.Equal(statementType.GetString(), StatementKindName(statement!));

        var selects = parsed.Statements.OfType<SelectStatement>().SelectMany(FlattenSelects).ToArray();
        if (expected.TryGetProperty("ctes", out var ctes))
        {
            var actualCtes = selects.SelectMany(select => select.With?.Ctes ?? Array.Empty<CteDefinition>())
                .Select(cte => cte.Name).ToArray();
            foreach (var cte in ctes.EnumerateArray().Select(value => value.GetString()!))
                Assert.Contains(actualCtes, actual => SameIdentifier(actual, cte));
        }

        var allCteNames = selects.SelectMany(select => select.With?.Ctes ?? Array.Empty<CteDefinition>())
            .Select(cte => cte.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = selects.SelectMany(select => EnumerateSources(select)).ToArray();
        if (expected.TryGetProperty("tables", out var tables))
        {
            foreach (var table in tables.EnumerateArray())
            {
                var name = GetString(table, "name");
                var match = sources.FirstOrDefault(source => source.Table is not null
                    && SameIdentifier(source.Table.Name, name)
                    && !allCteNames.Contains(source.Table.Name));
                Assert.NotNull(match);
                AssertOptionalIdentifier(table, "database", match!.Table!.Database);
                AssertOptionalIdentifier(table, "schema", match.Table.Schema);
                AssertOptionalIdentifier(table, "alias", match.Alias);
            }
        }
        if (expected.TryGetProperty("aliases", out var aliases))
        {
            foreach (var alias in aliases.EnumerateArray())
            {
                var aliasName = GetString(alias, "name");
                var relation = GetString(alias, "relation");
                Assert.Contains(sources, source => SameIdentifier(source.Alias, aliasName)
                    && (source.Table is null || SameIdentifier(source.Table.Name, relation)));
            }
        }
    }

    private static IEnumerable<SelectStatement> FlattenSelects(SelectStatement select)
    {
        yield return select;
        if (select.With is not null)
            foreach (var cte in select.With.Ctes)
                foreach (var nested in FlattenSelects(cte.Query))
                    yield return nested;
        foreach (var source in EnumerateSources(select))
            if (source.Subquery is not null)
                foreach (var nested in FlattenSelects(source.Subquery))
                    yield return nested;
    }

    private static IEnumerable<TableSource> EnumerateSources(SelectStatement select)
    {
        foreach (var reference in select.From ?? Array.Empty<TableReference>())
        {
            if (reference.Source.Table is not null || reference.Source.Subquery is not null)
                yield return reference.Source;
            foreach (var join in reference.Joins ?? Array.Empty<JoinClause>())
                yield return join.Source;
        }
    }

    private static string StatementKindName(Statement statement) => statement switch
    {
        SelectStatement => "select",
        InsertStatement => "insert",
        UpdateStatement => "update",
        DeleteStatement => "delete",
        CreateTableStatement or CreateViewStatement or CreateProcedureStatement => "create",
        _ => statement.GetType().Name.Replace("Statement", string.Empty, StringComparison.Ordinal).ToLowerInvariant()
    };

    private static void AssertOptionalIdentifier(JsonElement expected, string property, string? actual)
    {
        if (expected.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            Assert.True(SameIdentifier(actual, value.GetString()), $"Expected {property}={value.GetString()}, received {actual}.");
    }

    private static void AssertQuickFix(JsonElement root, string sql, ISchemaProvider? schema, string id)
    {
        var expected = root.GetProperty("expect");
        var code = GetString(expected, "code");
        var startOffset = -1;
        var endOffset = -1;
        if (expected.TryGetProperty("range", out var range)
            && range.TryGetProperty("start", out var start)
            && range.TryGetProperty("end", out var end))
        {
            startOffset = start.GetInt32();
            endOffset = end.GetInt32();
        }
        else if (GetString(expected, "find") is { Length: > 0 } needle)
        {
            startOffset = sql.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            endOffset = startOffset < 0 ? -1 : startOffset + needle.Length;
        }
        Assert.True(startOffset >= 0 && endOffset >= startOffset,
            $"[{id}] quick-fix fixture must define a range or find text for {code}.");

        var suggestedFix = GetString(expected, "suggestedFix");
        var issue = new LintIssue(code, string.Empty, LintSeverity.Error,
            startOffset, endOffset, SuggestedFix: suggestedFix.Length > 0 ? suggestedFix : null);
        var fix = NzLintCodeActions.GetQuickFix(issue, sql, schema);
        Assert.True(fix.HasValue, $"[{id}] diagnostic {code} did not produce a quick fix.");
        var fixedSql = fix!.Value.Apply(sql);

        if (expected.TryGetProperty("resultSql", out var expectedSql))
            Assert.Equal(expectedSql.GetString(), fixedSql);
        if (expected.TryGetProperty("resultContains", out var resultContains))
            Assert.Contains(resultContains.GetString() ?? string.Empty, fixedSql, StringComparison.OrdinalIgnoreCase);
        if (expected.TryGetProperty("suggestedFix", out var suggested))
            Assert.True(fix.Value.Description.Contains(suggested.GetString()!, StringComparison.OrdinalIgnoreCase)
                || fixedSql.Contains(suggested.GetString()!, StringComparison.OrdinalIgnoreCase),
                $"[{id}] quick fix did not use the expected suggestion.");
    }

    private static IReadOnlyList<LintIssue> CollectLintIssues(
        string sql, ISchemaProvider? schema, string pipeline)
    {
        using var engine = new LintEngine(SqlDialect.Netezza);
        var issues = new List<LintIssue>();
        if (pipeline is "cheap" or "full")
            issues.AddRange(engine.RunCheapRules(sql));
        if (pipeline is "expensive" or "full")
        {
            // The expensive analyzer needs an explicit metadata provider. An
            // empty provider keeps syntax-only cases on the real parser path.
            issues.AddRange(engine.RunExpensiveAnalysis(new LintConfig(
                sql, schema ?? new InMemorySchemaProvider(), DocumentUri: $"conformance:{Guid.NewGuid():N}" )).Issues);
        }
        return issues;
    }

    private static void AssertSignature(JsonElement root, string sql, int cursor, SqlDialect dialect, string id)
    {
        var result = NzSignatureHelpService.GetSignatureHelp(sql, cursor, dialect: dialect);
        Assert.NotNull(result);
        var expected = root.GetProperty("expect");
        if (expected.TryGetProperty("containsSignature", out var signature))
            Assert.Contains(result!.Signatures, item => item.Label == signature.GetString());
        if (expected.TryGetProperty("activeParameter", out var activeParameter))
            Assert.Equal(activeParameter.GetInt32(), result!.ActiveParameter);
    }

    private static void AssertFormatting(JsonElement root, string sql, SqlDialect dialect, string id)
    {
        var parser = DialectRuntime.CreateParser(DialectRuntime.Tokenize(sql, dialect).ToArray(), dialect);
        var statement = parser.Parse();
        Assert.NotNull(statement);
        Assert.Empty(parser.Errors);
        var formatted = NzSqlFormatter.Format(statement!);
        Assert.Equal(GetString(root.GetProperty("expect"), "formattedSql"), formatted);
    }

    private static void AssertHover(JsonElement root, string sql, int cursor, ISchemaProvider? schema, SqlDialect dialect, string id)
    {
        var hover = NzHoverService.GetHover(
            sql, cursor, schema, catalog: DialectRuntime.AuthoringCatalogOrNull(dialect), dialect: dialect);
        Assert.NotNull(hover);
        var expected = root.GetProperty("expect");
        if (expected.TryGetProperty("contains", out var contains))
            Assert.Contains(contains.GetString() ?? string.Empty, hover!.Content, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertRecovery(
        JsonElement root, string sql, int cursor, ISchemaProvider? schema, SqlDialect dialect, string id)
    {
        using var runtime = new ParsingRuntime(dialect);
        var parsed = runtime.Parse(sql);
        if (root.GetProperty("expect").TryGetProperty("statementDetected", out var detected) && detected.GetBoolean())
            Assert.NotEmpty(parsed.Statements);
        if (root.GetProperty("expect").TryGetProperty("aliasesDetected", out var aliases))
        {
            var actualAliases = parsed.Statements.SelectMany(statement => statement switch
            {
                SelectStatement select => EnumerateSources(select).Select(source => source.Alias).Where(alias => alias is not null),
                _ => Array.Empty<string?>()
            }).ToArray();
            foreach (var alias in aliases.EnumerateArray().Select(item => item.GetString()!))
                Assert.Contains(actualAliases, actual => SameIdentifier(actual, alias));
        }
        if (root.GetProperty("expect").TryGetProperty("completionAvailable", out var completionAvailable)
            && completionAvailable.GetBoolean())
        {
            var items = await CompletionOrchestrator.GetCompletions(
                sql, cursor, schema, dialect,
                options: new CompletionOrchestrationOptions { ForcedAutocomplete = true });
            Assert.NotEmpty(items.EngineItems);
        }
    }

    private static ISchemaProvider? LoadSchema(JsonElement root)
    {
        var fixtureName = GetString(root, "metadataFixture");
        if (fixtureName.Length == 0)
            return null;
        var fixtureParts = fixtureName.Split('/', 2, StringSplitOptions.None);
        var dialect = fixtureParts[0];
        var name = fixtureParts[1];
        var path = Path.Combine(ConformanceRoot(), "fixtures", "metadata", dialect, name + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var schema = new InMemorySchemaProvider();
        var qualificationProposals = new List<TableQualificationProposal>();
        foreach (var database in document.RootElement.GetProperty("databases").EnumerateArray())
        {
            var databaseName = GetString(database, "name");
            if (!database.TryGetProperty("schemas", out var schemas))
                continue;
            foreach (var schemaElement in schemas.EnumerateArray())
            {
                var schemaName = GetString(schemaElement, "name");
                AddMetadataObjects(schema, schemaElement, "tables", databaseName, schemaName, isView: false, qualificationProposals);
                AddMetadataObjects(schema, schemaElement, "views", databaseName, schemaName, isView: true, qualificationProposals);
            }
        }
        schema.SetTableQualificationProposals(qualificationProposals);
        return schema;
    }

    private static void AddMetadataObjects(
        InMemorySchemaProvider provider, JsonElement schema, string property,
        string database, string schemaName, bool isView, List<TableQualificationProposal> qualificationProposals)
    {
        if (!schema.TryGetProperty(property, out var objects))
            return;
        foreach (var item in objects.EnumerateArray())
        {
            var columns = item.TryGetProperty("columns", out var columnArray)
                ? columnArray.EnumerateArray().Select(column => new ColumnInfo(
                    GetString(column, "name"),
                    DataType: GetString(column, "type", GetString(column, "dataType")),
                    Description: GetString(column, "description"))).ToArray()
                : Array.Empty<ColumnInfo>();
            var tableName = GetString(item, "name");
            provider.AddTable(new TableInfo(
                tableName, schemaName, database,
                Columns: columns, IsView: isView));
            qualificationProposals.Add(new TableQualificationProposal(
                database, schemaName, tableName, $"{database}.{schemaName}.{tableName}", IsPreferred: true));
        }
    }

    private static int RemoveCursorMarker(JsonElement root, ref string sql)
    {
        var marker = GetString(root, "cursorMarker");
        if (marker.Length == 0)
            return sql.Length;
        var offset = sql.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(offset >= 0, $"[{GetString(root, "id")}] cursor marker {marker} was not found.");
        sql = sql.Remove(offset, marker.Length);
        return offset;
    }

    private static string RenderSql(string sql) => sql
        .Replace("{{proc}}", "JB_CF_PROCEDURE", StringComparison.Ordinal)
        .Replace("{{table}}", "JUST_DATA.ADMIN.JB_CF_TABLE", StringComparison.Ordinal)
        .Replace("{{table2}}", "JUST_DATA.ADMIN.JB_CF_TABLE2", StringComparison.Ordinal)
        .Replace("{{table3}}", "JUST_DATA.ADMIN.JB_CF_TABLE3", StringComparison.Ordinal)
        .Replace("{{bareTable}}", "JB_CF_TABLE", StringComparison.Ordinal)
        .Replace("{{bareTable2}}", "JB_CF_TABLE2", StringComparison.Ordinal)
        .Replace("{{bareTable3}}", "JB_CF_TABLE3", StringComparison.Ordinal)
        .Replace("{{database}}", "JUST_DATA", StringComparison.Ordinal)
        .Replace("{{schema}}", "ADMIN", StringComparison.Ordinal);

    private static bool OptionalMatches(JsonElement item, string property, string? actual) =>
        !item.TryGetProperty(property, out var expected)
        || expected.ValueKind == JsonValueKind.Null
        || string.Equals(expected.GetString(), actual, StringComparison.OrdinalIgnoreCase);

    private static bool SameIdentifier(string? left, string? right) =>
        string.Equals(left?.Trim('"'), right?.Trim('"'), StringComparison.OrdinalIgnoreCase);

    private static string GetString(JsonElement element, string property, string fallback = "") =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool TryDialect(string? name, out SqlDialect dialect)
    {
        if (Enum.TryParse(name, ignoreCase: true, out dialect) && Enum.IsDefined(dialect))
            return true;
        dialect = SqlDialect.Netezza;
        return false;
    }

    private static IEnumerable<(string Json, SqlDialect Dialect)> LoadCases()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(ConformanceRoot(), "dialects"), "*.jsonl", SearchOption.AllDirectories).Order())
        foreach (var line in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var name = GetString(root, "dialect");
            if (name == "common")
            {
                if (!root.TryGetProperty("appliesTo", out var appliesTo))
                    continue;
                foreach (var applies in appliesTo.EnumerateArray())
                    if (TryDialect(applies.GetString(), out var dialect))
                        yield return (line, dialect);
            }
            else if (TryDialect(name, out var dialect))
            {
                yield return (line, dialect);
            }
        }
    }

    private static string ConformanceRoot()
    {
        var configured = Environment.GetEnvironmentVariable("JUSTYBASE_SQL_CONFORMANCE_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = Path.GetFullPath(configured);
            if (Directory.Exists(Path.Combine(full, "dialects")))
                return full;
            throw new DirectoryNotFoundException($"JUSTYBASE_SQL_CONFORMANCE_PATH has no dialects/: {full}");
        }
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            var candidates = new[]
            {
                Path.Combine(current.FullName, "JustyBase.SqlConformance"),
                current.Parent is null ? string.Empty : Path.Combine(current.Parent.FullName, "JustyBase.SqlConformance")
            };
            var found = candidates.FirstOrDefault(candidate => candidate.Length > 0 && Directory.Exists(Path.Combine(candidate, "dialects")));
            if (found is not null)
                return found;
        }
        throw new DirectoryNotFoundException("Set JUSTYBASE_SQL_CONFORMANCE_PATH to the local JustyBase.SqlConformance repository.");
    }

    private sealed record CompletionContractItem(string Label, string Kind, string? Detail, string? Documentation);
}
