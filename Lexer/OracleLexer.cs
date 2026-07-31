using System.Text.RegularExpressions;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace JustyBase.NetezzaSqlParser.Lexer;

// Port of the reference dialect lexer src/dialects/oracle/sql/lexer.ts.
// Oracle-specific tokens are registered before the shared Netezza chain so
// exact Oracle keywords (and the @ dblink marker) win over shared tokens,
// e.g. "@" beats "@SET" and "ORDER SIBLINGS BY" beats "ORDER BY".
public static class OracleLexer
{
    private static readonly TokenizerBuilder<NzToken> Builder = NzLexer.AppendSharedTokens(
        new TokenizerBuilder<NzToken>()
            // OracleQualifiedFunction must come first: it is a broader identifier
            // form that only matches when a parenthesized call follows.
            .Match(Span.Regex(@"^[A-Za-z_][A-Za-z0-9_$#]*(?:\.[A-Za-z_][A-Za-z0-9_$#]*)+(?=\s*\()"), NzToken.OracleQualifiedFunction)

            // Multi-word keyword (requireDelimiters: false since it contains whitespace)
            .Match(Span.Regex(@"ORDER\s+SIBLINGS\s+BY\b", RegexOptions.IgnoreCase), NzToken.OracleOrderSiblingsBy, requireDelimiters: false)

            // Oracle keywords
            .Match(NzLexer.Kw("CONNECT"), NzToken.OracleConnect)
            .Match(NzLexer.Kw("BY"), NzToken.OracleBy)
            .Match(NzLexer.Kw("PRIOR"), NzToken.OraclePrior)
            .Match(NzLexer.Kw("NOCYCLE"), NzToken.OracleNocycle)
            .Match(NzLexer.Kw("PIVOT"), NzToken.OraclePivot)
            .Match(NzLexer.Kw("UNPIVOT"), NzToken.OracleUnpivot)
            .Match(NzLexer.Kw("RETURNING"), NzToken.OracleReturning)
            .Match(NzLexer.Kw("PRAGMA"), NzToken.OraclePragma)

            // Bind variables (:name or :name.subname). Registered before the
            // shared :: and := operators; "::" and ":=" cannot match because the
            // character following ':' is not a letter or underscore.
            .Match(Span.Regex(@"^:[A-Za-z_][A-Za-z0-9_$#]*(?:\.[A-Za-z_][A-Za-z0-9_$#]*)*"), NzToken.OracleBindVariable)

            // @ (database link marker). Registered before the shared @SET token.
            .Match(Span.EqualTo("@"), NzToken.OracleAtSign));

    public static Tokenizer<NzToken> Instance { get; } = Builder.Build();

    public static TokenList<NzToken> Tokenize(string input)
    {
        return Instance.Tokenize(input);
    }

    public static bool TryTokenize(string input, out TokenList<NzToken> result)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Trim().Length == 0)
        {
            result = new TokenList<NzToken>(Array.Empty<Token<NzToken>>());
            return true;
        }

        try
        {
            result = Instance.Tokenize(input);
            return result.Any();
        }
        catch (Superpower.ParseException)
        {
            result = new TokenList<NzToken>(Array.Empty<Token<NzToken>>());
            return false;
        }
    }
}
