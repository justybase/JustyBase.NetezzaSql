using System.Text.RegularExpressions;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace JustyBase.NetezzaSqlParser.Lexer;

/// <summary>
/// Port of src/dialects/db2/sql/lexer.ts. Db2 multi-word phrases are registered
/// before the shared Netezza chain so they win over Identifier / WITH / FOR.
/// </summary>
public static class Db2Lexer
{
    private static readonly TokenizerBuilder<NzToken> Builder = NzLexer.AppendSharedTokens(
        new TokenizerBuilder<NzToken>()
            .Match(Span.Regex(@"OPTIMIZE\s+FOR\b", RegexOptions.IgnoreCase), NzToken.Db2OptimizeFor, requireDelimiters: false)
            .Match(Span.Regex(@"WITH\s+UR\b", RegexOptions.IgnoreCase), NzToken.Db2WithUr, requireDelimiters: false)
            .Match(Span.Regex(@"WITH\s+CS\b", RegexOptions.IgnoreCase), NzToken.Db2WithCs, requireDelimiters: false)
            .Match(Span.Regex(@"WITH\s+RS\b", RegexOptions.IgnoreCase), NzToken.Db2WithRs, requireDelimiters: false)
            .Match(Span.Regex(@"WITH\s+RR\b", RegexOptions.IgnoreCase), NzToken.Db2WithRr, requireDelimiters: false)
            .Match(Span.Regex(@"FOR\s+READ\s+ONLY\b", RegexOptions.IgnoreCase), NzToken.Db2ForReadOnly, requireDelimiters: false)
            .Match(Span.Regex(@"FOR\s+UPDATE\b", RegexOptions.IgnoreCase), NzToken.Db2ForUpdate, requireDelimiters: false)
            .Match(Span.Regex(@"FINAL\s+TABLE\b", RegexOptions.IgnoreCase), NzToken.Db2FinalTable, requireDelimiters: false)
            .Match(Span.Regex(@"OLD\s+TABLE\b", RegexOptions.IgnoreCase), NzToken.Db2OldTable, requireDelimiters: false)
            .Match(Span.Regex(@"NEW\s+TABLE\b", RegexOptions.IgnoreCase), NzToken.Db2NewTable, requireDelimiters: false)
            .Match(Span.Regex(@"MODIFIED\s+BY\b", RegexOptions.IgnoreCase), NzToken.Db2ModifiedBy, requireDelimiters: false)
            .Match(Span.Regex(@"DECLARE\s+GLOBAL\s+TEMPORARY\b", RegexOptions.IgnoreCase), NzToken.Db2DeclareGlobalTemporary, requireDelimiters: false)
            .Match(Span.Regex(@"GENERATED\s+ALWAYS\b", RegexOptions.IgnoreCase), NzToken.Db2GeneratedAlways, requireDelimiters: false)
            .Match(Span.Regex(@"GENERATED\s+BY\s+DEFAULT\b", RegexOptions.IgnoreCase), NzToken.Db2GeneratedByDefault, requireDelimiters: false)
            .Match(Span.Regex(@"ORGANIZE\s+BY\b", RegexOptions.IgnoreCase), NzToken.Db2OrganizeBy, requireDelimiters: false)
            .Match(Span.Regex(@"DATA\s+CAPTURE\b", RegexOptions.IgnoreCase), NzToken.Db2DataCapture, requireDelimiters: false)
            .Match(Span.Regex(@"CURRENT\s+SCHEMA\b", RegexOptions.IgnoreCase), NzToken.Db2CurrentSchema, requireDelimiters: false)
            .Match(Span.Regex(@"CURRENT\s+SERVER\b", RegexOptions.IgnoreCase), NzToken.Db2CurrentServer, requireDelimiters: false)
            .Match(Span.Regex(@"CURRENT\s+DATE\b", RegexOptions.IgnoreCase), NzToken.Db2CurrentDate, requireDelimiters: false)
            .Match(Span.Regex(@"CURRENT\s+TIME\b", RegexOptions.IgnoreCase), NzToken.Db2CurrentTime, requireDelimiters: false)
            .Match(Span.Regex(@"CURRENT\s+TIMESTAMP\b", RegexOptions.IgnoreCase), NzToken.Db2CurrentTimestamp, requireDelimiters: false)
            .Match(Span.Regex(@"CURRENT\s+USER\b", RegexOptions.IgnoreCase), NzToken.Db2CurrentUser, requireDelimiters: false)
            .Match(Span.Regex(@"LANGUAGE\s+SQL\b", RegexOptions.IgnoreCase), NzToken.Db2LanguageSql, requireDelimiters: false)
            .Match(NzLexer.Kw("NICKNAME"), NzToken.Db2Nickname)
            .Match(NzLexer.Kw("IDENTITY"), NzToken.Db2Identity));

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
