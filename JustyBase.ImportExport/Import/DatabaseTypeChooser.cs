using JustyBase.ImportExport.Import.TypeChooser;

namespace JustyBase.ImportExport.Import;

public sealed record DetectedColumn(string Name, string NetezzaType, bool IsNullable = true);

/// <summary>
/// Sample-based batch type inference shared by CSV/pipe import modes. Retargeted onto the
/// vscode <see cref="NetezzaColumnTypeChooser"/> algorithm (the import type-inference SoT).
/// For the streaming per-row variant see <see cref="ImportTypeAnalyzer"/>. A fully empty
/// column falls back to an <c>NVARCHAR</c> sized from the <c>varcharLength</c> hint.
/// </summary>
public static class DatabaseTypeChooser
{
    private const int MaxNvarcharLength = 16_000;

    public static IReadOnlyList<DetectedColumn> Infer(
        IReadOnlyList<string> names,
        IReadOnlyList<IReadOnlyList<string?>> sampleRows,
        int varcharLength = 255,
        string decimalDelimiter = ".",
        bool inferBoolean = false)
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

            string type;
            if (values.Length == 0)
            {
                type = FormatNvarchar(SizeTextLength(varcharLength));
            }
            else
            {
                var chooser = new NetezzaColumnTypeChooser(decimalDelimiter, new ColumnTypeChooserOptions(InferBoolean: inferBoolean));
                foreach (string value in values)
                    chooser.RefreshCurrentType(value);
                type = chooser.CurrentType.ToString();
            }

            result.Add(new DetectedColumn(names[column], type, values.Length != sampleRows.Count));
        }
        return result;
    }

    /// <summary>
    /// Adds 20% headroom to <paramref name="maxLength"/>, then rounds up to a multiple of 10
    /// (e.g. 12 → 20). Result is clamped to Netezza <c>NVARCHAR</c> limits (min 10, max 16000).
    /// </summary>
    internal static int SizeTextLength(int maxLength)
    {
        if (maxLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxLength));

        int withHeadroom = (int)Math.Ceiling(maxLength * 1.2);
        int roundedUpToTen = ((withHeadroom + 9) / 10) * 10;
        return Math.Clamp(roundedUpToTen, 10, MaxNvarcharLength);
    }

    private static string FormatNvarchar(int length) => $"NVARCHAR({length})";
}
