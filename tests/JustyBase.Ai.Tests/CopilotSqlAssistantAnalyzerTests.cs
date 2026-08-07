using JustyBase.Ai.Models;
using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class CopilotSqlAssistantAnalyzerTests
{
    [Fact]
    public void BuildNetezzaOptimizationHints_ShouldRequireSql_WhenInputEmpty()
    {
        var result = CopilotSqlAssistantAnalyzer.BuildNetezzaOptimizationHints(string.Empty);
        Assert.Single(result);
        Assert.Contains("Provide SQL text", result[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNetezzaOptimizationHints_ShouldDetectSelectStarAndOrderByWithoutLimit()
    {
        const string sql = "SELECT * FROM SALES ORDER BY CREATED_AT;";
        var result = CopilotSqlAssistantAnalyzer.BuildNetezzaOptimizationHints(sql);

        Assert.Contains(result, x => x.Contains("SELECT *", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result, x => x.Contains("ORDER BY without LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result, x => x.Contains("EXPLAIN", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildNetezzaOptimizationHints_ShouldDetectLeadingWildcardLike()
    {
        const string sql = "SELECT ID FROM T_CUSTOMERS WHERE NAME LIKE '%SMITH';";
        var result = CopilotSqlAssistantAnalyzer.BuildNetezzaOptimizationHints(sql);

        Assert.Contains(result, x => x.Contains("leading wildcard", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildNetezzaOptimizationHints_ShouldDetectDmlWithoutWhere()
    {
        const string sql = "UPDATE T_FACT SET FLAG = 1;";
        var result = CopilotSqlAssistantAnalyzer.BuildNetezzaOptimizationHints(sql);

        Assert.Contains(result, x => x.Contains("without WHERE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildNetezzaOptimizationHints_ShouldDetectCtasWithoutDistribute()
    {
        const string sql = "CREATE TABLE TMP_X AS SELECT ID FROM SRC_X;";
        var result = CopilotSqlAssistantAnalyzer.BuildNetezzaOptimizationHints(sql);

        Assert.Contains(result, x => x.Contains("DISTRIBUTE ON", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildNetezzaOptimizationHints_ShouldSkipExplainHint_WhenExplainIsPresent()
    {
        const string sql = "EXPLAIN SELECT ID FROM T1;";
        var result = CopilotSqlAssistantAnalyzer.BuildNetezzaOptimizationHints(sql);

        Assert.DoesNotContain(result, x => x.Contains("Use EXPLAIN", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("select * from t", true)]
    [InlineData("WITH X AS (SELECT 1) SELECT * FROM X", true)]
    [InlineData("explain select 1", true)]
    [InlineData("show tables", true)]
    [InlineData("update t set a=1", false)]
    [InlineData("", false)]
    public void IsLikelyQuery_ShouldClassifyCorrectly(string sql, bool expected)
    {
        var result = CopilotSqlAssistantAnalyzer.IsLikelyQuery(sql);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseQualifiedName_ShouldParseThreePartName()
    {
        var result = CopilotSqlAssistantAnalyzer.ParseQualifiedName("DB1.SCHEMA1.OBJECT1");
        Assert.Equal("DB1", result.Database);
        Assert.Equal("SCHEMA1", result.Schema);
        Assert.Equal("OBJECT1", result.ObjectName);
    }

    [Fact]
    public void ParseQualifiedName_ShouldParseTwoPartName()
    {
        var result = CopilotSqlAssistantAnalyzer.ParseQualifiedName("SCHEMA1.OBJECT1");
        Assert.Null(result.Database);
        Assert.Equal("SCHEMA1", result.Schema);
        Assert.Equal("OBJECT1", result.ObjectName);
    }

    [Fact]
    public void ParseQualifiedName_ShouldParseSinglePartName()
    {
        var result = CopilotSqlAssistantAnalyzer.ParseQualifiedName("OBJECT1");
        Assert.Null(result.Database);
        Assert.Null(result.Schema);
        Assert.Equal("OBJECT1", result.ObjectName);
    }

    [Fact]
    public void FormatCellValue_ShouldHandleNullAndDbNull()
    {
        Assert.Equal("NULL", CopilotSqlAssistantAnalyzer.FormatCellValue(null));
        Assert.Equal("NULL", CopilotSqlAssistantAnalyzer.FormatCellValue(DBNull.Value));
    }

    [Fact]
    public void FormatCellValue_ShouldTruncateLongValues()
    {
        var input = new string('A', 30);
        var result = CopilotSqlAssistantAnalyzer.FormatCellValue(input, maxLength: 10);
        Assert.Equal("AAAAAAAAAA...", result);
    }
}

public sealed class ChatAttachmentTests
{
    [Fact]
    public void EffectiveDisplayName_ShouldPreferDisplayName()
    {
        var attachment = new ChatAttachment
        {
            Path = @"C:\temp\file.sql",
            DisplayName = "Query file",
            IsDirectory = false
        };

        Assert.Equal("Query file", attachment.EffectiveDisplayName);
        Assert.Equal("[FILE] Query file", attachment.DisplayLabel);
    }

    [Fact]
    public void EffectiveDisplayName_ShouldFallbackToPathFileName()
    {
        var attachment = new ChatAttachment
        {
            Path = @"C:\temp\folder",
            IsDirectory = true
        };

        Assert.Equal("folder", attachment.EffectiveDisplayName);
        Assert.Equal("[DIR] folder", attachment.DisplayLabel);
    }

    [Fact]
    public void Clone_ShouldCreateIndependentCopy()
    {
        var source = new ChatAttachment
        {
            Path = @"C:\temp\file.sql",
            DisplayName = "file.sql",
            IsDirectory = false,
            StartLine = 10,
            EndLine = 20
        };

        var clone = source.Clone();
        clone.DisplayName = "changed.sql";

        Assert.NotSame(source, clone);
        Assert.Equal(@"C:\temp\file.sql", clone.Path);
        Assert.Equal(10, clone.StartLine);
        Assert.Equal(20, clone.EndLine);
        Assert.Equal("file.sql", source.DisplayName);
    }
}
