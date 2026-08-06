using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace JustyBase.Netezza.Tests;

/// <summary>
/// In-memory <see cref="DbConnection"/> that serves scripted catalog rows based on the SQL text
/// the loader issues. Rows are matched by marker fragments (<c>_V_OBJECT_DATA</c>, <c>_V_RELATION_COLUMN</c>,
/// <c>_v_database</c>, <c>_V_PROCEDURE</c>) so the loader's stage selection is exercised.
/// </summary>
internal sealed class FakeCatalogConnection : DbConnection
{
    private readonly IReadOnlyList<object?[]> _objectRows;
    private readonly IReadOnlyList<object?[]> _columnRows;
    private readonly IReadOnlyList<object?[]> _databaseRows;
    private readonly IReadOnlyList<object?[]> _procedureRows;
    private readonly string? _failMarker;
    private ConnectionState _state = ConnectionState.Closed;

    public FakeCatalogConnection(
        IEnumerable<object?[]>? objectRows = null,
        IEnumerable<object?[]>? columnRows = null,
        IEnumerable<object?[]>? databaseRows = null,
        IEnumerable<object?[]>? procedureRows = null,
        string? failMarker = null)
    {
        _objectRows = objectRows?.ToList() ?? [];
        _columnRows = columnRows?.ToList() ?? [];
        _databaseRows = databaseRows?.ToList() ?? [];
        _procedureRows = procedureRows?.ToList() ?? [];
        _failMarker = failMarker;
    }

    [AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "FAKE";
    public override string DataSource => "fake";
    public override string ServerVersion => "1.0.0";
    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

    public override void Close() => _state = ConnectionState.Closed;

    public override void Open() => _state = ConnectionState.Open;

    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        _state = ConnectionState.Open;
        await Task.CompletedTask;
    }

    public override Task CloseAsync() => Task.CompletedTask;

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException();

    protected override DbCommand CreateDbCommand() => new FakeCatalogCommand(this);

    internal DbDataReader CreateReader(string commandText)
    {
        if (_failMarker is not null && commandText.Contains(_failMarker, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"scripted failure for marker {_failMarker}");
        }

        IReadOnlyList<object?[]>? rows = null;
        if (commandText.Contains("_V_RELATION_COLUMN", StringComparison.OrdinalIgnoreCase))
        {
            rows = _columnRows;
        }
        else if (commandText.Contains("_V_OBJECT_DATA", StringComparison.OrdinalIgnoreCase))
        {
            rows = _objectRows;
        }
        else if (commandText.Contains("_v_database", StringComparison.OrdinalIgnoreCase))
        {
            rows = _databaseRows;
        }
        else if (commandText.Contains("_V_PROCEDURE", StringComparison.OrdinalIgnoreCase))
        {
            rows = _procedureRows;
        }

        if (rows is null)
        {
            throw new InvalidOperationException($"no scripted rows for SQL: {commandText}");
        }

        return new FakeCatalogReader(rows);
    }

    private sealed class FakeCatalogCommand(FakeCatalogConnection connection) : DbCommand
    {
        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        [AllowNull]
        protected override DbConnection DbConnection { get; set; }

        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();

        public override void Cancel() { }

        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => connection.CreateReader(CommandText);

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(connection.CreateReader(CommandText));
        }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare() { }
    }

    private sealed class FakeCatalogReader(IReadOnlyList<object?[]> rows) : DbDataReader
    {
        private int _index = -1;

        public override int FieldCount => rows.FirstOrDefault()?.Length ?? 0;
        public override bool HasRows => rows.Count > 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override object this[int ordinal] => rows[_index][ordinal]!;
        public override object this[string name] => throw new NotSupportedException();
        public override int Depth => 0;

        public override bool Read() => ++_index < rows.Count;

        public override bool NextResult() => false;

        public override void Close() { }

        public override string GetName(int ordinal) => $"col{ordinal}";

        public override int GetOrdinal(string name) => throw new NotSupportedException();

        public override string GetDataTypeName(int ordinal) => "object";

        public override Type GetFieldType(int ordinal) => typeof(object);

        public override object GetValue(int ordinal) => rows[_index][ordinal] ?? DBNull.Value;

        public override int GetValues(object[] values)
        {
            int count = Math.Min(FieldCount, values.Length);
            for (int i = 0; i < count; i++)
            {
                values[i] = GetValue(i);
            }

            return count;
        }

        public override bool GetBoolean(int ordinal) => Convert.ToBoolean(GetValue(ordinal));

        public override byte GetByte(int ordinal) => Convert.ToByte(GetValue(ordinal));

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        public override char GetChar(int ordinal) => Convert.ToChar(GetValue(ordinal));

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        public override DateTime GetDateTime(int ordinal) => Convert.ToDateTime(GetValue(ordinal));

        public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetValue(ordinal));

        public override double GetDouble(int ordinal) => Convert.ToDouble(GetValue(ordinal));

        public override float GetFloat(int ordinal) => Convert.ToSingle(GetValue(ordinal));

        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);

        public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal));

        public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal));

        public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal));

        public override string GetString(int ordinal) => Convert.ToString(GetValue(ordinal))!;

        public override bool IsDBNull(int ordinal) => GetValue(ordinal) is null or DBNull;

        public override System.Collections.IEnumerator GetEnumerator() => throw new NotSupportedException();
    }
}
