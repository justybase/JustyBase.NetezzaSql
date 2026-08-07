using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class LocalDatabaseContextFormatterTests
{
    [Fact]
    public void BuildNoActiveConnectionContext_ReturnsExpectedBlock()
    {
        var result = LocalDatabaseContextFormatter.BuildNoActiveConnectionContext();

        Assert.Equal("[DATABASE_CONTEXT]\nNo active database connection.\n[/DATABASE_CONTEXT]", result);
    }

    [Fact]
    public void BuildDatabaseContext_IncludesConnectionDatabaseAndSchemas()
    {
        var result = LocalDatabaseContextFormatter.BuildDatabaseContext(
            "ConnA",
            "DB_MAIN",
            ["ADMIN", "PUBLIC"]);

        Assert.Contains("[DATABASE_CONTEXT]", result);
        Assert.Contains("Active Connection: ConnA", result);
        Assert.Contains("Active Database: DB_MAIN", result);
        Assert.Contains("Preferred: DB_MAIN.SCHEMA.OBJECT", result);
        Assert.Contains("  - DB_MAIN.ADMIN", result);
        Assert.Contains("  - DB_MAIN.PUBLIC", result);
        Assert.Contains("[/DATABASE_CONTEXT]", result);
    }

    [Fact]
    public void BuildDatabaseContext_EmptySchemas_StillContainsAvailableSchemasHeader()
    {
        var result = LocalDatabaseContextFormatter.BuildDatabaseContext(
            "ConnA",
            "DB_MAIN",
            []);

        Assert.Contains("Available schemas:", result);
        Assert.DoesNotContain("  - DB_MAIN.", result);
    }

    [Fact]
    public void BuildFallbackContext_ReturnsExpectedFormatHint()
    {
        var result = LocalDatabaseContextFormatter.BuildFallbackContext("ConnA", "DB_MAIN");

        Assert.Contains("Active Database: DB_MAIN", result);
        Assert.Contains("Connection: ConnA", result);
        Assert.Contains("DB_MAIN.SCHEMA.OBJECT or DB_MAIN..OBJECT", result);
    }
}
