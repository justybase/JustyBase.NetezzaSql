using System.Data.Common;
using System.Globalization;
using JustyBase.NetezzaDdl.Models;

namespace JustyBase.NetezzaDdl;

public static class NetezzaExternalOptionsMapper
{
    public static NetezzaExternalTableOptions ToOptions(NetezzaExternalTableCachedInfo info)
        => new()
        {
            DataObject = info.DataObject,
            Delimiter = info.Delimiter == "\t" ? "\\t" : info.Delimiter,
            Encoding = info.Encoding,
            Timestyle = info.Timestyle,
            RemoteSource = info.RemoteSource,
            SkipRows = info.SkipRows,
            MaxErrors = info.MaxErrors,
            EscapeChar = info.EscapeChar,
            DecimalDelim = info.DecimalDelim,
            LogDir = info.LogDir,
            QuotedValue = info.QuotedValue,
            NullValue = info.NullValue,
            CrInString = info.CrInString,
            TruncString = info.TruncString,
            CtrlChars = info.CtrlChars,
            IgnoreZero = info.IgnoreZero,
            TimeExtraZeros = info.TimeExtraZeros,
            Y2Base = info.Y2Base,
            FillRecord = info.FillRecord,
            Compress = info.Compress,
            IncludeHeader = info.IncludeHeader,
            LfInString = info.LfInString,
            DateStyle = info.DateStyle,
            DateDelim = info.DateDelim,
            TimeDelim = info.TimeDelim,
            BoolStyle = info.BoolStyle,
            Format = info.Format,
            SocketBufSize = info.SocketBufSize,
            RecordDelim = info.RecordDelim,
            MaxRows = info.MaxRows,
            RequireQuotes = info.RequireQuotes,
            RecordLength = info.RecordLength,
            DateTimeDelim = info.DateTimeDelim,
            RejectFile = info.RejectFile,
        };

    public static NetezzaExternalTableCachedInfo FromReader(DbDataReader rd)
        => new()
        {
            Delimiter = NormalizeDelimiter(rd.GetValue(0)),
            Encoding = rd.GetValue(1) as string,
            Timestyle = rd.GetValue(2) as string,
            RemoteSource = rd.GetValue(3) as string,
            SkipRows = ReadInt64(rd.GetValue(4)),
            MaxErrors = ReadInt64(rd.GetValue(5)),
            EscapeChar = rd.GetValue(6) is string esc && esc.Length > 0 ? esc : null,
            LogDir = rd.GetValue(7) as string,
            DecimalDelim = rd.GetValue(8) as string,
            QuotedValue = rd.GetValue(9) as string,
            NullValue = rd.GetValue(10) as string,
            CrInString = ReadBool(rd.GetValue(11)),
            TruncString = ReadBool(rd.GetValue(12)),
            CtrlChars = ReadBool(rd.GetValue(13)),
            IgnoreZero = ReadBool(rd.GetValue(14)),
            TimeExtraZeros = ReadBool(rd.GetValue(15)),
            Y2Base = ReadInt16(rd.GetValue(16)),
            FillRecord = ReadBool(rd.GetValue(17)),
            Compress = rd.GetValue(18)?.ToString(),
            IncludeHeader = ReadBool(rd.GetValue(19)),
            LfInString = ReadBool(rd.GetValue(20)),
            DateStyle = rd.GetValue(21) as string,
            DateDelim = rd.GetValue(22) as string,
            TimeDelim = rd.GetValue(23) as string,
            BoolStyle = rd.GetValue(24) as string,
            Format = rd.GetValue(25) as string,
            SocketBufSize = ReadInt32(rd.GetValue(26)),
            RecordDelim = rd.GetValue(27) as string,
            MaxRows = ReadInt64(rd.GetValue(28)),
            RequireQuotes = ReadBool(rd.GetValue(29)),
            RecordLength = rd.GetValue(30)?.ToString(),
            DateTimeDelim = rd.GetValue(31) as string,
            RejectFile = rd.GetValue(32) as string,
        };

    public static NetezzaExternalTableCachedInfo FromLegacyReader(DbDataReader rd)
    {
        string? recordDelim = rd.GetString(31).Replace("\r", "\\r").Replace("\n", "\\n");
        return new NetezzaExternalTableCachedInfo
        {
            DataObject = rd.GetValue(2) as string,
            Delimiter = rd.GetValue(4) as string,
            Encoding = rd.GetValue(5) as string,
            Timestyle = rd.GetValue(6) as string,
            RemoteSource = rd.GetValue(7) as string,
            SkipRows = ReadInt64(rd.GetValue(8)),
            MaxErrors = ReadInt64(rd.GetValue(9)),
            EscapeChar = rd.GetValue(10) as string,
            LogDir = rd.GetString(11),
            DecimalDelim = rd.GetValue(12) as string,
            QuotedValue = rd.GetValue(13) as string,
            NullValue = rd.GetValue(14) as string,
            CrInString = rd.GetValue(15) as bool?,
            TruncString = rd.GetValue(16) as bool?,
            CtrlChars = rd.GetValue(17) as bool?,
            IgnoreZero = rd.GetValue(18) as bool?,
            TimeExtraZeros = rd.GetValue(19) as bool?,
            Y2Base = ReadInt16(rd.GetValue(20)),
            FillRecord = rd.GetValue(21) as bool?,
            Compress = rd.GetValue(22) as string,
            IncludeHeader = rd.GetValue(23) as bool?,
            LfInString = rd.GetValue(24) as bool?,
            DateStyle = rd.GetValue(25) as string,
            DateDelim = rd.GetValue(26) as string,
            TimeDelim = rd.GetValue(27) as string,
            BoolStyle = rd.GetValue(28) as string,
            Format = rd.GetValue(29) as string,
            SocketBufSize = ReadInt32(rd.GetValue(30)),
            RecordDelim = recordDelim,
            MaxRows = ReadInt64(rd.GetValue(32)),
            RequireQuotes = rd.GetValue(33) as bool?,
            RecordLength = rd.GetValue(34) as string,
            DateTimeDelim = rd.GetValue(35) as string,
            RejectFile = rd.GetValue(36) as string,
        };
    }

    private static string? NormalizeDelimiter(object? value)
    {
        if (value is DBNull or null)
            return null;

        string text = value.ToString()!;
        return text == "\t" ? "\\t" : text;
    }

    private static bool? ReadBool(object? value)
    {
        if (value is DBNull or null)
            return null;

        if (value is bool b)
            return b;

        return bool.TryParse(value.ToString(), out bool parsed) ? parsed : null;
    }

    private static long? ReadInt64(object? value)
    {
        if (value is DBNull or null)
            return null;

        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static short? ReadInt16(object? value)
    {
        if (value is DBNull or null)
            return null;

        try
        {
            return Convert.ToInt16(value, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static int? ReadInt32(object? value)
    {
        if (value is DBNull or null)
            return null;

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
