using System.Globalization;

namespace JustyBase.Core.Grid;

public sealed record CellStats(
    int Count,
    int NullCount,
    int NumericCount,
    int DistinctCount,
    decimal? Sum,
    decimal? Average,
    decimal? Minimum,
    decimal? Maximum);

/// <summary>
/// Typed selection stats (Avalonia SoT). Numeric min/max/sum use decimal conversion,
/// not lexical string compare.
/// </summary>
public static class CellStatsCalculator
{
    private static readonly HashSet<TypeCode> NumericTypeCodes =
    [
        TypeCode.Byte,
        TypeCode.SByte,
        TypeCode.UInt16,
        TypeCode.Int16,
        TypeCode.Int32,
        TypeCode.Int64,
        TypeCode.Single,
        TypeCode.Double,
        TypeCode.Decimal,
    ];

    public static CellStats Calculate(IEnumerable<object?> values)
        => Calculate(values.Select(value => (value, InferTypeCode(value))));

    public static CellStats Calculate(IEnumerable<(object? Value, TypeCode TypeCode)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var materialized = values.ToArray();

        decimal sum = 0;
        int numericCount = 0;
        int nullCount = 0;
        decimal? min = null;
        decimal? max = null;
        var distinct = new HashSet<object>();

        foreach (var (value, typeCode) in materialized)
        {
            if (value is null || value == DBNull.Value)
            {
                nullCount++;
                continue;
            }

            distinct.Add(value);
            if (!NumericTypeCodes.Contains(typeCode))
                continue;

            if (!TryToDecimal(value, out decimal number))
                continue;

            numericCount++;
            sum += number;
            min = min is null ? number : Math.Min(min.Value, number);
            max = max is null ? number : Math.Max(max.Value, number);
        }

        int nonNull = materialized.Length - nullCount;
        return new CellStats(
            materialized.Length,
            nullCount,
            numericCount,
            distinct.Count,
            numericCount == 0 ? null : sum,
            numericCount == 0 ? null : sum / numericCount,
            min,
            max);
    }

    private static TypeCode InferTypeCode(object? value)
        => value is null || value == DBNull.Value ? TypeCode.Empty : Type.GetTypeCode(value.GetType());

    private static bool TryToDecimal(object value, out decimal number)
    {
        try
        {
            number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return decimal.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out number);
        }
    }
}
