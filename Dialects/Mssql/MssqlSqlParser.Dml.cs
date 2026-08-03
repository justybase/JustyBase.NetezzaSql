using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

public partial class MssqlSqlParser
{
    // ====== DML OUTPUT clause ======
    // T-SQL OUTPUT is an opaque token range (like Oracle RETURNING INTO is
    // structured). INSERT carries OUTPUT between the column list and VALUES;
    // UPDATE/DELETE carry it after the SET/alias and before the WHERE tail.

    private IReadOnlyList<Token<NzToken>> ParseOutputClause(params NzToken[] stopKinds)
    {
        var start = _pos;
        Expect(NzToken.MssqlOutput);
        var depth = 0;
        while (Peek().Kind != NzToken.Semicolon && Peek().Kind != NzToken.Unknown)
        {
            if (depth == 0 && Array.IndexOf(stopKinds, Peek().Kind) >= 0)
                break;
            if (Peek().Kind == NzToken.LParen) depth++;
            else if (Peek().Kind == NzToken.RParen)
            {
                if (depth == 0) break;
                depth--;
            }
            Advance();
        }
        return _tokens[start.._pos];
    }

    // ====== INSERT ======
    // Full T-SQL shape: INSERT INTO t (cols) OUTPUT ... VALUES (...) | SELECT ...

    protected override InsertStatement? ParseInsert()
    {
        var insertTok = Expect(NzToken.Insert);

        if (Peek().Kind != NzToken.Into)
        {
            AddParserError("Expected INTO after INSERT, got " + DescribeToken(Peek().Kind),
                Peek(), "PAR114");
            return null;
        }
        Expect(NzToken.Into);

        var (table, _) = ParseTableName();

        IReadOnlyList<string>? columns = null;
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var colList = new List<string>();
            if (Peek().Kind != NzToken.RParen)
            {
                colList.Add(ExpectNameToken().ToStringValue());
                while (Peek().Kind == NzToken.Comma)
                {
                    Advance();
                    colList.Add(ExpectNameToken().ToStringValue());
                }
            }
            else
            {
                AddParserError("Empty INSERT column list is not allowed", Peek(), "PAR119");
            }
            Expect(NzToken.RParen);
            columns = colList;
        }

        IReadOnlyList<Token<NzToken>>? outputTokens = null;
        if (Peek().Kind == NzToken.MssqlOutput)
            outputTokens = ParseOutputClause(NzToken.Values, NzToken.Select, NzToken.With);

        IReadOnlyList<IReadOnlyList<Expression>>? values = null;
        SelectStatement? sourceQuery = null;

        if (Peek().Kind == NzToken.With)
        {
            Advance();
            if (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
            {
                Advance();
                if (Peek().Kind == NzToken.LParen)
                {
                    Advance();
                    ParseSelectStatement();
                    Expect(NzToken.RParen);
                }
            }
            if (Peek().Kind == NzToken.Select)
                sourceQuery = ParseSelectStatement();
        }
        else if (Peek().Kind == NzToken.Values)
        {
            Advance();
            var rows = new List<IReadOnlyList<Expression>>();

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
            else
            {
                AddParserError("Empty row in VALUES is not allowed", Peek(), "PAR119");
            }
            Expect(NzToken.RParen);
            rows.Add(row);

            while (Peek().Kind == NzToken.Comma)
            {
                Advance();
                Expect(NzToken.LParen);
                row = new List<Expression>();
                if (Peek().Kind != NzToken.RParen)
                {
                    row.Add(ParseExpression());
                    while (Peek().Kind == NzToken.Comma)
                    {
                        Advance();
                        row.Add(ParseExpression());
                    }
                }
                else
                {
                    AddParserError("Empty row in VALUES is not allowed", Peek(), "PAR119");
                }
                Expect(NzToken.RParen);
                rows.Add(row);
            }

            values = rows;
        }
        else if (Peek().Kind == NzToken.Select)
        {
            sourceQuery = ParseSelectStatement();
        }
        else
        {
            AddParserError("Expected VALUES or SELECT after INSERT", Peek(), "PAR117");
            return null;
        }

        var insert = new InsertStatement(FromToken(insertTok), table, columns, values, sourceQuery);
        return insert with { OutputTokens = outputTokens };
    }

    // ====== UPDATE / DELETE ======
    // OUTPUT appears after the SET list / target alias and before WHERE.

    protected override UpdateStatement? ParseUpdate()
    {
        var update = base.ParseUpdate();
        if (update is null) return null;
        if (Peek().Kind != NzToken.MssqlOutput) return update;

        // T-SQL order: SET list, OUTPUT ..., FROM ..., WHERE ...
        // base.ParseUpdate stops before OUTPUT, so FROM/WHERE must be re-parsed here.
        var outputTokens = ParseOutputClause(NzToken.From, NzToken.Where);
        IReadOnlyList<TableReference>? from = update.From;
        if (Peek().Kind == NzToken.From)
        {
            Advance();
            from = ParseTableReferences();
        }
        Expression? where = update.Where;
        if (Peek().Kind == NzToken.Where)
        {
            Advance();
            where = ParseExpression();
        }
        return update with { From = from, Where = where, OutputTokens = outputTokens };
    }

    protected override DeleteStatement? ParseDelete()
    {
        var delete = base.ParseDelete();
        if (delete is null) return null;
        if (Peek().Kind != NzToken.MssqlOutput) return delete;

        // T-SQL order: DELETE [FROM] target, OUTPUT ..., FROM ..., WHERE ...
        var outputTokens = ParseOutputClause(NzToken.From, NzToken.Where);
        IReadOnlyList<TableReference>? from = delete.From;
        if (Peek().Kind == NzToken.From)
        {
            Advance();
            from = ParseTableReferences();
        }
        Expression? where = delete.Where;
        if (Peek().Kind == NzToken.Where)
        {
            Advance();
            where = ParseExpression();
        }
        return delete with { From = from, Where = where, OutputTokens = outputTokens };
    }
}
