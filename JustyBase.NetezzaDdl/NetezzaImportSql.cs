namespace JustyBase.NetezzaDdl;

/// <summary>Builds Netezza SQL used by streaming-import adapters.</summary>
public static class NetezzaImportSql
{
    public static string CreateRandomDistributionTable(string tableName, IReadOnlyList<string> columnDefinitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(columnDefinitions);
        if (columnDefinitions.Count == 0 || columnDefinitions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one column definition is required.", nameof(columnDefinitions));

        return $"CREATE TABLE {NetezzaNameHelper.QuoteNameIfNeeded(tableName)} ({string.Join(",", columnDefinitions)}){Environment.NewLine}DISTRIBUTE ON RANDOM;{Environment.NewLine}{Environment.NewLine}";
    }

    public static string InsertFromExternalPipe(string tableName, string pipeName, IReadOnlyList<string> columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0 || columns.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one column is required.", nameof(columns));

        return $"INSERT INTO {NetezzaNameHelper.QuoteNameIfNeeded(tableName)} SELECT * FROM EXTERNAL '\\\\.\\pipe\\{NetezzaNameHelper.EscapeLiteral(pipeName)}' ({string.Join(',', columns)}) ";
    }

    public static string InsertSameAsFromExternalPipe(string tableName, string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        string cleanTable = NetezzaNameHelper.QuoteNameIfNeeded(tableName);
        return $"INSERT INTO {cleanTable} SELECT * FROM EXTERNAL '\\\\.\\pipe\\{NetezzaNameHelper.EscapeLiteral(pipeName)}' SAMEAS {cleanTable} ";
    }
}
