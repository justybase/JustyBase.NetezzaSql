using System.Runtime.CompilerServices;
using JustyBase.Core.Database;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// End-to-end headless seam tests: the shared <see cref="CompletionOrchestrator"/>
/// consumes <see cref="ISqlDbWordListProvider"/> through the engine-backed
/// request builder and <see cref="SqlWordListService"/>.
/// </summary>
public sealed class CompletionOrchestratorWordListTests
{
    [Fact]
    public async Task GetCompletions_without_provider_returns_engine_items_only()
    {
        var result = await CompletionOrchestrator.GetCompletions("SELE", 4, schema: null);
        Assert.Contains(result.EngineItems, i => i.Label == "SELECT");
        Assert.Empty(result.WordListItems);
    }

    [Theory]
    [InlineData(' ', true)]
    [InlineData('\t', true)]
    [InlineData('\n', true)]
    [InlineData('M', false)]
    [InlineData(null, false)]
    public void CompletionGate_suppresses_whitespace_triggers(char? trigger, bool expected)
    {
        Assert.Equal(expected, CompletionGate.ShouldSuppressTrigger(trigger));
    }

    [Fact]
    public async Task GetCompletions_explicitInvocationAfterWhitespace_returnsItems()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES", Columns: [new ColumnInfo("ID")]));

        var result = await CompletionOrchestrator.GetCompletions(
            "SELECT * FROM ",
            14,
            schema,
            options: new CompletionOrchestrationOptions { ForcedAutocomplete = true });

        Assert.Contains(result.EngineItems, item => item.Label == "EMPLOYEES");
    }

    [Fact]
    public async Task GetCompletions_after_word_char_still_returns_items()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", Columns: [new ColumnInfo("ID")]));
        var result = await CompletionOrchestrator.GetCompletions("SELECT * FROM DIM", 17, schema);
        Assert.NotEmpty(result.EngineItems);
    }

    [Fact]
    public async Task GetCompletions_merges_word_list_items_after_engine_items()
    {
        var provider = new FakeWordListProvider(
            new SqlWordListItem("DIMDATE", SqlWordListKind.Table, "Table", "dimension date"));

        var result = await CompletionOrchestrator.GetCompletions(
            "SELECT * FROM DIM", 17, schema: null, wordListProvider: provider);

        Assert.Contains(result.WordListItems,
            i => i.Label == "DIMDATE"
                 && i.Kind == SqlWordListKind.Table
                 && i.Detail == "Table");
    }

    [Fact]
    public async Task GetCompletions_merges_qualified_word_list_labels()
    {
        var provider = new FakeWordListProvider(
            new SqlWordListItem("JBL_LIVE.JBL_ORDERS", SqlWordListKind.Table, "table"));

        var result = await CompletionOrchestrator.GetCompletions(
            "SELECT * FROM JBL_LIVE.", 23, schema: null, wordListProvider: provider);

        Assert.Contains(result.WordListItems,
            i => i.Label == "JBL_LIVE.JBL_ORDERS");
    }

    [Fact]
    public async Task GetCompletions_dedupes_by_label()
    {
        var provider = new FakeWordListProvider(
            new SqlWordListItem("SELECT", SqlWordListKind.Keyword));

        var result = await CompletionOrchestrator.GetCompletions(
            "SELE", 4, schema: null, wordListProvider: provider);

        Assert.Contains(result.EngineItems, i => i.Label == "SELECT");
        Assert.DoesNotContain(result.WordListItems, i => i.Label == "SELECT");
    }

    [Fact]
    public async Task Orchestrator_is_deterministic_for_same_input()
    {
        const string sql = "SELECT * FROM ";
        var first = await CompletionOrchestrator.GetCompletions(sql, sql.Length, null);
        var second = await CompletionOrchestrator.GetCompletions(sql, sql.Length, null);

        Assert.Equal(
            first.EngineItems.Select(item => item.Label),
            second.EngineItems.Select(item => item.Label));
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
