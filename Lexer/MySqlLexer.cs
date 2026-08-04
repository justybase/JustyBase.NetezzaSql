using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace JustyBase.NetezzaSqlParser.Lexer;

/// <summary>MySQL 8 lexical additions.</summary>
public static class MySqlLexer
{
    private static readonly TokenizerBuilder<NzToken> Builder = NzLexer.AppendSharedTokens(
        new TokenizerBuilder<NzToken>()
            // MySQL hash comments must win over the shared Hash token.
            .Ignore(Span.Regex(@"^#[^\r\n]*"))
            .Match(Span.Regex(@"^`(?:``|[^`])*`"), NzToken.MySqlBacktickIdentifier));

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
