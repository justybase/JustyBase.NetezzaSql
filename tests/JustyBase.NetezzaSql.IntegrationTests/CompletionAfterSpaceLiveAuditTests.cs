using JustyBase.Netezza.Schema;
using JustyBase.NetezzaDriver;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Read-only live audit (NZ_DEV_* gated): prints what the completion engine would
/// offer at each "after space / after letter" position on the real catalog.
/// Soft-skips when NZ_DEV_* is not set. No schema is modified.
/// </summary>
public sealed class CompletionAfterSpaceLiveAuditTests
{
    [Fact]
    public async Task Print_CompletionLists_AfterSpace_OnLiveCatalog()
    {
        if (!NetezzaLiveTestHost.TryCreateConnection(out var connection))
        {
            return;
        }

        using var conn = connection!;
        conn.Open();
        try
        {
            var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(
                conn,
                conn.Database,
                new NetezzaCatalogLoadOptions { EagerColumns = true, LazyColumnThreshold = int.MaxValue });
            var provider = new InMemorySchemaProvider();
            NetezzaSchemaProviderAdapter.Apply(provider, snapshot);

            var allNames = provider.GetTableNames(null, null)?.Select(x => x.Name).ToList() ?? [];
            string? table = allNames.FirstOrDefault(n => n.Equals("DIMACCOUNT", StringComparison.OrdinalIgnoreCase))
                            ?? allNames.FirstOrDefault(n => n.StartsWith("DIM", StringComparison.OrdinalIgnoreCase))
                            ?? allNames.FirstOrDefault();
            Assert.NotNull(table);

            var snapshotTable = snapshot.Tables.FirstOrDefault(t =>
                t.Name.Equals(table, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(snapshotTable);
            var tableInfo = provider.GetTable(conn.Database, snapshotTable!.Schema, snapshotTable.Name);
            string? column = tableInfo?.Columns?.Select(c => c.Name).FirstOrDefault();
            Assert.NotNull(column);

            string aliasQuery = $"SELECT * FROM {table} A ";
            string whereCol = $"SELECT * FROM {table} A WHERE A.{column} ";
            string whereFull = $"SELECT * FROM {table} A WHERE A.{column} = 1 ";
            string whereFullAnd = $"SELECT * FROM {table} A WHERE A.{column} = 1 A";

            var cases = new (string Name, string Sql)[]
            {
                ("C1 after alias + space", aliasQuery),
                ("C1 after alias + W", aliasQuery + "W"),
                ("C1 after alias + L", aliasQuery + "L"),
                ("C2 after column + space", whereCol),
                ("C3 after full predicate + space", whereFull),
                ("C3 after full predicate + A", whereFullAnd),
                ("C4 after SELECT * + space", "SELECT * "),
                ("C5 SelectList + letter S", "SELECT S"),
                ("C6 FROM list after comma", $"SELECT * FROM {table} A, "),
                ("C7 ORDER BY + space", $"SELECT * FROM {table} A ORDER BY "),
                ("dot WHERE A.", $"SELECT * FROM {table} A WHERE A."),
            };

            var output = new System.Text.StringBuilder();
            output.AppendLine($"catalog: {conn.Database}, tables: {allNames.Count}, table: {table}, column: {column}");
            foreach (var (name, sql) in cases)
            {
                var items = new NzCompletionEngine(provider).GetCompletions(sql, sql.Length);
                output.AppendLine($"--- {name}: [{sql}]");
                output.AppendLine($"    count={items.Count}: {string.Join(", ", items.Take(20).Select(i => $"{i.Label}({i.Kind})"))}");
            }

            Console.WriteLine(output.ToString());
        }
        finally
        {
            conn.Close();
        }
    }
}
