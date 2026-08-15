using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlLsp;
using JustyBase.NetezzaSqlLsp.Services;

namespace JustyBase.NetezzaSql.Tests;

public sealed class SqliteSqlParserTests
{
    private static (IReadOnlyList<Statement> Statements, IReadOnlyList<ValidationError> Errors) Parse(string sql)
    {
        var tokens = SqliteLexer.Tokenize(sql).ToArray();
        var parser = new SqliteSqlParser(tokens);
        var statements = new List<Statement>();
        while (parser.Position < tokens.Length)
        {
            var statement = parser.Parse();
            if (statement is not null) statements.Add(statement);
            else if (parser.Position >= tokens.Length) break;
        }
        return (statements, parser.Errors);
    }

    private static string Single(string sql) => NzSqlFormatter.Format(Assert.Single(Parse(sql).Statements));

    [Fact]
    public void Lexer_RecognizesSqliteExtensions()
    {
        var kinds = SqliteLexer.Tokenize(
            "ATTACH 'x.db' AS aux PRAGMA journal_mode AUTOINCREMENT ON CONFLICT DO UPDATE RETURNING WINDOW")
            .Select(t => t.Kind);
        Assert.Contains(NzToken.SqliteAttach, kinds);
        Assert.Contains(NzToken.SqlitePragma, kinds);
        Assert.Contains(NzToken.SqliteAutoincrement, kinds);
        Assert.Contains(NzToken.SqliteConflict, kinds);
        Assert.Contains(NzToken.SqliteReturning, kinds);
        Assert.Contains(NzToken.SqliteWindow, kinds);
    }

