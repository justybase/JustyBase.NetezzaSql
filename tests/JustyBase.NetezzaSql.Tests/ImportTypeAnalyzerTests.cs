using JustyBase.ImportExport.Import;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ImportTypeAnalyzerTests
{
    private static ImportTypeAnalyzer Analyze(int columnCount, Action<ImportTypeAnalyzer> feed, bool inferBoolean = false)
    {
        var analyzer = new ImportTypeAnalyzer(columnCount, inferBoolean: inferBoolean);
        feed(analyzer);
        return analyzer;
    }

    [Fact]
    public void Pure_integers_choose_integer()
    {
        var analyzer = Analyze(1, a => { foreach (var v in new[] { "1", "2", "3" }) a.AddValue(0, v); });
        DetectedImportColumnType t = analyzer.Choose()[0];

        Assert.Equal(ImportColumnKind.Integer, t.Kind);
        Assert.False(t.IsNullable);
    }

    [Fact]
    public void Zero_value_chooses_integer()
    {
        var analyzer = Analyze(1, a => a.AddValue(0, "0"));
        Assert.Equal(ImportColumnKind.Integer, analyzer.Choose()[0].Kind);
    }

    [Fact]
    public void Integer_and_decimal_mix_upgrades_to_numeric()
    {
        var analyzer = Analyze(1, a => { foreach (var v in new[] { "1", "2.5" }) a.AddValue(0, v); });
        DetectedImportColumnType t = analyzer.Choose()[0];

        Assert.Equal(ImportColumnKind.Numeric, t.Kind);
        Assert.Equal(16, t.LengthOrPrecision);
        Assert.Equal(1, t.Scale);
    }

    [Fact]
    public void Pure_decimals_choose_numeric_with_digit_scale()
    {
        var analyzer = Analyze(1, a => { foreach (var v in new[] { "2.5", "3.5" }) a.AddValue(0, v); });
        DetectedImportColumnType t = analyzer.Choose()[0];

        Assert.Equal(ImportColumnKind.Numeric, t.Kind);
        Assert.Equal(16, t.LengthOrPrecision);
        Assert.Equal(1, t.Scale);
    }

    [Fact]
    public void Numeric_precision_floor_is_sixteen()
    {
        var analyzer = Analyze(1, a => { foreach (var v in new[] { "12.5", "123.45", "12345.678" }) a.AddValue(0, v); });
        DetectedImportColumnType t = analyzer.Choose()[0];

        Assert.Equal(ImportColumnKind.Numeric, t.Kind);
        Assert.Equal(16, t.LengthOrPrecision);
        Assert.Equal(3, t.Scale);
    }

    [Fact]
    public void Comma_decimal_delimiter_detects_numeric()
    {
        var analyzer = new ImportTypeAnalyzer(1, decimalDelimiter: ",");
        analyzer.AddValue(0, "123,45");
        DetectedImportColumnType t = analyzer.Choose()[0];

        Assert.Equal(ImportColumnKind.Numeric, t.Kind);
        Assert.Equal(2, t.Scale);
    }

    [Fact]
    public void Text_and_numeric_mix_becomes_nvarchar()
    {
        var analyzer = Analyze(1, a => { a.AddValue(0, "abc"); a.AddValue(0, "1.5"); });
        DetectedImportColumnType t = analyzer.Choose()[0];

        Assert.Equal(ImportColumnKind.Nvarchar, t.Kind);
        Assert.Equal(20, t.LengthOrPrecision);
    }

    [Fact]
    public void Negative_integer_falls_back_to_text_vscode_semantics()
    {
        var analyzer = Analyze(1, a => a.AddValue(0, "-12345"));
        Assert.Equal(ImportColumnKind.Nvarchar, analyzer.Choose()[0].Kind);
    }

    [Fact]
    public void Very_long_integer_falls_back_to_text()
    {
        var analyzer = Analyze(1, a => a.AddValue(0, "123456789012345678901234567890"));
        Assert.Equal(ImportColumnKind.Nvarchar, analyzer.Choose()[0].Kind);
    }

    [Fact]
    public void Leading_zero_value_stays_text()
    {
        var analyzer = Analyze(1, a => a.AddValue(0, "001"));
        Assert.Equal(ImportColumnKind.Nvarchar, analyzer.Choose()[0].Kind);
    }

    [Fact]
    public void Boolean_column_chooses_boolean_when_enabled()
    {
        var analyzer = Analyze(1, a => { foreach (var v in new[] { "true", "false" }) a.AddValue(0, v); }, inferBoolean: true);
        Assert.Equal(ImportColumnKind.Boolean, analyzer.Choose()[0].Kind);
    }

    [Fact]
    public void Boolean_values_are_text_by_default()
    {
        var analyzer = Analyze(1, a => { foreach (var v in new[] { "true", "false" }) a.AddValue(0, v); });
        Assert.Equal(ImportColumnKind.Nvarchar, analyzer.Choose()[0].Kind);
    }

    [Fact]
    public void Date_only_values_choose_date()
    {
        var analyzer = Analyze(1, a => { foreach (var v in new[] { "2024-01-15", "2024-02-01" }) a.AddValue(0, v); });
        Assert.Equal(ImportColumnKind.Date, analyzer.Choose()[0].Kind);
    }

    [Fact]
    public void Iso_datetime_with_time_chooses_timestamp()
    {
        var analyzer = Analyze(1, a =>
        {
            a.AddValue(0, "2024-06-07 14:30");
            a.AddValue(0, "2024-06-07 14:30:45");
        });
        Assert.Equal(ImportColumnKind.TimeStamp, analyzer.Choose()[0].Kind);
    }

    [Fact]
    public void Dotted_date_chooses_timestamp()
    {
        var analyzer = Analyze(1, a => a.AddValue(0, "07.06.2024"));
        Assert.Equal(ImportColumnKind.TimeStamp, analyzer.Choose()[0].Kind);
    }

    [Fact]
    public void Empty_values_mark_column_nullable()
    {
        var analyzer = Analyze(1, a => { foreach (var v in new[] { "1", "", null }) a.AddValue(0, v); });
        DetectedImportColumnType t = analyzer.Choose()[0];

        Assert.Equal(ImportColumnKind.Integer, t.Kind);
        Assert.True(t.IsNullable);
    }

    [Fact]
    public void All_empty_column_is_nullable_nvarchar()
    {
        var analyzer = Analyze(1, a => { foreach (var v in new[] { "", null }) a.AddValue(0, v); });
        DetectedImportColumnType t = analyzer.Choose()[0];

        Assert.Equal(ImportColumnKind.Nvarchar, t.Kind);
        Assert.Equal(255, t.LengthOrPrecision);
        Assert.True(t.IsNullable);
    }

    [Fact]
    public void Header_override_forces_integer()
    {
        var analyzer = Analyze(1, a => a.AddValue(0, "abc"));
        DetectedImportColumnType t = analyzer.Choose(["code_#INTEGER"])[0];

        Assert.Equal(ImportColumnKind.Integer, t.Kind);
    }

    [Theory]
    [InlineData("_#TEXT", ImportColumnKind.Nvarchar)]
    [InlineData("_#NUMERIC", ImportColumnKind.Numeric)]
    [InlineData("_#DATE", ImportColumnKind.Date)]
    [InlineData("_#TIMESTAMP", ImportColumnKind.TimeStamp)]
    [InlineData("_#INTEGER", ImportColumnKind.Integer)]
    public void Header_overrides_force_column_types(string suffix, ImportColumnKind expected)
    {
        var analyzer = Analyze(1, a => a.AddValue(0, "1.5"));
        DetectedImportColumnType t = analyzer.Choose(["col" + suffix])[0];

        Assert.Equal(expected, t.Kind);
    }

    [Fact]
    public void Pesel_column_stays_text()
    {
        var withName = Analyze(1, a => a.AddValue(0, "12345678901", columnName: "Pesel"));
        var withoutName = Analyze(1, a => a.AddValue(0, "12345678901"));

        Assert.Equal(ImportColumnKind.Nvarchar, withName.Choose(["Pesel"])[0].Kind);
        Assert.Equal(ImportColumnKind.Integer, withoutName.Choose(["id"])[0].Kind);
    }

    [Fact]
    public void Header_token_forces_text_at_choose()
    {
        var analyzer = Analyze(1, a => a.AddValue(0, "12345678901"));
        DetectedImportColumnType t = analyzer.Choose(["PESEL"])[0];

        Assert.Equal(ImportColumnKind.Nvarchar, t.Kind);
    }

    [Fact]
    public void Treat_all_columns_as_text_forces_nvarchar()
    {
        var analyzer = Analyze(1, a => a.AddValue(0, "1", treatAllColumnsAsText: true));
        Assert.Equal(ImportColumnKind.Nvarchar, analyzer.Choose()[0].Kind);
    }

    [Fact]
    public void Long_text_column_sizes_nvarchar_from_data()
    {
        string longText = new string('a', 60);
        var analyzer = Analyze(1, a =>
        {
            a.AddValue(0, longText);
            a.AddValue(0, "1.5");
        });
        DetectedImportColumnType t = analyzer.Choose()[0];

        Assert.Equal(ImportColumnKind.Nvarchar, t.Kind);
        Assert.Equal(65, t.LengthOrPrecision);
    }

    [Fact]
    public void AddCell_supports_preclassified_excel_values()
    {
        var analyzer = new ImportTypeAnalyzer(1);
        analyzer.AddCell(0, ImportColumnKind.TimeStamp);
        analyzer.AddCell(0, ImportColumnKind.TimeStamp);

        Assert.Equal(ImportColumnKind.TimeStamp, analyzer.Choose()[0].Kind);
    }

    [Fact]
    public void AddCell_integer_and_numeric_upgrade()
    {
        var analyzer = new ImportTypeAnalyzer(1);
        analyzer.AddCell(0, ImportColumnKind.Integer);
        analyzer.AddCell(0, ImportColumnKind.Numeric);

        DetectedImportColumnType t = analyzer.Choose()[0];
        Assert.Equal(ImportColumnKind.Numeric, t.Kind);
    }
}
