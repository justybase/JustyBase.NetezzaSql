using JustyBase.ImportExport.Import;
using JustyBase.ImportExport.Import.TypeChooser;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>
/// Golden type-inference fixture matrix. This is the single source of truth for import type
/// inference across all hosts: shared tests run it directly, and the Avalonia/Legacy parity tests
/// (2.2-C/2.2-D) compare host output against the same goldens. Every case is asserted both through
/// the streaming <see cref="NetezzaColumnTypeChooser"/> and the sample-based
/// <see cref="DatabaseTypeChooser.Infer"/> batch entry point.
/// </summary>
public sealed record ImportTypeFixture(
    string Name,
    IReadOnlyList<string> Values,
    string ExpectedType,
    string DecimalDelimiter = ".",
    bool InferBoolean = false);

public sealed class ImportTypeFixtureMatrixTests
{
    public static TheoryData<ImportTypeFixture> Cases()
    {
        var data = new TheoryData<ImportTypeFixture>
        {
            new("int_small", ["1", "2", "3"], "BIGINT"),
            new("int_zero", ["0"], "BIGINT"),
            new("int_with_negative_falls_back_to_text", ["0", "-5", "42"], "NVARCHAR(20)"),
            new("int_up_to_14_digits", ["99999999999999"], "BIGINT"),
            new("int_15_digits_upgrades_to_numeric", ["999999999999999"], "NUMERIC(16,0)"),
            new("int_very_long_falls_back_to_text", ["123456789012345678901234567890"], "NVARCHAR(35)"),
            new("dec_dot", ["1.5", "2.25"], "NUMERIC(16,2)"),
            new("dec_comma", ["12,5", "1,25"], "NUMERIC(16,2)", DecimalDelimiter: ","),
            new("dec_single_comma", ["1,5"], "NUMERIC(16,1)", DecimalDelimiter: ","),
            new("dec_leading_zero_integer_part", ["0.5", "1.0"], "NUMERIC(16,1)"),
            new("leading_zero_text", ["001", "002"], "NVARCHAR(20)"),
            new("zero_leading_decimal_forced_text", ["00.5"], "NVARCHAR(20)"),
            new("date_iso", ["2024-01-15", "2024-02-01"], "DATE"),
            new("date_iso_short_components", ["2024-6-7"], "DATE"),
            new("datetime_iso_hms", ["2024-06-07 14:30:45"], "DATETIME"),
            new("datetime_iso_hm", ["2024-06-07 14:30"], "DATETIME"),
            new("date_dotted", ["07.06.2024"], "DATETIME"),
            new("datetime_dotted", ["07.06.2024 14:30"], "DATETIME"),
            new("bool_when_enabled", ["true", "false"], "BOOLEAN", InferBoolean: true),
            new("bool_text_by_default", ["true", "false"], "NVARCHAR(20)"),
            new("text_short", ["hello", "world"], "NVARCHAR(20)"),
            new("text_long", [new string('a', 100)], "NVARCHAR(105)"),
            new("text_single_char", ["x"], "NVARCHAR(20)"),
            new("mix_int_decimal_upgrades_numeric", ["1", "2.5"], "NUMERIC(16,1)"),
            new("mix_numeric_date_falls_back_to_text", ["12.5", "2024-01-01"], "NVARCHAR(20)"),
            new("mix_all_falls_back_to_text", ["abc", "1", "2.5", "2024-01-01"], "NVARCHAR(20)"),
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Golden_matrix_matches_streaming_chooser_and_batch_infer(ImportTypeFixture fixture)
    {
        var chooser = new NetezzaColumnTypeChooser(fixture.DecimalDelimiter, new ColumnTypeChooserOptions(InferBoolean: fixture.InferBoolean));
        foreach (string value in fixture.Values)
            chooser.RefreshCurrentType(value);

        Assert.Equal(fixture.ExpectedType, chooser.CurrentType.ToString());

        var rows = fixture.Values.Select(v => (IReadOnlyList<string?>)[v]).ToArray();
        var detected = DatabaseTypeChooser.Infer(
            ["c"],
            rows,
            decimalDelimiter: fixture.DecimalDelimiter,
            inferBoolean: fixture.InferBoolean);

        Assert.Equal(fixture.ExpectedType, detected[0].NetezzaType);
    }

    [Fact]
    public void Golden_matrix_header_tokens_force_nvarchar()
    {
        foreach (string header in new[] { "PESEL", "NRB", "IBAN", "BAN", "klient_PESEL", "BAN_1" })
            Assert.True(ImportTypeInferenceUtils.HeaderForcesTextImportType(header), header);
    }
}