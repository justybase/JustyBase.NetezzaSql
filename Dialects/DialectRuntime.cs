using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Parser;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Dialects;

/// <summary>
/// Central dialect dispatch for tokenize / parse / quality rules / authoring.
/// Keeps LSP and caching layers free of growing <c>if (dialect == …)</c> chains.
/// </summary>
public static class DialectRuntime
{
    private static readonly QualityRuleRegistry NetezzaRules = new(NzLintRules.AllRules);
    private static readonly QualityRuleRegistry OracleRules = new(OracleLintRules.AllRules);
    private static readonly QualityRuleRegistry Db2Rules = new(Db2LintRules.AllRules);
    private static readonly QualityRuleRegistry MssqlRules = new(MssqlLintRules.AllRules);
    private static readonly QualityRuleRegistry MySqlRules = new(MySqlLintRules.AllRules);
    private static readonly QualityRuleRegistry PostgreSqlRules = new(PostgreSqlLintRules.AllRules);

    public static string DiagnosticSource(SqlDialect dialect) => dialect switch
    {
        SqlDialect.Oracle => "Oracle SQL",
        SqlDialect.Db2 => "Db2 SQL",
        SqlDialect.Mssql => "MSSQL SQL",
        SqlDialect.MySql => "MySQL SQL",
        SqlDialect.PostgreSql => "PostgreSQL SQL",
        SqlDialect.Access => "Access SQL",
        _ => "Netezza SQL",
    };

    public static QualityRuleRegistry QualityRules(SqlDialect dialect) => dialect switch
    {
        SqlDialect.Oracle => OracleRules,
        SqlDialect.Db2 => Db2Rules,
        SqlDialect.Mssql => MssqlRules,
        SqlDialect.MySql => MySqlRules,
        SqlDialect.PostgreSql => PostgreSqlRules,
        _ => NetezzaRules,
    };

    public static ISqlAuthoringCatalog AuthoringCatalog(SqlDialect dialect) => dialect switch
    {
        SqlDialect.Oracle => OracleSqlCatalog.Instance,
        SqlDialect.Db2 => Db2SqlCatalog.Instance,
        SqlDialect.Mssql => MssqlSqlCatalog.Instance,
        SqlDialect.MySql => MySqlSqlCatalog.Instance,
        SqlDialect.PostgreSql => PostgreSqlSqlCatalog.Instance,
        _ => NetezzaSqlAuthoringCatalog.Instance,
    };

    public static ISqlAuthoringCatalog? AuthoringCatalogOrNull(SqlDialect dialect) => dialect switch
    {
        SqlDialect.Oracle => OracleSqlCatalog.Instance,
        SqlDialect.Db2 => Db2SqlCatalog.Instance,
        SqlDialect.Mssql => MssqlSqlCatalog.Instance,
        SqlDialect.MySql => MySqlSqlCatalog.Instance,
        SqlDialect.PostgreSql => PostgreSqlSqlCatalog.Instance,
        _ => null, // callers default to Netezza catalog
    };

    public static TokenList<NzToken> Tokenize(string sql, SqlDialect dialect) => dialect switch
    {
        SqlDialect.Oracle => OracleLexer.Tokenize(sql),
        SqlDialect.Db2 => Db2Lexer.Tokenize(sql),
        SqlDialect.Mssql => MssqlLexer.Tokenize(sql),
        SqlDialect.MySql => MySqlLexer.Tokenize(sql),
        SqlDialect.PostgreSql => PostgreSqlLexer.Tokenize(sql),
        SqlDialect.Access => AccessLexer.Tokenize(sql),
        _ => NzLexer.Tokenize(sql),
    };

    public static NzSqlParser CreateParser(Token<NzToken>[] tokens, SqlDialect dialect) => dialect switch
    {
        SqlDialect.Oracle => new OracleSqlParser(tokens),
        SqlDialect.Db2 => new Db2SqlParser(tokens),
        SqlDialect.Mssql => new MssqlSqlParser(tokens),
        SqlDialect.MySql => new MySqlSqlParser(tokens),
        SqlDialect.PostgreSql => new PostgreSqlSqlParser(tokens),
        _ => new NzSqlParser(tokens),
    };

    public static SqlDialect ParseName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SqlDialect.Netezza;
        if (value.Equals("oracle", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.Oracle;
        if (value.Equals("db2", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.Db2;
        if (value.Equals("mssql", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.Mssql;
        if (value.Equals("mysql", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.MySql;
        if (value.Equals("access", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.Access;
        if (value.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("pg", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.PostgreSql;
        return SqlDialect.Netezza;
    }
}
