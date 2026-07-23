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
}
