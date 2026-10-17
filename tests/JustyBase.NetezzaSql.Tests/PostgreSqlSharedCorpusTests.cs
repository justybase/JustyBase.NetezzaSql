using System.Text.Json;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.Tests.NetezzaSqlParser;
using Xunit;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>
/// Local-only PostgreSQL corpus checks. All cases come from the sibling
/// JustyBase.SqlConformance checkout via JUSTYBASE_SQL_CONFORMANCE_PATH;
/// this class is excluded from standard CI with the other SqlConformance tests.
/// </summary>
[Trait("Category", "SqlConformance")]
public sealed class PostgreSqlSharedCorpusTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        var rows = SharedSqlConformanceTests.SharedParserCases()
            .Where(data => (SqlDialect)data[1] == SqlDialect.PostgreSql && (string)data[7] == "True")
            .Select(data => new object[] { (string)data[0], ((string)data[5]).Replace("{{cf}}", "JB_CF_FIXTURE", StringComparison.Ordinal) })
            .ToArray();
        // Without a corpus the shared cases are empty; surface the readable
        // conformance error instead of an empty-theory discovery failure.
        if (rows.Length == 0)
            yield return new object[] { SharedSqlConformanceTests.MissingRepoSentinelId, string.Empty };
        else
            foreach (var row in rows)
                yield return row;
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public void Formatting_preserves_accepted_PostgreSql_syntax(string id, string sql)
    {
        if (id == SharedSqlConformanceTests.MissingRepoSentinelId)
            throw SharedSqlConformanceTests.RequireConformanceRootException();
        using var parser = new ParsingRuntime(SqlDialect.PostgreSql);
        var before = parser.Parse(sql);
        Assert.True(before.Valid, id);
        var formatted = string.Join(";\n", before.Statements.Select(NzSqlFormatter.Format));
        var after = parser.Parse(formatted);
        Assert.True(after.Valid, $"{id}: {formatted}\n{string.Join(" | ", after.Errors.Select(error => error.Message))}");
        Assert.Equal(before.Statements.Count, after.Statements.Count);
        if (id == "postgresql.parser.materialized-cte.001") Assert.Contains("NOT MATERIALIZED", formatted, StringComparison.Ordinal);
        if (id == "postgresql.parser.drop-index.001") Assert.Contains("CASCADE", formatted, StringComparison.Ordinal);
        if (Environment.GetEnvironmentVariable("JUSTYBASE_POSTGRES_FORMATTED_OUTPUT") is { Length: > 0 } output)
            File.AppendAllText(output, JsonSerializer.Serialize(new { id, sql = formatted }) + "\n");
    }

    [Fact]
    public void Version_warnings_follow_context_changes_for_unchanged_SQL()
    {
        if (!SharedSqlConformanceTests.TryGetConformanceRoot(out _))
            throw SharedSqlConformanceTests.RequireConformanceRootException();
        var sql = (string)PositiveCases().Single(row => (string)row[0] == "postgresql.parser.virtual.001")[1];
        using var engine = new LintEngine(SqlDialect.PostgreSql);
        var schema = new InMemorySchemaProvider();
        var config = new LintConfig(sql, schema, DocumentUri: "postgresql-version-test", ServerVersionNum: 170000);
        Assert.Contains(engine.RunExpensiveAnalysis(config).Issues, issue => issue.RuleId == "PG001");
        Assert.DoesNotContain(engine.RunExpensiveAnalysis(config with { ServerVersionNum = 180000 }).Issues, issue => issue.RuleId == "PG001");
        Assert.DoesNotContain(engine.RunExpensiveAnalysis(config with { ServerVersionNum = null }).Issues, issue => issue.RuleId == "PG001");
    }
}
