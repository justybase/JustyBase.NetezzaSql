using JustyBase.Netezza.Completion;
using JustyBase.NetezzaSqlParser.Completion;

namespace JustyBase.Netezza.Tests;

public sealed class SqlCompletionMergePolicyTests
{
    [Fact]
    public void SchemaObjectSkipsFallback()
    {
        IReadOnlyList<CompletionItem> items =
            [new CompletionItem("EMPLOYEES", CompletionKind.Table, "table")];

        Assert.False(SqlCompletionMergePolicy.ShouldRunLegacyPath(items, "SELECT * FROM "));
    }

    [Fact]
    public void KeywordOnlyUsesFallback()
    {
        IReadOnlyList<CompletionItem> items =
            [new CompletionItem("SELECT", CompletionKind.Keyword, "keyword")];

        Assert.True(SqlCompletionMergePolicy.ShouldRunLegacyPath(items, "SELECT * FROM "));
    }

    [Fact]
    public void EmptyResultUsesFallback()
        => Assert.True(SqlCompletionMergePolicy.ShouldRunLegacyPath([], "SELECT * FROM "));

    [Fact]
    public void OversizedDocumentSkipsFallback()
    {
        var sql = new string('a', SqlCompletionMergePolicy.MaxSqlLengthForLegacyPath + 1);

        Assert.False(SqlCompletionMergePolicy.ShouldRunLegacyPath([], sql));
    }

    [Fact]
    public void DocumentAtLimitSkipsFallback()
    {
        var sql = new string('a', SqlCompletionMergePolicy.MaxSqlLengthForLegacyPath);

        Assert.False(SqlCompletionMergePolicy.ShouldRunLegacyPath([], sql));
    }

    [Theory]
    [InlineData(CompletionKind.Column)]
    [InlineData(CompletionKind.Cte)]
    [InlineData(CompletionKind.Database)]
    public void SchemaCompletionSkipsFallback(CompletionKind kind)
    {
        IReadOnlyList<CompletionItem> items = [new CompletionItem("OBJECT", kind, "metadata")];

        Assert.False(SqlCompletionMergePolicy.ShouldRunLegacyPath(items, "SELECT "));
    }
}
