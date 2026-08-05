using System.Globalization;

namespace JustyBase.ImportExport.Import;

public enum CsvCellKind
{
    Null,
    String,
    Int64,
    Double,
    DateTime,
    Boolean
}

public readonly record struct CsvCell(
    CsvCellKind Kind,
    string? StringValue = null,
    long Int64Value = 0,
    decimal DecimalValue = 0,
    DateTime DateTimeValue = default,
    bool BooleanValue = false)
{
    public static readonly CsvCell Null = new(CsvCellKind.Null);
}
