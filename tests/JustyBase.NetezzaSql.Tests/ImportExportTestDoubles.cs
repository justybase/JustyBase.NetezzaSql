using System.Collections;
using System.Data;

namespace JustyBase.NetezzaSql.Tests;

internal sealed class StubDataReader : IDataReader
{
    private readonly string[] _names;
    private readonly Type[] _types;
    private readonly object?[][] _rows;
    private int _index = -1;

    public StubDataReader(IReadOnlyList<(string Name, Type Type)> columns, params object?[][] rows)
    {
        _names = columns.Select(c => c.Name).ToArray();
        _types = columns.Select(c => c.Type).ToArray();
        _rows = rows;
    }

    public int FieldCount => _names.Length;

    public object this[int i] => GetValue(i);

    public object this[string name] => GetValue(GetOrdinal(name));

    public int Depth => 0;

    public bool IsClosed => false;

    public int RecordsAffected => 0;

    public void Close() { }

    public void Dispose() { }

    public bool GetBoolean(int i) => (bool)GetValue(i)!;

    public byte GetByte(int i) => (byte)GetValue(i)!;

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();

    public char GetChar(int i) => (char)GetValue(i)!;

    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();

    public IDataReader GetData(int i) => throw new NotSupportedException();

    public string GetDataTypeName(int i) => _types[i].Name;

    public DateTime GetDateTime(int i) => (DateTime)GetValue(i)!;

    public decimal GetDecimal(int i) => (decimal)GetValue(i)!;

    public double GetDouble(int i) => (double)GetValue(i)!;

    public Type GetFieldType(int i) => _types[i];

    public float GetFloat(int i) => (float)GetValue(i)!;

    public Guid GetGuid(int i) => (Guid)GetValue(i)!;

    public short GetInt16(int i) => (short)GetValue(i)!;

    public int GetInt32(int i) => (int)GetValue(i)!;

    public long GetInt64(int i) => Convert.ToInt64(GetValue(i), System.Globalization.CultureInfo.InvariantCulture);

    public string GetName(int i) => _names[i];

    public int GetOrdinal(string name) => Array.IndexOf(_names, name);

    public string GetString(int i) => (string)GetValue(i)!;

    public object GetValue(int i) => _rows[_index][i] ?? DBNull.Value;

    public int GetValues(object[] values)
    {
        for (int i = 0; i < FieldCount; i++)
            values[i] = GetValue(i);
        return FieldCount;
    }

    public bool IsDBNull(int i) => _rows[_index][i] is null;

    public bool NextResult() => false;

    public bool Read()
    {
        _index++;
        return _index < _rows.Length;
    }

    public DataTable? GetSchemaTable() => throw new NotSupportedException();

    public IEnumerator GetEnumerator() => throw new NotSupportedException();
}
