using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzExpensiveRulesTests
{
    [Fact]
    public void NZ103_AggregateWithLiteralAndColumn_Detected()
    {
        var config = new LintConfig("SELECT COUNT(*), 1 AS num, status FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ103");
    }
}
