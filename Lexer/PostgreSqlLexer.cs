using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace JustyBase.NetezzaSqlParser.Lexer;

/// <summary>PostgreSQL lexical additions layered over the shared SQL lexer.</summary>
public static class PostgreSqlLexer
{
    private static readonly TokenizerBuilder<NzToken> Builder = NzLexer.AppendSharedTokens(
        new TokenizerBuilder<NzToken>()
            // This token deliberately wins over the Netezza keyword chain so
            // unsupported storage commands become parser diagnostics.
            .Match(Span.Regex(@"(?i)\b(?:DISTRIBUTE|ORGANIZE|GROOM|VERSIONS|RECLAIM|BACKUPSET)\b"),
                NzToken.PostgreSqlUnsupportedNetezza)
            .Match(Span.EqualTo("#>>"), NzToken.PostgreSqlJsonTextPath)
            .Match(Span.EqualTo("#>"), NzToken.PostgreSqlJsonPath)
            .Match(Span.EqualTo("->>"), NzToken.PostgreSqlJsonTextArrow)
            .Match(Span.EqualTo("->"), NzToken.PostgreSqlJsonArrow)
            .Match(NzLexer.Kw("LATERAL"), NzToken.PostgreSqlLateral)
            .Match(NzLexer.Kw("RETURNING"), NzToken.PostgreSqlReturning)
            .Match(NzLexer.Kw("CONFLICT"), NzToken.PostgreSqlConflict)
            .Match(NzLexer.Kw("DO"), NzToken.PostgreSqlDo)
            .Match(NzLexer.Kw("NOTHING"), NzToken.PostgreSqlNothing)
            .Match(NzLexer.Kw("ARRAY"), NzToken.PostgreSqlArray));

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
