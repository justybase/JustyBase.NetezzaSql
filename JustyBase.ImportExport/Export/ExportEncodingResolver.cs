using System.Text;

namespace JustyBase.ImportExport.Export;

/// <summary>
/// Shared resolution of export encodings and newline styles.
/// Superset of the previously duplicated per-host helpers: Avalonia's named-encoding
/// aliases (<c>utf-8</c>, <c>utf8_bm</c>, <c>latin1</c>, <c>utf16</c>, <c>utf32</c>, …) and
/// Legacy's codepage integer / ASP.NET name support (via
/// <see cref="CodePagesEncodingProvider"/>), defaulting to UTF-8.
/// </summary>
public static class ExportEncodingResolver
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static Encoding Resolve(string? encodingName)
    {
        string value = string.IsNullOrWhiteSpace(encodingName) ? "utf-8" : encodingName.Trim();

        if (value.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || value.Equals("utf8", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8;
        if (value.Equals("utf8_bm", StringComparison.OrdinalIgnoreCase) || value.Equals("utf-8_bm", StringComparison.OrdinalIgnoreCase))
            return Utf8NoBom;
        if (value.Equals("ascii", StringComparison.OrdinalIgnoreCase))
            return Encoding.ASCII;
        if (value.Equals("latin1", StringComparison.OrdinalIgnoreCase))
            return Encoding.Latin1;
        if (value.Equals("utf16", StringComparison.OrdinalIgnoreCase) || value.Equals("utf-16", StringComparison.OrdinalIgnoreCase) || value.Equals("unicode", StringComparison.OrdinalIgnoreCase))
            return Encoding.Unicode;
        if (value.Equals("utf32", StringComparison.OrdinalIgnoreCase) || value.Equals("utf-32", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF32;
        if (value.Equals("bigendianunicode", StringComparison.OrdinalIgnoreCase))
            return Encoding.BigEndianUnicode;
        if (value.Equals("default", StringComparison.OrdinalIgnoreCase))
            return Encoding.Default;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return int.TryParse(value, out int codePage)
            ? Encoding.GetEncoding(codePage)
            : Encoding.GetEncoding(value);
    }

    public static string ResolveNewLine(string? value)
        => string.IsNullOrEmpty(value) ? Environment.NewLine
            : value.Replace("\\r", "\r", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal);
}