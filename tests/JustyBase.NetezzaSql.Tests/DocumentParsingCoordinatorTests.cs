using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class DocumentParsingCoordinatorTests
{
    [Fact]
    public void Hover_WarmsSharedParseCache()
    {
        var coordinator = new DocumentParsingCoordinator();
        var schema = SqlTestHelpers.CreateStandardMockSchema();
        const string sql = "SELECT COUNT( FROM TESTDB..EMPLOYEES";
        const string uri = "doc-hover-cache";

        var runtime = coordinator.GetOrCreate(uri);
        runtime.Parse(sql);
        var (_, missesAfterFirst) = runtime.GetStats();

        NzHoverService.GetHover(sql, 12, schema, coordinator, uri);
        NzSignatureHelpService.GetSignatureHelp(sql, 12, coordinator, uri);

        var (hitsAfterHover, _) = runtime.GetStats();
        Assert.True(missesAfterFirst > 0);
        Assert.True(hitsAfterHover > 0);
        coordinator.Dispose();
    }
}
