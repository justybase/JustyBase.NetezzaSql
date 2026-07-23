using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class InMemorySchemaProviderTests
{
    [Fact]
    public void GetTable_ResolvesQuotedIdentifiersAgainstCatalogNames()
    {
        var provider = new InMemorySchemaProvider();
        provider.AddTable(new TableInfo("Orders", "Sales", "Warehouse"));

        var table = provider.GetTable("\"warehouse\"", "\"sales\"", "\"orders\"");

        Assert.NotNull(table);
        Assert.Equal("Orders", table!.Name);
    }

    [Fact]
    public void GetTable_ResolvesDatabaseDoubleDotNotationAcrossSchemas()
    {
        var provider = new InMemorySchemaProvider();
        provider.AddTable(new TableInfo("ORDERS", "PUBLIC", "WAREHOUSE"));

        Assert.True(provider.TableExists("WAREHOUSE", null, "ORDERS"));
    }

    [Fact]
    public void CanValidateUnqualifiedTableReferences_StaysConservativeWithoutHostContext()
    {
        var provider = new InMemorySchemaProvider();
        Assert.False(provider.CanValidateUnqualifiedTableReferences());

        provider.AddTable(new TableInfo("ORDERS"));

        Assert.False(provider.CanValidateUnqualifiedTableReferences());
    }

    [Fact]
    public void MetadataLists_AreStableAndOrdered()
    {
        var provider = new InMemorySchemaProvider();
        provider.AddTable(new TableInfo("ZEBRA", "B", "Z"));
        provider.AddTable(new TableInfo("ALPHA", "A", "A", IsView: true));

        Assert.Equal(["A", "Z"], provider.GetDatabases());
        Assert.Equal(["A", "B"], provider.GetSchemas(null));
        Assert.Equal(["ALPHA", "ZEBRA"], provider.GetTableNames(null, null)!.Select(item => item.Name));
    }

    [Fact]
    public void QualificationProposals_ResolveQuotedLookupName()
    {
        var provider = new InMemorySchemaProvider();
        provider.SetTableQualificationProposals([
            new TableQualificationProposal("WAREHOUSE", "PUBLIC", "ORDERS", "WAREHOUSE..ORDERS")
        ]);

        var proposal = Assert.Single(provider.ProposeTableQualification(null, null, "\"orders\"")!);

        Assert.Equal("WAREHOUSE..ORDERS", proposal.QualifiedText);
    }
}
