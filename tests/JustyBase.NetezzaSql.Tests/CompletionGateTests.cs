using JustyBase.NetezzaSqlParser.Completion;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class CompletionGateTests
{
    [Theory]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('\n')]
    [InlineData('\r')]
    public void Whitespace_triggers_are_suppressed(char trigger)
        => Assert.True(CompletionGate.ShouldSuppressTrigger(trigger));

    [Theory]
    [InlineData('a')]
    [InlineData('.')]
    public void Non_whitespace_triggers_are_not_suppressed(char trigger)
        => Assert.False(CompletionGate.ShouldSuppressTrigger(trigger));

    [Fact]
    public void Null_trigger_is_explicit()
    {
        Assert.True(CompletionGate.IsExplicitTrigger(null));
        Assert.False(CompletionGate.IsExplicitTrigger('.'));
    }
}
