using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.NetezzaSql.Tests;

public sealed class MySqlLexerTests
{
    [Fact]
    public void TokenizeMySql_BackticksAndHashComments()
    {
        var tokens = MySqlLexer.Tokenize("SELECT `department``name` FROM `departments` # ignored").ToArray();

        Assert.Contains(tokens, t => t.Kind == NzToken.MySqlBacktickIdentifier);
        Assert.Contains(tokens, t => t.Kind == NzToken.MySqlBacktickIdentifier
            && t.ToStringValue() == "`department``name`");
        Assert.DoesNotContain(tokens, t => t.ToStringValue().Contains("ignored", StringComparison.Ordinal));
    }

    [Fact]
    public void TokenizeMySql_UnclosedBacktickFails()
    {
        Assert.False(MySqlLexer.TryTokenize("SELECT `broken", out _));
    }
}
