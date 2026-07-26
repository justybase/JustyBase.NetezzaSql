using System.IO.Pipes;
using System.Text;
using JustyBase.ImportExport.Import;
using JustyBase.NetezzaDriver;
using JustyBase.NetezzaDdl;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Environment-gated end-to-end coverage for the shared import contract.
/// Soft-skip when NZ_DEV_* is missing. Pipe topology failures soft-skip unless
/// NZ_REQUIRE_PIPE=1 (strict local/pipe-capable setups).
/// </summary>
public sealed class NetezzaLiveImportTests
{
    private static string CreateLogDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "jb-nz-live", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Typed_pipe_create_insert_preserves_escaped_values()
    {
        if (!TryCreateConnection(out NzConnection? connection) || connection is null)
            return;

        await using (connection)
        {
            connection.Open();
            string table = "JB_CORE_PIPE_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            string pipe = NetezzaPipeImportExecutor.CreatePipeName("jb_core");
            string logDir = CreateLogDirectory();
            try
            {
                Execute(connection, NetezzaImportSql.CreateRandomDistributionTable(table, ["ID INTEGER", "TXT NVARCHAR(200)"]));
                var options = new NetezzaImportUsingOptions
                {
                    Delimiter = "\\t",
                    EncodingName = "utf-8",
                    EscapeChar = "\\",
                    MaxErrors = 0,
                    NullValue = "",
                    CrInString = true,
                    LfInString = true,
                    LogDirectory = logDir
                };
                string insert = NetezzaImportEngine.BuildInsertSql(table, pipe, ["ID INTEGER", "TXT NVARCHAR(200)"], options);
                // Pipe escaping SoT (same as ServeDataReader): '\' + real delimiter/newline bytes.
                var escapeChars = System.Buffers.SearchValues.Create(['\\', '\t', '\n', '\r']);
                string field = NetezzaPipeImportExecutor.Sanitize(
                    "contains\tdelimiter\nvalue",
                    escapeChars,
                    "\\\\",
                    '\t',
                    "\\\t",
                    "\\\n");
                if (!await ExecutePipeInsertAsync(connection, insert, pipe, ["1\talpha", "2\t" + field]))
                    return;

                Assert.Equal(2L, Convert.ToInt64(ExecuteScalar(connection, $"SELECT COUNT(*) FROM {table}")));
                Assert.Equal("contains\tdelimiter\nvalue", Convert.ToString(ExecuteScalar(connection, $"SELECT TXT FROM {table} WHERE ID = 2")));
            }
            finally
            {
                TryDrop(connection, table);
                TryDeleteDirectory(logDir);
            }
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task SameAs_pipe_import_uses_existing_table_shape()
    {
        if (!TryCreateConnection(out NzConnection? connection) || connection is null)
            return;

        await using (connection)
        {
            connection.Open();
            string table = "JB_CORE_SAMEAS_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            string pipe = NetezzaPipeImportExecutor.CreatePipeName("jb_sameas");
            string logDir = CreateLogDirectory();
            try
            {
                Execute(connection, NetezzaImportSql.CreateRandomDistributionTable(table, ["ID INTEGER", "TXT NVARCHAR(200)"]));
                Execute(connection, $"INSERT INTO {table} VALUES (1, 'seed')");
                string insert = NetezzaImportEngine.BuildInsertSql(table, pipe, [], new NetezzaImportUsingOptions
                {
                    Delimiter = "\\t",
                    EncodingName = "utf-8",
                    MaxErrors = 0,
                    LogDirectory = logDir
                }, sameAs: true);
                if (!await ExecutePipeInsertAsync(connection, insert, pipe, ["2\tfrom sameas"]))
                    return;
                Assert.Equal(2L, Convert.ToInt64(ExecuteScalar(connection, $"SELECT COUNT(*) FROM {table}")));
            }
            finally
            {
                TryDrop(connection, table);
                TryDeleteDirectory(logDir);
            }
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Fast_raw_pipe_import_loads_filtered_lines()
    {
        if (!TryCreateConnection(out NzConnection? connection) || connection is null)
            return;

        await using (connection)
        {
            connection.Open();
            string table = "JB_CORE_FAST_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            string pipe = NetezzaPipeImportExecutor.CreatePipeName("jb_fast");
            string logDir = CreateLogDirectory();
            try
            {
                Execute(connection, NetezzaImportSql.CreateRandomDistributionTable(table, ["LINE NVARCHAR(200)"]));
                string insert = NetezzaImportEngine.BuildInsertSql(
                    table,
                    pipe,
                    ["LINE NVARCHAR(200)"],
                    new NetezzaImportUsingOptions
                    {
                        Delimiter = ",",
                        EncodingName = "utf-8",
                        MaxErrors = 0,
                        SkipRows = 0,
                        LogDirectory = logDir
                    });

                async IAsyncEnumerable<string> Lines()
                {
                    await foreach (string line in FastCsvImportEngine.ReadRawAsync(
                                       new StringReader("keep-a\nskip-b\nkeep-c\n"),
                                       new FastCsvRawOptions(HasHeader: false, FilterPattern: "^keep")))
                        yield return line;
                }

                var serve = NetezzaPipeImportExecutor.ServeRawLinesAsync(Lines(), pipe);
                await Task.Delay(50);
                try
                {
                    if (!await TryExecuteInsert(connection, insert))
                    {
                        await CancelServe(serve);
                        return;
                    }

                    await serve.WaitAsync(TimeSpan.FromSeconds(30));
                    Assert.Equal(2L, Convert.ToInt64(ExecuteScalar(connection, $"SELECT COUNT(*) FROM {table}")));
                }
                catch
                {
                    await CancelServe(serve);
                    throw;
                }
            }
            finally
            {
                TryDrop(connection, table);
                TryDeleteDirectory(logDir);
            }
        }
    }

    private static async Task<bool> ExecutePipeInsertAsync(NzConnection connection, string sql, string pipeName, IReadOnlyList<string> lines)
    {
        async IAsyncEnumerable<string> Source()
        {
            foreach (string line in lines)
                yield return line;
            await Task.CompletedTask;
        }

        var serve = NetezzaPipeImportExecutor.ServeRawLinesAsync(Source(), pipeName);
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

    private static async Task<bool> TryExecuteInsert(NzConnection connection, string sql)
    {
        try
        {
            Execute(connection, sql);
            return true;
        }
        catch (Exception error) when (IsPipeTopologyError(error))
        {
            if (RequirePipe())
                throw;
            Console.WriteLine($"Live pipe test soft-skipped (set NZ_REQUIRE_PIPE=1 to fail): {error.Message.Trim()}");
            return false;
        }
    }

    private static bool IsPipeTopologyError(Exception error)
        => error.Message.Contains("Relative path not allowed", StringComparison.OrdinalIgnoreCase)
           || error.Message.Contains("named pipe", StringComparison.OrdinalIgnoreCase);

    private static bool RequirePipe()
        => string.Equals(Environment.GetEnvironmentVariable("NZ_REQUIRE_PIPE"), "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Environment.GetEnvironmentVariable("NZ_REQUIRE_PIPE"), "true", StringComparison.OrdinalIgnoreCase);

    private static async Task CancelServe(Task serve)
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

    private static bool TryCreateConnection(out NzConnection? connection)
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

    private static void Execute(NzConnection connection, string sql)
    {
        using var command = connection.CreateCommand(sql);
        command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(NzConnection connection, string sql)
    {
        using var command = connection.CreateCommand(sql);
        return command.ExecuteScalar();
    }

    private static void TryDrop(NzConnection connection, string table)
    {
        try { Execute(connection, $"DROP TABLE {table}"); }
        catch { }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch { }
    }
}
