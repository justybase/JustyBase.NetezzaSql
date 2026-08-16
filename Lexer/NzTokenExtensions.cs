namespace JustyBase.NetezzaSqlParser.Lexer;

using Superpower.Model;

public static class NzTokenExtensions
{
    public static bool IsIdentifierLike(this NzToken token) =>
        token is NzToken.Identifier
            or NzToken.QuotedIdentifier
            or NzToken.MySqlBacktickIdentifier
            or NzToken.MssqlBracketedIdentifier
            or NzToken.AccessBracketedIdentifier
            or NzToken.AccessBacktickIdentifier
            or NzToken.SqliteBracketedIdentifier;

    public static bool IsParameterLike(this NzToken token) =>
        token is NzToken.Parameter
            or NzToken.DollarNumber
            or NzToken.OracleBindVariable
            or NzToken.MssqlVariable
            or NzToken.AccessNamedParameter;

    public static string ToIdentifierText(this Token<NzToken> token) =>
        token.ToStringValue().UnquoteIdentifier();

    public static string UnquoteIdentifier(this string value)
    {
        if (value.Length < 2)
            return value;

        if (value[0] == '[' && value[^1] == ']')
            return value[1..^1].Replace("]]", "]", StringComparison.Ordinal);
        if (value[0] == '`' && value[^1] == '`')
            return value[1..^1].Replace("``", "`", StringComparison.Ordinal);
        if (value[0] == '"' && value[^1] == '"')
            return value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);

        return value;
    }
}
