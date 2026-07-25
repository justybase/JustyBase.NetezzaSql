using JustyBase.Netezza.Metadata;
using JustyBase.Netezza.Models;
using JustyBase.Netezza.Schema;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Netezza.Tests;

public sealed class MetadataPrefetchAndAdapterParityTests
{
    [Fact]
    public void PrefetchStages_AreOrderedLikeLiteContract()
    {
        Assert.Equal(
            [
                MetadataPrefetchStage.Databases,
                MetadataPrefetchStage.Schemas,
                MetadataPrefetchStage.Objects,
                MetadataPrefetchStage.Procedures,
                MetadataPrefetchStage.Columns
            ],
            MetadataPrefetchContract.OrderedStages);
        Assert.True(MetadataPrefetchContract.ShouldDeferColumnHydration(500));
        Assert.False(MetadataPrefetchContract.ShouldDeferColumnHydration(499));
    }

    [Fact]
    public void Adapter_LoadsExternalsAndTables_ForCompletionFromSchemaDot()
    {
        var provider = new InMemorySchemaProvider();
        var snapshot = new NetezzaSchemaSnapshot(
            Tables:
            [
                new NetezzaSchemaTable(
                    "CUSTOMERS",
                    "PUBLIC",
                    "SALES",
                    Columns: [new NetezzaSchemaColumn("ID", "INTEGER")])
            ],
            ExternalTables:
            [
                new NetezzaSchemaTable("EXT_ORDERS", "PUBLIC", "SALES", Description: "external")
            ],
            Procedures:
            [
                new NetezzaProcedureDefinition("SALES", "PUBLIC", "SP_RUN", "INTEGER", "BEGIN RETURN 1; END;")
            ]);

        NetezzaSchemaProviderAdapter.Apply(provider, snapshot);

        Assert.NotNull(provider.GetTable("SALES", "PUBLIC", "CUSTOMERS"));
        Assert.NotNull(provider.GetTable("SALES", "PUBLIC", "EXT_ORDERS"));
        Assert.Single(snapshot.Procedures!);

        var engine = new NzCompletionEngine(provider);
        var sql = "SELECT * FROM PUBLIC.";
        var items = engine.GetCompletions(sql, sql.Length);
        Assert.Contains(items, i => string.Equals(i.Label, "CUSTOMERS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => string.Equals(i.Label, "EXT_ORDERS", StringComparison.OrdinalIgnoreCase));
    }
}
