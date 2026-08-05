using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// <see cref="IDataReader"/> over pre-parsed clipboard/XML rows
/// (<see cref="OneCellValue"/>), honoring the detected column kinds.
/// </summary>
public sealed class DataReaderFromLines(OneCellValue[][] linesX, IReadOnlyList<IImportColumn> columns) : IDataReader
{
    private readonly OneCellValue[][] _linesX = linesX;
    private readonly IReadOnlyList<IImportColumn> _columns = columns;
    private int _currentRowNum;
    private OneCellValue[] CurrentRow => _linesX[_currentRowNum];

    private string GetOriginalValue(int index)
        => CurrentRow[index].OriginalValue ?? throw new InvalidDataException($"Cell {index} in row {_currentRowNum} has no value.");

    public object this[int i] => _linesX[i];

    public object this[string name] => throw new NotImplementedException();

    public int Depth => throw new NotImplementedException();

    private bool _isClosed;
    public bool IsClosed => _isClosed;

    public int RecordsAffected => throw new NotImplementedException();

    public int FieldCount => CurrentRow?.Length ?? _linesX[0].Length;

    public void Close() => _isClosed = true;

    public void Dispose() => Close();

    public bool GetBoolean(int i) => bool.Parse(GetOriginalValue(i));

    public byte GetByte(int i) => byte.Parse(GetOriginalValue(i));

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
        => throw new NotImplementedException();

    public char GetChar(int i) => char.Parse(GetOriginalValue(i));

    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length)
        => throw new NotImplementedException();

    public IDataReader GetData(int i) => throw new NotImplementedException();

    public string GetDataTypeName(int i) => ImportColumnKindExtensions.GetNativeType(_columns[i].Kind).ToString();

    public DateTime GetDateTime(int i) => DateTime.Parse(GetOriginalValue(i));

    public decimal GetDecimal(int i)
        => decimal.Parse(GetOriginalValue(i), NumberStyles.Number, ImportColumnKindExtensions.NumberWithDot);

    public double GetDouble(int i)
        => double.Parse(GetOriginalValue(i), NumberStyles.Number, ImportColumnKindExtensions.NumberWithDot);

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type GetFieldType(int i) => ImportColumnKindExtensions.GetNativeType(_columns[i].Kind);

    public float GetFloat(int i) => float.Parse(GetOriginalValue(i), ImportColumnKindExtensions.NumberWithDot);

    public Guid GetGuid(int i) => throw new NotImplementedException();

    public short GetInt16(int i) => short.Parse(GetOriginalValue(i));

    public int GetInt32(int i) => int.Parse(GetOriginalValue(i));

    public long GetInt64(int i) => long.Parse(GetOriginalValue(i));

    public string GetName(int i) => _columns[i].Name;

    public int GetOrdinal(string name)
    {
        for (int i = 0; i < _columns.Count; i++)
        {
            if (_columns[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public DataTable? GetSchemaTable() => null;

    public string GetString(int i)
    {
        if (CurrentRow is null)
        {
            return "";
        }

        // Text columns keep the original value (e.g. 2023/01 vs 2023-01-01); typed
        // columns use the canonical "optimized" representation.
        return _columns[i].Kind == ImportColumnKind.Nvarchar
            ? CurrentRow[i]?.OriginalValue ?? ""
            : CurrentRow[i]?.TypePreferedValue ?? "";
    }

    public object GetValue(int i) => _columns[i].Kind switch
    {
        ImportColumnKind.Integer => GetInt64(i),
        ImportColumnKind.Numeric => GetDecimal(i),
        ImportColumnKind.Nvarchar => GetString(i),
        ImportColumnKind.Date or ImportColumnKind.TimeStamp => GetDateTime(i),
        ImportColumnKind.Boolean => GetBoolean(i),
        _ => GetString(i),
    };

    public int GetValues(object[] values)
    {
        for (int i = 0; i < CurrentRow.Length; i++)
        {
            values[i] = GetValue(i);
        }
        return values.Length;
    }

    public bool IsDBNull(int i) => CurrentRow[i] is null;

    public bool NextResult() => throw new NotImplementedException();

    public bool Read()
    {
        if (_isClosed)
        {
            return false;
        }

        bool res = ++_currentRowNum < _linesX.Length;
        if (!res)
        {
            _isClosed = true;
        }

        return res;
    }
}
