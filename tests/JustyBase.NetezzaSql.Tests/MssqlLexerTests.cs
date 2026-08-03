using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSql.Tests;

public sealed class MssqlLexerTests
{
    private static Token<NzToken>[] T(string sql) => MssqlLexer.Tokenize(sql).ToArray();

    [Fact]
    public void Tokenize_Top_Output_Apply()
    {
        var kinds = T("SELECT TOP 10 id FROM t OUTPUT x CROSS APPLY f OUTER APPLY g")
            .Select(t => t.Kind)
            .ToArray();
        Assert.Contains(NzToken.MssqlTop, kinds);
        Assert.Contains(NzToken.MssqlOutput, kinds);
        Assert.Contains(NzToken.MssqlCrossApply, kinds);
        Assert.Contains(NzToken.MssqlOuterApply, kinds);
    }

    [Fact]
    public void Tokenize_CrossApply_Beats_Cross()
    {
        var tokens = T("SELECT * FROM a CROSS APPLY b");
        Assert.Contains(tokens, t => t.Kind == NzToken.MssqlCrossApply);
        Assert.DoesNotContain(tokens, t => t.Kind == NzToken.Cross);
    }

    [Fact]
    public void Tokenize_CrossJoin_StillUses_Cross()
    {
        var tokens = T("SELECT * FROM a CROSS JOIN b");
        Assert.DoesNotContain(tokens, t => t.Kind == NzToken.MssqlCrossApply);
        Assert.Contains(tokens, t => t.Kind == NzToken.Cross);
    }

    [Fact]
    public void Tokenize_Variable_Beats_AtSet()
    {
        var tokens = T("SET @count = 1");
        Assert.Contains(tokens, t => t.Kind == NzToken.MssqlVariable);
        Assert.DoesNotContain(tokens, t => t.Kind == NzToken.AtSet);
    }

    [Theory]
    [InlineData("@p")]
    [InlineData("@param1")]
    [InlineData("@@SPID")]
    [InlineData("@x$id")]
    public void Tokenize_Variable_Forms(string variable)
    {
        var tokens = T($"SELECT {variable}");
        Assert.Contains(tokens, t => t.Kind == NzToken.MssqlVariable);
    }

    [Fact]
    public void Tokenize_BracketedIdentifier_IsSingleToken()
    {
        var tokens = T("SELECT [Order Id] FROM [Sales].[Order Items]");
        var bracketed = tokens.Where(t => t.Kind == NzToken.MssqlBracketedIdentifier).ToList();
        Assert.Equal(3, bracketed.Count);
        Assert.DoesNotContain(tokens, t => t.Kind == NzToken.LBracket);
    }

    [Fact]
    public void Tokenize_EscapedBracket_IsSingleToken()
    {
        var tokens = T("SELECT [na]]me] FROM t");
        Assert.Single(tokens, t => t.Kind == NzToken.MssqlBracketedIdentifier);
    }

    [Fact]
    public void Tokenize_Go_Try_Catch_Proc()
    {
        var kinds = T("GO BEGIN TRY SELECT 1 END TRY BEGIN CATCH THROW END CATCH GO EXEC dbo.p")
            .Select(t => t.Kind)
            .ToArray();
        Assert.Contains(NzToken.MssqlGo, kinds);
        Assert.Contains(NzToken.MssqlTry, kinds);
        Assert.Contains(NzToken.MssqlCatch, kinds);
    }

    [Fact]
    public void Tokenize_NStringPrefix_IsIdentifierPlusString()
    {
        var tokens = T("SELECT N'abc'");
        Assert.Contains(tokens, t => t.Kind == NzToken.Identifier && t.ToStringValue() == "N");
        Assert.Contains(tokens, t => t.Kind == NzToken.StringLiteral);
    }

    [Fact]
    public void TryTokenize_Empty_Succeeds()
    {
        Assert.True(MssqlLexer.TryTokenize("   ", out var tokens));
        Assert.Empty(tokens);
    }
}
