using System.Runtime.CompilerServices;
using JustyBase.Core.Database;
using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlLsp.Services;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// End-to-end headless seam tests: the LSP completion service consumes the
/// shared <see cref="ISqlDbWordListProvider"/> contract through the engine-backed
/// request builder and <see cref="SqlWordListService"/>.
/// </summary>
public sealed class CompletionServiceWordListTests
{
    [Fact]
    public async Task GetCompletions_without_provider_returns_engine_items_only()
    {
        var list = await CompletionService.GetCompletions("SELE", 0, 4, schema: null);
        Assert.Contains(list.Items!, i => i.Label == "SELECT");
    }

    [Fact]
    public async Task GetCompletions_merges_word_list_items_after_engine_items()
    {
        var provider = new FakeWordListProvider(
            new SqlWordListItem("DIMDATE", SqlWordListKind.Table, "Table", "dimension date"));

        var list = await CompletionService.GetCompletions(
            "SELECT * FROM DIM", 0, 17, schema: null, wordListProvider: provider);

        Assert.Contains(list.Items!,
            i => i.Label == "DIMDATE"
                 && i.Kind == CompletionItemKind.Struct
                 && i.InsertText == "DIMDATE"
                 && i.Detail == "Table");
    }

    [Fact]
    public async Task GetCompletions_merges_qualified_word_list_labels()
    {
        var provider = new FakeWordListProvider(
            new SqlWordListItem("JBL_LIVE.JBL_ORDERS", SqlWordListKind.Table, "table"));

        var list = await CompletionService.GetCompletions(
            "SELECT * FROM JBL_LIVE.", 0, 23, schema: null, wordListProvider: provider);

        Assert.Contains(list.Items!,
            i => i.Label == "JBL_LIVE.JBL_ORDERS" && i.InsertText == "JBL_LIVE.JBL_ORDERS");
    }

    [Fact]
    public async Task GetCompletions_dedupes_by_label()
    {
        var provider = new FakeWordListProvider(
            new SqlWordListItem("SELECT", SqlWordListKind.Keyword));

        var list = await CompletionService.GetCompletions(
            "SELE", 0, 4, schema: null, wordListProvider: provider);

        var selectItems = list.Items!.Where(i => i.Label == "SELECT").ToList();
        Assert.Single(selectItems); // engine keyword wins; the provider duplicate is dropped
    }

    private sealed class FakeWordListProvider : ISqlDbWordListProvider
    {
        private readonly IReadOnlyList<SqlWordListItem> _items;

        public FakeWordListProvider(params SqlWordListItem[] items) => _items = items;

        public async IAsyncEnumerable<SqlWordListItem> GetWordsListAsync(
            SqlWordListRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var item in _items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
    }
}
