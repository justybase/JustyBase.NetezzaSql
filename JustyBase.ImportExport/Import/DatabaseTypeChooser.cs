using System.Globalization;

namespace JustyBase.ImportExport.Import;

public sealed record DetectedColumn(string Name, string NetezzaType, bool IsNullable = true);

/// <summary>
/// UI-free type inference shared by CSV, Excel, and named-pipe import modes.
/// Hosts can replace the sample source while keeping the resulting SQL model.
/// </summary>
public static class DatabaseTypeChooser
{
    public static IReadOnlyList<DetectedColumn> Infer(
        IReadOnlyList<string> names,
        IReadOnlyList<IReadOnlyList<string?>> sampleRows,
        int varcharLength = 255)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(sampleRows);
        if (varcharLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(varcharLength));

        var result = new List<DetectedColumn>(names.Count);
        for (int column = 0; column < names.Count; column++)
        {
            var values = sampleRows
                .Where(row => column < row.Count && !string.IsNullOrWhiteSpace(row[column]))
                .Select(row => row[column]!)
                .ToArray();
            string type = InferType(values, varcharLength);
            result.Add(new DetectedColumn(names[column], type, values.Length != sampleRows.Count));
        }
        return result;
    }

    private static string InferType(IReadOnlyList<string> values, int varcharLength)
    {
        if (values.Count == 0)
            return $"VARCHAR({varcharLength})";
        if (values.All(value => bool.TryParse(value, out _)))
            return "BOOLEAN";
        if (values.All(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            return "INTEGER";
        if (values.All(value => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)))
            return "NUMERIC(38,10)";
        if (values.All(value => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _)))
            return "DATETIME";
        int length = Math.Clamp(values.Max(value => value.Length), 1, 32_767);
        return $"VARCHAR({Math.Max(length, Math.Min(varcharLength, 255))})";
    }
}
