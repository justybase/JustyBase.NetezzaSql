namespace JustyBase.NetezzaSqlParser.Dialects;

/// <summary>
/// SQL dialect used by the lexer, parser, linter and authoring services.
/// Netezza is the default; Oracle and Db2 add dialect-specific lexical forms,
/// statement parsing, quality rules and authoring catalogs.
/// </summary>
public enum SqlDialect
{
    Netezza,
    Oracle,
    Db2,
}
