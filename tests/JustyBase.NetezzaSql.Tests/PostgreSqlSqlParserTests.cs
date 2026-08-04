using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlLsp;
using JustyBase.NetezzaSqlLsp.Services;

namespace JustyBase.NetezzaSql.Tests;

public sealed class PostgreSqlSqlParserTests
{
    private static (IReadOnlyList<Statement> Statements, IReadOnlyList<ValidationError> Errors) Parse(string sql)
    {
        var tokens = PostgreSqlLexer.Tokenize(sql).ToArray();
        var parser = new PostgreSqlSqlParser(tokens);
        var statements = new List<Statement>();
        while (parser.Position < tokens.Length)
        {
            var statement = parser.Parse();
            if (statement is not null) statements.Add(statement);
            else if (parser.Position >= tokens.Length) break;
        }
        return (statements, parser.Errors);
    }

    [Fact]
    public void Lexer_RecognizesPostgreSqlExtensions()
    {
        var kinds = PostgreSqlLexer.Tokenize("payload->>'name' #> ARRAY['a'] LATERAL RETURNING ON CONFLICT DO NOTHING").Select(t => t.Kind);
        Assert.Contains(NzToken.PostgreSqlJsonTextArrow, kinds);
        Assert.Contains(NzToken.PostgreSqlJsonPath, kinds);
        Assert.Contains(NzToken.PostgreSqlArray, kinds);
        Assert.Contains(NzToken.PostgreSqlLateral, kinds);
        Assert.Contains(NzToken.PostgreSqlReturning, kinds);
        Assert.Contains(NzToken.PostgreSqlConflict, kinds);
    }

