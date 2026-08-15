using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace JustyBase.NetezzaSqlParser.Lexer;

/// <summary>
/// SQLite lexical additions layered over the shared SQL lexer.
///
/// SQLite differs from ANSI in a few lexical ways which are registered before
/// the shared Netezza chain so they win:
///   - identifiers may be quoted with [brackets] or `backticks` in addition to
///     double quotes (the shared chain already handles double quotes)
///   - blob literals use the X'AB12' form
///   - JSON operators -&gt; and -&gt;&gt; (shared tokens with PostgreSQL)
///   - ATTACH / AUTOINCREMENT / COLLATE / CONFLICT / DETACH / DO / GENERATED /
///     NOTHING / PRAGMA / RETURNING / SAVEPOINT / STRICT / VACUUM / VIRTUAL /
///     WINDOW / WITHOUT are dedicated keywords
/// </summary>
public static class SqliteLexer
{
    private static readonly TokenizerBuilder<NzToken> Builder = NzLexer.AppendSharedTokens(
        new TokenizerBuilder<NzToken>()
            .Match(Span.Regex(@"^\[(?:[^\]]|\]\])*\]"), NzToken.SqliteBracketedIdentifier)
            .Match(Span.Regex(@"^`[^`]*`"), NzToken.MySqlBacktickIdentifier)
            // Blob literals must precede the shared string literal chain.
            .Match(Span.Regex(@"^[xX]'(?:[^']|'')*'"), NzToken.SqliteBlobLiteral)
            .Match(Span.EqualTo("->>"), NzToken.JsonTextArrow)
            .Match(Span.EqualTo("->"), NzToken.JsonArrow)
            .Match(NzLexer.Kw("AUTOINCREMENT"), NzToken.SqliteAutoincrement)
            .Match(NzLexer.Kw("ATTACH"), NzToken.SqliteAttach)
            .Match(NzLexer.Kw("COLLATE"), NzToken.SqliteCollate)
            .Match(NzLexer.Kw("CONFLICT"), NzToken.SqliteConflict)
            .Match(NzLexer.Kw("DETACH"), NzToken.SqliteDetach)
            .Match(NzLexer.Kw("DO"), NzToken.SqliteDo)
            .Match(NzLexer.Kw("GENERATED"), NzToken.SqliteGenerated)
            .Match(NzLexer.Kw("NOTHING"), NzToken.SqliteNothing)
            .Match(NzLexer.Kw("PRAGMA"), NzToken.SqlitePragma)
            .Match(NzLexer.Kw("RETURNING"), NzToken.SqliteReturning)
            .Match(NzLexer.Kw("SAVEPOINT"), NzToken.SqliteSavepoint)
            .Match(NzLexer.Kw("STRICT"), NzToken.SqliteStrict)
            .Match(NzLexer.Kw("VACUUM"), NzToken.SqliteVacuum)
            .Match(NzLexer.Kw("VIRTUAL"), NzToken.SqliteVirtual)
            .Match(NzLexer.Kw("WINDOW"), NzToken.SqliteWindow)
            .Match(NzLexer.Kw("WITHOUT"), NzToken.SqliteWithout));

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
