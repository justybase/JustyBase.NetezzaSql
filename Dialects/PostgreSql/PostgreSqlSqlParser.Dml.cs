using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.NetezzaSqlParser.Parser;

public sealed partial class PostgreSqlSqlParser
{
    protected override InsertStatement? ParseInsert()
    {
        var insert = base.ParseInsert();
        if (insert is null) return null;

        PostgreSqlOnConflictClause? conflict = null;
        if (Peek().Kind == NzToken.On)
            conflict = ParseOnConflict();
        var returning = ParseReturningClause();
        return insert with { OnConflict = conflict, Returning = returning };
    }

    protected override UpdateStatement? ParseUpdate()
    {
        var update = base.ParseUpdate();
        if (update is null) return null;
        return update with { Returning = ParseReturningClause() };
    }

    protected override DeleteStatement? ParseDelete()
    {
        var delete = base.ParseDelete();
        if (delete is null) return null;
        return delete with { Returning = ParseReturningClause() };
    }

    private PostgreSqlOnConflictClause ParseOnConflict()
    {
        var on = Advance();
        Expect(NzToken.PostgreSqlConflict);
        IReadOnlyList<string>? columns = null;
        string? constraintName = null;
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
        else if (Peek().Kind == NzToken.On)
        {
            Advance();
            Expect(NzToken.Constraint);
            constraintName = StripQuotes(ExpectNameToken().ToStringValue());
        }

        Expect(NzToken.PostgreSqlDo);
        if (Peek().Kind == NzToken.PostgreSqlNothing)
        {
            Advance();
            return new PostgreSqlOnConflictClause(FromToken(on), columns, true,
                ConstraintName: constraintName);
        }

        Expect(NzToken.Update);
        Expect(NzToken.Set);
        var items = new List<UpdateSetItem> { ParsePostgreSqlSetItem() };
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            items.Add(ParsePostgreSqlSetItem());
        }
        Expression? where = null;
        if (Peek().Kind == NzToken.Where)
        {
            Advance();
            where = ParseExpression();
        }
        return new PostgreSqlOnConflictClause(FromToken(on), columns, false, items, where,
            constraintName);
    }

    private UpdateSetItem ParsePostgreSqlSetItem()
    {
        var column = ExpectNameToken();
        var pos = FromToken(column);
        string? qualifier = null;
        var name = StripQuotes(column.ToStringValue());
        if (Peek().Kind == NzToken.Dot)
        {
            Advance();
            qualifier = name;
            name = StripQuotes(ExpectNameToken().ToStringValue());
        }
        Expect(NzToken.EqualsOp);
        return new UpdateSetItem(pos, new ColumnReference(pos, qualifier, name), ParseExpression());
    }

    private ReturningClause? ParseReturningClause()
    {
        if (Peek().Kind != NzToken.PostgreSqlReturning)
            return null;
        var returning = Advance();
        var items = new List<ReturningItem>();
        items.Add(ParseReturningItem());
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            items.Add(ParseReturningItem());
        }

        var columns = items.Select(item => item.Expression switch
        {
            ColumnReference column => column.Qualifier is null
                ? column.Name
                : $"{column.Qualifier}.{column.Name}",
            StarExpression => "*",
            _ => string.Empty
        }).ToArray();
        return new ReturningClause(FromToken(returning), columns, Items: items);
    }

    private ReturningItem ParseReturningItem()
    {
        var expression = ParseExpression();
        var alias = ParseAliasName();
        return new ReturningItem(expression.Position, expression, alias);
    }
}