    [Fact]
    public void Parse_PostgreSqlCommonQuerySurface()
    {
        var (statements, errors) = Parse("SELECT DISTINCT ON (id) id, payload->>'name' FROM public.items i CROSS JOIN LATERAL (SELECT i.id) x ORDER BY id");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));

        Assert.Empty(errors);
        Assert.Single(select.DistinctOn!);
        Assert.True(select.From![0].Joins![0].Source.Lateral);
        Assert.IsType<BinaryExpression>(select.SelectList[1].Expression);
        Assert.Contains("DISTINCT ON", NzSqlFormatter.Format(select), StringComparison.Ordinal);
        Assert.Contains("LATERAL", NzSqlFormatter.Format(select), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_PostgreSqlDistinctOnPreservesSetOperations()
    {
        var (statements, errors) = Parse(
            "SELECT DISTINCT ON (id) id FROM public.items UNION SELECT id FROM public.archive_items");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));

        Assert.Empty(errors);
        Assert.Single(select.SetOperations!);
        Assert.Single(select.CompoundSelects!);
        Assert.Contains("UNION", NzSqlFormatter.Format(select), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("INSERT INTO public.items (id) VALUES (1) ON CONFLICT (id) DO NOTHING RETURNING id")]
    [InlineData("INSERT INTO public.items (id) VALUES (1) ON CONFLICT (id) DO UPDATE SET id = 2 RETURNING id")]
    [InlineData("UPDATE public.items SET id = 2 WHERE id = 1 RETURNING id")]
    [InlineData("DELETE FROM public.items WHERE id = 1 RETURNING id")]
    public void Parse_PostgreSqlDmlExtensions(string sql)
    {
        var (statements, errors) = Parse(sql);
        Assert.Single(statements);
        Assert.Empty(errors);
        if (statements[0] is InsertStatement insert)
        {
            Assert.NotNull(insert.OnConflict);
            Assert.NotNull(insert.Returning);
        }
        Assert.Contains("RETURNING", NzSqlFormatter.Format(statements[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_PostgreSqlReturningAcceptsExpressionsAndAliases()
    {
        var (statements, errors) = Parse(
            "UPDATE public.items SET id = 2 RETURNING id + 1 AS next_id, now()");
        var update = Assert.IsType<UpdateStatement>(Assert.Single(statements));

        Assert.Empty(errors);
        Assert.Equal(2, update.Returning!.Items!.Count);
        Assert.Equal("next_id", update.Returning.Items[0].Alias);
        var formatted = NzSqlFormatter.Format(update);
        Assert.Contains("id + 1 AS next_id", formatted, StringComparison.Ordinal);
        Assert.Contains("now()", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_PostgreSqlAllowsNetezzaWordsAsIdentifiers()
    {
        var (statements, errors) = Parse(
            "SELECT groom, organize FROM groom_items WHERE groom = 1");

        Assert.Single(statements);
        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_PostgreSqlLateralAcceptsTableFunctions()
    {
        var (statements, errors) = Parse(
            "SELECT x.value FROM public.items i CROSS JOIN LATERAL jsonb_array_elements(i.payload) AS x");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        var source = select.From![0].Joins![0].Source;

        Assert.Empty(errors);
        Assert.True(source.Lateral);
        Assert.NotNull(source.TableFunction);
        Assert.Contains("LATERAL jsonb_array_elements", NzSqlFormatter.Format(select), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_PostgreSqlOnConflictAcceptsConstraintTarget()
    {
        var (statements, errors) = Parse(
            "INSERT INTO public.items (id) VALUES (1) ON CONFLICT ON CONSTRAINT items_pkey DO NOTHING");
        var insert = Assert.IsType<InsertStatement>(Assert.Single(statements));

        Assert.Empty(errors);
        Assert.Equal("items_pkey", insert.OnConflict!.ConstraintName);
        Assert.Contains("ON CONSTRAINT items_pkey", NzSqlFormatter.Format(insert), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_PostgreSqlArrayAndCast()
    {
        var (statements, errors) = Parse("SELECT ARRAY[1, 2, 3]::integer[] FROM public.items");
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        var cast = Assert.IsType<CastFunctionExpression>(select.SelectList[0].Expression);

        Assert.Empty(errors);
        Assert.Equal("integer[]", cast.TargetType.Name);
        Assert.IsType<ArrayExpression>(cast.Expression);
    }

    [Fact]
    public void Parse_PostgreSqlDdlTypes()
    {
        var (statements, errors) = Parse("CREATE TABLE public.items (id SERIAL PRIMARY KEY, payload JSONB NOT NULL, tags INTEGER[])");
        var table = Assert.IsType<CreateTableStatement>(Assert.Single(statements));

        Assert.Empty(errors);
        Assert.Equal("SERIAL", table.Columns![0].Type.Name);
        Assert.Equal("JSONB", table.Columns[1].Type.Name);
        Assert.Equal("INTEGER[]", table.Columns[2].Type.Name);
    }

    [Theory]
    [InlineData("SELECT * FROM db..items")]
    [InlineData("SELECT * FROM db.schema.items")]
    [InlineData("CREATE TABLE public.items (id integer) DISTRIBUTE ON (id)")]
    [InlineData("GROOM TABLE public.items")]
    public void Parse_PostgreSqlRejectsNonPostgreSqlSyntax(string sql)
    {
        Assert.NotEmpty(Parse(sql).Errors);
    }

    [Fact]
    public void DialectRuntime_UsesPostgreSqlComponents()
    {
        Assert.Equal(SqlDialect.PostgreSql, DialectRuntime.ParseName("pg"));
        Assert.Equal(SqlDialect.PostgreSql, DialectRuntime.ParseName("postgres"));
        Assert.True(SqlDialectCapabilitiesCatalog.For(SqlDialect.PostgreSql).SupportsFetchFirst);
        Assert.Equal("PostgreSQL SQL", DialectRuntime.DiagnosticSource(SqlDialect.PostgreSql));
        Assert.IsType<PostgreSqlSqlParser>(DialectRuntime.CreateParser(
            DialectRuntime.Tokenize("SELECT 1", SqlDialect.PostgreSql).ToArray(), SqlDialect.PostgreSql));
        Assert.Same(PostgreSqlSqlCatalog.Instance, DialectRuntime.AuthoringCatalog(SqlDialect.PostgreSql));
        Assert.Empty(DialectRuntime.QualityRules(SqlDialect.PostgreSql).AllRules);
        Assert.True(PostgreSqlSqlCatalog.Instance.TryGetFunction("JSONB_BUILD_OBJECT", out _));
        Assert.True(PostgreSqlSqlCatalog.Instance.TryGetDataType("UUID", out _));
    }

    [Fact]
    public async Task Lsp_UsesPostgreSqlRuntimeAndCatalog()
    {
        Assert.Equal(SqlDialect.PostgreSql, LspDialectArgs.Parse(["--dialect", "postgres"]));
        var diagnostics = LintService.Lint("SELECT JSONB_BUILD_OBJECT('id', id) FROM public.items", null, SqlDialect.PostgreSql);
        Assert.Empty(diagnostics);
        var completions = await CompletionService.GetCompletions("SELECT ", 0, 7, null, SqlDialect.PostgreSql);
        Assert.Contains(completions.Items!, item => item.Label == "JSONB_BUILD_OBJECT");
    }
}
