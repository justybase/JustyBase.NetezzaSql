using JustyBase.Sqlite.Schema;
using Microsoft.Data.Sqlite;

namespace JustyBase.Sqlite.Tests;

public sealed class SqliteSchemaLoaderTests
{
    private static SqliteConnection OpenMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static void Seed(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                email TEXT DEFAULT 'n/a'
            );
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE
            );
            CREATE VIEW active_users AS SELECT id, name FROM users;
            CREATE TRIGGER users_audit AFTER INSERT ON users
            BEGIN
                INSERT INTO orders (id, user_id) VALUES (0, NEW.id);
            END;
            """;
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task LoadDatabasesAsync_ReturnsMain()
    {
        await using var connection = OpenMemory();
        var databases = await SqliteSchemaLoader.LoadDatabasesAsync(connection);
        Assert.Contains("main", databases);
    }

    [Fact]
    public async Task LoadCatalogAsync_LoadsObjectsAndColumns()
    {
        await using var connection = OpenMemory();
        Seed(connection);

        var snapshot = await SqliteSchemaLoader.LoadCatalogAsync(connection, "main");

        Assert.Equal(2, snapshot.Tables.Count(t => !t.IsView));
        Assert.Contains(snapshot.Tables, t => t.Name == "users" && !t.IsView);
        Assert.Contains(snapshot.Tables, t => t.Name == "active_users" && t.IsView);

        var users = snapshot.Tables.Single(t => t.Name == "users");
        Assert.Equal(3, users.Columns!.Count);
        var id = users.Columns[0];
        Assert.Equal("INTEGER", id.DataType);
        Assert.True(id.IsPrimaryKey);
        Assert.True(users.Columns[1].NotNull);
        Assert.Equal("'n/a'", users.Columns[2].DefaultValue);
    }

    [Fact]
    public async Task LoadCatalogAsync_LoadsTriggers()
    {
        await using var connection = OpenMemory();
        Seed(connection);

        var snapshot = await SqliteSchemaLoader.LoadCatalogAsync(connection, "main");
        var trigger = Assert.Single(snapshot.Triggers!);
        Assert.Equal("users_audit", trigger.Name);
        Assert.Contains("AFTER INSERT ON users", trigger.Sql);
    }

    [Fact]
    public async Task LoadCatalogAsync_HidesSqliteInternals()
    {
        await using var connection = OpenMemory();
        Seed(connection);

        var snapshot = await SqliteSchemaLoader.LoadCatalogAsync(connection, "main");
        Assert.DoesNotContain(snapshot.Tables, t => t.Name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HydrateColumnsAsync_LoadsSingleTable()
    {
        await using var connection = OpenMemory();
        Seed(connection);

        var columns = await SqliteSchemaLoader.HydrateColumnsAsync(connection, "main", "orders");
        Assert.Equal(2, columns.Count);
        Assert.True(columns[1].NotNull);
        Assert.True(columns[0].IsPrimaryKey);
    }

    [Fact]
    public async Task LoadCatalogAsync_DeferredColumns_WhenAboveThreshold()
    {
        await using var connection = OpenMemory();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE big (a INTEGER, b TEXT)";
            command.ExecuteNonQuery();
        }

        var snapshot = await SqliteSchemaLoader.LoadCatalogAsync(
            connection, "main", new SqliteCatalogLoadOptions { EagerColumns = true, LazyColumnThreshold = 1 });
        Assert.Null(snapshot.Tables.Single().Columns);
    }

    [Fact]
    public async Task LoadCatalogAsync_AttachedDatabase()
    {
        await using var connection = OpenMemory();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "ATTACH DATABASE ':memory:' AS aux; CREATE TABLE aux.extra (x INTEGER)";
            command.ExecuteNonQuery();
        }

        var snapshot = await SqliteSchemaLoader.LoadCatalogAsync(connection, "aux");
        var table = Assert.Single(snapshot.Tables);
        Assert.Equal("extra", table.Name);
        Assert.Equal("aux", table.Database);
    }

    [Fact]
    public async Task LoadCatalogAsync_HidesVirtualTableShadowTables()
    {
        await using var connection = OpenMemory();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE VIRTUAL TABLE search_index USING fts5(title, body)";
            command.ExecuteNonQuery();
        }

        var snapshot = await SqliteSchemaLoader.LoadCatalogAsync(connection, "main");
        Assert.Single(snapshot.Tables);
        Assert.Equal("search_index", snapshot.Tables[0].Name);
        Assert.DoesNotContain(snapshot.Tables, t => t.Name.EndsWith("_data", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot.Tables, t => t.Name.EndsWith("_idx", StringComparison.OrdinalIgnoreCase));
    }
}
