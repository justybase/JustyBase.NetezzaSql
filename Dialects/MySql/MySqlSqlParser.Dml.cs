using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Parser;

public sealed partial class MySqlSqlParser
{
    protected override InsertStatement? ParseInsert()
    {
        var insertToken = Expect(NzToken.Insert);
        var ignore = IsWord(Peek(), "IGNORE");
        if (ignore)
            Advance();

        Expect(NzToken.Into);
        var (table, _) = ParseTableName();

        IReadOnlyList<string>? columns = null;
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var list = new List<string>();
            if (Peek().Kind != NzToken.RParen)
            {
                list.Add(StripQuotes(ExpectNameToken().ToStringValue()));
                while (Peek().Kind == NzToken.Comma)
                {
                    Advance();
                    list.Add(StripQuotes(ExpectNameToken().ToStringValue()));
                }
            }
            Expect(NzToken.RParen);
            columns = list;
        }

        IReadOnlyList<IReadOnlyList<Expression>>? values = null;
        SelectStatement? source = null;
        if (Peek().Kind == NzToken.Values)
        {
            Advance();
            var rows = new List<IReadOnlyList<Expression>>();
            do
            {
                Expect(NzToken.LParen);
                var row = new List<Expression>();
                if (Peek().Kind != NzToken.RParen)
                {
                    row.Add(ParseExpression());
                    while (Peek().Kind == NzToken.Comma)
                    {
                        Advance();
                        row.Add(ParseExpression());
                    }
                }
                Expect(NzToken.RParen);
                rows.Add(row);
                if (Peek().Kind != NzToken.Comma)
                    break;
                Advance();
            } while (Peek().Kind == NzToken.LParen);
            values = rows;
        }
        else if (Peek().Kind == NzToken.Select)
        {
            source = ParseSelectStatement();
        }
        else
        {
            AddParserError("Expected VALUES or SELECT after INSERT", Peek(), "PAR117");
            return null;
        }

        IReadOnlyList<Token<NzToken>>? duplicate = null;
        if (Peek().Kind == NzToken.On && IsWord(Peek(1), "DUPLICATE") && Peek(2).Kind == NzToken.Key
            && Peek(3).Kind == NzToken.Update)
        {
            Advance(); Advance(); Advance(); Advance();
            var start = _pos;
            var depth = 0;
            while (Peek().Kind is not (NzToken.Semicolon or NzToken.Unknown))
            {
                if (Peek().Kind == NzToken.LParen) depth++;
                else if (Peek().Kind == NzToken.RParen && depth > 0) depth--;
                Advance();
            }
            duplicate = _tokens[start.._pos];
        }
        else if (Peek().Kind == NzToken.On && IsWord(Peek(1), "DUPLICATE"))
        {
            AddParserError("Expected ON DUPLICATE KEY UPDATE", Peek(), "PAR001");
        }

        return new InsertStatement(FromToken(insertToken), table, columns, values, source,
            MySqlIgnore: ignore, MySqlOnDuplicateKeyUpdateTokens: duplicate);
    }

    private static bool IsWord(Token<NzToken> token, string value) =>
        string.Equals(token.ToStringValue(), value, StringComparison.OrdinalIgnoreCase);
}
