using System.Globalization;
using System.Text.RegularExpressions;

namespace JustyBase.ImportExport.Import.TypeChooser;

/// <summary>Options passed to <see cref="NetezzaColumnTypeChooser"/> (mirror of <c>ColumnTypeChooserOptions</c>).</summary>
public sealed record ColumnTypeChooserOptions(bool ForceText = false, bool InferBoolean = false);

/// <summary>
/// Faithful C# port of the vscode <c>ColumnTypeChooser</c> for Netezza
/// (<c>src/dialects/netezza/import/typeMapping.ts</c>). Monotonic upgrade path:
/// BIGINT → NUMERIC → DATE → DATETIME → NVARCHAR; a column never downgrades. Values with
/// leading zeros (and PESEL-like columns, forced by callers via <c>ForceText</c>) stay textual.
/// </summary>
public sealed partial class NetezzaColumnTypeChooser : DatabaseColumnTypeChooser
{
    private readonly char _decimalDelimiter;
    private readonly bool _forceText;
    private readonly bool _inferBoolean;
    private bool _firstTime = true;
    private int _maxPrecision;
    private int _maxScale;

    public NetezzaColumnTypeChooser(string decimalDelimiter = ".", ColumnTypeChooserOptions? options = null)
    {
        _decimalDelimiter = decimalDelimiter == "." ? '.' : decimalDelimiter[0];
        _forceText = options?.ForceText == true;
        _inferBoolean = options?.InferBoolean == true;
        _currentType = _forceText
            ? new NetezzaImportDataType("NVARCHAR", length: 20)
            : new NetezzaImportDataType("BIGINT");
    }

    private DatabaseImportDataType _currentType;

    public override DatabaseImportDataType CurrentType => _currentType;

    public override int GetMaxScale() => _maxScale;

    public override int GetMaxPrecision() => _maxPrecision;

    public override DatabaseImportDataType RefreshCurrentType(string strVal)
    {
        _currentType = GetType(strVal);
        return _currentType;
    }

    private NetezzaImportDataType CreateTextType(string strVal)
    {
        int tmpLen = Math.Max(strVal.Length + 5, 20);
        if (CurrentType.Length is int currentLen && tmpLen < currentLen)
            tmpLen = currentLen;
        _firstTime = false;
        return new NetezzaImportDataType("NVARCHAR", length: tmpLen);
    }

    private NetezzaImportDataType GetType(string strVal)
    {
        string currentDbType = CurrentType.DbType;
        int strLen = strVal.Length;

        if (_forceText || ImportTypeInferenceUtils.ValueForcesTextImportType(strVal))
            return CreateTextType(strVal);

        string strValNoSpace = WhitespaceRegex().Replace(strVal, "");
        int strLenNoSpace = strValNoSpace.Length;

        if (_inferBoolean && BooleanRegex().IsMatch(strValNoSpace))
        {
            _firstTime = false;
            return new NetezzaImportDataType("BOOLEAN");
        }

        if (currentDbType == "BIGINT"
            && DigitsOnlyRegex().IsMatch(strValNoSpace)
            && strLenNoSpace > 0
            && strLenNoSpace < 15
            && (strValNoSpace == "0" || !strValNoSpace.StartsWith('0')))
        {
            _firstTime = false;
            return new NetezzaImportDataType("BIGINT");
        }

        int decimalCnt = CountOccurrences(strValNoSpace, _decimalDelimiter);

        if (currentDbType is "BIGINT" or "NUMERIC" && decimalCnt <= 1)
        {
            string strValClean = strValNoSpace.Replace(_decimalDelimiter.ToString(), "");
            if (DigitsOnlyRegex().IsMatch(strValClean)
                && strLenNoSpace > 0
                && strLenNoSpace < 20
                && (!strValClean.StartsWith('0') || decimalCnt > 0 || strValClean == "0"))
            {
                _firstTime = false;

                int delimIndex = strValNoSpace.IndexOf(_decimalDelimiter);
                string integerPart = delimIndex < 0 ? strValNoSpace : strValNoSpace[..delimIndex];
                string decimalPart = delimIndex < 0 ? string.Empty : strValNoSpace[(delimIndex + 1)..];
                if (integerPart.Length == 0)
                    integerPart = "0";

                int precision = integerPart.Length + decimalPart.Length;
                int scale = decimalPart.Length;

                _maxPrecision = Math.Max(_maxPrecision, precision);
                _maxScale = Math.Max(_maxScale, scale);

                int finalPrecision = Math.Min(Math.Max(_maxPrecision, 16), 38);
                int finalScale = Math.Min(_maxScale, 18);
                return new NetezzaImportDataType("NUMERIC", finalPrecision, finalScale);
            }
        }

        if ((currentDbType == "DATE" || _firstTime)
            && CountOccurrences(strVal, '-') == 2
            && strLen >= 8
            && strLen <= 10)
        {
            string[] parts = strVal.Split('-');
            if (parts.Length == 3
                && parts.All(p => DigitsOnlyRegex().IsMatch(p))
                && TryCreateDate(parts[0], parts[1], parts[2]))
            {
                _firstTime = false;
                return new NetezzaImportDataType("DATE");
            }
        }

        if ((currentDbType == "DATETIME" || _firstTime)
            && CountOccurrences(strVal, '-') == 2
            && strLen >= 12
            && strLen <= 20)
        {
            Match iso = IsoDateTimeRegex().Match(strVal);
            if (iso.Success && TryCreateDateTime(iso))
            {
                _firstTime = false;
                return new NetezzaImportDataType("DATETIME");
            }
        }

        if ((currentDbType == "DATETIME" || _firstTime)
            && CountOccurrences(strVal, '.') >= 2)
        {
            Match dotted = DottedDateTimeRegex().Match(strVal);
            if (dotted.Success && TryCreateDottedDateTime(dotted))
            {
                _firstTime = false;
                return new NetezzaImportDataType("DATETIME");
            }
        }

        return CreateTextType(strVal);
    }

