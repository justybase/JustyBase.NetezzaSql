using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.NetezzaSql.Tests;

public sealed class NetezzaAuthoringCatalogTests
{
    [Fact]
    public void Catalog_ContainsCoreNetezzaFunctionsAndTypes()
    {
        Assert.Contains(NetezzaSqlCatalog.BuiltinFunctions, f => f.Name == "HASH");
        Assert.Contains(NetezzaSqlCatalog.BuiltinFunctions, f => f.Name == "GROUP_CONCAT");
        Assert.Contains(NetezzaSqlCatalog.BuiltinFunctions, f => f.Name == "PERCENTILE_CONT");
        Assert.True(NetezzaSqlCatalog.TryGetDataType("DOUBLE PRECISION", out var type));
        Assert.Equal("FLOAT8", type.CanonicalName);
        Assert.Contains("VARCHAR", NetezzaSqlCatalog.DataTypeNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignatureHelp_UsesCatalogOverloadsAndActiveParameter()
    {
        const string sql = "SELECT GROUP_CONCAT(value, ";
        var result = NzSignatureHelpService.GetSignatureHelp(sql, sql.Length);

        Assert.NotNull(result);
        Assert.True(result!.Signatures.Length >= 2);
        Assert.Equal(1, result.ActiveParameter);
        Assert.Contains(result.Signatures, signature => signature.Label.Contains("SEPARATOR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SymbolService_ResolvesNestedCteDeclarationAndReferences()
    {
        const string sql = "WITH first_cte AS (SELECT 1), second_cte AS (SELECT * FROM first_cte) SELECT * FROM second_cte";
        int offset = sql.LastIndexOf("first_cte", StringComparison.Ordinal);

        var symbol = NzSymbolService.GetSymbol(sql, offset);
        var definition = NzSymbolService.GetDefinition(sql, offset);

        Assert.NotNull(symbol);
        Assert.Equal(SqlSymbolKind.Cte, symbol!.Kind);
        Assert.Equal(2, symbol.Occurrences.Count);
        Assert.NotNull(definition);
        Assert.True(definition!.IsDefinition);
    }

    [Fact]
    public void Rename_PreservesQuotedIdentifier()
    {
        const string sql = "SELECT \"x\".id FROM orders \"x\" WHERE \"x\".id = 1";
        var occurrences = new[]
        {
            new SymbolOccurrence(1, "x", SqlSymbolKind.Alias, 7, 10, true, null),
            new SymbolOccurrence(2, "x", SqlSymbolKind.Alias, 26, 29, false, 1),
            new SymbolOccurrence(3, "x", SqlSymbolKind.Alias, 35, 38, false, 1)
        };
        var renamed = NzRenameService.ApplyRename(sql, new SqlRenameInfo("x", SqlSymbolKind.Alias, occurrences), "new alias");

        Assert.Contains("\"new alias\".id", renamed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"unterminated", false)]
    [InlineData("\"quoted_identifier\"", true)]
    [InlineData("plain_identifier", true)]
    public void Rename_ValidatesQuotedIdentifierBoundaries(string identifier, bool expected)
    {
        Assert.Equal(expected, NzRenameService.IsValidIdentifier(identifier));
    }

    [Fact]
    public void Rename_DoesNotInsertUnterminatedQuotedIdentifier()
    {
        const string sql = "SELECT \"x\".id FROM orders \"x\" WHERE \"x\".id = 1";
        var occurrences = new[]
        {
            new SymbolOccurrence(1, "x", SqlSymbolKind.Alias, 7, 10, true, null),
            new SymbolOccurrence(2, "x", SqlSymbolKind.Alias, 26, 29, false, 1),
            new SymbolOccurrence(3, "x", SqlSymbolKind.Alias, 35, 38, false, 1)
        };

        var renamed = NzRenameService.ApplyRename(
            sql,
            new SqlRenameInfo("x", SqlSymbolKind.Alias, occurrences),
            "\"unterminated");

        Assert.Equal(sql, renamed);
    }

    [Fact]
    public void SemanticClassification_SkipsLargeDocuments()
    {
        var classifier = new NzSemanticTokenClassifier();
        var sql = new string('x', 500_001);

        Assert.Empty(classifier.Classify(sql, "large-document"));
    }

    [Fact]
    public void Completion_IncludesCatalogFunctionDetails()
    {
        var result = new NzCompletionEngine().GetCompletions("SELECT ", "SELECT ".Length);

        var hash = Assert.Single(result, item => item.Label == "HASH");
        Assert.Equal(CompletionKind.Function, hash.Kind);
        Assert.Equal("HASH(expression)", hash.Detail);
    }

    [Fact]
    public void AlterTableCompletion_SupportsNetezzaDistribution()
    {
        var tokens = NzLexer.Tokenize("ALTER TABLE orders DISTRIBUTE ").ToArray();
        var phase = AlterTableCompletion.AnalyzePhase(tokens);

        Assert.Equal(AlterTablePhase.DistributeOn, phase);
        Assert.Contains("ON", AlterTableCompletion.GetKeywordsForPhase(phase));
        Assert.Contains("HASH", AlterTableCompletion.GetKeywordsForPhase(phase));
        Assert.Contains("RANDOM", AlterTableCompletion.GetKeywordsForPhase(phase));
    }
}
