using System.Runtime.CompilerServices;
using JustyBase.Core.Database;
using JustyBase.NetezzaSqlParser.Completion;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class CompletionOrchestratorTests
{
    [Fact]
    public async Task Returns_engine_items_without_word_list_provider()
    {
        var result = await CompletionOrchestrator.GetCompletions("SELE", 4, null);

        Assert.Contains(result.EngineItems, item => item.Label == "SELECT");
        Assert.Empty(result.WordListItems);
    }

    [Fact]
    public async Task Merge_policy_can_disable_word_list_path()
    {
        var provider = new RecordingProvider(new SqlWordListItem("DIM", SqlWordListKind.Table));

        var result = await CompletionOrchestrator.GetCompletions(
            "SELECT * FROM DIM", 17, null,
            wordListProvider: provider,
            mergePolicy: (_, _) => false);

        Assert.Empty(result.WordListItems);
        Assert.False(provider.WasCalled);
    }

    [Fact]
    public async Task Word_list_is_deduplicated_against_engine_items()
    {
        var provider = new RecordingProvider(
            new SqlWordListItem("SELECT", SqlWordListKind.Keyword),
            new SqlWordListItem("DIMDATE", SqlWordListKind.Table),
            new SqlWordListItem("DIMDATE", SqlWordListKind.Table));

        var result = await CompletionOrchestrator.GetCompletions(
            "SELE", 4, null, wordListProvider: provider);

        Assert.Single(result.WordListItems);
        Assert.Equal("DIMDATE", result.WordListItems[0].Label);
        Assert.Single(result.EngineItems, item => item.Label == "SELECT");
    }

    [Fact]
    public async Task Passes_connection_context_to_word_list_provider()
    {
        var provider = new RecordingProvider();

        await CompletionOrchestrator.GetCompletions(
            "SELECT * FROM DIM", 17, null,
            wordListProvider: provider,
            connectionName: "Netezza",
            databaseName: "JUST_DATA");

        Assert.Equal("Netezza", provider.Request?.ConnectionName);
        Assert.Equal("JUST_DATA", provider.Request?.DatabaseName);
    }

    private sealed class RecordingProvider : ISqlDbWordListProvider
    {
        private readonly IReadOnlyList<SqlWordListItem> _items;

        public RecordingProvider(params SqlWordListItem[] items) => _items = items;

        public SqlWordListRequest? Request { get; private set; }
        public bool WasCalled { get; private set; }

        public async IAsyncEnumerable<SqlWordListItem> GetWordsListAsync(
            SqlWordListRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            Request = request;
            foreach (var item in _items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }
    }
}
