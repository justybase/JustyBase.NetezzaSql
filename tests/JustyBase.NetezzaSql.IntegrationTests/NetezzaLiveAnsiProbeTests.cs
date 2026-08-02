using JustyBase.NetezzaDriver;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Read-only probes for syntax whose support is release-dependent in Netezza.
/// </summary>
public sealed class NetezzaLiveAnsiProbeTests
{
    [Fact]
    [Trait("Category", "Live")]
    public void LiveSyntax_ConfirmsNetezzaOffsetAndLimitSupport()
    {
        if (!NetezzaLiveTestHost.TryCreateConnection(out var connection) || connection is null)
            return;

        using (connection)
        {
            connection.Open();

            Assert.Equal(1L, ExecuteScalar(connection, "SELECT 1 OFFSET 0"));
            Assert.Equal(1L, ExecuteScalar(connection, "SELECT 1 LIMIT 1 OFFSET 0"));
            Assert.Throws<NetezzaException>(() => ExecuteScalar(connection, "SELECT 1 FETCH FIRST 1 ROW ONLY"));
            Assert.Throws<NetezzaException>(() => ExecuteScalar(connection, "SELECT 1 OFFSET 0 ROWS FETCH FIRST 1 ROW ONLY"));
            Assert.Throws<NetezzaException>(() => ExecuteScalar(connection, "SELECT 1 OFFSET 0 ROWS FETCH NEXT 1 ROW ONLY"));
        }
    }

    private static long ExecuteScalar(NzConnection connection, string sql)
    {
        using var command = connection.CreateCommand(sql);
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
