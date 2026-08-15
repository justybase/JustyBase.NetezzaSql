using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Parser;

public sealed partial class SqliteSqlParser
{
    // ====== INSERT / UPSERT ======

    protected override InsertStatement? ParseInsert()
    {
        // INSERT [OR ROLLBACK|ABORT|REPLACE|FAIL|IGNORE] INTO ...
        // REPLACE INTO ... (synonym for INSERT OR REPLACE INTO ...)
        var firstTok = Peek();
        IReadOnlyList<Token<NzToken>>? orTokens = null;
        if (Peek().Kind == NzToken.Replace)
        {
            firstTok = Advance();
            Expect(NzToken.Into);
        }
        else
        {
            firstTok = Expect(NzToken.Insert);
            if (Peek().Kind == NzToken.Or)
            {
                var tokens = new List<Token<NzToken>>();
                tokens.Add(Advance());
                tokens.Add(Advance()); // ROLLBACK | ABORT | REPLACE | FAIL | IGNORE
                orTokens = tokens;
            }
            Expect(NzToken.Into);
        }

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

        IReadOnlyList<IReadOnlyList<Expression>>? values = null;
        SelectStatement? sourceQuery = null;

        if (Peek().Kind == NzToken.Values)
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

        var conflict = SqliteParseOnConflictClause();
        var returning = SqliteParseReturning();

        return new InsertStatement(FromToken(firstTok), table, columns, values, sourceQuery,
            Returning: returning, OnConflict: conflict, SqliteOrTokens: orTokens);
    }

    private PostgreSqlOnConflictClause? SqliteParseOnConflictClause()
    {
        if (Peek().Kind != NzToken.On)
            return null;
        var on = Advance();
        Expect(NzToken.SqliteConflict);

        IReadOnlyList<string>? columns = null;
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var names = new List<string>();
            if (Peek().Kind != NzToken.RParen)
            {
                names.Add(StripQuotes(ExpectNameToken().ToStringValue()));
                while (Peek().Kind == NzToken.Comma)
                {
                    Advance();
                    names.Add(StripQuotes(ExpectNameToken().ToStringValue()));
                }
            }
            Expect(NzToken.RParen);
            columns = names;
        }

        Expect(NzToken.SqliteDo);
        if (Peek().Kind == NzToken.SqliteNothing)
        {
            Advance();
            return new PostgreSqlOnConflictClause(FromToken(on), columns, true);
        }

        Expect(NzToken.Update);
        Expect(NzToken.Set);
        var items = new List<UpdateSetItem> { SqliteParseSetItem() };
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            items.Add(SqliteParseSetItem());
        }
        Expression? where = null;
        if (Peek().Kind == NzToken.Where)
        {
            Advance();
            where = ParseExpression();
        }
        return new PostgreSqlOnConflictClause(FromToken(on), columns, false, items, where);
    }

    // ====== UPDATE ======

    protected override UpdateStatement? ParseUpdate()
    {
        var updateTok = Expect(NzToken.Update);

        // UPDATE [OR ROLLBACK|ABORT|REPLACE|FAIL|IGNORE] table ...
        IReadOnlyList<Token<NzToken>>? orTokens = null;
        if (Peek().Kind == NzToken.Or)
        {
            var tokens = new List<Token<NzToken>>();
            tokens.Add(Advance());
            tokens.Add(Advance());
            orTokens = tokens;
        }

        var (table, _) = ParseTableName();

        string? alias = null;
        if (Peek().Kind == NzToken.As)
        {
            Advance();
            if (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
                alias = Advance().ToStringValue();
        }
        else if (Peek().Kind == NzToken.Identifier)
        {
            var nxt = Peek(1).Kind;
            if (nxt != NzToken.Dot && nxt != NzToken.LParen)
                alias = Advance().ToStringValue();
        }

        if (Peek().Kind != NzToken.Set)
        {
            AddParserError("Expected SET after table name in UPDATE", Peek(), "PAR115");
            return null;
        }
        Expect(NzToken.Set);

        var setItems = new List<UpdateSetItem> { SqliteParseSetItem() };
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            setItems.Add(SqliteParseSetItem());
        }

        IReadOnlyList<TableReference>? from = null;
        if (Peek().Kind == NzToken.From)
        {
            Advance();
            from = ParseTableReferences();
        }

        Expression? where = null;
        if (Peek().Kind == NzToken.Where)
        {
            Advance();
            where = ParseExpression();
        }

        var returning = SqliteParseReturning();
        return new UpdateStatement(FromToken(updateTok), table, alias, setItems, from, where,
            Returning: returning, SqliteOrTokens: orTokens);
    }

    private UpdateSetItem SqliteParseSetItem()
    {
        var colTok = ExpectNameToken();
        string? qualifier = null;
        string colName;
        var colPos = FromToken(colTok);

        if (Peek().Kind == NzToken.Dot)
        {
            qualifier = StripQuotes(colTok.ToStringValue());
            Advance();
            colTok = ExpectNameToken();
            colName = StripQuotes(colTok.ToStringValue());
        }
        else
        {
            colName = StripQuotes(colTok.ToStringValue());
        }

        Expect(NzToken.EqualsOp);
        var value = ParseExpression();
        return new UpdateSetItem(colPos, new ColumnReference(colPos, qualifier, colName), value);
    }

    // ====== DELETE ======

    protected override DeleteStatement? ParseDelete()
    {
        var delete = base.ParseDelete();
        if (delete is null)
            return null;
        return delete with { Returning = SqliteParseReturning() };
    }

    // ====== RETURNING ======

    private ReturningClause? SqliteParseReturning()
    {
        if (Peek().Kind != NzToken.SqliteReturning)
            return null;
        var returning = Advance();
        var items = new List<ReturningItem>();
        items.Add(SqliteParseReturningItem());
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            items.Add(SqliteParseReturningItem());
        }

        var columns = items.Select(item => item.Expression switch
        {
            ColumnReference column => column.Qualifier is null
                ? column.Name
                : $"{column.Qualifier}.{column.Name}",
            StarExpression => "*",
            _ => string.Empty,
        }).ToArray();
        return new ReturningClause(FromToken(returning), columns, Items: items);
    }

    private ReturningItem SqliteParseReturningItem()
    {
        var expression = ParseExpression();
        var alias = ParseAliasName();
        return new ReturningItem(expression.Position, expression, alias);
    }
}
