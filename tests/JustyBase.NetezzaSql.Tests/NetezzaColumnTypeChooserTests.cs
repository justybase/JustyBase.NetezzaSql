using JustyBase.ImportExport.Import;
using JustyBase.ImportExport.Import.TypeChooser;

namespace JustyBase.NetezzaSql.Tests;

public sealed class NetezzaColumnTypeChooserTests
{
    private static NetezzaColumnTypeChooser Chooser(string decimalDelimiter = ".", bool inferBoolean = false)
        => new(decimalDelimiter, new ColumnTypeChooserOptions(InferBoolean: inferBoolean));

    [Fact]
    public void Initial_type_is_bigint()
        => Assert.Equal("BIGINT", new NetezzaColumnTypeChooser().CurrentType.DbType);

    [Fact]
    public void Initial_type_is_nvarchar20_when_force_text()
    {
        var chooser = new NetezzaColumnTypeChooser(options: new ColumnTypeChooserOptions(ForceText: true));
        Assert.Equal("NVARCHAR", chooser.CurrentType.DbType);
        Assert.Equal(20, chooser.CurrentType.Length);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("99999999999999")]
    [InlineData("0")]
    [InlineData("100")]
    public void Integers_detect_bigint(string value)
        => Assert.Equal("BIGINT", Chooser().RefreshCurrentType(value).DbType);

    [Fact]
    public void Decimal_detects_numeric_with_scale()
    {
        var chooser = Chooser();
        var type = chooser.RefreshCurrentType("123.45");
        Assert.Equal("NUMERIC", type.DbType);
        Assert.Equal(16, type.Precision);
        Assert.Equal(2, type.Scale);
    }

    [Fact]
    public void Comma_delimiter_detects_numeric()
    {
        var type = Chooser(decimalDelimiter: ",").RefreshCurrentType("123,45");
        Assert.Equal("NUMERIC", type.DbType);
        Assert.Equal(2, type.Scale);
    }

    [Fact]
    public void Tracks_max_precision_and_scale()
    {
        var chooser = Chooser();
        chooser.RefreshCurrentType("12.5");
        chooser.RefreshCurrentType("123.45");
        chooser.RefreshCurrentType("12345.678");

        Assert.Equal(8, chooser.GetMaxPrecision());
        Assert.Equal(3, chooser.GetMaxScale());
    }

    [Theory]
    [InlineData("2024-06-07", "DATE")]
    [InlineData("2024-6-7", "DATE")]
    [InlineData("2024-06-07 14:30", "DATETIME")]
    [InlineData("2024-06-07 14:30:45", "DATETIME")]
    [InlineData("07.06.2024 14:30", "DATETIME")]
    [InlineData("07.06.2024", "DATETIME")]
    [InlineData("hello world", "NVARCHAR")]
    public void Detects_dates_and_falls_back_to_text(string value, string expected)
        => Assert.Equal(expected, Chooser().RefreshCurrentType(value).DbType);

    [Fact]
    public void Mixed_content_falls_back_to_nvarchar()
    {
        var chooser = Chooser();
        Assert.Equal("BIGINT", chooser.RefreshCurrentType("123").DbType);
        Assert.Equal("NVARCHAR", chooser.RefreshCurrentType("abc123").DbType);
    }

    [Fact]
    public void Upgrades_from_bigint_to_numeric()
    {
        var chooser = Chooser();
        Assert.Equal("BIGINT", chooser.RefreshCurrentType("100").DbType);
        Assert.Equal("NUMERIC", chooser.RefreshCurrentType("12.5").DbType);
    }

    [Fact]
    public void Does_not_downgrade_from_numeric_to_bigint()
    {
        var chooser = Chooser();
        Assert.Equal("NUMERIC", chooser.RefreshCurrentType("12.5").DbType);
        Assert.Equal("NUMERIC", chooser.RefreshCurrentType("100").DbType);
    }

    [Fact]
    public void Negative_integers_fall_back_to_nvarchar()
        => Assert.Equal("NVARCHAR", Chooser().RefreshCurrentType("-12345").DbType);

