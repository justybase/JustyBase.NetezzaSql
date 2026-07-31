using System.Globalization;

namespace JustyBase.ImportExport.Import;

public sealed record DetectedColumn(string Name, string NetezzaType, bool IsNullable = true);

/// <summary>
/// UI-free batch type inference shared by sample-based CSV/pipe import modes.
/// This is <b>not</b> a drop-in for Avalonia's streaming <c>DatabaseTypeChooser</c>
/// (<c>InitTypes</c>/<c>ChooseTypes</c>); parity requires an explicit audit before unification.
/// Text columns prefer <c>NVARCHAR</c> with length = ceil(maxLen × 1.2) rounded up to the next 10.
/// </summary>
public static class DatabaseTypeChooser
{
    private const int MaxNvarcharLength = 16_000;

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
            return FormatNvarchar(SizeTextLength(varcharLength));
        if (values.All(value => bool.TryParse(value, out _)))
            return "BOOLEAN";
        // Padded digit codes ("001", "-05") must stay textual; plain "0" / "-5" remain numeric.
        if (values.All(IsPlainIntegerToken))
            return "INTEGER";
        if (values.All(IsPlainDecimalToken))
            return "NUMERIC(38,10)";
        if (values.All(value => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _)))
            return "DATETIME";

        int maxLen = Math.Clamp(values.Max(value => value.Length), 1, MaxNvarcharLength);
        return FormatNvarchar(SizeTextLength(maxLen));
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

    private static bool IsPlainIntegerToken(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
           && !HasSignificantLeadingZeros(value);

    private static bool IsPlainDecimalToken(string value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
           && !HasSignificantLeadingZeros(value);

    /// <summary>
    /// Detects integer-like tokens with meaningful leading zeros after an optional sign
    /// (<c>001</c>, <c>00</c>, <c>-05</c>, <c>+012</c>). Plain <c>0</c>/<c>-0</c> and
    /// decimals like <c>0.5</c> are not treated as significant leading zeros.
    /// </summary>
    private static bool HasSignificantLeadingZeros(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan().Trim();
        if (span.Length == 0)
            return false;

        int i = 0;
        if (span[0] is '+' or '-')
            i++;
        if (i >= span.Length || span[i] != '0')
            return false;
        if (span.Length - i == 1)
            return false; // "0" / "-0"

        ReadOnlySpan<char> afterLeadingZero = span[(i + 1)..];
        if (afterLeadingZero[0] == '.')
            return false; // "0.5" stays numeric

        // Only digit padding counts (001, 00, -05) — not "0x" / mixed tokens.
        foreach (char c in afterLeadingZero)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }
}