    [Fact]
    public void Parse_CommonSelectSurface()
    {
        var (statements, errors) = Parse("SELECT a, b AS label FROM items WHERE a > 1 ORDER BY b LIMIT 10 OFFSET 5");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.Equal(10, select.Limit!.Limit);
        Assert.Equal(5, select.Limit.Offset);
        Assert.Contains("LIMIT 10 OFFSET 5", NzSqlFormatter.Format(select), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT * FROM items LIMIT 5")]
    [InlineData("SELECT * FROM items LIMIT 5, 10")]
    [InlineData("SELECT * FROM items LIMIT ?")]
    [InlineData("SELECT * FROM items LIMIT -1")]
    public void Parse_LimitForms(string sql)
    {
        var (statements, errors) = Parse(sql);
        Assert.Single(statements);
        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_WindowClause()
    {
        var (statements, errors) = Parse(
            "SELECT row_number() OVER w, rank() OVER w FROM items WINDOW w AS (PARTITION BY a ORDER BY b)");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.NotNull(select.SqliteWindowTokens);
        Assert.Contains("WINDOW w AS", NzSqlFormatter.Format(select), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CreateTable_Basic()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, email TEXT UNIQUE, created TEXT DEFAULT (datetime('now')))");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.True(table.IfNotExists);
        Assert.Equal(4, table.Columns!.Count);
        Assert.NotNull(table.Columns[0].MySqlAttributeTokens);
        Assert.Contains("AUTOINCREMENT", NzSqlFormatter.Format(table), StringComparison.Ordinal);
        var formatted = NzSqlFormatter.Format(table);
        Assert.Contains("DEFAULT", formatted, StringComparison.Ordinal);
        Assert.Contains("datetime('now')", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CreateTable_WithoutRowidAndStrict()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE kv (k TEXT PRIMARY KEY, v BLOB) WITHOUT ROWID, STRICT");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        var formatted = NzSqlFormatter.Format(table);
        Assert.Contains("WITHOUT ROWID", formatted, StringComparison.Ordinal);
        Assert.Contains("STRICT", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CreateTable_ReferencesTail()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE ON UPDATE SET NULL DEFERRABLE INITIALLY DEFERRED)");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        var formatted = NzSqlFormatter.Format(table);
        Assert.Contains("ON DELETE CASCADE", formatted, StringComparison.Ordinal);
        Assert.Contains("ON UPDATE SET NULL", formatted, StringComparison.Ordinal);
        Assert.Contains("DEFERRABLE INITIALLY DEFERRED", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CreateTable_TableLevelConstraints()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE line_items (order_id INTEGER NOT NULL, sku TEXT NOT NULL, qty INTEGER CHECK (qty > 0), PRIMARY KEY (order_id, sku) ON CONFLICT REPLACE, FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE)");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.Equal(2, table.Constraints!.Count);
        var pk = Assert.IsType<PrimaryKeyConstraint>(table.Constraints[0]);
        Assert.NotNull(pk.SqliteConflictTokens);
        var fk = Assert.IsType<ForeignKeyConstraint>(table.Constraints[1]);
        Assert.NotNull(fk.SqliteTailTokens);
        var formatted = NzSqlFormatter.Format(table);
        Assert.Contains("ON CONFLICT REPLACE", formatted, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CreateTable_GeneratedColumn()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE products (id INTEGER PRIMARY KEY, price REAL NOT NULL, tax REAL GENERATED ALWAYS AS (price * 0.2) STORED, net REAL AS (price - tax) VIRTUAL)");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        var formatted = NzSqlFormatter.Format(table);
        Assert.Contains("GENERATED ALWAYS AS (price * 0.2) STORED", formatted, StringComparison.Ordinal);
        Assert.Contains("AS (price - tax) VIRTUAL", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CreateTable_CollateAndColumnCheck()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE t (name TEXT COLLATE NOCASE NOT NULL, age INTEGER CHECK (age >= 0))");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        var formatted = NzSqlFormatter.Format(table);
        Assert.Contains("COLLATE NOCASE", formatted, StringComparison.Ordinal);
        Assert.Contains("CHECK (age >= 0)", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CreateTable_AsSelect()
    {
        var (statements, errors) = Parse("CREATE TABLE backup AS SELECT * FROM users");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.NotNull(table.AsSelect);
    }

    [Fact]
    public void Parse_CreateTable_TypelessColumns()
    {
        var (statements, errors) = Parse("CREATE TABLE t (a, b)");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.Equal(string.Empty, table.Columns![0].Type.Name);
    }

    [Fact]
    public void Parse_CreateVirtualTable()
    {
        var (statements, errors) = Parse(
            "CREATE VIRTUAL TABLE search_index USING fts5(title, body, tokenize = 'porter')");
        var statement = Assert.IsType<SqliteCreateVirtualTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.Contains("fts5", NzSqlFormatter.Format(statement), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CreateTrigger()
    {
        var (statements, errors) = Parse(
            "CREATE TRIGGER audit_users AFTER INSERT ON users BEGIN INSERT INTO audit_log(action, ts) VALUES ('insert', datetime('now')); END");
        var statement = Assert.IsType<SqliteCreateTriggerStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.Contains("audit_users", NzSqlFormatter.Format(statement), StringComparison.Ordinal);
        var formatted = NzSqlFormatter.Format(statement);
        Assert.Contains("AFTER INSERT ON users", formatted, StringComparison.Ordinal);
        Assert.Contains("datetime", formatted, StringComparison.Ordinal);
        Assert.Contains("'now'", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_InsertOrReplaceAndUpsert()
    {
        var (statements, errors) = Parse(
            "INSERT OR REPLACE INTO users (id, name) VALUES (1, 'a') ON CONFLICT (id) DO UPDATE SET name = excluded.name WHERE users.id = 1 RETURNING id");
        var insert = Assert.IsType<InsertStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.NotNull(insert.OnConflict);
        Assert.NotNull(insert.Returning);
        Assert.Contains("INSERT OR REPLACE", NzSqlFormatter.Format(insert), StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (id) DO UPDATE SET name = excluded.name", NzSqlFormatter.Format(insert), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("INSERT OR IGNORE INTO t (a) VALUES (1)")]
    [InlineData("INSERT OR FAIL INTO t (a) VALUES (1)")]
    [InlineData("INSERT OR ABORT INTO t (a) VALUES (1)")]
    [InlineData("INSERT OR ROLLBACK INTO t (a) VALUES (1)")]
    [InlineData("REPLACE INTO t (a) VALUES (1)")]
    public void Parse_InsertConflictResolutions(string sql)
    {
        var (statements, errors) = Parse(sql);
        Assert.Single(statements);
        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_InsertDoNothing()
    {
        var (statements, errors) = Parse(
            "INSERT INTO t (id) VALUES (1) ON CONFLICT DO NOTHING");
        var insert = Assert.IsType<InsertStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.True(insert.OnConflict!.DoNothing);
        Assert.Null(insert.OnConflict.ConflictColumns);
    }

    [Fact]
    public void Parse_UpdateOrWithFromAndReturning()
    {
        var (statements, errors) = Parse(
            "UPDATE OR IGNORE users SET name = (SELECT 'x') FROM other WHERE users.id = other.id RETURNING id");
        var update = Assert.IsType<UpdateStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.NotNull(update.From);
        Assert.NotNull(update.Returning);
        Assert.Contains("UPDATE OR IGNORE", NzSqlFormatter.Format(update), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DeleteReturning()
    {
        var (statements, errors) = Parse("DELETE FROM users WHERE id = 1 RETURNING *");
        var delete = Assert.IsType<DeleteStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.NotNull(delete.Returning);
        Assert.Equal("*", delete.Returning.Columns[0]);
    }

    [Theory]
    [InlineData("PRAGMA journal_mode = WAL")]
    [InlineData("PRAGMA main.table_info('users')")]
    [InlineData("PRAGMA foreign_keys = ON")]
    [InlineData("ATTACH DATABASE 'aux.db' AS aux")]
    [InlineData("DETACH DATABASE aux")]
    [InlineData("VACUUM")]
    [InlineData("VACUUM INTO 'backup.db'")]
    [InlineData("REINDEX")]
    [InlineData("SAVEPOINT sp1")]
    [InlineData("ROLLBACK TO SAVEPOINT sp1")]
    [InlineData("RELEASE SAVEPOINT sp1")]
    [InlineData("EXPLAIN QUERY PLAN SELECT * FROM users")]
    [InlineData("ANALYZE")]
    public void Parse_CommandStatements(string sql)
    {
        var (statements, errors) = Parse(sql);
        Assert.Single(statements);
        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_TableNameQuoting()
    {
        var (statements, errors) = Parse("SELECT * FROM [odd name] JOIN `tick` ON [odd name].id = `tick`.id");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.Equal("odd name", select.From![0].Source.Table!.Name);
        Assert.Equal("tick", select.From[0].Joins![0].Source.Table!.Name);
    }

    [Fact]
    public void Parse_MultiWordTypeNames()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE t (a DOUBLE PRECISION, b CHARACTER VARYING(20), c VARCHAR(255))");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.Equal("DOUBLE PRECISION", table.Columns![0].Type.Name);
        Assert.Equal("CHARACTER VARYING", table.Columns[1].Type.Name);
        Assert.Equal(["20"], table.Columns[1].Type.Parameters);
    }

    [Fact]
    public void Parse_JsonOperators()
    {
        var (statements, errors) = Parse("SELECT payload->'a'->>'b' FROM events");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.IsType<BinaryExpression>(select.SelectList[0].Expression);
    }

    [Theory]
    [InlineData("SELECT * FROM db..items")]
    [InlineData("SELECT * FROM db.schema.items")]
    [InlineData("MERGE INTO t USING s ON t.id = s.id WHEN MATCHED THEN DELETE")]
    [InlineData("CREATE SEQUENCE seq1")]
    [InlineData("CREATE DATABASE x")]
    [InlineData("CREATE TABLE t (a INTEGER PRIMARY KEY) DISTRIBUTE ON (a)")]
    [InlineData("GROOM TABLE t")]
    [InlineData("SELECT * FROM t FETCH FIRST 10 ROWS ONLY")]
    public void Parse_RejectsNonSqliteSyntax(string sql)
    {
        Assert.NotEmpty(Parse(sql).Errors);
    }

    [Fact]
    public void Parse_BlobLiteral()
    {
        var (statements, errors) = Parse("SELECT X'AB12CD'");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        var literal = Assert.IsType<Literal>(select.SelectList[0].Expression);
        Assert.Equal(LiteralKind.Blob, literal.Kind);
        Assert.Contains("X'AB12CD'", NzSqlFormatter.Format(select), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WindowNameAfterOver_RoundTrips()
    {
        var (statements, errors) = Parse(
            "SELECT row_number() OVER w FROM items WINDOW w AS (ORDER BY id)");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        var call = Assert.IsType<FunctionCall>(select.SelectList[0].Expression);
        Assert.Equal("w", call.Over!.WindowName);
        var formatted = NzSqlFormatter.Format(select);
        Assert.Contains("OVER w", formatted, StringComparison.Ordinal);
        Assert.Contains("WINDOW w AS", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TempTrigger_FormatsTempBeforeTrigger()
    {
        var (statements, errors) = Parse(
            "CREATE TEMP TRIGGER IF NOT EXISTS audit_users AFTER INSERT ON users BEGIN INSERT INTO log(ts) VALUES (datetime('now')); END");
        var statement = Assert.IsType<SqliteCreateTriggerStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.True(statement.IfNotExists);
        var formatted = NzSqlFormatter.Format(statement);
        Assert.Contains("CREATE TEMP TRIGGER", formatted, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CreateVirtualTable_IfNotExistsRoundTrips()
    {
        var (statements, errors) = Parse(
            "CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5(title)");
        var statement = Assert.IsType<SqliteCreateVirtualTableStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.True(statement.IfNotExists);
        Assert.Contains("IF NOT EXISTS", NzSqlFormatter.Format(statement), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT * FROM items LIMIT ?", "LIMIT ?")]
    [InlineData("SELECT * FROM items LIMIT 10 OFFSET ?", "LIMIT 10 OFFSET ?")]
    [InlineData("SELECT * FROM items LIMIT ? OFFSET ?", "LIMIT ? OFFSET ?")]
    [InlineData("SELECT * FROM items LIMIT 2, ?", "LIMIT 2, ?")]
    public void Parse_NonNumericLimitOperands_RoundTrip(string sql, string expectedClause)
    {
        var (statements, errors) = Parse(sql);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        Assert.NotNull(select.Limit!.SqliteTokens);
        Assert.Contains(expectedClause, NzSqlFormatter.Format(select), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TriggerWithCaseInWhenClause()
    {
        var (statements, errors) = Parse(
            "CREATE TRIGGER t BEFORE INSERT ON x WHEN CASE WHEN a > 0 THEN 1 ELSE 0 END > 0 BEGIN INSERT INTO log(v) VALUES (NEW.a); END");
        var statement = Assert.IsType<SqliteCreateTriggerStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        var formatted = NzSqlFormatter.Format(statement);
        Assert.Contains("CASE WHEN a > 0 THEN 1 ELSE 0 END", formatted, StringComparison.Ordinal);
        // Opaque token ranges space out punctuation (NEW . a).
        Assert.Contains("NEW", formatted, StringComparison.Ordinal);
        Assert.Contains("log", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TriggerWithCaseInsideBlock()
    {
        var (statements, errors) = Parse(
            "CREATE TRIGGER t AFTER UPDATE ON x BEGIN UPDATE y SET v = CASE WHEN OLD.v > 0 THEN 1 ELSE 0 END WHERE id = NEW.id; END");
        var statement = Assert.IsType<SqliteCreateTriggerStatement>(Assert.Single(statements));
        Assert.Empty(errors);
        var formatted = NzSqlFormatter.Format(statement);
        Assert.Contains("CASE WHEN", formatted, StringComparison.Ordinal);
        Assert.Contains("THEN 1 ELSE 0 END", formatted, StringComparison.Ordinal);
        Assert.Contains("OLD", formatted, StringComparison.Ordinal);
        Assert.Contains("NEW", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void DialectRuntime_UsesSqliteComponents()
    {
        Assert.Equal(SqlDialect.Sqlite, DialectRuntime.ParseName("sqlite"));
        Assert.Equal(SqlDialect.Sqlite, DialectRuntime.ParseName("sqlite3"));
        Assert.True(SqlDialectCapabilitiesCatalog.For(SqlDialect.Sqlite).SupportsLimit);
        Assert.False(SqlDialectCapabilitiesCatalog.For(SqlDialect.Sqlite).SupportsMerge);
        Assert.False(SqlDialectCapabilitiesCatalog.For(SqlDialect.Sqlite).SupportsFetchFirst);
        Assert.Equal("SQLite SQL", DialectRuntime.DiagnosticSource(SqlDialect.Sqlite));
        Assert.IsType<SqliteSqlParser>(DialectRuntime.CreateParser(
            DialectRuntime.Tokenize("SELECT 1", SqlDialect.Sqlite).ToArray(), SqlDialect.Sqlite));
        Assert.Same(SqliteSqlCatalog.Instance, DialectRuntime.AuthoringCatalog(SqlDialect.Sqlite));
        Assert.NotEmpty(DialectRuntime.QualityRules(SqlDialect.Sqlite).AllRules);
        Assert.True(SqliteSqlCatalog.Instance.TryGetFunction("JSON_EXTRACT", out _));
        Assert.True(SqliteSqlCatalog.Instance.TryGetDataType("BLOB", out _));
    }

    [Fact]
    public async Task Lsp_UsesSqliteRuntimeAndCatalog()
    {
        Assert.Equal(SqlDialect.Sqlite, LspDialectArgs.Parse(["--dialect", "sqlite"]));
        var diagnostics = LintService.Lint("SELECT JSON_EXTRACT(payload, '$.a') FROM events", null, SqlDialect.Sqlite);
        Assert.Empty(diagnostics);
        var completions = await CompletionService.GetCompletions("SELECT", 0, 6, null, SqlDialect.Sqlite);
        Assert.Contains(completions.Items!, item => item.Label == "JSON_EXTRACT");
    }
}
