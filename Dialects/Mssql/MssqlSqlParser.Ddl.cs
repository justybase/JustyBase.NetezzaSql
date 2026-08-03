using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

public partial class MssqlSqlParser
{
    // ====== Netezza-only Rejections ======

    protected override (DistributeClause? Distribute, OrganizeClause? Organize) ParseTableStorageClauses()
    {
        var k = Peek().Kind;
        if (k is NzToken.Distribute or NzToken.Organize)
        {
            AddParserError($"{k} is Netezza-only syntax and is not supported in MSSQL",
                Peek(), "PAR001");
        }
        return base.ParseTableStorageClauses();
    }

    // ====== IDENTITY columns ======
    // CREATE TABLE t (id INT IDENTITY(1,1) PRIMARY KEY, ...)

    protected override bool IsDialectColumnClauseStart() =>
        Peek().Kind == NzToken.Identifier &&
        Peek().ToStringValue().Equals("IDENTITY", StringComparison.OrdinalIgnoreCase);

    protected override bool TryParseDialectColumnClause()
    {
        if (Peek().Kind != NzToken.Identifier ||
            !Peek().ToStringValue().Equals("IDENTITY", StringComparison.OrdinalIgnoreCase))
            return false;

        Advance(); // IDENTITY
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            if (Peek().Kind == NzToken.NumberLiteral)
            {
                Advance();
                if (Peek().Kind == NzToken.Comma)
                {
                    Advance();
                    if (Peek().Kind == NzToken.NumberLiteral)
                        Advance();
                }
            }
            if (Peek().Kind == NzToken.RParen)
                Advance();
        }
        return true;
    }
}
