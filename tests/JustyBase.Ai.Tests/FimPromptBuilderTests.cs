using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Prompting;

namespace JustyBase.Ai.Tests;

public sealed class FimPromptBuilderTests
{
    [Fact]
    public void Qwen_Build_UsesOfficialFimTokens()
    {
        var builder = new QwenFimPromptBuilder();
        var prompt = builder.Build("SELECT ", " FROM t");
        Assert.Equal("<|fim_prefix|>SELECT <|fim_suffix|> FROM t<|fim_middle|>", prompt);
        Assert.Contains("<|endoftext|>", builder.StopSequences);
    }

    [Fact]
    public void DeepSeek_Build_UsesOfficialFimTokens()
    {
        var builder = new DeepSeekFimPromptBuilder();
        var prompt = builder.Build("SELECT ", " FROM t");
        var expected = "<" + "\uFF5Cfim\u2581begin\uFF5C" + ">SELECT <" + "\uFF5Cfim\u2581hole\uFF5C" + "> FROM t<" + "\uFF5Cfim\u2581end\uFF5C" + ">";
        Assert.Equal(expected, prompt);
    }

    [Fact]
    public void ContextExtractor_RespectsLimitsAndCaret()
    {
        var text = new string('a', 5000) + "|" + new string('b', 2000);
        var caret = 5000;
        var (prefix, suffix) = FimContextExtractor.Extract(text, caret, prefixLimit: 100, suffixLimit: 50);
        Assert.Equal(100, prefix.Length);
        Assert.Equal(50, suffix.Length);
        Assert.Equal(new string('a', 100), prefix);
        Assert.Equal("|" + new string('b', 49), suffix);
    }

    [Theory]
    [InlineData(512, 0.60, 0.40, 1229, 819)]
    [InlineData(1536, 0.65, 0.35, 3994, 2150)]
    [InlineData(4096, 0.70, 0.30, 11469, 4915)]
    public void FimPresets_ResolveCharBudgets(
        int maxPromptTokens,
        double prefixPct,
        double suffixPct,
        int expectedPrefix,
        int expectedSuffix)
    {
        var (p, s) = FimPresets.ResolveCharBudgets(maxPromptTokens, prefixPct, suffixPct);
        Assert.Equal(expectedPrefix, p);
        Assert.Equal(expectedSuffix, s);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(47, 50)]
    [InlineData(200, 200)]
    [InlineData(999, 200)]
    public void ContextExtractor_ClampMaxTokens(int input, int expected)
    {
        Assert.Equal(expected, FimContextExtractor.ClampMaxTokens(input));
    }

    [Fact]
    public void Catalog_ResolvesDefaultAndLicenseModels()
    {
        var catalog = new FimModelCatalog();
        Assert.Equal(FimModelIds.Qwen25Coder3B, catalog.Resolve(null).Id);
        Assert.Equal(FimModelIds.Qwen25Coder15B, catalog.Resolve(FimModelIds.Qwen25Coder15B).Id);
        Assert.Equal(FimModelIds.Qwen25Coder3B, catalog.Resolve(FimModelIds.Qwen25Coder3B).Id);
        Assert.Equal(FimModelIds.Qwen25Coder7B, catalog.Resolve(FimModelIds.Qwen25Coder7B).Id);
        Assert.Contains("1.5B", catalog.Resolve(FimModelIds.Qwen25Coder15B).FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3B", catalog.Resolve(FimModelIds.Qwen25Coder3B).FileName, StringComparison.OrdinalIgnoreCase);

        var codestral = catalog.Resolve(FimModelIds.Codestral22B);
        Assert.True(codestral.RequiresLicenseAcceptance);
        Assert.Contains("MNPL", codestral.LicenseName, StringComparison.OrdinalIgnoreCase);

        var gemma = catalog.Resolve(FimModelIds.CodeGemma2B);
        Assert.True(gemma.RequiresLicenseAcceptance);
    }
}
