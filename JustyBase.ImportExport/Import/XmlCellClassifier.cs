using System.Globalization;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Clipboard/XML cell classifier (port of the Avalonia <c>DbXMLImportJob</c> helper):
/// turns a raw string value into a canonical typed literal plus a proposed
/// <see cref="ImportColumnKind"/>. Used by the XML import path and by the host
/// clipboard-to-SELECT builder.
/// </summary>
public static class XmlCellClassifier
{
    private const NumberStyles NumberExcelStyle = NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowExponent;

    private static readonly CultureInfo UsCulture = CultureInfo.CreateSpecificCulture("en-US");

    private static readonly NumberFormatInfo NumberWithDot = new()
    {
        NumberDecimalSeparator = ".",
        NumberDecimalDigits = 6
    };

    public static string GetValueStringRepresentationWithType(
        out ImportColumnKind proposedKind,
        ReadOnlySpan<char> stringValue,
        bool dataTypeAnnotation = true,
        string textQualifier = "'")
    {
        if (stringValue.Length == 0 || stringValue.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            proposedKind = ImportColumnKind.NoInfo;
            return "";
        }

        bool integerTest = int.TryParse(stringValue, NumberExcelStyle, CultureInfo.CurrentCulture, out int intNumber);

        bool decimalTest = decimal.TryParse(stringValue, NumberExcelStyle, CultureInfo.CurrentCulture, out decimal decimalNumber);
        if (!decimalTest)
        {
            decimalTest = decimal.TryParse(stringValue, NumberExcelStyle, UsCulture, out decimalNumber);
        }

        if (integerTest && (int)decimalNumber == intNumber) // "simple" number
        {
            if (stringValue[0] == '0' && stringValue.Length > 1) // 0123456
            {
                proposedKind = ImportColumnKind.Nvarchar;
                return GetTextQualifiedString(stringValue, textQualifier);
            }

            proposedKind = ImportColumnKind.Integer;
            return intNumber.ToString(CultureInfo.InvariantCulture);
        }

        if (decimalTest && stringValue.Length >= 9 && !stringValue.ContainsAnyExceptInRange('0', '9')) // REGON, IBAN, etc.
        {
            proposedKind = ImportColumnKind.Nvarchar;
            return GetTextQualifiedString(stringValue, textQualifier);
        }

        if (decimalTest) // "simple" number
        {
            proposedKind = ImportColumnKind.Numeric;
            return Math.Round(decimalNumber, 6).ToString(NumberWithDot);
        }

        if (stringValue[^1] == '%')
        {
            decimalTest = decimal.TryParse(stringValue[..^1], NumberExcelStyle, CultureInfo.CurrentCulture, out decimalNumber);
            if (!decimalTest)
            {
                decimalTest = decimal.TryParse(stringValue, NumberExcelStyle, UsCulture, out decimalNumber);
            }

            if (decimalTest)
            {
                proposedKind = ImportColumnKind.Numeric;
                return Math.Round(decimalNumber * 0.01m, 6).ToString(NumberWithDot);
            }
        }

        bool dataTimeTest = DateTime.TryParse(stringValue, out DateTime dateTimeValue);
        if (!dataTimeTest)
        {
            dataTimeTest = DateTime.TryParse(stringValue, UsCulture, DateTimeStyles.None, out dateTimeValue);
        }

        if (dataTimeTest)
        {
            proposedKind = ImportColumnKind.TimeStamp;
            string type = dataTypeAnnotation ? "timestamp " : "";
            return $"{type}{GetTextQualifiedString(dateTimeValue.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), textQualifier)}";
        }

        proposedKind = ImportColumnKind.Nvarchar;
        return GetTextQualifiedString(stringValue, textQualifier);
    }

    private static string GetTextQualifiedString(ReadOnlySpan<char> text, string textQualifier)
    {
        if (textQualifier.Length == 0)
        {
            return text.ToString();
        }

        return $"{textQualifier}{text}{textQualifier}";
    }
}
