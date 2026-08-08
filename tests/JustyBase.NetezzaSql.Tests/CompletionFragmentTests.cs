using JustyBase.NetezzaSqlParser.Completion;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class CompletionFragmentTests
{
    [Theory]
    [InlineData("SELECT A.COL", 12, "A.COL")]
    [InlineData("SELECT A.", 9, "A.")]
    [InlineData("SELECT A.COL ", 13, "")]
    [InlineData("SELECT (COL", 11, "COL")]
    [InlineData("SELECT COL;", 10, "COL")]
    public void Extracts_fragment_with_shared_boundaries(string text, int offset, string expected)
        => Assert.Equal(expected, CompletionFragment.GetLastWordFromText(text, offset));

    [Fact]
    public void Returns_null_for_oversized_fragment()
    {
        string text = new string('A', 129);

        Assert.Null(CompletionFragment.GetLastWordFromText(text, text.Length));
    }
}
