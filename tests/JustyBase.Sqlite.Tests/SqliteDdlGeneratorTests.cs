using JustyBase.Sqlite.Ddl;
using JustyBase.Sqlite.Models;

namespace JustyBase.Sqlite.Tests;

public sealed class SqliteDdlGeneratorTests
{
    [Fact]
    public void CreateTable_SinglePk_InlinePrimaryKey()
    {
        var table = new SqliteSchemaTable("users", "main", Columns:
        [
            new SqliteSchemaColumn("id", "INTEGER", IsPrimaryKey: true),
            new SqliteSchemaColumn("name", "TEXT", NotNull: true),
            new SqliteSchemaColumn("email", "TEXT", DefaultValue: "'n/a'"),
        ]);

        var ddl = SqliteDdlGenerator.CreateTable(table);

        Assert.Equal("""
            CREATE TABLE "users" (
              "id" INTEGER PRIMARY KEY,
              "name" TEXT NOT NULL,
              "email" TEXT DEFAULT 'n/a'
            )
            """, ddl);
    }

    [Fact]
    public void CreateTable_CompositePk_TableLevelConstraint()
    {
        var table = new SqliteSchemaTable("line_items", Columns:
        [
            new SqliteSchemaColumn("order_id", "INTEGER", NotNull: true, IsPrimaryKey: true),
            new SqliteSchemaColumn("sku", "TEXT", NotNull: true, IsPrimaryKey: true),
            new SqliteSchemaColumn("qty", "INTEGER"),
        ]);

        var ddl = SqliteDdlGenerator.CreateTable(table);

        Assert.Contains("\"order_id\" INTEGER NOT NULL", ddl, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (\"order_id\", \"sku\")", ddl, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTable_AttachedDatabase_IsQualified()
    {
        var table = new SqliteSchemaTable("orders", "aux", Columns:
        [
            new SqliteSchemaColumn("id", "INTEGER", IsPrimaryKey: true),
        ]);

        var ddl = SqliteDdlGenerator.CreateTable(table);

        Assert.StartsWith("CREATE TABLE \"aux\".\"orders\"", ddl, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTable_MainDatabase_IsNotQualified()
    {
        var table = new SqliteSchemaTable("orders", "main", Columns:
        [
            new SqliteSchemaColumn("id", "INTEGER", IsPrimaryKey: true),
        ]);

        Assert.StartsWith("CREATE TABLE \"orders\"", SqliteDdlGenerator.CreateTable(table), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTable_TypelessColumns()
    {
        var table = new SqliteSchemaTable("t", Columns: [new SqliteSchemaColumn("a")]);
        var ddl = SqliteDdlGenerator.CreateTable(table);
        Assert.Contains("\"a\"", ddl, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteIdentifier_DoublesEmbeddedQuotes()
    {
        Assert.Equal("\"a\"\"b\"", SqliteDdlGenerator.QuoteIdentifier("a\"b"));
    }
}
