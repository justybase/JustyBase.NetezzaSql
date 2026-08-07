using System.Text.RegularExpressions;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace JustyBase.NetezzaSqlParser.Lexer;

/// <summary>
/// Lexer for Microsoft Access SQL.
///
/// Access differs from ANSI in a few lexical ways which are registered before
/// the shared Netezza chain so they win:
///   - identifiers are quoted with [...] (or backticks), not double quotes
///   - double quotes delimit strings just like single quotes ("" escapes)
///   - date literals use the #...# form
///   - '&amp;' is the string-concatenation operator
///   - TOP [n] PERCENT / DISTINCTROW / TRANSFORM / PIVOT keywords
///   - '*' '?' '#' '[...]' wildcards only appear inside LIKE string literals,
///     so they need no dedicated tokens
/// </summary>
public static class AccessLexer
{
    private static readonly TokenizerBuilder<NzToken> Builder = NzLexer.AppendSharedTokens(
        new TokenizerBuilder<NzToken>()
            .Match(Span.Regex(@"^\[(?:[^\]]|\]\])*\]"), NzToken.AccessBracketedIdentifier)
            .Match(Span.Regex(@"^`[^`]*`"), NzToken.AccessBacktickIdentifier)
            .Match(Span.Regex(@"^#[^#]*#"), NzToken.AccessDateLiteral)
            // Access double-quoted strings (must precede the shared "quoted identifier")
            .Match(Span.Regex(@"^""(?:[^""]|"""")*"""), NzToken.StringLiteral)
            .Match(Span.EqualTo("&"), NzToken.AccessAmpersand)
            .Match(NzLexer.Kw("TOP"), NzToken.AccessTop)
            .Match(NzLexer.Kw("PERCENT"), NzToken.AccessPercent)
            .Match(NzLexer.Kw("DISTINCTROW"), NzToken.AccessDistinctRow)
            .Match(NzLexer.Kw("TRANSFORM"), NzToken.AccessTransform)
            .Match(NzLexer.Kw("PIVOT"), NzToken.AccessPivot));

    public static Tokenizer<NzToken> Instance { get; } = Builder.Build();

    public static TokenList<NzToken> Tokenize(string input) => Instance.Tokenize(input);

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
