using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

public partial class OracleSqlParser
{
    // ====== Oracle Type Suffixes ======

    // TIMESTAMP WITH [LOCAL] TIME ZONE and INTERVAL YEAR/DAY TO MONTH/SECOND
    // are consumed as type-name suffixes after the base data type name.

    protected override void ParseDataTypeSuffix()
    {
        if (Peek().Kind != NzToken.With) return;
        var afterWith = Peek(1);

        if (afterWith.Kind == NzToken.Identifier &&
            afterWith.ToStringValue().Equals("TIME", StringComparison.OrdinalIgnoreCase))
        {
            Advance(); Advance(); // WITH TIME
            if (Peek().Kind == NzToken.Identifier &&
                Peek().ToStringValue().Equals("ZONE", StringComparison.OrdinalIgnoreCase))
                Advance();
        }
        else if (afterWith.Kind == NzToken.Identifier &&
                 afterWith.ToStringValue().Equals("LOCAL", StringComparison.OrdinalIgnoreCase) &&
                 Peek(2).Kind == NzToken.Identifier &&
                 Peek(2).ToStringValue().Equals("TIME", StringComparison.OrdinalIgnoreCase))
        {
            Advance(); Advance(); Advance(); // WITH LOCAL TIME
            if (Peek().Kind == NzToken.Identifier &&
                Peek().ToStringValue().Equals("ZONE", StringComparison.OrdinalIgnoreCase))
                Advance();
        }
    }

    // ====== Netezza-only Rejections ======

    protected override (DistributeClause? Distribute, OrganizeClause? Organize) ParseTableStorageClauses()
    {
        var k = Peek().Kind;
        if (k is NzToken.Distribute or NzToken.Organize)
        {
            AddParserError($"{k} is Netezza-only syntax and is not supported in Oracle",
                Peek(), "PAR001");
        }
        return base.ParseTableStorageClauses();
    }

    protected override bool SupportsEmptyQualifiedNameSegment => false;
}
