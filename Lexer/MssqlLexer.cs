using System.Text.RegularExpressions;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace JustyBase.NetezzaSqlParser.Lexer;

/// <summary>
/// Port of src/dialects/mssql/sql/lexer.ts. T-SQL lexical additions are
/// registered before the shared Netezza chain so they win over Identifier /
/// @SET / LBracket-RBracket. Strings are the shared '(...)''-escaped literal
/// (the N'...' prefix lexes as an Identifier followed by a StringLiteral, as
/// in the reference lexer); #temp/##temp tables are intentionally not lexed.
/// </summary>
public static class MssqlLexer
{
    private static readonly TokenizerBuilder<NzToken> Builder = NzLexer.AppendSharedTokens(
        new TokenizerBuilder<NzToken>()
            .Match(Span.Regex(@"^@@?[A-Za-z_][\w$]*"), NzToken.MssqlVariable)
            .Match(Span.Regex(@"^\[(?:[^\]]|\]\])*\]"), NzToken.MssqlBracketedIdentifier)
            .Match(Span.Regex(@"CROSS\s+APPLY\b", RegexOptions.IgnoreCase), NzToken.MssqlCrossApply, requireDelimiters: false)
            .Match(Span.Regex(@"OUTER\s+APPLY\b", RegexOptions.IgnoreCase), NzToken.MssqlOuterApply, requireDelimiters: false)
            .Match(NzLexer.Kw("TOP"), NzToken.MssqlTop)
            .Match(NzLexer.Kw("OUTPUT"), NzToken.MssqlOutput)
            .Match(NzLexer.Kw("GO"), NzToken.MssqlGo)
            .Match(NzLexer.Kw("TRY"), NzToken.MssqlTry)
            .Match(NzLexer.Kw("CATCH"), NzToken.MssqlCatch)
            .Match(NzLexer.Kw("PROC"), NzToken.MssqlProc)
            .Match(NzLexer.Kw("RECOMPILE"), NzToken.MssqlRecompile)
            .Match(NzLexer.Kw("ENCRYPTION"), NzToken.MssqlEncryption));

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
