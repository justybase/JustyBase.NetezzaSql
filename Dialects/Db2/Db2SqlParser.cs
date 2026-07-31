using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

/// <summary>
/// Db2 LUW dialect parser. Port of src/dialects/db2/sql/parser.ts.
/// Extends the shared Netezza parser with OPTIMIZE FOR / isolation /
/// FOR READ ONLY, FINAL|OLD|NEW TABLE sources, DGTT, CREATE ALIAS/NICKNAME,
/// and thin CREATE PROCEDURE units, while rejecting Netezza-only syntax.
/// </summary>
public partial class Db2SqlParser : NzSqlParser
{
    public Db2SqlParser(Token<NzToken>[] tokens) : base(tokens)
    {
    }

    protected override bool SupportsEmptyQualifiedNameSegment => false;

    public override Statement? Parse()
    {
        SkipSemicolons();
        if (_pos >= _tokens.Length)
            return null;
        var k = Peek().Kind;

        Statement? result;
        if (k == NzToken.Db2DeclareGlobalTemporary)
            result = ParseDeclareGlobalTempTable();
        else if (k == NzToken.Create)
            result = ParseDb2CreateOrFallback();
        else if (k is NzToken.Groom or NzToken.Generate)
        {
            AddParserError($"{k} is Netezza-only syntax and is not supported in Db2",
                Peek(), "PAR001");
            Advance();
            result = null;
        }
        else
        {
            result = base.Parse();
        }

        if (result is null)
            SynchronizeStatement();
        return result;
    }

    private Statement? ParseDb2CreateOrFallback()
    {
        var look = Peek(1).Kind == NzToken.Or ? 3 : 1;
        var obj = Peek(look).Kind;

        if (obj == NzToken.External)
        {
            AddParserError("CREATE EXTERNAL TABLE is Netezza-only syntax and is not supported in Db2",
                Peek(look), "PAR001");
            Advance();
            return null;
        }

        if (obj == NzToken.Synonym)
        {
            AddParserError("CREATE SYNONYM is not supported on Db2 LUW; use CREATE ALIAS",
                Peek(look), "PAR001");
            Advance();
            return null;
        }

        if (StartsDb2Procedure())
            return ParseDb2ProcedureUnit();

        if (obj == NzToken.Alias)
            return ParseCreateAlias();

        if (obj == NzToken.Db2Nickname)
            return ParseCreateNickname();

        return base.Parse();
    }

    private bool StartsDb2Procedure()
    {
        if (Peek().Kind != NzToken.Create)
            return false;
        if (Peek(1).Kind == NzToken.Procedure)
            return true;
        return Peek(1).Kind == NzToken.Or
            && Peek(2).Kind == NzToken.Replace
            && Peek(3).Kind == NzToken.Procedure;
    }

    private Db2DeclareGlobalTempTableStatement ParseDeclareGlobalTempTable()
    {
        var start = Expect(NzToken.Db2DeclareGlobalTemporary);
        Expect(NzToken.Table);
        var (name, _) = ParseTableName();
        ParseCommandTail();
        return new Db2DeclareGlobalTempTableStatement(FromToken(start), name);
    }

    private Db2CreateAliasStatement ParseCreateAlias()
    {
        var start = Expect(NzToken.Create);
        Expect(NzToken.Alias);
        var (alias, _) = ParseTableName();
        Expect(NzToken.For);
        var (target, _) = ParseTableName();
        return new Db2CreateAliasStatement(FromToken(start), alias, target);
    }

    private Db2CreateNicknameStatement ParseCreateNickname()
    {
        var start = Expect(NzToken.Create);
        Expect(NzToken.Db2Nickname);
        var (nickname, _) = ParseTableName();
        Expect(NzToken.For);
        var (target, _) = ParseTableName();
        ParseCommandTail();
        return new Db2CreateNicknameStatement(FromToken(start), nickname, target);
    }

    private Db2ProcedureUnitStatement? ParseDb2ProcedureUnit()
    {
        var startTok = Peek();
        var startIdx = _pos;

        // Header tokens until BEGIN (thin opaque unit).
        while (Peek().Kind != NzToken.Begin && Peek().Kind != NzToken.Unknown)
            Advance();

        if (Peek().Kind != NzToken.Begin)
        {
            AddParserError("Unterminated Db2 PROCEDURE unit (expected BEGIN)", startTok, "PAR001");
            return null;
        }

        Advance(); // BEGIN
        var depth = 1;
        while (depth > 0 && Peek().Kind != NzToken.Unknown)
        {
            if (Peek().Kind == NzToken.Begin)
                depth++;
            else if (Peek().Kind == NzToken.End)
            {
                depth--;
                if (depth == 0)
                {
                    Advance(); // END
                    if (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
                        Advance();
                    break;
                }
            }
            Advance();
        }

        if (depth != 0)
        {
            AddParserError("Unterminated Db2 PROCEDURE unit (expected END)", startTok, "PAR001");
            return null;
        }

        var tokens = _tokens[startIdx.._pos].ToArray();
        // Name: skip CREATE [OR REPLACE] PROCEDURE <name>
        var namePos = 1;
        if (tokens.Length > 3 && tokens[1].Kind == NzToken.Or)
            namePos = 3;
        TableName name;
        if (namePos + 1 < tokens.Length &&
            tokens[namePos].Kind == NzToken.Procedure &&
            tokens[namePos + 1].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
        {
            name = new TableName(StripQuotes(tokens[namePos + 1].ToStringValue()));
        }
        else
        {
            name = new TableName("procedure");
        }

        return new Db2ProcedureUnitStatement(FromToken(startTok), name, tokens);
    }
}
