using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class LocalSqlEditorContextFormatterTests
{
    [Fact]
    public void HasValidSelection_ShouldReturnTrue_ForInRangeSelection()
    {
        var context = ("select * from t", "select", 0, 6, 6);

        var result = LocalSqlEditorContextFormatter.HasValidSelection(context);

        Assert.True(result);
    }

    [Fact]
    public void HasValidSelection_ShouldReturnFalse_ForOutOfRangeSelection()
    {
        var context = ("select * from t", "", 10, 20, 10);

        var result = LocalSqlEditorContextFormatter.HasValidSelection(context);

        Assert.False(result);
    }

    [Fact]
    public void HasValidSelection_ShouldReturnFalse_ForNegativeSelectionStart()
    {
        var context = ("select * from t", "", -1, 3, 0);

        var result = LocalSqlEditorContextFormatter.HasValidSelection(context);

        Assert.False(result);
    }

    [Fact]
    public void GetSelectedText_ShouldFallbackToSlice_WhenSelectedTextIsEmpty()
    {
        var context = ("select * from t", "", 7, 1, 8);

        var result = LocalSqlEditorContextFormatter.GetSelectedText(context);

        Assert.Equal("*", result);
    }

    [Fact]
    public void GetSelectedText_ShouldReturnEmpty_WhenSelectionIsInvalid()
    {
        var context = ("select * from t", "", 20, 1, 20);

        var result = LocalSqlEditorContextFormatter.GetSelectedText(context);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetSelectedText_ShouldPreferProvidedSelectedText_WhenAvailable()
    {
        var context = ("select * from t", "SELECT *", 0, 6, 6);

        var result = LocalSqlEditorContextFormatter.GetSelectedText(context);

        Assert.Equal("SELECT *", result);
    }

    [Fact]
    public void MarkSelectedSqlRegion_ShouldWrapSelectionWithMarkers()
    {
        var context = ("select * from t", "", 7, 1, 8);

        var result = LocalSqlEditorContextFormatter.MarkSelectedSqlRegion(context);

        Assert.StartsWith("select ", result, StringComparison.Ordinal);
        Assert.Contains("/*<SELECTION_START>", result, StringComparison.Ordinal);
        Assert.Contains("/*<SELECTION_END>*/", result, StringComparison.Ordinal);
        Assert.EndsWith(" from t", result, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkSelectedSqlRegion_ShouldReturnOriginal_WhenSelectionIsInvalid()
    {
        const string sql = "select * from t";
        var context = (sql, "", 20, 1, 20);

        var result = LocalSqlEditorContextFormatter.MarkSelectedSqlRegion(context);

        Assert.Equal(sql, result);
    }

    [Theory]
    [InlineData("No active SQL document. Please open and select an SQL document to get its content.", true)]
    [InlineData("Error getting current SQL: boom", true)]
    [InlineData("error getting current sql: boom", true)]
    [InlineData("select 1", false)]
    public void IsUnavailableSqlMessage_ShouldRecognizeKnownPrefixes(string input, bool expected)
    {
        var result = LocalSqlEditorContextFormatter.IsUnavailableSqlMessage(input);

        Assert.Equal(expected, result);
    }
}
