using JustyBase.ImportExport.Import.TypeChooser;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Batch facade over per-column <see cref="NetezzaColumnTypeChooser"/> instances (the import
/// type-inference SoT, ported from vscode). Hosts feed raw string values column by column
/// (<see cref="AddValue"/>) or pre-classified kinds (<see cref="AddCell"/>), then call
/// <see cref="Choose"/>. The <c>_#TEXT</c>/<c>_#NUMERIC</c>/<c>_#INTEGER</c>/<c>_#DATE</c>/
/// <c>_#TIMESTAMP</c> header suffixes remain supported as explicit overrides on top of the
/// inferred type, and header tokens (PESEL/NRB/IBAN/BAN) force NVARCHAR per the vscode heuristics.
/// </summary>
public sealed class ImportTypeAnalyzer
{
    public const int DefaultNvarcharLength = 255;
    private const int DefaultForcedNumericPrecision = 20;
    private const int DefaultForcedNumericScale = 6;

    private readonly int _columnCount;
    private readonly string _decimalDelimiter;
    private readonly bool _inferBoolean;
    private readonly NetezzaColumnTypeChooser?[] _choosers;
    private readonly int[] _rawMaxLengths;
    private readonly bool[] _hasNull;
    private readonly string?[] _seenColumnNames;

    public ImportTypeAnalyzer(int columnCount, string decimalDelimiter = ".", bool inferBoolean = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnCount);
        _columnCount = columnCount;
        _decimalDelimiter = decimalDelimiter;
        _inferBoolean = inferBoolean;
        _choosers = new NetezzaColumnTypeChooser[columnCount];
        _rawMaxLengths = new int[columnCount];
        _hasNull = new bool[columnCount];
        _seenColumnNames = new string?[columnCount];
    }

    public int ColumnCount => _columnCount;

    /// <summary>
    /// Feeds one raw CSV/text value. Column-level <c>treatAllColumnsAsText</c> and Pesel/Regon
    /// column names force NVARCHAR for that column; otherwise the monotonic vscode chooser decides.
    /// </summary>
    public void AddValue(int column, ReadOnlySpan<char> value, string? columnName = null, bool treatAllColumnsAsText = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        if (column >= _columnCount)
            throw new ArgumentOutOfRangeException(nameof(column));

        _rawMaxLengths[column] = Math.Max(_rawMaxLengths[column], value.Length);
        if (_seenColumnNames[column] is null && !string.IsNullOrEmpty(columnName))
            _seenColumnNames[column] = columnName;

        if (value.Length == 0)
        {
            _hasNull[column] = true;
            return;
        }

        _choosers[column] ??= new NetezzaColumnTypeChooser(
            _decimalDelimiter,
            new ColumnTypeChooserOptions(
                ForceText: treatAllColumnsAsText || CsvCellTypeResolver.IsTextColumnName(columnName ?? string.Empty),
                InferBoolean: _inferBoolean));

        _choosers[column]!.RefreshCurrentType(value.ToString());
    }

    /// <summary>Feeds a pre-classified cell (Excel/typed path) via a canonical string value.</summary>
    public void AddCell(int column, ImportColumnKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        if (column >= _columnCount)
            throw new ArgumentOutOfRangeException(nameof(column));
        if (kind == ImportColumnKind.NoInfo)
            return;

        _choosers[column] ??= new NetezzaColumnTypeChooser(
            _decimalDelimiter,
            new ColumnTypeChooserOptions(InferBoolean: _inferBoolean));
        _choosers[column]!.RefreshCurrentType(CanonicalStringForKind(kind));
    }

    public IReadOnlyList<DetectedImportColumnType> Choose(
        IReadOnlyList<string>? columnNames = null,
        int defaultNvarcharLength = DefaultNvarcharLength)
    {
        if (defaultNvarcharLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(defaultNvarcharLength));

        var result = new DetectedImportColumnType[_columnCount];
        for (int column = 0; column < _columnCount; column++)
        {
            string name = columnNames is not null && column < columnNames.Count
                ? columnNames[column]
                : _seenColumnNames[column] ?? string.Empty;
            result[column] = ChooseColumn(column, name, defaultNvarcharLength);
        }
        return result;
    }

    private DetectedImportColumnType ChooseColumn(int column, string name, int defaultNvarcharLength)
    {
        bool nullable = _hasNull[column];

        if (name.EndsWith("_#TEXT", StringComparison.Ordinal))
            return Nvarchar(GetTextLength(column, defaultNvarcharLength), nullable);
        if (name.EndsWith("_#NUMERIC", StringComparison.Ordinal))
            return new DetectedImportColumnType(ImportColumnKind.Numeric, DefaultForcedNumericPrecision, DefaultForcedNumericScale, nullable);
        if (name.EndsWith("_#INTEGER", StringComparison.Ordinal))
            return new DetectedImportColumnType(ImportColumnKind.Integer, IsNullable: nullable);
        if (name.EndsWith("_#DATE", StringComparison.Ordinal))
            return new DetectedImportColumnType(ImportColumnKind.Date, IsNullable: nullable);
        if (name.EndsWith("_#TIMESTAMP", StringComparison.Ordinal))
            return new DetectedImportColumnType(ImportColumnKind.TimeStamp, IsNullable: nullable);

        if (ImportTypeInferenceUtils.HeaderForcesTextImportType(name))
            return Nvarchar(GetTextLength(column, defaultNvarcharLength), nullable);

        NetezzaColumnTypeChooser? chooser = _choosers[column];
        if (chooser is null)
            return Nvarchar(defaultNvarcharLength, nullable);

        return ToDetected(chooser.CurrentType, nullable, defaultNvarcharLength);
    }

    private static DetectedImportColumnType ToDetected(DatabaseImportDataType type, bool nullable, int defaultNvarcharLength)
    {
        return type.DbType switch
        {
            "BIGINT" => new DetectedImportColumnType(ImportColumnKind.Integer, IsNullable: nullable),
            "NUMERIC" => new DetectedImportColumnType(
                ImportColumnKind.Numeric,
                type.Precision ?? 0,
                type.Scale ?? 0,
                nullable),
            "DATE" => new DetectedImportColumnType(ImportColumnKind.Date, IsNullable: nullable),
            "DATETIME" => new DetectedImportColumnType(ImportColumnKind.TimeStamp, IsNullable: nullable),
            "BOOLEAN" => new DetectedImportColumnType(ImportColumnKind.Boolean, IsNullable: nullable),
            _ => Nvarchar(type.Length ?? defaultNvarcharLength, nullable)
        };
    }

    private int GetTextLength(int column, int defaultNvarcharLength)
    {
        int detected = _choosers[column] is { CurrentType.Length: int len } ? len : 0;
        return Math.Max(defaultNvarcharLength, Math.Max(detected, _rawMaxLengths[column]));
    }

    private static DetectedImportColumnType Nvarchar(int length, bool nullable)
        => new(ImportColumnKind.Nvarchar, Math.Max(1, length), IsNullable: nullable);

    private static string CanonicalStringForKind(ImportColumnKind kind) => kind switch
    {
        ImportColumnKind.Integer => "1",
        ImportColumnKind.Numeric => "1.5",
        ImportColumnKind.Date => "2024-01-01",
        ImportColumnKind.TimeStamp => "2024-01-01 12:00:00",
        ImportColumnKind.Boolean => "true",
        ImportColumnKind.Nvarchar => "x",
        _ => "1"
    };
}
