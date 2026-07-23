using JustyBase.NetezzaSqlParser.Caching;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ParserRuntimeConformanceTests
{
    [Fact]
    public void ParsingRuntime_CachesIdenticalDocuments()
    {
        using var runtime = new ParsingRuntime();
        const string sql = "SELECT id FROM orders;";

        var first = runtime.Parse(sql);
        var second = runtime.Parse(sql);
        var stats = runtime.GetStats();

        Assert.True(first.Valid);
        Assert.True(second.Valid);
        Assert.Equal(1, stats.hits);
        Assert.Equal(1, stats.misses);
    }

    [Fact]
    public void ParsingRuntime_InvalidatesOneDocumentWithoutClearingOthers()
    {
        using var runtime = new ParsingRuntime();
        const string firstSql = "SELECT id FROM first_table;";
        const string secondSql = "SELECT id FROM second_table;";

        runtime.Parse(firstSql);
        runtime.Parse(secondSql);
        runtime.Parse(firstSql);
        runtime.Invalidate(firstSql);
        runtime.Parse(firstSql);
        runtime.Parse(secondSql);

        var stats = runtime.GetStats();
        Assert.Equal(2, stats.hits);
        Assert.Equal(3, stats.misses);
    }

    [Fact]
    public void ParsingRuntime_ClearResetsCacheStatistics()
    {
        using var runtime = new ParsingRuntime();
        runtime.Parse("SELECT 1;");
        runtime.Parse("SELECT 1;");

        runtime.Clear();
        var afterClear = runtime.GetStats();

        Assert.Equal(0, afterClear.hits);
        Assert.Equal(0, afterClear.misses);
        runtime.Parse("SELECT 1;");
        Assert.Equal((0, 1), runtime.GetStats());
    }

    [Fact]
    public void DocumentParseSession_RejectsUseAfterDispose()
    {
        var session = new DocumentParseSession();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.GetOrParse("SELECT 1;"));
    }

    [Fact]
    public void StatementIndex_DetectsOnlyChangedStatement()
    {
        var previous = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2;");
        var next = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 3;");
        var diff = StatementIndexBuilder.DiffIndexes(previous, next);

        Assert.Single(diff.DirtyIndices);
        Assert.Equal(1, diff.DirtyIndices[0]);
    }
}
