using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class LocalToolNameMappingTests
{
    [Theory]
    [InlineData("get_current_sql", "GetCurrentSql")]
    [InlineData("execute_sql", "ExecuteSql")]
    [InlineData("apply_sql_document_change", "ApplySqlFix")]
    [InlineData("browse_schema_objects", "BrowseSchemaObjects")]
    [InlineData("get_object_columns", "GetObjectColumns")]
    [InlineData("export_schema", "ExportSchema")]
    public void MapLocalToolName_ConvertsSnakeCaseToPascalCase(string input, string expected)
    {
        Assert.Equal(expected, LocalChatService.MapLocalToolName(input));
    }

    [Theory]
    [InlineData("GetCurrentSql")]
    [InlineData("ExecuteSql")]
    [InlineData("ApplySqlFix")]
    [InlineData("BrowseSchemaObjects")]
    public void MapLocalToolName_KeepsAdvertisedPascalCaseNames(string input)
    {
        // AIFunctionFactory advertises the PascalCase method names directly; they must
        // round-trip unchanged so the agent loop can execute them.
        Assert.Equal(input, LocalChatService.MapLocalToolName(input));
    }

    [Fact]
    public void MapLocalToolName_RejectsUnknownTool()
    {
        Assert.Equal("shell", LocalChatService.MapLocalToolName("shell"));
    }
}
