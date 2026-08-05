using System.Buffers;
using System.Globalization;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Pure CSV cell type inference. Superset of the per-host helpers: it is the
/// Avalonia variant (bool detection, <c>Pesel</c>/<c>Regon</c> columns kept as text,
/// <c>TreatAllColumnsAsText</c>) rather than the Legacy subset.
/// </summary>
public static class CsvCellTypeResolver
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    private static readonly SearchValues<char> NumericMarkers = SearchValues.Create(",.E");

    public static bool IsTextColumnName(string columnName)
        => columnName.Equals("Regon", StringComparison.OrdinalIgnoreCase)
        || columnName.Equals("Pesel", StringComparison.OrdinalIgnoreCase);

    public static CsvCell Infer(ReadOnlySpan<char> value, string columnName, bool treatAllColumnsAsText)
    {
        if (value.Length == 0)
            return CsvCell.Null;

        if (treatAllColumnsAsText || IsTextColumnName(columnName))
            return new CsvCell(CsvCellKind.String, value.ToString());

        if ((value[0] == '-' || char.IsDigit(value[0]))
            && value.Length < 40
            && value.ContainsAny(NumericMarkers)
            && (decimal.TryParse(value, out decimal decimalValue)
                || decimal.TryParse(value, NumberStyles.Any, InvariantCulture, out decimalValue)))
        {
            return new CsvCell(CsvCellKind.Double, DecimalValue: decimalValue);
        }

        if (value.Length < 20 && value[0] != '0' && long.TryParse(value, out long int64Value))
            return new CsvCell(CsvCellKind.Int64, Int64Value: int64Value);

        if (DateTime.TryParse(value, out DateTime dateTimeValue))
            return new CsvCell(CsvCellKind.DateTime, DateTimeValue: dateTimeValue);

        if (bool.TryParse(value, out bool boolValue))
            return new CsvCell(CsvCellKind.Boolean, BooleanValue: boolValue);

        return new CsvCell(CsvCellKind.String, value.ToString());
    }
}
