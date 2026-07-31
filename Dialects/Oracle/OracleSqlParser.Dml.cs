using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

public partial class OracleSqlParser
{
    // ====== DML RETURNING INTO ======

    protected override InsertStatement? ParseInsert()
    {
        var insert = base.ParseInsert();
        if (insert is null) return null;
        return ParseReturningClause(insert);
    }

    protected override UpdateStatement? ParseUpdate()
    {
        var update = base.ParseUpdate();
        if (update is null) return null;
        return ParseReturningClause(update);
    }

    protected override DeleteStatement? ParseDelete()
    {
        var delete = base.ParseDelete();
        if (delete is null) return null;
        return ParseReturningClause(delete);
    }

    private InsertStatement ParseReturningClause(InsertStatement insert)
    {
        if (Peek().Kind != NzToken.OracleReturning) return insert;
        return insert with { Returning = ParseReturningInto(Peek()) };
    }

    private UpdateStatement ParseReturningClause(UpdateStatement update)
    {
        if (Peek().Kind != NzToken.OracleReturning) return update;
        return update with { Returning = ParseReturningInto(Peek()) };
    }

    private DeleteStatement ParseReturningClause(DeleteStatement delete)
    {
        if (Peek().Kind != NzToken.OracleReturning) return delete;
        return delete with { Returning = ParseReturningInto(Peek()) };
    }

    // RETURNING column [, ...] INTO :variable [, ...]
    private OracleReturningClause ParseReturningInto(Token<NzToken> returningTok)
    {
        Advance(); // RETURNING

        var columns = new List<string>();
        columns.Add(ExpectNameToken().ToStringValue());
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            columns.Add(ExpectNameToken().ToStringValue());
        }

        Expect(NzToken.Into);

        var intoVariables = new List<string>();
        intoVariables.Add(ParseReturningIntoVariable());
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            intoVariables.Add(ParseReturningIntoVariable());
        }

        return new OracleReturningClause(FromToken(returningTok), columns, intoVariables);
    }

    private string ParseReturningIntoVariable() =>
        Peek().Kind == NzToken.OracleBindVariable
            ? Advance().ToStringValue()
            : ExpectNameToken().ToStringValue();
}
