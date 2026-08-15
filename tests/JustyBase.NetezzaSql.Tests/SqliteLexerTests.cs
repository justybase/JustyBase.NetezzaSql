using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSql.Tests;

public sealed class SqliteLexerTests
{
    private static Token<NzToken>[] T(string sql) => SqliteLexer.Tokenize(sql).ToArray();

    [Fact]
    public void Tokenize_DedicatedKeywords()
    {
        var kinds = T("ATTACH DETACH AUTOINCREMENT COLLATE CONFLICT DO NOTHING PRAGMA RETURNING SAVEPOINT STRICT VACUUM VIRTUAL WINDOW WITHOUT")
            .Select(t => t.Kind)
            .ToArray();
        Assert.Contains(NzToken.SqliteAttach, kinds);
        Assert.Contains(NzToken.SqliteDetach, kinds);
        Assert.Contains(NzToken.SqliteAutoincrement, kinds);
        Assert.Contains(NzToken.SqliteCollate, kinds);
        Assert.Contains(NzToken.SqliteConflict, kinds);
        Assert.Contains(NzToken.SqliteDo, kinds);
        Assert.Contains(NzToken.SqliteNothing, kinds);
        Assert.Contains(NzToken.SqlitePragma, kinds);
        Assert.Contains(NzToken.SqliteReturning, kinds);
        Assert.Contains(NzToken.SqliteSavepoint, kinds);
        Assert.Contains(NzToken.SqliteStrict, kinds);
        Assert.Contains(NzToken.SqliteVacuum, kinds);
        Assert.Contains(NzToken.SqliteVirtual, kinds);
        Assert.Contains(NzToken.SqliteWindow, kinds);
        Assert.Contains(NzToken.SqliteWithout, kinds);
    }

    [Fact]
    public void Tokenize_BlobLiteral()
    {
        var tokens = T("SELECT X'AB12CD'");
        Assert.Contains(tokens, t => t.Kind == NzToken.SqliteBlobLiteral);
    }

    [Fact]
    public void Tokenize_JsonOperators()
    {
        var kinds = T("SELECT payload->'a'->>'b'").Select(t => t.Kind).ToArray();
        Assert.Contains(NzToken.JsonArrow, kinds);
        Assert.Contains(NzToken.JsonTextArrow, kinds);
    }

    [Fact]
    public void Tokenize_BracketedAndBacktickIdentifiers()
    {
        var kinds = T("SELECT [weird name], `tick` FROM [tbl]").Select(t => t.Kind).ToArray();
        Assert.Contains(NzToken.SqliteBracketedIdentifier, kinds);
        Assert.Contains(NzToken.MySqlBacktickIdentifier, kinds);
    }

    [Fact]
    public void Tokenize_KeywordsRemainUsableAsIdentifiers()
    {
        // SQLite permits most keywords as identifiers; the lexer must not
        // reject the stream when a keyword word appears in a name position.
        var kinds = T("SELECT attach, vacuum FROM pragma").Select(t => t.Kind).ToArray();
        Assert.Contains(NzToken.SqliteAttach, kinds);
        Assert.Contains(NzToken.SqliteVacuum, kinds);
        Assert.Contains(NzToken.SqlitePragma, kinds);
    }

    [Fact]
    public void TryTokenize_Empty_Succeeds()
    {
        Assert.True(SqliteLexer.TryTokenize("   ", out var tokens));
        Assert.Empty(tokens);
    }
}
