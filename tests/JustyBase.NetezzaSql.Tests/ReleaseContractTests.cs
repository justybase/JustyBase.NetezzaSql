using JustyBase.NetezzaCatalogSql;
using JustyBase.NetezzaDdl;
using JustyBase.NetezzaDdl.Models;
using JustyBase.NetezzaSqlParser.Authoring;
using CatalogSqlApi = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ReleaseContractTests
{
    [Fact]
    public void PublicApis_FromTheImplementedPhasesRemainAvailable()
    {
        Assert.NotNull(typeof(NetezzaSqlCatalog).GetMethod(nameof(NetezzaSqlCatalog.TryGetFunction)));
        Assert.NotNull(typeof(NetezzaSqlCatalog).GetMethod(nameof(NetezzaSqlCatalog.TryGetDataType)));
        Assert.NotNull(typeof(NetezzaBatchDdlBuilder).GetMethod(nameof(NetezzaBatchDdlBuilder.Build)));
        Assert.NotNull(typeof(CatalogSqlApi).GetMethod(nameof(CatalogSqlApi.GetSchemasSql)));
        Assert.NotNull(typeof(CatalogSqlApi).GetMethod(nameof(CatalogSqlApi.GetObjectTypesSql)));
        Assert.NotNull(typeof(CatalogSqlApi).GetMethod(nameof(CatalogSqlApi.GetTableStorageStatsSql)));
        Assert.NotNull(typeof(CatalogSqlApi).GetMethod(nameof(CatalogSqlApi.GetObjectDescriptionsSql)));
    }

    [Fact]
    public void EmptyBatch_IsAValidDeterministicNoOp()
    {
        var result = new NetezzaBatchDdlBuilder().BuildDetailed(new NetezzaBatchDdlInput());

        Assert.Equal(string.Empty, result.Sql);
        Assert.Empty(result.SkippedObjects);
    }
}
