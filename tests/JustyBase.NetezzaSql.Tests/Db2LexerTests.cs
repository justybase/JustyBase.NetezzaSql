using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSql.Tests;

public sealed class Db2LexerTests
{
    private static Token<NzToken>[] T(string sql) => Db2Lexer.Tokenize(sql).ToArray();

    [Fact]
    public void Tokenize_OptimizeFor_ForReadOnly_WithUr()
    {
        var kinds = T("SELECT 1 FROM T OPTIMIZE FOR 10 ROWS FOR READ ONLY WITH UR")
            .Select(t => t.Kind)
            .ToArray();
        Assert.Contains(NzToken.Db2OptimizeFor, kinds);
        Assert.Contains(NzToken.Db2ForReadOnly, kinds);
        Assert.Contains(NzToken.Db2WithUr, kinds);
    }

    [Fact]
    public void Tokenize_CurrentDate_And_CurrentSchema()
    {
        var kinds = T("SELECT CURRENT DATE FROM SYSIBM.SYSDUMMY1 WHERE CURRENT SCHEMA = X")
            .Select(t => t.Kind)
            .ToArray();
        Assert.Contains(NzToken.Db2CurrentDate, kinds);
        Assert.Contains(NzToken.Db2CurrentSchema, kinds);
    }

    [Theory]
    [InlineData("WITH UR", NzToken.Db2WithUr)]
    [InlineData("WITH CS", NzToken.Db2WithCs)]
    [InlineData("WITH RS", NzToken.Db2WithRs)]
    [InlineData("WITH RR", NzToken.Db2WithRr)]
    public void Tokenize_Isolation_Beats_With(string phrase, NzToken expected)
    {
        var tokens = T($"SELECT 1 FROM T {phrase}");
        Assert.Contains(tokens, t => t.Kind == expected);
        Assert.DoesNotContain(tokens, t => t.Kind == NzToken.With);
    }

    [Fact]
    public void Tokenize_ForReadOnly_Beats_For()
    {
        var tokens = T("SELECT 1 FROM T FOR READ ONLY");
        Assert.Contains(tokens, t => t.Kind == NzToken.Db2ForReadOnly);
    }

    [Fact]
    public void Tokenize_FinalOldNewTable()
    {
        var kinds = T("SELECT * FROM FINAL TABLE (INSERT INTO T VALUES (1))")
            .Select(t => t.Kind).ToArray();
        Assert.Contains(NzToken.Db2FinalTable, kinds);
    }

    [Fact]
    public void Tokenize_DeclareGlobalTemporary_Nickname_Identity()
    {
        var kinds = T("DECLARE GLOBAL TEMPORARY TABLE SESSION.T (ID INTEGER IDENTITY) CREATE NICKNAME N FOR S.T")
            .Select(t => t.Kind).ToArray();
        Assert.Contains(NzToken.Db2DeclareGlobalTemporary, kinds);
        Assert.Contains(NzToken.Db2Nickname, kinds);
        Assert.Contains(NzToken.Db2Identity, kinds);
    }

    [Fact]
    public void TryTokenize_Empty_Succeeds()
    {
        Assert.True(Db2Lexer.TryTokenize("   ", out var tokens));
        Assert.Empty(tokens);
    }
}
