using System.Runtime.CompilerServices;
using JustyBase.Core.Database;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class SqlWordListServiceTests
{
    [Theory]
    [InlineData("SELECT * FROM EMP", 17, "EMP")]
    [InlineData("SELECT * FROM ADMIN.", 20, "ADMIN.")]
    [InlineData("SELECT * FROM JUST_DATA..dim", 28, "JUST_DATA..dim")]
    [InlineData("SELECT * FROM A.X", 17, "A.X")]
    public void FromText_extracts_dotted_word_fragment(string sql, int cursor, string expected)
    {
        var request = SqlWordListRequest.FromText(sql, cursor, "conn", "JUST_DATA");
        Assert.Equal(expected, request.Fragment);
        Assert.Equal("conn", request.ConnectionName);
        Assert.Equal("JUST_DATA", request.DatabaseName);
        Assert.Empty(request.AliasDbTable);
        Assert.Empty(request.WithHints);
    }

    [Theory]
    [InlineData("", 0, "")]
    [InlineData("   ", 3, "")]
    [InlineData("SELECT", 100, "SELECT")]
    [InlineData("SELECT", -1, "")]
    public void FromText_handles_edge_inputs(string sql, int cursor, string expected)
    {
        Assert.Equal(expected, SqlWordListRequest.FromText(sql, cursor).Fragment);
    }

    [Fact]
    public void Constructor_rejects_null_provider()
    {
        Assert.Throws<ArgumentNullException>(() => new SqlWordListService(null!));
    }

    [Fact]
    public async Task GetWordsListAsync_streams_provider_items_with_default_extractor()
    {
        var provider = new FakeWordListProvider(
            new SqlWordListItem("EMPLOYEES", SqlWordListKind.Table, "Table"),
            new SqlWordListItem("EMP_ID", SqlWordListKind.Column, "INTEGER"));
        var service = new SqlWordListService(provider);

        var results = new List<SqlWordListItem>();
        await foreach (var item in service.GetWordsListAsync(
                           "SELECT * FROM EMP", 17, "conn", "JUST_DATA"))
        {
            results.Add(item);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("EMPLOYEES", results[0].Label);
        Assert.Equal("EMP", provider.LastRequest!.Fragment);
        Assert.Equal("conn", provider.LastRequest.ConnectionName);
        Assert.Empty(provider.LastRequest.AliasDbTable);
    }

    [Fact]
    public async Task GetWordsListAsync_uses_injected_extractor_with_hints()
    {
        var provider = new FakeWordListProvider();
        var service = new SqlWordListService(provider, (sql, cursor, conn, db) =>
            SqlWordListRequest.FromText(sql, cursor, conn, db) with
            {
                AliasDbTable = new Dictionary<string, IReadOnlyList<string>>
                {
                    ["ADMIN.EMP"] = new List<string> { "E" }
                }
            });

        var results = new List<SqlWordListItem>();
        await foreach (var item in service.GetWordsListAsync("SELECT * FROM ADMIN.EMP E WHERE E.", 34))
        {
            results.Add(item);
        }

        Assert.Empty(results);
        Assert.Equal("E.", provider.LastRequest!.Fragment);
        Assert.True(provider.LastRequest.AliasDbTable.TryGetValue("ADMIN.EMP", out var aliases));
        Assert.Contains(aliases, a => a.Equals("E", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetWordsListAsync_observes_cancellation()
    {
        var provider = new FakeWordListProvider(
            new SqlWordListItem("A", SqlWordListKind.Table),
            new SqlWordListItem("B", SqlWordListKind.Table));
        var service = new SqlWordListService(provider);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in service.GetWordsListAsync("SELECT A", 8, cancellationToken: cts.Token))
            {
            }
        });
    }

    private sealed class FakeWordListProvider : ISqlDbWordListProvider
    {
        private readonly IReadOnlyList<SqlWordListItem> _items;

        public SqlWordListRequest? LastRequest { get; private set; }

        public FakeWordListProvider(params SqlWordListItem[] items) => _items = items;

        public async IAsyncEnumerable<SqlWordListItem> GetWordsListAsync(
            SqlWordListRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            foreach (var item in _items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
    }
}
