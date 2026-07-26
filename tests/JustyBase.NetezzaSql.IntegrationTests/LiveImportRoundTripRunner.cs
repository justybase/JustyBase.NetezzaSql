using JustyBase.ImportExport.Import;
using JustyBase.NetezzaDdl;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>One CSV → infer → CREATE → pipe INSERT → SELECT proof case.</summary>
public sealed record LiveImportCase(
    string Name,
    string CsvText,
    IReadOnlyList<string> ColumnNames,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> ExpectedRows,
    IReadOnlyDictionary<string, string>? ExpectedInferredTypes = null,
    CsvImportOptions? CsvOptions = null,
    int VarcharLength = 255,
    string? NullValue = "",
    bool MapVarcharToNvarchar = true);

/// <summary>
/// Runs the shared host import path against a live Netezza connection:
/// parse CSV → <see cref="DatabaseTypeChooser.Infer"/> → CREATE TABLE → pipe INSERT → SELECT equality.
/// </summary>
internal static class LiveImportRoundTripRunner
{
    public static async Task RunAsync(LiveImportCase importCase)
    {
        ArgumentNullException.ThrowIfNull(importCase);
        if (!NetezzaLiveTestHost.TryCreateConnection(out var connection) || connection is null)
            return;

        await using (connection)
        {
            connection.Open();

            var dataRows = await ParseDataRowsAsync(importCase);
            Assert.Equal(importCase.ExpectedRows.Count, dataRows.Count);

            var detected = DatabaseTypeChooser.Infer(importCase.ColumnNames, dataRows, importCase.VarcharLength);
            Assert.Equal(importCase.ColumnNames.Count, detected.Count);

            if (importCase.ExpectedInferredTypes is not null)
            {
                foreach ((string column, string expectedType) in importCase.ExpectedInferredTypes)
                {
                    DetectedColumn col = detected.Single(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));
                    Assert.Equal(expectedType, col.NetezzaType);
                }
            }

            string[] ddlColumns = detected
                .Select(c => $"{NetezzaNameHelper.QuoteNameIfNeeded(c.Name)} {MapTypeForLiveDdl(c.NetezzaType, importCase.MapVarcharToNvarchar)}")
                .ToArray();
            string[] externalColumns = detected
                .Select(c => $"{NetezzaNameHelper.QuoteNameIfNeeded(c.Name)} {MapTypeForLiveDdl(c.NetezzaType, importCase.MapVarcharToNvarchar)}")
                .ToArray();

            string table = "JB_INF_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            string pipe = NetezzaPipeImportExecutor.CreatePipeName("jb_inf");
            string logDir = NetezzaLiveTestHost.CreateLogDirectory();
            try
            {
                NetezzaLiveTestHost.Execute(connection, NetezzaImportSql.CreateRandomDistributionTable(table, ddlColumns));

                var options = NetezzaLiveTestHost.DefaultPipeUsingOptions(logDir, importCase.NullValue);
                string insert = NetezzaImportEngine.BuildInsertSql(table, pipe, externalColumns, options);

                var escapeChars = System.Buffers.SearchValues.Create(['\\', '\t', '\n', '\r']);
                var pipeLines = dataRows
                    .Select(row =>
                    {
                        var cells = new string[importCase.ColumnNames.Count];
                        for (int i = 0; i < importCase.ColumnNames.Count; i++)
                        {
                            string? raw = i < row.Count ? row[i] : null;
                            if (raw is null)
                            {
                                cells[i] = importCase.NullValue ?? string.Empty;
                                continue;
                            }

                            cells[i] = NetezzaPipeImportExecutor.Sanitize(
                                raw,
                                escapeChars,
                                "\\\\",
                                '\t',
                                "\\\t",
                                "\\\n");
                        }

                        return string.Join('\t', cells);
                    })
                    .ToArray();

                if (!await NetezzaLiveTestHost.ExecutePipeInsertAsync(connection, insert, pipe, pipeLines))
                    return;

                long count = Convert.ToInt64(NetezzaLiveTestHost.ExecuteScalar(connection, $"SELECT COUNT(*) FROM {table}"));
                Assert.Equal(importCase.ExpectedRows.Count, count);

                string orderBy = NetezzaNameHelper.QuoteNameIfNeeded(importCase.ColumnNames[0]);
                string selectList = string.Join(", ", importCase.ColumnNames.Select(NetezzaNameHelper.QuoteNameIfNeeded));
                var actualRows = NetezzaLiveTestHost.ExecuteReaderRows(
                    connection,
                    $"SELECT {selectList} FROM {table} ORDER BY {orderBy}",
                    importCase.ColumnNames.Count);

                Assert.Equal(importCase.ExpectedRows.Count, actualRows.Count);
                for (int r = 0; r < importCase.ExpectedRows.Count; r++)
                {
                    IReadOnlyDictionary<string, string?> expected = importCase.ExpectedRows[r];
                    object?[] actual = actualRows[r];
                    for (int c = 0; c < importCase.ColumnNames.Count; c++)
                    {
                        string name = importCase.ColumnNames[c];
                        expected.TryGetValue(name, out string? expectedValue);
                        string? actualValue = NormalizeCell(actual[c]);
                        Assert.True(
                            CellsEqual(expectedValue, actualValue, detected[c].NetezzaType),
                            $"Case '{importCase.Name}' row {r} column '{name}': expected '{expectedValue}' actual '{actualValue}' (inferred {detected[c].NetezzaType}).");
                    }
                }
            }
            finally
            {
                NetezzaLiveTestHost.TryDrop(connection, table);
                NetezzaLiveTestHost.TryDeleteDirectory(logDir);
            }
        }
    }

    /// <summary>
    /// Live DDL mapping only: VARCHAR → NVARCHAR for Unicode-safe round-trips without changing production Infer.
    /// </summary>
    internal static string MapTypeForLiveDdl(string inferredType, bool mapVarcharToNvarchar)
    {
        if (!mapVarcharToNvarchar)
            return inferredType;
        if (inferredType.StartsWith("VARCHAR(", StringComparison.OrdinalIgnoreCase))
            return "N" + inferredType;
        return inferredType;
    }

    private static async Task<List<IReadOnlyList<string?>>> ParseDataRowsAsync(LiveImportCase importCase)
    {
        var csvOptions = importCase.CsvOptions ?? new CsvImportOptions(HasHeader: true, NullValue: importCase.NullValue);
        using var reader = new StringReader(importCase.CsvText);
        var rows = new List<IReadOnlyList<string?>>();
        await foreach (var row in FastCsvImportEngine.ReadAsync(reader, csvOptions))
            rows.Add(row);

        // When HasHeader is true, ReadAsync already skipped the header record.
        // When HasHeader is false, ColumnNames come from the case and every row is data.
        return rows;
    }

    private static string? NormalizeCell(object? value)
    {
        if (value is null or DBNull)
            return null;
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool CellsEqual(string? expected, string? actual, string inferredType)
    {
        if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(actual))
            return true;
        if (expected is null || actual is null)
            return expected == actual;

        if (inferredType.Equals("BOOLEAN", StringComparison.OrdinalIgnoreCase))
        {
            bool.TryParse(expected, out bool eBool);
            // Driver may return t/f, True/False, 1/0.
            string a = actual.Trim();
            bool aBool = a is "1" or "t" or "T" or "true" or "True" or "TRUE"
                || (bool.TryParse(a, out bool parsed) && parsed);
            return eBool == aBool;
        }

        if (inferredType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(expected, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long eInt)
            && long.TryParse(actual, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long aInt))
            return eInt == aInt;

        if (inferredType.StartsWith("NUMERIC", StringComparison.OrdinalIgnoreCase)
            && decimal.TryParse(expected, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal eDec)
            && decimal.TryParse(actual, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal aDec))
            return eDec == aDec;

        if (inferredType.Equals("DATETIME", StringComparison.OrdinalIgnoreCase)
            && DateTime.TryParse(expected, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out DateTime eDt)
            && DateTime.TryParse(actual, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out DateTime aDt))
            return eDt == aDt;

        return string.Equals(expected, actual, StringComparison.Ordinal);
    }
}
