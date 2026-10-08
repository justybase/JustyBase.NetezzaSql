using JustyBase.NetezzaSqlParser.Authoring;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSignatureHelpServiceTests
{
    [Fact]
    public void SignatureHelp_TracksActiveParameter()
    {
        const string sql = "SELECT NVL(col1, col2) FROM t";
        var offset = sql.IndexOf("col2", StringComparison.Ordinal) + 2;

        var result = NzSignatureHelpService.GetSignatureHelp(sql, offset);

        Assert.NotNull(result);
        Assert.Equal(1, result!.ActiveParameter);
        Assert.Equal("NVL(value, replacement)", result.Signatures[0].Label);
    }

    [Fact]
    public void SignatureHelp_ReturnsNullOutsideFunctionCall()
    {
        const string sql = "SELECT col1 FROM t";
        var offset = sql.IndexOf("col1", StringComparison.Ordinal);

        var result = NzSignatureHelpService.GetSignatureHelp(sql, offset);

        Assert.Null(result);
    }
    [Theory]
    [InlineData("SELECT ROUND(AVG(", "AVG(expression)", 0)]
    [InlineData("SELECT ROUND(AVG(x), ", "ROUND(value [, scale])", 1)]
    [InlineData("SELECT SUBSTRING('a,b', ", "SUBSTRING(string, start, length)", 1)]
    [InlineData("SELECT NVL((1 + 2), ", "NVL(value, replacement)", 1)]
    public void SignatureHelp_TracksInnermostOpenCall(string sql, string label, int parameter)
    {
        var result = NzSignatureHelpService.GetSignatureHelp(sql, sql.Length);
        Assert.NotNull(result);
        Assert.Equal(label, result.Signatures[result.ActiveSignature].Label);
        Assert.Equal(parameter, result.ActiveParameter);
    }

    [Fact]
    public void SignatureHelp_ReturnsNullAfterClosedCall()
        => Assert.Null(NzSignatureHelpService.GetSignatureHelp("SELECT AVG(x) ", 14));
}
