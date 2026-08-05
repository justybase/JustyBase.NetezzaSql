using JustyBase.ImportExport.Import;
using JustyBase.NetezzaDriver;
using JustyBase.NetezzaDdl;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Shared helpers for environment-gated live Netezza tests (pipe import, smoke, round-trips).
/// Soft-skips when NZ_DEV_* is missing; pipe topology failures soft-skip unless NZ_REQUIRE_PIPE=1.
/// </summary>
internal static class NetezzaLiveTestHost
{
    public static string CreateLogDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "jb-nz-live", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static NetezzaImportUsingOptions DefaultPipeUsingOptions(string logDirectory, string? nullValue = "", bool crLfInString = true)
        => new()
        {
            Delimiter = "\\t",
            EncodingName = "utf-8",
            EscapeChar = "\\",
            MaxErrors = 0,
            NullValue = nullValue,
            CrInString = crLfInString,
            LfInString = crLfInString,
            LogDirectory = logDirectory,
            BoolStyle = "TRUE_FALSE"
        };

    public static async Task<bool> ExecutePipeInsertAsync(
        NzConnection connection,
        string sql,
        string pipeName,
        IReadOnlyList<string> lines)
    {
        async IAsyncEnumerable<string> Source()
        {
            foreach (string line in lines)
                yield return line;
            await Task.CompletedTask;
        }

        return await ExecutePipeInsertAsync(connection, sql, pipeName, Source());
    }

    public static async Task<bool> ExecutePipeInsertAsync(
        NzConnection connection,
        string sql,
        string pipeName,
        IAsyncEnumerable<string> lines)
    {
        var serve = NetezzaPipeImportExecutor.ServeRawLinesAsync(lines, pipeName);
        await Task.Delay(50);
        try
        {
            if (!await TryExecuteInsert(connection, sql))
            {
                await CancelServe(serve);
                return false;
            }

            await serve.WaitAsync(TimeSpan.FromSeconds(30));
            return true;
        }
        catch
        {
            await CancelServe(serve);
            throw;
        }
    }

    public static async Task<bool> TryExecuteInsert(NzConnection connection, string sql)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Execute(connection, sql);
                return true;
            }
            catch (Exception error) when (IsRetryableLiveError(error))
            {
                if (attempt >= 2)
                {
                    if (RequirePipe() || IsTransportError(error))
                        throw;
                    Console.WriteLine($"Live pipe test soft-skipped (set NZ_REQUIRE_PIPE=1 to fail): {error.Message.Trim()}");
                    return false;
                }

                // The XferTable pipe handshake and the connection transport are flaky on some
                // topologies ("Error opening file" / read timeouts when the server cannot reach
                // the client or is momentarily busy); retry once before deciding.
                Console.WriteLine($"Live test retry (attempt {attempt}): {error.Message.Trim()}");
                await Task.Delay(2_000).ConfigureAwait(false);
            }
        }
    }

    public static bool IsRetryableLiveError(Exception error)
        => IsPipeTopologyError(error) || IsTransportError(error);

    public static bool IsPipeTopologyError(Exception error)
        => error.Message.Contains("Relative path not allowed", StringComparison.OrdinalIgnoreCase)
           || error.Message.Contains("named pipe", StringComparison.OrdinalIgnoreCase)
           || error.Message.Contains("Error opening file", StringComparison.OrdinalIgnoreCase);

    public static bool IsTransportError(Exception error)
        => error.Message.Contains("Unable to read data from the transport connection", StringComparison.OrdinalIgnoreCase)
           || error is IOException
           || error.InnerException is System.Net.Sockets.SocketException;

    public static bool RequirePipe()
        => string.Equals(Environment.GetEnvironmentVariable("NZ_REQUIRE_PIPE"), "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Environment.GetEnvironmentVariable("NZ_REQUIRE_PIPE"), "true", StringComparison.OrdinalIgnoreCase);

    public static async Task CancelServe(Task serve)
    {
        try
        {
            await serve.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            /* pipe abandoned / timed out waiting for driver connect */
        }
    }

    public static bool TryCreateConnection(out NzConnection? connection)
    {
        string? host = Environment.GetEnvironmentVariable("NZ_DEV_HOST");
        string? database = Environment.GetEnvironmentVariable("NZ_DEV_DATABASE");
        string? user = Environment.GetEnvironmentVariable("NZ_DEV_USER");
        string? password = Environment.GetEnvironmentVariable("NZ_DEV_PASSWORD");
        int port = int.TryParse(Environment.GetEnvironmentVariable("NZ_DEV_PORT"), out int parsed) ? parsed : 5480;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("Live import test not executed: set NZ_DEV_HOST, NZ_DEV_DATABASE, NZ_DEV_USER and NZ_DEV_PASSWORD.");
            connection = null;
            return false;
        }
        connection = new NzConnection(user, password, host, database, port);
        return true;
    }

    public static void Execute(NzConnection connection, string sql)
    {
        using var command = connection.CreateCommand(sql);
        command.ExecuteNonQuery();
    }

    public static object? ExecuteScalar(NzConnection connection, string sql)
    {
        using var command = connection.CreateCommand(sql);
        return command.ExecuteScalar();
    }

    public static List<object?[]> ExecuteReaderRows(NzConnection connection, string sql, int fieldCount)
    {
        using var command = connection.CreateCommand(sql);
        using var reader = command.ExecuteReader();
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var cells = new object?[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                cells[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(cells);
        }
        return rows;
    }

    public static void TryDrop(NzConnection connection, string table)
    {
        try { Execute(connection, $"DROP TABLE {table}"); }
        catch { }
    }

    public static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch { }
    }
}
