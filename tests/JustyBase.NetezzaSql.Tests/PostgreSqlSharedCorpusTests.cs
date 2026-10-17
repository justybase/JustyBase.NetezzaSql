using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.Tests.NetezzaSqlParser;

namespace JustyBase.NetezzaSql.Tests;

public sealed class PostgreSqlSharedCorpusTests
{
    public static IEnumerable<object[]> PositiveCases() => SharedSqlConformanceTests.SharedParserCases()
        .Where(data => (SqlDialect)data[1] == SqlDialect.PostgreSql && (string)data[7] == "True")
        .Select(data => new object[] { (string)data[0], ((string)data[5]).Replace("{{cf}}", "JB_CF_FIXTURE", StringComparison.Ordinal) });

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public void Formatting_preserves_accepted_PostgreSql_syntax(string id, string sql)
    {
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
        var sql = (string)PositiveCases().Single(row => (string)row[0] == "postgresql.parser.virtual.001")[1];
        using var engine = new LintEngine(SqlDialect.PostgreSql);
        var schema = new InMemorySchemaProvider();
        var config = new LintConfig(sql, schema, DocumentUri: "postgresql-version-test", ServerVersionNum: 170000);
        Assert.Contains(engine.RunExpensiveAnalysis(config).Issues, issue => issue.RuleId == "PG001");
        Assert.DoesNotContain(engine.RunExpensiveAnalysis(config with { ServerVersionNum = 180000 }).Issues, issue => issue.RuleId == "PG001");
        Assert.DoesNotContain(engine.RunExpensiveAnalysis(config with { ServerVersionNum = null }).Issues, issue => issue.RuleId == "PG001");
    }

    [Fact]
    public void Pinned_snapshot_content_matches_its_manifest()
    {
        Assert.True(SharedSqlConformanceTests.TryGetConformanceRoot(out var root));
        var manifestPath = Path.Combine(root!, "snapshot.json");
        if (!File.Exists(manifestPath))
        {
            Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JUSTYBASE_SQL_CONFORMANCE_PATH")));
            return;
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateObject())
        {
            var text = File.ReadAllText(Path.Combine(root!, file.Name)).Replace("\r\n", "\n", StringComparison.Ordinal);
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            Assert.Equal(file.Value.GetString(), digest);
            hashes.Add(file.Name, digest);
        }
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(hashes)));
        Assert.Equal(manifest.RootElement.GetProperty("contentHash").GetString(), contentHash);
    }
}
