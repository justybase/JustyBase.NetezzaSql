using JustyBase.ImportExport.Import;
using JustyBase.NetezzaDdl;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Environment-gated end-to-end coverage for the shared import contract.
/// Soft-skip when NZ_DEV_* is missing. Pipe topology failures soft-skip unless
/// NZ_REQUIRE_PIPE=1 (strict local/pipe-capable setups).
/// </summary>
public sealed class NetezzaLiveImportTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Typed_pipe_create_insert_preserves_escaped_values()
    {
        if (!NetezzaLiveTestHost.TryCreateConnection(out var connection) || connection is null)
            return;

        await using (connection)
        {
            connection.Open();
            string table = "JB_CORE_PIPE_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            string pipe = NetezzaPipeImportExecutor.CreatePipeName("jb_core");
            string logDir = NetezzaLiveTestHost.CreateLogDirectory();
            try
            {
                NetezzaLiveTestHost.Execute(connection, NetezzaImportSql.CreateRandomDistributionTable(table, ["ID INTEGER", "TXT NVARCHAR(200)"]));
                var options = NetezzaLiveTestHost.DefaultPipeUsingOptions(logDir);
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
                if (!await NetezzaLiveTestHost.ExecutePipeInsertAsync(connection, insert, pipe, ["1\talpha", "2\t" + field]))
                    return;

                Assert.Equal(2L, Convert.ToInt64(NetezzaLiveTestHost.ExecuteScalar(connection, $"SELECT COUNT(*) FROM {table}")));
                Assert.Equal("contains\tdelimiter\nvalue", Convert.ToString(NetezzaLiveTestHost.ExecuteScalar(connection, $"SELECT TXT FROM {table} WHERE ID = 2")));
            }
            finally
            {
                NetezzaLiveTestHost.TryDrop(connection, table);
                NetezzaLiveTestHost.TryDeleteDirectory(logDir);
            }
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task SameAs_pipe_import_uses_existing_table_shape()
    {
        if (!NetezzaLiveTestHost.TryCreateConnection(out var connection) || connection is null)
            return;

        await using (connection)
        {
            connection.Open();
            string table = "JB_CORE_SAMEAS_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            string pipe = NetezzaPipeImportExecutor.CreatePipeName("jb_sameas");
            string logDir = NetezzaLiveTestHost.CreateLogDirectory();
            try
            {
                NetezzaLiveTestHost.Execute(connection, NetezzaImportSql.CreateRandomDistributionTable(table, ["ID INTEGER", "TXT NVARCHAR(200)"]));
                NetezzaLiveTestHost.Execute(connection, $"INSERT INTO {table} VALUES (1, 'seed')");
                string insert = NetezzaImportEngine.BuildInsertSql(table, pipe, [], new NetezzaImportUsingOptions
                {
                    Delimiter = "\\t",
                    EncodingName = "utf-8",
                    MaxErrors = 0,
                    LogDirectory = logDir
                }, sameAs: true);
                if (!await NetezzaLiveTestHost.ExecutePipeInsertAsync(connection, insert, pipe, ["2\tfrom sameas"]))
                    return;
                Assert.Equal(2L, Convert.ToInt64(NetezzaLiveTestHost.ExecuteScalar(connection, $"SELECT COUNT(*) FROM {table}")));
            }
            finally
            {
                NetezzaLiveTestHost.TryDrop(connection, table);
                NetezzaLiveTestHost.TryDeleteDirectory(logDir);
            }
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Fast_raw_pipe_import_loads_filtered_lines()
    {
        if (!NetezzaLiveTestHost.TryCreateConnection(out var connection) || connection is null)
            return;

        await using (connection)
        {
            connection.Open();
            string table = "JB_CORE_FAST_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            string pipe = NetezzaPipeImportExecutor.CreatePipeName("jb_fast");
            string logDir = NetezzaLiveTestHost.CreateLogDirectory();
            try
            {
                NetezzaLiveTestHost.Execute(connection, NetezzaImportSql.CreateRandomDistributionTable(table, ["LINE NVARCHAR(200)"]));
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
                    if (!await NetezzaLiveTestHost.TryExecuteInsert(connection, insert))
                    {
                        await NetezzaLiveTestHost.CancelServe(serve);
                        return;
                    }

                    await serve.WaitAsync(TimeSpan.FromSeconds(30));
                    Assert.Equal(2L, Convert.ToInt64(NetezzaLiveTestHost.ExecuteScalar(connection, $"SELECT COUNT(*) FROM {table}")));
                }
                catch
                {
                    await NetezzaLiveTestHost.CancelServe(serve);
                    throw;
                }
            }
            finally
            {
                NetezzaLiveTestHost.TryDrop(connection, table);
                NetezzaLiveTestHost.TryDeleteDirectory(logDir);
            }
        }
    }
}