    private static bool TryCreateDate(string year, string month, string day)
    {
        try
        {
            _ = new DateTime(
                int.Parse(year, CultureInfo.InvariantCulture),
                int.Parse(month, CultureInfo.InvariantCulture),
                int.Parse(day, CultureInfo.InvariantCulture));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryCreateDateTime(Match iso)
    {
        try
        {
            int sec = iso.Groups[7].Success ? int.Parse(iso.Groups[7].Value, CultureInfo.InvariantCulture) : 0;
            _ = new DateTime(
                int.Parse(iso.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(iso.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(iso.Groups[3].Value, CultureInfo.InvariantCulture),
                int.Parse(iso.Groups[4].Value, CultureInfo.InvariantCulture),
                int.Parse(iso.Groups[5].Value, CultureInfo.InvariantCulture),
                sec);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryCreateDottedDateTime(Match dotted)
    {
        try
        {
            int day = int.Parse(dotted.Groups[1].Value, CultureInfo.InvariantCulture);
            int month = int.Parse(dotted.Groups[2].Value, CultureInfo.InvariantCulture);
            int year = int.Parse(dotted.Groups[3].Value, CultureInfo.InvariantCulture);
            int hour = dotted.Groups[4].Success ? int.Parse(dotted.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
            int min = dotted.Groups[5].Success ? int.Parse(dotted.Groups[5].Value, CultureInfo.InvariantCulture) : 0;
            int sec = dotted.Groups[6].Success ? int.Parse(dotted.Groups[6].Value, CultureInfo.InvariantCulture) : 0;

            if (month is >= 1 and <= 12 && day is >= 1 and <= 31)
            {
                var date = new DateTime(year, month, day, hour, min, sec);
                return date.Year == year && date.Month == month && date.Day == day;
            }
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static int CountOccurrences(string value, char c)
    {
        int count = 0;
        foreach (char ch in value)
        {
            if (ch == c)
                count++;
        }
        return count;
    }

    [GeneratedRegex(@"\s")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(?:true|false)$", RegexOptions.IgnoreCase)]
    private static partial Regex BooleanRegex();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex DigitsOnlyRegex();

    [GeneratedRegex(@"^(\d{4})-(\d{1,2})-(\d{1,2})[\s|T](\d{2}):(\d{2})(:?(\d{2}))?$")]
    private static partial Regex IsoDateTimeRegex();

    [GeneratedRegex(@"^(\d{1,2})\.(\d{1,2})\.(\d{4})(?:\s+(\d{1,2}):(\d{1,2})(?::(\d{1,2}))?)?$")]
    private static partial Regex DottedDateTimeRegex();
}
