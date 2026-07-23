using System.Data;
using System.Text;
using JustyBase.NetezzaDdl;
using JustyBase.NetezzaDdl.Models;

namespace JustyBase.NetezzaSql.Tests;

public sealed class DdlSurfaceCoverageTests
{
    [Fact]
    public void MaintenanceTemplates_ProduceEverySupportedStatement()
    {
        var deleted = NetezzaDdlTemplates.GetDeletedRecordsSql("DB.S.T", ["first name", "id"], NetezzaNameHelper.QuoteNameIfNeeded);
        var templates = new[]
        {
            deleted,
            NetezzaDdlTemplates.GetGrantSelectSql("DB.S.T"),
            NetezzaDdlTemplates.GetOrganizeTemplateSql("DB.S.T"),
            NetezzaDdlTemplates.GetGroomSql("DB.S.T"),
            NetezzaDdlTemplates.GetGenerateStatsSql("DB.S.T"),
            NetezzaDdlTemplates.GetAddTableCommentTemplateSql("DB.S.T"),
            NetezzaDdlTemplates.GetCheckDistributeSql("DB", "S", "T", "T"),
            NetezzaDdlTemplates.GetCreateFluidSampleSql("DB.S.T"),
            NetezzaDdlTemplates.CreateProcedurePattern
        };

        Assert.All(templates, Assert.NotEmpty);
        Assert.Contains("deletexid != 0", deleted, StringComparison.Ordinal);
        Assert.Contains("GROOM TABLE DB.S.T", templates[3], StringComparison.Ordinal);
        Assert.Contains("_V_TABLE_STORAGE_STAT", templates[6], StringComparison.Ordinal);
    }

    [Fact]
    public void TableBuilder_EmitsEachKeyKindAndRandomDistribution()
    {
        var input = new NetezzaTableDdlInput(
            "DB", "S", "T", [new NetezzaColumnDdl("ID", "INTEGER")],
            Keys:
            [
                new NetezzaKeyDdl('p', "PK_T", ["ID"]),
                new NetezzaKeyDdl('u', "UQ_T", ["ID"]),
                new NetezzaKeyDdl('f', "FK_T", ["ID"], "REFDB", "REFS", "REFT", ["RID"], "CASCADE", "RESTRICT")
            ],
            OrganizeColumns: ["ID"]);

        var ddl = new NetezzaDdlTextBuilder().BuildCreateTable(input, []);

        Assert.Contains("DISTRIBUTE ON RANDOM", ddl, StringComparison.Ordinal);
        Assert.Contains("ORGANIZE ON (ID)", ddl, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (ID)", ddl, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (ID)", ddl, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY (ID) REFERENCES REFDB.REFS.REFT(RID) ON DELETE CASCADE ON UPDATE RESTRICT", ddl, StringComparison.Ordinal);
    }

    [Fact]
    public void RemainingBuilders_HandleOwnerArgumentsAndOptionalComments()
    {
        var builder = new NetezzaDdlTextBuilder();
        var viewBuffer = new StringBuilder();
        var procedureBuffer = new StringBuilder();
        var synonymBuffer = new StringBuilder();

        builder.AppendCreateView(viewBuffer, new NetezzaViewDdlInput("DB", "S", "V", "SELECT 1", "view's comment"));
        builder.AppendCreateProcedure(procedureBuffer, new NetezzaProcedureDdlInput("DB", "S", "P", "INTEGER", "RETURN 1;", "(p INTEGER)", ExecuteAsOwner: true));
        builder.AppendCreateSynonym(synonymBuffer, new NetezzaSynonymDdlInput("DB", "S", "ALIAS", "DB.S.T", Owner: "OWNER"));

        Assert.Contains("COMMENT ON VIEW DB.S.V IS 'view''s comment'", viewBuffer.ToString(), StringComparison.Ordinal);
        Assert.Contains("P(p INTEGER)", procedureBuffer.ToString(), StringComparison.Ordinal);
        Assert.Contains("EXECUTE AS OWNER", procedureBuffer.ToString(), StringComparison.Ordinal);
        Assert.Contains("CREATE SYNONYM DB.OWNER.ALIAS", synonymBuffer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ImportSql_ValidatesInputsAndSupportsSameAsPipe()
    {
        Assert.Contains("SAMEAS \"orders\"", NetezzaImportSql.InsertSameAsFromExternalPipe("orders", "load"), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => NetezzaImportSql.CreateRandomDistributionTable("", ["ID INTEGER"]));
        Assert.Throws<ArgumentException>(() => NetezzaImportSql.CreateRandomDistributionTable("T", []));
        Assert.Throws<ArgumentException>(() => NetezzaImportSql.InsertFromExternalPipe("T", "P", []));
        Assert.Throws<ArgumentException>(() => NetezzaImportSql.InsertSameAsFromExternalPipe("T", ""));
    }

    [Fact]
    public void ExternalOptionsMapper_MapsCachedAndLegacyReaderValues()
    {
        var options = NetezzaExternalOptionsMapper.ToOptions(new NetezzaExternalTableCachedInfo
        {
            DataObject = "file", Delimiter = "\t", Encoding = "UTF8", SkipRows = 3,
            CrInString = true, RecordDelim = "\n", RequireQuotes = false
        });
        Assert.Equal("\\t", options.Delimiter);
        Assert.Equal(3, options.SkipRows);
        Assert.True(options.CrInString);

        var table = new DataTable();
        for (var i = 0; i < 37; i++) table.Columns.Add($"C{i}", typeof(object));
        var row = table.NewRow();
        for (var i = 0; i < 37; i++) row[i] = DBNull.Value;
        row[2] = "file";
        row[4] = "\t";
        row[11] = "/tmp";
        row[31] = "\r\n";
        table.Rows.Add(row);
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        var legacy = NetezzaExternalOptionsMapper.FromLegacyReader(reader);
        Assert.Equal("\\r\\n", legacy.RecordDelim);
        Assert.Equal("file", legacy.DataObject);
    }
}
