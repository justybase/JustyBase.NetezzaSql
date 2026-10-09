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
[Trait("Category", "SqlConformance")]
public sealed class SharedSqlConformanceAuthoringTests
{
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "completion", "diagnostics", "semantic", "quick-fixes", "signature",
        "formatting", "hover", "recovery"
    };

    public static IEnumerable<object[]> SharedCases()
    {
        var yielded = false;
        foreach (var (json, dialect) in LoadCases())
        {
            yielded = true;
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
        if (!yielded)
            yield return [SharedSqlConformanceTests.MissingRepoSentinelId, "{}", SqlDialect.Netezza];
    }

    [Theory]
    [MemberData(nameof(SharedCases))]
    public async Task Shared_authoring_behavior_matches_the_contract(
        string id,
        string caseJson,
        SqlDialect dialect)
    {
        if (id == SharedSqlConformanceTests.MissingRepoSentinelId)
            throw new DirectoryNotFoundException(SharedSqlConformanceTests.MissingRepoMessage);
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
                    AssertSemantic(root, sql, cursor, schema, dialect, id);
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
            options: new CompletionOrchestrationOptions
            {
                ForcedAutocomplete = true,
                TriggerKind = TriggerKindOf(root),
            });
        var items = result.EngineItems
            .Select(item => new CompletionContractItem(
                item.Label, NormalizeKind(item.Kind), item.Detail, item.Documentation, item.InsertText))
            .Concat(result.WordListItems.Select(item => new CompletionContractItem(
                item.Label, NormalizeKind(item.Kind), item.Detail, item.Description, null)))
            .ToArray();
        var expected = root.GetProperty("expect");
        if (Environment.GetEnvironmentVariable("CONFORMANCE_DUMP_COMPLETION") is { Length: > 0 } dumpPath)
        {
            File.AppendAllText(dumpPath, $"COMPLETION_DUMP {id} {JsonSerializer.Serialize(items.Take(14).Select(i => new[] { i.Label, i.Kind, i.InsertText, i.Detail }))}" + Environment.NewLine);
        }

        AssertContainsItems(expected, "contains", items, id);
        AssertAbsentItems(expected, "notContains", items, id);
        if (expected.TryGetProperty("available", out var available))
            Assert.Equal(available.GetBoolean(), items.Length > 0);
        AssertOrderedItems(expected, "top", items, id);
        AssertTopN(expected, items, id);
        AssertExactItems(expected, items, id);
    }

    private static string NormalizeKind(CompletionKind kind) => kind switch
    {
        CompletionKind.View => "view",
        CompletionKind.Reference => "reference",
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
                (label.Length == 0 || SameIdentifier(candidate.Label, label))
                && (kind.Length == 0 || candidate.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                && OptionalMatches(item, "detail", candidate.Detail)
                && OptionalMatches(item, "documentation", candidate.Documentation)
                && OptionalMatches(item, "insertText", candidate.InsertText) && InsertTextContains(item, candidate.InsertText));
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
                (label.Length == 0 || SameIdentifier(candidate.Label, label))
                && (kind.Length == 0 || candidate.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                && InsertTextContains(item, candidate.InsertText));
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
            Assert.True(index < actual.Length, $"[{id}] missing ranked completion at {index}.");
            var candidate = actual[index];
            Assert.True(label.Length == 0 || SameIdentifier(candidate.Label, label), $"[{id}] expected {label} at rank {index}, received {candidate.Label}.");
            if (item.ValueKind == JsonValueKind.Object)
            {
                Assert.True(OptionalMatches(item, "kind", candidate.Kind));
                Assert.True(OptionalMatches(item, "detail", candidate.Detail));
                Assert.True(OptionalMatches(item, "documentation", candidate.Documentation));
                Assert.True(OptionalMatches(item, "insertText", candidate.InsertText) && InsertTextContains(item, candidate.InsertText));
            }
            index++;
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
        else if (topN.TryGetProperty("items", out _))
            AssertContainsItems(topN, "items", actual.Take(count).ToArray(), id);
        Assert.True(actual.Length >= count, $"[{id}] expected at least {count} ranked completion items, received {actual.Length}.");
    }

    private static void AssertExactItems(JsonElement expected, CompletionContractItem[] actual, string id)
    {
        if (!expected.TryGetProperty("exact", out var exact))
            return;
        Assert.Equal(exact.GetArrayLength(), actual.Length);
        AssertOrderedItems(expected, "exact", actual, id);
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
        var actual = CollectLintIssues(sql, schema, pipeline);
        AssertDiagnosticExpectation(expected, actual, sql, schema, id);
    }

    private static void AssertDiagnosticExpectation(
        JsonElement expected, IReadOnlyList<LintIssue> actual, string sql, ISchemaProvider? schema, string id)
    {
        if (!expected.TryGetProperty("diagnostics", out var diagnostics))
            return;
        var actualCodes = actual.Select(issue => issue.RuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in new[] { "anyOfCodes", "containsCodes" })
        {
            if (!diagnostics.TryGetProperty(property, out var codes))
                continue;
            var expectedCodes = codes.EnumerateArray().Select(code => code.GetString()!).ToArray();
            if (property == "anyOfCodes")
            {
                if (expectedCodes.Length > 0)
                    Assert.True(expectedCodes.Any(actualCodes.Contains),
                        $"[{id}] expected any of [{string.Join(", ", expectedCodes)}], received [{string.Join(", ", actualCodes)}].");
            }
            else
            {
                foreach (var code in expectedCodes)
                    Assert.True(actualCodes.Contains(code), $"[{id}] expected diagnostic {code}, received [{string.Join(", ", actualCodes)}].");
            }
        }
        if (diagnostics.TryGetProperty("notContainsCodes", out var excluded))
            foreach (var code in excluded.EnumerateArray().Select(item => item.GetString()!))
                Assert.DoesNotContain(code, actualCodes);

        if (diagnostics.TryGetProperty("containsSeverities", out var severities))
        {
            foreach (var severity in severities.EnumerateObject())
            {
                var issue = actual.FirstOrDefault(candidate =>
                    candidate.RuleId.Equals(severity.Name, StringComparison.OrdinalIgnoreCase));
                Assert.NotNull(issue);
                Assert.Equal(severity.Value.GetString(), SeverityName(issue!.Severity));
            }
        }

        if (diagnostics.TryGetProperty("containsRanges", out var ranges))
        {
            foreach (var range in ranges.EnumerateArray())
            {
                var code = GetString(range, "code");
                var start = range.GetProperty("start").GetInt32();
                var end = range.GetProperty("end").GetInt32();
                var issue = actual.FirstOrDefault(candidate =>
                    candidate.RuleId.Equals(code, StringComparison.OrdinalIgnoreCase)
                    && candidate.StartOffset == start && candidate.EndOffset == end);
                Assert.NotNull(issue);
                if (range.TryGetProperty("text", out var text))
                {
                    Assert.True(start >= 0 && end >= start && end <= sql.Length,
                        $"[{id}] expected range for {code} falls outside the SQL text.");
                    Assert.Equal(text.GetString(), sql[start..end]);
                }
            }
        }

        if (diagnostics.TryGetProperty("containsFixes", out var fixes))
        {
            foreach (var expectedFix in fixes.EnumerateArray())
            {
                var code = GetString(expectedFix, "code");
                var candidates = actual.Where(issue =>
                    issue.RuleId.Equals(code, StringComparison.OrdinalIgnoreCase));
                var matchingFix = candidates
                    .Select(issue => NzLintCodeActions.GetQuickFix(issue, sql, schema))
                    .FirstOrDefault(fix => fix.HasValue);
                Assert.True(matchingFix.HasValue,
                    $"[{id}] production diagnostics did not provide a usable {code} quick fix.");
                if (expectedFix.TryGetProperty("titleContains", out var title))
                    Assert.Contains(title.GetString() ?? "", matchingFix!.Value.Description, StringComparison.OrdinalIgnoreCase);
                var fixedSql = matchingFix!.Value.Apply(sql);
                if (expectedFix.TryGetProperty("newTextContains", out var newText))
                    Assert.Contains(newText.GetString() ?? "", fixedSql, StringComparison.OrdinalIgnoreCase);
                if (expectedFix.TryGetProperty("safety", out var safety))
                {
                    var info = candidates.Select(issue => NzLintCodeActions.GetQuickFixInfo(issue, sql, schema))
                        .FirstOrDefault(fix => fix is not null);
                    Assert.NotNull(info);
                    Assert.Equal(safety.GetString(), info!.Safety == SqlQuickFixSafety.Safe ? "safe" : "review-required");
                }
            }
        }
    }

    private static string SeverityName(LintSeverity severity) => severity switch
    {
        LintSeverity.Error => "error",
        LintSeverity.Warning => "warning",
        LintSeverity.Information => "information",
        LintSeverity.Hint => "hint",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
    };
    private static void AssertSemantic(JsonElement root, string sql, int cursor, ISchemaProvider? schema, SqlDialect dialect, string id)
    {
        var expected = root.GetProperty("expect");
        if (expected.TryGetProperty("diagnostics", out _))
        {
            var issues = CollectLintIssues(sql, schema, GetString(expected, "pipeline", "full"));
            AssertDiagnosticExpectation(expected, issues, sql, schema, id);
            return;
        }

        if (expected.TryGetProperty("columnIdentity", out var columnIdentity))
        {
            AssertColumnIdentity(columnIdentity, sql, cursor, schema, dialect, id);
            return;
        }

        if (expected.TryGetProperty("definition", out var definition))
        {
            var actual = NzSymbolService.GetDefinition(sql, cursor);
            Assert.NotNull(actual);
            Assert.Equal(definition.GetProperty("start").GetInt32(), actual.StartAbsolute);
            Assert.Equal(definition.GetProperty("end").GetInt32(), actual.EndAbsolute);
            Assert.Equal(GetString(definition, "text"), sql[actual.StartAbsolute..actual.EndAbsolute]);
        }

        if (expected.TryGetProperty("navigation", out var navigation))
        {
            var symbol = NzSymbolService.GetSymbol(sql, cursor);
            Assert.NotNull(symbol);
            Assert.Equal(GetString(navigation, "name"), symbol.OldName);
            var kind = symbol.Kind switch
            {
                SqlSymbolKind.Cte => "cte",
                SqlSymbolKind.Alias => "table_alias",
                SqlSymbolKind.Table => "table",
                _ => throw new ArgumentOutOfRangeException()
            };
            Assert.Equal(GetString(navigation, "kind"), kind);
            var actualDefinition = Assert.Single(symbol.Occurrences, occurrence => occurrence.IsDefinition);
            AssertNavigationRange(navigation.GetProperty("definition"), actualDefinition, sql);
            var expectedReferences = navigation.GetProperty("references").EnumerateArray().ToArray();
            var includeDeclaration = navigation.TryGetProperty("includeDeclaration", out var include) && include.GetBoolean();
            var actualReferences = NzSymbolService.GetReferences(sql, cursor, includeDeclaration)
                .OrderBy(occurrence => occurrence.StartAbsolute).ToArray();
            Assert.Equal(expectedReferences.Length, actualReferences.Length);
            for (var index = 0; index < expectedReferences.Length; index++)
                AssertNavigationRange(expectedReferences[index], actualReferences[index], sql);
            if (navigation.TryGetProperty("targetKind", out var targetKind)) Assert.Equal("local_document_definition", targetKind.GetString());
            if (navigation.TryGetProperty("rename", out var rename))
            {
                var edits = NzRenameService.GetRenameEdits(sql, cursor, GetString(rename, "newName"));
                Assert.NotNull(edits);
                var expectedEdits = rename.GetProperty("edits").EnumerateArray().ToArray();
                Assert.Equal(expectedEdits.Length, edits.Count);
                var resultSql = sql;
                for (var index = 0; index < edits.Count; index++)
                {
                    Assert.Equal(expectedEdits[index].GetProperty("start").GetInt32(), edits[index].StartOffset);
                    Assert.Equal(expectedEdits[index].GetProperty("end").GetInt32(), edits[index].EndOffset);
                    Assert.Equal(GetString(expectedEdits[index], "text"), edits[index].NewText);
                }
                foreach (var edit in edits.Reverse()) resultSql = resultSql[..edit.StartOffset] + edit.NewText + resultSql[edit.EndOffset..];
                Assert.Equal(GetString(rename, "resultSql"), resultSql);
            }
        }

        using var runtime = new ParsingRuntime(dialect);
        var parsed = runtime.Parse(sql);
        if (expected.TryGetProperty("scopeAtCursor", out _))
        {
            if (expected.TryGetProperty("valid", out var scopeExpectedValid))
            {
                Assert.Equal(scopeExpectedValid.GetBoolean(), parsed.Valid);
            }
        }
        else
        {
            Assert.True(parsed.Valid, $"[{id}] expected a valid semantic-model input: {string.Join(" | ", parsed.Errors.Select(error => error.Code + ": " + error.Message))}");
        }
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

        var sources = selects.SelectMany(select => EnumerateSources(select)).ToArray();
        if (expected.TryGetProperty("tables", out var tables))
        {
            foreach (var table in tables.EnumerateArray())
            {
                var name = GetString(table, "name");
                var match = sources.FirstOrDefault(source => source.Table is not null
                    && SameIdentifier(source.Table.Name, name));
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

        if (expected.TryGetProperty("scopeAtCursor", out var scopeAtCursor))
        {
            var actual = SqlScopeAtCursorResolver.Resolve(sql, cursor, schema, dialect);
            Assert.NotNull(actual);
            if (scopeAtCursor.TryGetProperty("visibleRelations", out var visibleRelations))
            {
                var expectedRelations = visibleRelations.EnumerateArray().ToArray();
                foreach (var relation in expectedRelations)
                {
                    var name = GetString(relation, "name");
                    var alias = GetString(relation, "alias", name);
                    var kind = GetString(relation, "kind");
                    Assert.Contains(actual!.VisibleRelations, item =>
                        SameIdentifier(item.Name, name)
                        && SameIdentifier(item.Alias, alias)
                        && string.Equals(ScopeRelationKindName(item.Kind), kind, StringComparison.OrdinalIgnoreCase));
                }
                if (scopeAtCursor.TryGetProperty("exactRelations", out var exactRelations)
                    && exactRelations.GetBoolean())
                {
                    Assert.Equal(expectedRelations.Length, actual!.VisibleRelations.Count);
                }
            }
            if (scopeAtCursor.TryGetProperty("absentRelations", out var absentRelations))
            {
                foreach (var relation in absentRelations.EnumerateArray())
                {
                    var name = GetString(relation, "name");
                    var kind = GetString(relation, "kind");
                    Assert.DoesNotContain(actual!.VisibleRelations, item =>
                        SameIdentifier(item.Name, name)
                        && (kind.Length == 0
                            || string.Equals(ScopeRelationKindName(item.Kind), kind, StringComparison.OrdinalIgnoreCase)));
                }
            }
            if (scopeAtCursor.TryGetProperty("visibleCtes", out var visibleCtes))
            {
                Assert.Equal(
                    visibleCtes.EnumerateArray().Select(value => value.GetString()!.ToUpperInvariant()).OrderBy(value => value),
                    actual!.VisibleCtes.Select(value => value.ToUpperInvariant()).OrderBy(value => value));
            }
            if (scopeAtCursor.TryGetProperty("visibleAliases", out var visibleAliases))
            {
                Assert.Equal(
                    visibleAliases.EnumerateArray().Select(value => value.GetString()!.ToUpperInvariant()).OrderBy(value => value),
                    actual!.VisibleAliases.Select(value => value.ToUpperInvariant()).OrderBy(value => value));
            }
        }
    }

    private static string ScopeRelationKindName(SqlScopeRelationKind kind) => kind switch
    {
        SqlScopeRelationKind.Table => "table",
        SqlScopeRelationKind.Cte => "cte",
        SqlScopeRelationKind.DerivedTable => "derived_table",
        SqlScopeRelationKind.ScriptLocalTable => "script_local_table",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static void AssertColumnIdentity(JsonElement expected, string sql, int cursor, ISchemaProvider? schema, SqlDialect dialect, string id)
    {
        var identity = NzColumnIdentityService.Resolve(sql, cursor, schema, dialect);
        Assert.True(identity is not null, $"[{id}] production column identity was not resolved");
        Assert.True(string.Equals(GetString(expected, "name"), identity!.Name, StringComparison.OrdinalIgnoreCase),
            $"[{id}] column name differs: {identity.Name}");
        var status = identity.Status switch
        {
            SqlColumnResolutionStatus.Resolved => "resolved",
            SqlColumnResolutionStatus.Ambiguous => "ambiguous",
            _ => "unresolved",
        };
        Assert.True(GetString(expected, "status") == status, $"[{id}] column status differs: {status}");
        if (expected.TryGetProperty("targetKind", out var targetKind))
            Assert.True(targetKind.GetString() == (identity.IsLocalDefinition ? "local_document_definition" : "catalog_object_target"),
                $"[{id}] column target kind differs");
        if (expected.TryGetProperty("relationKind", out var relationKind))
        {
            var actualKind = identity.RelationKind switch
            {
                SqlColumnRelationKind.Table => "table",
                SqlColumnRelationKind.Cte => "cte",
                SqlColumnRelationKind.DerivedTable => "derived_table",
                SqlColumnRelationKind.ScriptLocalTable => "script_local_table",
                SqlColumnRelationKind.OutputAlias => "output_alias",
                _ => null,
            };
            Assert.True(relationKind.GetString() == actualKind, $"[{id}] relation kind differs: {actualKind}");
        }
        if (expected.TryGetProperty("relation", out var relation))
            Assert.True(string.Equals(relation.GetString(), identity.Relation, StringComparison.OrdinalIgnoreCase),
                $"[{id}] relation differs: {identity.Relation}");
        if (expected.TryGetProperty("definition", out var definition))
        {
            var actual = NzColumnIdentityService.GetDefinition(sql, cursor, schema, dialect);
            Assert.True(actual is not null, $"[{id}] production column definition was not resolved");
            AssertColumnRange(definition, actual!, sql, id);
        }
        else
        {
            Assert.True(identity.Definition is null, $"[{id}] unexpected local column definition");
        }
        if (expected.TryGetProperty("catalog", out var catalog)) AssertCatalogColumn(catalog, identity.Catalog, id, "catalog");
        if (expected.TryGetProperty("origin", out var origin)) AssertCatalogColumn(origin, identity.Origin, id, "origin");
        if (expected.TryGetProperty("candidates", out var candidates))
        {
            var wanted = candidates.EnumerateArray().Select(item => item.GetString()!.ToUpperInvariant()).OrderBy(x => x).ToArray();
            var actual = identity.Candidates.Select(item => item.ToUpperInvariant()).OrderBy(x => x).ToArray();
            Assert.True(wanted.SequenceEqual(actual), $"[{id}] ambiguity candidates differ: {string.Join(",", actual)}");
        }
        if (expected.TryGetProperty("references", out var references))
        {
            var includeDeclaration = expected.TryGetProperty("includeDeclaration", out var include) && include.GetBoolean();
            var actual = NzColumnIdentityService.GetReferences(sql, cursor, includeDeclaration, schema, dialect);
            var wanted = references.EnumerateArray().ToArray();
            Assert.True(wanted.Length == actual.Count,
                $"[{id}] column references differ: {string.Join(",", actual.Select(item => $"{item.StartOffset}-{item.EndOffset}"))}");
            for (var index = 0; index < wanted.Length; index++) AssertColumnRange(wanted[index], actual[index], sql, id);
        }
    }

    private static void AssertColumnRange(JsonElement expected, SqlColumnOccurrence actual, string sql, string id)
    {
        Assert.True(expected.GetProperty("start").GetInt32() == actual.StartOffset
                    && expected.GetProperty("end").GetInt32() == actual.EndOffset
                    && GetString(expected, "text") == sql[actual.StartOffset..actual.EndOffset],
            $"[{id}] column range differs: {actual.StartOffset}-{actual.EndOffset}");
    }

    private static void AssertCatalogColumn(JsonElement expected, SqlCatalogColumn? actual, string id, string label)
    {
        Assert.True(actual is not null, $"[{id}] production {label} column is missing");
        static bool Same(JsonElement element, string property, string? value) =>
            !element.TryGetProperty(property, out var wanted) || wanted.ValueKind == JsonValueKind.Null
                ? value is null || !element.TryGetProperty(property, out _)
                : string.Equals(wanted.GetString(), value, StringComparison.OrdinalIgnoreCase);
        Assert.True(Same(expected, "database", actual!.Database) && Same(expected, "schema", actual.Schema)
                    && Same(expected, "relation", actual.Relation) && Same(expected, "column", actual.Column),
            $"[{id}] production {label} differs: {actual}");
    }

    private static void AssertNavigationRange(JsonElement expected, SymbolOccurrence actual, string sql)
    {
        Assert.Equal(expected.GetProperty("start").GetInt32(), actual.StartAbsolute);
        Assert.Equal(expected.GetProperty("end").GetInt32(), actual.EndAbsolute);
        Assert.Equal(GetString(expected, "text"), sql[actual.StartAbsolute..actual.EndAbsolute]);
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

    internal static string StatementKindName(Statement statement) => statement switch
    {
        SelectStatement => "select",
        InsertStatement => "insert",
        UpdateStatement => "update",
        DeleteStatement => "delete",
        CreateTableStatement or CreateViewStatement or CreateProcedureStatement
            or CreateExternalTableStatement or CreateSequenceStatement => "create",
        AlterTableStatement => "alter",
        DropStatement => "drop",
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
        if (expected.TryGetProperty("fixAll", out var fixAll))
        {
            AssertFixAll(fixAll, sql, schema, id);
            if (!expected.TryGetProperty("code", out _)) return;
        }
        var code = GetString(expected, "code");
        var issue = CollectLintIssues(sql, schema, "full").FirstOrDefault(candidate =>
            candidate.RuleId.Equals(code, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(issue);
        if (expected.TryGetProperty("fixAllEligible", out var eligible))
            Assert.True(eligible.GetBoolean() == NzLintCodeActions.IsSafeForFixAll(code!),
                $"[{id}] production Fix All eligibility for {code} differs.");
        var fix = NzLintCodeActions.GetQuickFix(issue!, sql, schema);
        Assert.True(fix.HasValue, $"[{id}] production diagnostic {code} did not produce a quick fix.");
        if (expected.TryGetProperty("titleContains", out var title)
            || expected.TryGetProperty("safety", out _)
            || expected.TryGetProperty("edits", out _))
        {
            var action = NzLintCodeActions.GetQuickFixInfo(issue!, sql, schema);
            Assert.NotNull(action);
            if (title.ValueKind == JsonValueKind.String) Assert.Contains(title.GetString()!, action.Title);
            if (expected.TryGetProperty("safety", out var safety))
                Assert.Equal(safety.GetString(), action.Safety == SqlQuickFixSafety.Safe ? "safe" : "review-required");
            if (expected.TryGetProperty("edits", out var edits))
            {
                var actual = action.Edits.OrderBy(edit => edit.StartOffset).ThenBy(edit => edit.EndOffset).ToArray();
                var wanted = edits.EnumerateArray().OrderBy(edit => edit.GetProperty("start").GetInt32()).ThenBy(edit => edit.GetProperty("end").GetInt32()).ToArray();
                Assert.Equal(wanted.Length, actual.Length);
                for (var index = 0; index < wanted.Length; index++)
                {
                    Assert.Equal(wanted[index].GetProperty("start").GetInt32(), actual[index].StartOffset);
                    Assert.Equal(wanted[index].GetProperty("end").GetInt32(), actual[index].EndOffset);
                    Assert.Equal(GetString(wanted[index], "text"), actual[index].NewText);
                }
            }
        }
        var fixedSql = fix!.Value.Apply(sql);

        if (expected.TryGetProperty("resultSql", out var expectedSql))
            Assert.Equal(expectedSql.GetString(), fixedSql);
        if (expected.TryGetProperty("resultContains", out var resultContains))
            Assert.Contains(resultContains.GetString() ?? string.Empty, fixedSql, StringComparison.OrdinalIgnoreCase);
        if (expected.TryGetProperty("suggestedFix", out var suggested))
            Assert.True(fix.Value.Description.Contains(suggested.GetString()!, StringComparison.OrdinalIgnoreCase)
                || fixedSql.Contains(suggested.GetString()!, StringComparison.OrdinalIgnoreCase),
                $"[{id}] quick fix did not use the expected suggestion.");
        if (expected.TryGetProperty("range", out var range)
            && range.TryGetProperty("start", out var start)
            && range.TryGetProperty("end", out var end))
        {
            Assert.Equal(start.GetInt32(), issue!.StartOffset);
            Assert.Equal(end.GetInt32(), issue.EndOffset);
        }
        if (expected.TryGetProperty("find", out var find))
        {
            Assert.True(issue!.StartOffset >= 0 && issue.EndOffset >= issue.StartOffset && issue.EndOffset <= sql.Length,
                $"[{id}] production quick-fix range falls outside the SQL text.");
            Assert.Contains(find.GetString() ?? string.Empty, sql[issue.StartOffset..issue.EndOffset], StringComparison.OrdinalIgnoreCase);
        }
    }
    private static void AssertFixAll(JsonElement fixAll, string sql, ISchemaProvider? schema, string id)
    {
        var issues = CollectLintIssues(sql, schema, "full");
        var result = NzLintCodeActions.ApplyAllSafeFixes(sql, issues, schema);
        Assert.True(fixAll.GetProperty("resultSql").GetString() == result,
            $"[{id}] production Fix All result differs: {result}");
        if (fixAll.TryGetProperty("notApplied", out var notApplied))
        {
            foreach (var code in notApplied.EnumerateArray().Select(item => item.GetString()!))
                Assert.True(issues.Any(issue => issue.RuleId.Equals(code, StringComparison.OrdinalIgnoreCase)),
                    $"[{id}] production diagnostics did not report {code}.");
        }
        if (fixAll.TryGetProperty("idempotent", out var idempotent) && idempotent.GetBoolean())
        {
            var again = NzLintCodeActions.ApplyAllSafeFixes(result, CollectLintIssues(result, schema, "full"), schema);
            Assert.True(result == again, $"[{id}] production Fix All is not idempotent: {again}");
        }
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
        if (expected.TryGetProperty("targetKind", out var targetKind))
            Assert.Equal(targetKind.GetString(), hover!.TargetKind, ignoreCase: true);
    }

    private static async Task AssertRecovery(
        JsonElement root, string sql, int cursor, ISchemaProvider? schema, SqlDialect dialect, string id)
    {
        using var runtime = new ParsingRuntime(dialect);
        var parsed = runtime.Parse(sql);
        var expected = root.GetProperty("expect");
        if (expected.TryGetProperty("statementDetected", out var detected))
        {
            if (detected.GetBoolean())
                Assert.NotEmpty(parsed.Statements);
            else
                Assert.Empty(parsed.Statements);
        }
        var selects = parsed.Statements.OfType<SelectStatement>().SelectMany(FlattenSelects).ToArray();
        if (expected.TryGetProperty("aliasesDetected", out var aliases))
        {
            var actualAliases = selects.SelectMany(EnumerateSources)
                .Select(source => source.Alias).Where(alias => alias is not null).ToArray();
            foreach (var alias in aliases.EnumerateArray().Select(item => item.GetString()!))
                Assert.Contains(actualAliases, actual => SameIdentifier(actual, alias));
        }
        if (expected.TryGetProperty("ctesDetected", out var ctes))
        {
            var actualCtes = selects.SelectMany(select => select.With?.Ctes ?? Array.Empty<CteDefinition>())
                .Select(cte => cte.Name).ToArray();
            foreach (var cte in ctes.EnumerateArray().Select(item => item.GetString()!))
                Assert.Contains(actualCtes, actual => SameIdentifier(actual, cte));
        }
        if (expected.TryGetProperty("completionAvailable", out var completionAvailable))
        {
            var items = await CompletionOrchestrator.GetCompletions(
                sql, cursor, schema, dialect,
                options: new CompletionOrchestrationOptions { ForcedAutocomplete = true });
            var available = items.EngineItems.Count > 0 || items.WordListItems.Count > 0;
            Assert.Equal(completionAvailable.GetBoolean(), available);
        }
    }

    private static CompletionTriggerKind TriggerKindOf(JsonElement root)
        => root.TryGetProperty("trigger", out var trigger)
           && string.Equals(trigger.GetString(), "automatic", StringComparison.OrdinalIgnoreCase)
            ? CompletionTriggerKind.Automatic
            : CompletionTriggerKind.Explicit;

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
            if (item.TryGetProperty("foreignKeys", out var foreignKeys))
            {
                foreach (var foreignKey in foreignKeys.EnumerateArray())
                {
                    var references = foreignKey.GetProperty("references");
                    provider.AddForeignKey(database, schemaName, tableName, new ForeignKeyRelation(
                        foreignKey.GetProperty("columns").EnumerateArray().Select(column => column.GetString()!).ToArray(),
                        GetString(references, "table"),
                        references.GetProperty("columns").EnumerateArray().Select(column => column.GetString()!).ToArray(),
                        ReferencedSchema: references.TryGetProperty("schema", out _) ? GetString(references, "schema") : null,
                        ReferencedDatabase: references.TryGetProperty("database", out _) ? GetString(references, "database") : null,
                        Name: foreignKey.TryGetProperty("name", out _) ? GetString(foreignKey, "name") : null));
                }
            }
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

    private static bool InsertTextContains(JsonElement item, string? actual) =>
        !item.TryGetProperty("insertTextContains", out var parts)
        || parts.EnumerateArray().All(part =>
            (actual ?? string.Empty).Contains(part.GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase));

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
        if (!TryGetConformanceRoot(out var conformanceRoot) || conformanceRoot is null)
            yield break;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(conformanceRoot, "dialects"), "*.jsonl", SearchOption.AllDirectories).Order())
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

    private static string ConformanceRoot() =>
        TryGetConformanceRoot(out var root) && root is not null
            ? root
            : throw new DirectoryNotFoundException("Set JUSTYBASE_SQL_CONFORMANCE_PATH to the local JustyBase.SqlConformance repository.");

    private static bool TryGetConformanceRoot(out string? root)
    {
        var configured = Environment.GetEnvironmentVariable("JUSTYBASE_SQL_CONFORMANCE_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = Path.GetFullPath(configured);
            if (Directory.Exists(Path.Combine(full, "dialects")))
            {
                root = full;
                return true;
            }
            root = null;
            return false;
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
            {
                root = found;
                return true;
            }
        }
        root = null;
        return false;
    }

    private sealed record CompletionContractItem(string Label, string Kind, string? Detail, string? Documentation, string? InsertText);
}
