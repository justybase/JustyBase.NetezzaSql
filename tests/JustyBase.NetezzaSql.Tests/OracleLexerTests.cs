using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class OracleLexerTests
{
    private static Token<NzToken>[] T(string sql) => OracleLexer.Tokenize(sql).ToArray();

    [Fact]
    public void Tokenize_OracleOnlyKeywordsAndBindVariable()
    {
        var t = T("CONNECT BY PRIOR :x ORDER SIBLINGS BY");
        Assert.Collection(t,
            token => Assert.Equal(NzToken.OracleConnect, token.Kind),
            token => Assert.Equal(NzToken.OracleBy, token.Kind),
            token => Assert.Equal(NzToken.OraclePrior, token.Kind),
            token => Assert.Equal(NzToken.OracleBindVariable, token.Kind),
            token => Assert.Equal(NzToken.OracleOrderSiblingsBy, token.Kind));
    }

    [Fact]
    public void Tokenize_ParameterModeInIsSharedKeyword()
    {
        var t = T("p IN NUMBER");
        Assert.Collection(t,
            token => Assert.Equal(NzToken.Identifier, token.Kind),
            token => Assert.Equal(NzToken.In, token.Kind),
            token => Assert.Equal(NzToken.Identifier, token.Kind));
    }

    [Fact]
    public void Tokenize_QualifiedBindVariable()
    {
        var t = T(":NEW.ID");
        var token = Assert.Single(t);
        Assert.Equal(NzToken.OracleBindVariable, token.Kind);
        Assert.Equal(":NEW.ID", token.ToStringValue());
    }

    [Fact]
    public void Tokenize_BindVariable_AllowsDollarAndHash()
    {
        var t = T(":v$#_x");
        var token = Assert.Single(t);
        Assert.Equal(NzToken.OracleBindVariable, token.Kind);
        Assert.Equal(":v$#_x", token.ToStringValue());
    }

    [Fact]
    public void Tokenize_QualifiedFunctionCall()
    {
        var t = T("SELECT DBMS_METADATA.GET_DDL('TABLE', 'T') FROM DUAL");
        Assert.Equal(NzToken.Select, t[0].Kind);
        Assert.Equal(NzToken.OracleQualifiedFunction, t[1].Kind);
        Assert.Equal("DBMS_METADATA.GET_DDL", t[1].ToStringValue());
        Assert.Equal(NzToken.LParen, t[2].Kind);
        Assert.Equal(NzToken.StringLiteral, t[3].Kind);
        Assert.Equal(NzToken.Comma, t[4].Kind);
        Assert.Equal(NzToken.StringLiteral, t[5].Kind);
        Assert.Equal(NzToken.RParen, t[6].Kind);
        Assert.Equal(NzToken.From, t[7].Kind);
    }

    [Fact]
    public void Tokenize_QualifiedFunction_ThreePartPath()
    {
        var t = T("a.b.c(x)");
        Assert.Equal(NzToken.OracleQualifiedFunction, t[0].Kind);
        Assert.Equal("a.b.c", t[0].ToStringValue());
    }

    [Fact]
    public void Tokenize_QualifiedNameWithoutParen_IsIdentifierPath()
    {
        var t = T("HR.EMPLOYEES");
        Assert.Collection(t,
            token => Assert.Equal(NzToken.Identifier, token.Kind),
            token => Assert.Equal(NzToken.Dot, token.Kind),
            token => Assert.Equal(NzToken.Identifier, token.Kind));
    }

    [Fact]
    public void Tokenize_DatabaseLinkAtSign()
    {
        var t = T("SELECT * FROM HR.EMPLOYEES@PROD");
        Assert.Equal(NzToken.From, t[2].Kind);
        Assert.Equal(NzToken.Identifier, t[3].Kind);
        Assert.Equal(NzToken.Dot, t[4].Kind);
        Assert.Equal(NzToken.Identifier, t[5].Kind);
        Assert.Equal(NzToken.OracleAtSign, t[6].Kind);
        Assert.Equal(NzToken.Identifier, t[7].Kind);
        Assert.Equal("PROD", t[7].ToStringValue());
    }

    [Fact]
    public void Tokenize_AtSign_BeatsSharedAtSet()
    {
        var t = T("@SET");
        Assert.Collection(t,
            token => Assert.Equal(NzToken.OracleAtSign, token.Kind),
            token => Assert.Equal(NzToken.Set, token.Kind));
    }

    [Fact]
    public void Tokenize_RemainingOracleKeywords()
    {
        var t = T("RETURNING PRAGMA PIVOT UNPIVOT NOCYCLE");
        Assert.Collection(t,
            token => Assert.Equal(NzToken.OracleReturning, token.Kind),
            token => Assert.Equal(NzToken.OraclePragma, token.Kind),
            token => Assert.Equal(NzToken.OraclePivot, token.Kind),
            token => Assert.Equal(NzToken.OracleUnpivot, token.Kind),
            token => Assert.Equal(NzToken.OracleNocycle, token.Kind));
    }

    [Fact]
    public void Tokenize_OrderSiblingsBy_BeatsSharedOrderBy()
    {
        var t = T("ORDER SIBLINGS BY x");
        Assert.Equal(NzToken.OracleOrderSiblingsBy, t[0].Kind);

        var plain = T("SELECT * FROM t ORDER BY x");
        Assert.Equal(NzToken.OrderBy, plain[4].Kind);
    }

    [Fact]
    public void Tokenize_SharedChain_StillWorks()
    {
        var t = T("SELECT 1 FROM dual WHERE col IS NOT NULL");
        Assert.Equal(NzToken.Select, t[0].Kind);
        Assert.Equal(NzToken.NumberLiteral, t[1].Kind);
        Assert.Equal(NzToken.From, t[2].Kind);
        Assert.Equal(NzToken.Identifier, t[3].Kind);
        Assert.Equal(NzToken.Where, t[4].Kind);
        Assert.Equal(NzToken.Is, t[6].Kind);
        Assert.Equal(NzToken.Not, t[7].Kind);
        Assert.Equal(NzToken.Null, t[8].Kind);
    }

    [Fact]
    public void Tokenize_DoubleColonAndAssign_StillWork()
    {
        var t = T("SELECT a::INT; SET x := 1");
        Assert.Equal(NzToken.DoubleColon, t[2].Kind);
        Assert.Equal(NzToken.Assign, t[7].Kind);
    }

    [Fact]
    public void Tokenize_AlternativeQuotedString()
    {
        var t = T("SELECT q'[Oracle ''quoted'' text]' FROM DUAL");
        Assert.Equal(NzToken.Select, t[0].Kind);
        Assert.Equal(NzToken.Identifier, t[1].Kind);
        Assert.Equal("q", t[1].ToStringValue());
        Assert.Equal(NzToken.StringLiteral, t[2].Kind);
        Assert.Equal("'[Oracle ''quoted'' text]'", t[2].ToStringValue());
        Assert.Equal(NzToken.From, t[3].Kind);
    }

    [Fact]
    public void Tokenize_TimestampWithTimeZone()
    {
        var t = T("event_at TIMESTAMP WITH TIME ZONE");
        Assert.Collection(t,
            token => Assert.Equal(NzToken.Identifier, token.Kind),
            token => Assert.Equal(NzToken.Identifier, token.Kind),
            token => Assert.Equal(NzToken.With, token.Kind),
            token => Assert.Equal(NzToken.Identifier, token.Kind),
            token => Assert.Equal(NzToken.Identifier, token.Kind));
    }

    [Fact]
    public void Tokenize_Limit_StillTokenizesInOracleMode()
    {
        var t = T("SELECT * FROM dual LIMIT 1");
        Assert.Equal(NzToken.Limit, t[4].Kind);
    }

    [Fact]
    public void TryTokenize_RejectsLoneColon()
    {
        var success = OracleLexer.TryTokenize("SELECT :1", out var tokens);
        Assert.False(success);
        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_NetezzaLexer_UnawareOfOracleTokens()
    {
        var t = NzLexer.Tokenize("CONNECT BY PRIOR x").ToArray();
        Assert.Collection(t,
            token => Assert.Equal(NzToken.Identifier, token.Kind),
            token => Assert.Equal(NzToken.Identifier, token.Kind),
            token => Assert.Equal(NzToken.Identifier, token.Kind),
            token => Assert.Equal(NzToken.Identifier, token.Kind));
    }
}
