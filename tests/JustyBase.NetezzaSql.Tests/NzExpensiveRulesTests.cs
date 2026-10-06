using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzExpensiveRulesTests : IDisposable
{
    private readonly LintEngine _engine = new();

    private readonly ISchemaProvider _schema = SqlTestHelpers.CreateStandardMockSchema();
}
