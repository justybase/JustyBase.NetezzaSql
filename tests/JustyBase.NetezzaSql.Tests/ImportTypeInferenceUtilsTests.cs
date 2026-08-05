using JustyBase.ImportExport.Import;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ImportTypeInferenceUtilsTests
{
    [Theory]
    [InlineData("PESEL", true)]
    [InlineData("pesel", true)]
    [InlineData("NRB", true)]
    [InlineData("IBAN", true)]
    [InlineData("BAN", true)]
    [InlineData("PESEL_2", true)]
    [InlineData("klient_PESEL", true)]
    [InlineData("pesel_number", true)]
    [InlineData("ID", false)]
    [InlineData("PE SEL", false)]
    [InlineData("", false)]
    public void HeaderForcesTextImportType_detects_tokens(string header, bool expected)
        => Assert.Equal(expected, ImportTypeInferenceUtils.HeaderForcesTextImportType(header));

    [Theory]
    [InlineData("001", true)]
    [InlineData("0.5", false)]
    [InlineData("00.5", true)]
    [InlineData("000", true)]
    [InlineData("123", false)]
    [InlineData("-5", false)]
    [InlineData("", false)]
    [InlineData(" 001 ", true)]
    public void ValueForcesTextImportType_detects_leading_zeros(string value, bool expected)
        => Assert.Equal(expected, ImportTypeInferenceUtils.ValueForcesTextImportType(value));
}
