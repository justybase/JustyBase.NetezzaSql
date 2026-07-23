using System.Data;
using JustyBase.NetezzaDdl;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ReviewRegressionTests
{
    [Fact]
    public void CompletionContext_StartsNewStatementAfterSemicolon()
    {
        const string sql = "SELECT 1; ";

        var context = CompletionContextExtractor.GetStatementContext(sql, sql.Length);

        Assert.Equal(sql.IndexOf(';') + 1, context.StatementStart);
        Assert.Equal(" ", CompletionContextExtractor.GetStatementLocalPrefix(sql, sql.Length));
    }

    [Fact]
    public void TryTokenize_ReturnsFalseForUnsupportedInput()
    {
        var success = NzLexer.TryTokenize("SELECT 1 §", out var tokens);

        Assert.False(success);
        Assert.Empty(tokens);
    }

    [Fact]
    public void TryTokenize_AcceptsWhitespaceOnlyInput()
    {
        var success = NzLexer.TryTokenize("  \r\n", out var tokens);

        Assert.True(success);
        Assert.Empty(tokens);
    }

    [Fact]
    public void ExternalOptionsMapper_ConvertsProviderNumericTypes()
    {
        var table = new DataTable();
        for (var i = 0; i < 33; i++)
            table.Columns.Add($"C{i}", typeof(object));

        var row = table.NewRow();
        for (var i = 0; i < 33; i++)
            row[i] = DBNull.Value;
        row[4] = 12;       // Int32 -> long?
        row[5] = (short)4; // Int16 -> long?
        row[16] = 25;      // Int32 -> short?
        row[26] = (long)64; // Int64 -> int?
        row[28] = 100;     // Int32 -> long?
        table.Rows.Add(row);

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        var mapped = NetezzaExternalOptionsMapper.FromReader(reader);

        Assert.Equal(12L, mapped.SkipRows);
        Assert.Equal(4L, mapped.MaxErrors);
        Assert.Equal((short)25, mapped.Y2Base);
        Assert.Equal(64, mapped.SocketBufSize);
        Assert.Equal(100L, mapped.MaxRows);
    }
}
