using JustyBase.ImportExport.Import;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ImportHeaderNormalizerTests
{
    [Theory]
    [InlineData("name", "NAME")]
    [InlineData("  spaced  ", "SPACED")]
    [InlineData("my header", "MY_HEADER")]
    [InlineData("order-details", "ORDER_DETAILS")]
    [InlineData("col$1", "COL$1")]
    [InlineData("", "COL_EMPTY")]
    [InlineData("   ", "COL_EMPTY")]
    [InlineData("1st_column", "COL_1ST_COLUMN")]
    [InlineData("10%", "COL_10")]
    [InlineData("_leading", "LEADING")]
    [InlineData("multi___underscores", "MULTI_UNDERSCORES")]
    public void NormalizeImportedHeader_sanitizes_and_uppercases(string header, string expected)
        => Assert.Equal(expected, ImportHeaderNormalizer.NormalizeImportedHeader(header));

    [Fact]
    public void NormalizeImportedHeader_preserves_case_when_requested()
        => Assert.Equal("CamelCase", ImportHeaderNormalizer.NormalizeImportedHeader("CamelCase", ImportHeaderCase.Preserve));

    [Fact]
    public void NormalizeImportedHeader_lowercases_when_requested()
        => Assert.Equal("camel_case", ImportHeaderNormalizer.NormalizeImportedHeader("Camel Case", ImportHeaderCase.Lower));

    [Fact]
    public void NormalizeAndDeduplicateHeaders_appends_suffix_to_duplicates()
    {
        string[] result = ImportHeaderNormalizer.NormalizeAndDeduplicateHeaders(["id", "Name", "name", "id"]);

        Assert.Equal(["ID", "NAME", "NAME_1", "ID_1"], result);
    }

    [Fact]
    public void NormalizeAndDeduplicateHeaders_handles_empty_duplicates()
    {
        string[] result = ImportHeaderNormalizer.NormalizeAndDeduplicateHeaders(["", ""]);

        Assert.Equal(["COL_EMPTY", "COL_EMPTY_1"], result);
    }
}
