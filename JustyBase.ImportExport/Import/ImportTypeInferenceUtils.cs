using System.Text.RegularExpressions;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Port of <c>src/import/importTypeInferenceUtils.ts</c>: header tokens (PESEL/NRB/IBAN/BAN)
/// and leading-zero values that force a column to NVARCHAR.
/// </summary>
public static partial class ImportTypeInferenceUtils
{
    private static readonly string[] TextImportHeaderTokens = ["PESEL", "NRB", "IBAN", "BAN"];

    public static bool HeaderForcesTextImportType(string header)
    {
        string normalizedHeader = NormalizeHeaderForTypeInference(header);
        if (normalizedHeader.Length == 0)
            return false;

        foreach (string token in TextImportHeaderTokens)
        {
            if (normalizedHeader == token
                || normalizedHeader.StartsWith(token + "_", StringComparison.Ordinal)
                || normalizedHeader.EndsWith("_" + token, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ValueForcesTextImportType(string value)
    {
        string normalizedValue = (value ?? string.Empty).Trim();
        if (normalizedValue.Length == 0)
            return false;
        return LeadingZeroRegex().IsMatch(normalizedValue);
    }

    private static string NormalizeHeaderForTypeInference(string header)
    {
        string value = (header ?? string.Empty).Trim().ToUpperInvariant();
        value = NonAlnumRegex().Replace(value, "_");
        value = UnderscoreRunRegex().Replace(value, "_");
        return value.Trim('_');
    }

    [GeneratedRegex(@"[^0-9A-Z]+")]
    private static partial Regex NonAlnumRegex();

    [GeneratedRegex(@"_+")]
    private static partial Regex UnderscoreRunRegex();

    [GeneratedRegex(@"^0\d+(?:[.,]\d+)?$")]
    private static partial Regex LeadingZeroRegex();
}
