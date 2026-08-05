using System.Text.RegularExpressions;

namespace JustyBase.ImportExport.Import;

public enum ImportHeaderCase
{
    Upper,
    Lower,
    Preserve
}

/// <summary>
/// Port of <c>src/import/importHeaderUtils.ts</c>: sanitizes raw header tokens into safe
/// identifier names and de-duplicates them case-insensitively. Netezza defaults to upper case
/// (matching the dialect's <c>generatedNameCase</c>).
/// </summary>
public static partial class ImportHeaderNormalizer
{
    public static string NormalizeImportedHeader(string header, ImportHeaderCase casePolicy = ImportHeaderCase.Upper)
    {
        string cleaned = SanitizeHeaderToken(header);

        if (cleaned.Length == 0)
            return ApplyCase("COL_EMPTY", casePolicy);

        if (char.IsDigit(cleaned[0]))
            cleaned = "COL_" + cleaned;
        else if (cleaned[0] == '_')
            cleaned = "COL" + cleaned;

        return ApplyCase(cleaned, casePolicy);
    }

    public static string[] NormalizeAndDeduplicateHeaders(IReadOnlyList<string> headers, ImportHeaderCase casePolicy = ImportHeaderCase.Upper)
    {
        var cleaned = new string[headers.Count];
        for (int i = 0; i < headers.Count; i++)
            cleaned[i] = NormalizeImportedHeader(headers[i], casePolicy);

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new string[cleaned.Length];
        for (int i = 0; i < cleaned.Length; i++)
        {
            string dedupeKey = cleaned[i].ToUpperInvariant();
            seen.TryGetValue(dedupeKey, out int count);
            seen[dedupeKey] = count + 1;
            result[i] = count == 0 ? cleaned[i] : $"{cleaned[i]}_{count}";
        }
        return result;
    }

    private static string SanitizeHeaderToken(string header)
    {
        string value = (header ?? string.Empty).Trim();
        value = InvalidTokenCharsRegex().Replace(value, "_");
        value = UnderscoreRunRegex().Replace(value, "_");
        return value.Trim('_');
    }

    private static string ApplyCase(string value, ImportHeaderCase casePolicy)
        => casePolicy switch
        {
            ImportHeaderCase.Upper => value.ToUpperInvariant(),
            ImportHeaderCase.Lower => value.ToLowerInvariant(),
            _ => value
        };

    [GeneratedRegex(@"[^0-9A-Za-z_$]+")]
    private static partial Regex InvalidTokenCharsRegex();

    [GeneratedRegex(@"_+")]
    private static partial Regex UnderscoreRunRegex();
}
