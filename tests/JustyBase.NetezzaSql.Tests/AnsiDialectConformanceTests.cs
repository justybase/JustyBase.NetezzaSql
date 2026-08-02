using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.NetezzaSql.Tests;

public sealed class AnsiDialectConformanceTests
{
    [Fact]
    public void AuthoringOverlays_PreserveSharedClausePhrasesAndMergeSignatures()
    {
        var catalogs = new (ISqlAuthoringCatalog Catalog, SqlFormatterProfile Formatter)[]
        {
            (NetezzaSqlAuthoringCatalog.Instance, NetezzaSqlAuthoringCatalog.Instance.FormatterProfile),
            (OracleSqlCatalog.Instance, OracleSqlCatalog.Instance.FormatterProfile),
            (Db2SqlCatalog.Instance, Db2SqlCatalog.Instance.FormatterProfile),
        };

        foreach (var (catalog, formatter) in catalogs)
        {
            Assert.Contains("GROUP BY", formatter.ClauseKeywords);
            Assert.Contains("ORDER BY", formatter.ClauseKeywords);
            Assert.Contains("PARTITION BY", formatter.ClauseKeywords);
            Assert.True(catalog.TryGetFunction("COALESCE", out var coalesce));
            Assert.NotEmpty(coalesce.Signatures);
        }

        Assert.True(OracleSqlCatalog.Instance.TryGetFunction("COALESCE", out var oracleCoalesce));
        Assert.Contains(oracleCoalesce.Signatures,
            signature => signature.Label.Equals("COALESCE(value1, value2, ...)", StringComparison.OrdinalIgnoreCase));
        Assert.False(OracleSqlCatalog.Instance.TryGetDataType("VARCHAR", out _));
    }

    [Fact]
    public void CapabilityMatrix_ExposesExplicitDialectSupport()
    {
        var netezza = SqlDialectCapabilitiesCatalog.For(SqlDialect.Netezza);
        var oracle = SqlDialectCapabilitiesCatalog.For(SqlDialect.Oracle);
        var db2 = SqlDialectCapabilitiesCatalog.For(SqlDialect.Db2);

        Assert.All(new[] { netezza, oracle, db2 }, capabilities =>
        {
            Assert.True(capabilities.SupportsMerge);
            Assert.True(capabilities.SupportsFetchFirst);
            Assert.True(capabilities.SupportsAnsiOffsetFetch);
        });
        Assert.True(netezza.SupportsLimit);
        Assert.False(oracle.SupportsLimit);
        Assert.False(db2.SupportsLimit);
    }

    [Theory]
    [InlineData(SqlDialect.Netezza)]
    [InlineData(SqlDialect.Oracle)]
    [InlineData(SqlDialect.Db2)]
    public void Merge_IsStructuredAcrossAllDialects(SqlDialect dialect)
    {
        const string sql = "MERGE INTO target AS t USING source AS s ON (t.id = s.id) " +
            "WHEN MATCHED THEN UPDATE SET t.value = s.value " +
            "WHEN NOT MATCHED THEN INSERT (id, value) VALUES (s.id, s.value)";

        var (statements, errors) = Parse(sql, dialect);
        Assert.Empty(errors);
        var merge = Assert.IsType<MergeStatement>(Assert.Single(statements));
        Assert.Equal("target", merge.Target.Name, ignoreCase: true);
        Assert.Equal(2, merge.Clauses.Count);
        Assert.IsType<MergeMatchedUpdateClause>(merge.Clauses[0]);
        Assert.IsType<MergeNotMatchedInsertClause>(merge.Clauses[1]);
    }

    [Theory]
    [InlineData(SqlDialect.Netezza, "SELECT id FROM t LIMIT 10")]
    [InlineData(SqlDialect.Netezza, "SELECT id FROM t LIMIT 10 OFFSET 3")]
    [InlineData(SqlDialect.Netezza, "SELECT id FROM t OFFSET 3 ROWS FETCH NEXT 10 ROWS ONLY")]
    [InlineData(SqlDialect.Oracle, "SELECT id FROM t OFFSET 3 ROWS FETCH FIRST 10 ROWS ONLY")]
    [InlineData(SqlDialect.Oracle, "SELECT id FROM t OFFSET 3 ROWS FETCH NEXT 10 ROWS ONLY")]
    [InlineData(SqlDialect.Oracle, "SELECT id FROM t OFFSET 3 ROWS")]
    [InlineData(SqlDialect.Oracle, "SELECT id FROM t FETCH FIRST 10 ROWS ONLY")]
    [InlineData(SqlDialect.Db2, "SELECT id FROM t OFFSET 3 ROWS FETCH FIRST 10 ROWS ONLY")]
    [InlineData(SqlDialect.Db2, "SELECT id FROM t OFFSET 3 ROWS")]
    [InlineData(SqlDialect.Db2, "SELECT id FROM t FETCH FIRST 10 ROWS ONLY")]
    public void OffsetFetchAndLimit_PreserveSyntaxAndShape(SqlDialect dialect, string sql)
    {
        var (statements, errors) = Parse(sql, dialect);
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));

        if (sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
        {
            Assert.NotNull(select.Limit);
            Assert.Equal(LimitClauseSyntax.Limit, select.Limit!.Syntax);
            Assert.Equal(sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase) ? 3 : null,
                select.Limit.Offset);
        }
        else
        {
            Assert.NotNull(select.OffsetFetch);
            Assert.Equal(sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase) ? 3 : null,
                select.OffsetFetch!.Offset);
            Assert.Equal(sql.Contains("FETCH", StringComparison.OrdinalIgnoreCase) ? 10 : null,
                select.OffsetFetch.FetchCount);
            if (select.Limit is not null)
                Assert.Equal(LimitClauseSyntax.Fetch, select.Limit.Syntax);
        }

        var formatted = NzSqlFormatter.Format(select);
        if (sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
            Assert.Contains("LIMIT", formatted, StringComparison.OrdinalIgnoreCase);
        else
            Assert.True(
                formatted.Contains("OFFSET", StringComparison.OrdinalIgnoreCase) ||
                formatted.Contains("FETCH", StringComparison.OrdinalIgnoreCase),
                formatted);
    }

    [Fact]
    public void OffsetFetch_DoesNotChangeCteOrSubqueryScopeShape()
    {
        const string sql = "WITH page AS (SELECT id FROM source OFFSET 2 ROWS FETCH FIRST 5 ROWS ONLY) " +
            "SELECT page.id FROM page WHERE page.id > 0";
        var (statements, errors) = Parse(sql, SqlDialect.Oracle);

        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        var cte = Assert.Single(select.With!.Ctes);
        Assert.NotNull(cte.Query.OffsetFetch);
        var from = Assert.Single(select.From!);
        Assert.Equal("page", from.Source.Table!.Name, ignoreCase: true);
    }

    private static (IReadOnlyList<Statement> Statements, IReadOnlyList<ValidationError> Errors) Parse(
        string sql,
        SqlDialect dialect)
    {
        var tokens = DialectRuntime.Tokenize(sql, dialect).ToArray();
        var parser = DialectRuntime.CreateParser(tokens, dialect);
        var statements = new List<Statement>();
        var errors = new List<ValidationError>();

        while (parser.Position < tokens.Length)
        {
            var before = parser.Errors.Count;
            var statement = parser.Parse();
            errors.AddRange(parser.Errors.Skip(before));
            if (statement is null)
                break;
            statements.Add(statement);
        }

        return (statements, errors);
    }
}