    [Fact]
    public void Very_long_integers_fall_back_to_nvarchar()
        => Assert.Equal("NVARCHAR", Chooser().RefreshCurrentType("123456789012345678901234567890").DbType);

    [Theory]
    [InlineData("001")]
    [InlineData("00.5")]
    [InlineData("000")]
    public void Leading_zero_values_force_nvarchar(string value)
        => Assert.Equal("NVARCHAR", Chooser().RefreshCurrentType(value).DbType);

    [Fact]
    public void Decimal_with_zero_integer_part_stays_numeric()
    {
        var type = Chooser().RefreshCurrentType("0.5");
        Assert.Equal("NUMERIC", type.DbType);
        Assert.Equal(1, type.Scale);
    }

    [Fact]
    public void Boolean_values_detect_boolean_when_enabled()
    {
        var chooser = Chooser(inferBoolean: true);
        Assert.Equal("BOOLEAN", chooser.RefreshCurrentType("true").DbType);
        Assert.Equal("BOOLEAN", chooser.RefreshCurrentType("false").DbType);
    }

    [Fact]
    public void Boolean_values_are_text_by_default()
    {
        var chooser = Chooser();
        Assert.Equal("NVARCHAR", chooser.RefreshCurrentType("true").DbType);
        Assert.Equal("NVARCHAR", chooser.RefreshCurrentType("false").DbType);
    }

    [Fact]
    public void Force_text_never_detects_numbers()
    {
        var chooser = new NetezzaColumnTypeChooser(options: new ColumnTypeChooserOptions(ForceText: true));
        Assert.Equal("NVARCHAR", chooser.RefreshCurrentType("12345").DbType);
        Assert.Equal("NVARCHAR", chooser.RefreshCurrentType("2024-06-07").DbType);
    }

    [Fact]
    public void Numeric_precision_is_floored_at_sixteen()
    {
        var type = Chooser().RefreshCurrentType("12345.678");
        Assert.Equal(16, type.Precision);
        Assert.Equal(3, type.Scale);
    }

    [Fact]
    public void Numeric_precision_and_scale_respect_ddl_limits()
    {
        var type = Chooser().RefreshCurrentType("0." + new string('1', 17));
        Assert.True(type.Precision is <= 38);
        Assert.True(type.Scale is <= 18);
    }

    [Fact]
    public void Nvarchar_length_uses_data_length_with_headroom()
    {
        var type = Chooser().RefreshCurrentType("hello");
        Assert.Equal(20, type.Length);
    }

    [Fact]
    public void Nvarchar_length_is_monotonic()
    {
        var chooser = Chooser();
        chooser.RefreshCurrentType("a");
        var type = chooser.RefreshCurrentType(new string('x', 100));
        Assert.Equal(105, type.Length);
    }

    [Fact]
    public void ToString_renders_ddl()
    {
        Assert.Equal("BIGINT", new NetezzaImportDataType("BIGINT").ToString());
        Assert.Equal("DATE", new NetezzaImportDataType("DATE").ToString());
        Assert.Equal("DATETIME", new NetezzaImportDataType("DATETIME").ToString());
        Assert.Equal("BOOLEAN", new NetezzaImportDataType("BOOLEAN").ToString());
        Assert.Equal("NUMERIC(16,2)", new NetezzaImportDataType("NUMERIC", 16, 2).ToString());
        Assert.Equal("NVARCHAR(20)", new NetezzaImportDataType("NVARCHAR", length: 20).ToString());
    }

    [Fact]
    public void Mapper_creates_chooser_and_data_type()
    {
        var mapper = NetezzaImportTypeMapper.Instance;
        DatabaseColumnTypeChooser chooser = mapper.CreateColumnTypeChooser(",");
        Assert.Equal("NUMERIC", chooser.RefreshCurrentType("123,45").DbType);

        DatabaseImportDataType dataType = mapper.CreateDataType("NVARCHAR", length: 30);
        Assert.Equal("NVARCHAR(30)", dataType.ToString());
    }
}
