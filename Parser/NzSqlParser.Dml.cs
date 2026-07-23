using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

/// <summary>
/// Hand-written recursive-descent parser for Netezza SQL.
/// Consumes Superpower tokens and produces an AST.
/// Partial class split across multiple files by grammar area.
/// </summary>
public partial class NzSqlParser
{
    // ====== INSERT Statement ======

    private InsertStatement? ParseInsert()
    {
        var insertTok = Expect(NzToken.Insert);

        if (Peek().Kind != NzToken.Into)
        {
            AddParserError("Expected INTO after INSERT, got " + DescribeToken(Peek()),
                Peek(), "PAR114");
            return null;
        }
        Expect(NzToken.Into);

        var (table, _) = ParseTableName();

        // Optional column list: (col1, col2, ...)
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

        if (Peek().Kind == NzToken.With)
        {
            Advance();
            // Netezza also accepts INSERT ... WITH name (SELECT ...) SELECT ...
            // without the AS keyword in this position.
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
            Advance(); // VALUES
            var rows = new List<IReadOnlyList<Expression>>();

            // Parse first row
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

            // Parse subsequent rows
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

        return new InsertStatement(FromToken(insertTok), table, columns, values, sourceQuery);
    }

    private InsertStatement? ParseInsertWithClause(WithClause with)
    {
        var insert = ParseInsert();
        return insert;
    }

    // ====== UPDATE Statement ======

    private UpdateStatement? ParseUpdate()
    {
        var updateTok = Expect(NzToken.Update);

        var (table, _) = ParseTableName();

        // Optional alias (bare, not AS — Netezza doesn't support AS for UPDATE)
        string? alias = null;
        if (Peek().Kind == NzToken.As)
        {
            var asTok = Advance();
            if (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
                alias = Advance().ToStringValue();
            _errors.Add(new ValidationError(
                "UPDATE table AS alias is not supported in Netezza — use UPDATE table alias without AS",
                "error", SourcePosition.FromToken(asTok), "SQL046"));
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

        var setItems = new List<UpdateSetItem>();
        setItems.Add(ParseUpdateSetItem());
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            setItems.Add(ParseUpdateSetItem());
        }

        // Optional FROM clause (Netezza/T-SQL extension)
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

        return new UpdateStatement(FromToken(updateTok), table, alias, setItems, from, where);
    }

    private UpdateSetItem ParseUpdateSetItem()
    {
        // Parse qualified or unqualified column reference
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
        var colRef = new ColumnReference(colPos, qualifier, colName);
        return new UpdateSetItem(colPos, colRef, value);
    }

    // ====== DELETE Statement ======

    private DeleteStatement? ParseDelete()
    {
        var deleteTok = Expect(NzToken.Delete);

        if (Peek().Kind != NzToken.From)
        {
            AddParserError("Expected FROM after DELETE", Peek(), "PAR116");
            return null;
        }
        Expect(NzToken.From);

        var (table, _) = ParseTableName();

        string? tableAlias = null;
        if (Peek().Kind == NzToken.As)
        {
            Advance();
            tableAlias = Expect(NzToken.Identifier).ToStringValue();
        }
        else if (Peek().Kind == NzToken.Identifier)
        {
            var nxt = Peek(1).Kind;
            if (nxt != NzToken.Dot && nxt != NzToken.LParen)
                tableAlias = Advance().ToStringValue();
        }

        Expression? where = null;
        if (Peek().Kind == NzToken.Where)
        {
            Advance();
            where = ParseExpression();
        }

        return new DeleteStatement(FromToken(deleteTok), table, tableAlias, where);
    }

    // ====== MERGE Statement ======

    private Statement ParseMerge()
    {
        var m = Advance(); // MERGE
        Expect(NzToken.Into);
        var (targetTable, _) = ParseTableName();

        string? targetAlias = null;
        if (Peek().Kind == NzToken.As)
        {
            Advance();
            targetAlias = Expect(NzToken.Identifier).ToStringValue();
        }
        else if (Peek().Kind == NzToken.Identifier)
        {
            var nxt = Peek(1).Kind;
            if (nxt != NzToken.Dot && nxt != NzToken.LParen)
                targetAlias = Advance().ToStringValue();
        }

        Expect(NzToken.Using);
        var source = ParseTableSource();
        Expect(NzToken.On);
        var condition = ParseExpression();

        var clauses = new List<MergeClause>();
        while (Peek().Kind == NzToken.When)
        {
            var clause = ParseMergeClause();
            if (clause is not null)
                clauses.Add(clause);
            else
                break;
        }

        return new MergeStatement(FromToken(m), targetTable, targetAlias, source, condition, clauses);
    }

    private MergeClause? ParseMergeClause()
    {
        var whenTok = Expect(NzToken.When);
        bool notMatched = false;

        if (Peek().Kind == NzToken.Not)
        {
            Expect(NzToken.Not);
            Expect(NzToken.Matched);
            notMatched = true;
        }
        else
        {
            Expect(NzToken.Matched);
        }

        Expression? condition = null;
        if (Peek().Kind == NzToken.And)
        {
            Advance();
            condition = ParseExpression();
        }

        Expect(NzToken.Then);

        if (notMatched)
        {
            Expect(NzToken.Insert);
            IReadOnlyList<string>? columns = null;
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                var colList = new List<string>();
                if (Peek().Kind == NzToken.Identifier || Peek().Kind == NzToken.QuotedIdentifier)
                {
                    colList.Add(ExpectNameToken().ToStringValue());
                    while (Peek().Kind == NzToken.Comma)
                    {
                        Advance();
                        colList.Add(ExpectNameToken().ToStringValue());
                    }
                }
                Expect(NzToken.RParen);
                columns = colList;
            }

            Expect(NzToken.Values);
            Expect(NzToken.LParen);
            var values = new List<Expression>();
            if (Peek().Kind != NzToken.RParen)
            {
                values.Add(ParseExpression());
                while (Peek().Kind == NzToken.Comma)
                {
                    Advance();
                    values.Add(ParseExpression());
                }
            }
            Expect(NzToken.RParen);

            return new MergeNotMatchedInsertClause(FromToken(whenTok), condition, columns, values);
        }
        else
        {
            var next = Peek().Kind;
            if (next == NzToken.Update)
            {
                Advance();
                Expect(NzToken.Set);
                var setItems = new List<UpdateSetItem>();
                setItems.Add(ParseUpdateSetItem());
                while (Peek().Kind == NzToken.Comma)
                {
                    Advance();
                    setItems.Add(ParseUpdateSetItem());
                }

                Expression? where = null;
                if (Peek().Kind == NzToken.Where)
                {
                    Advance();
                    where = ParseExpression();
                }

                return new MergeMatchedUpdateClause(FromToken(whenTok), condition, setItems, where);
            }
            else if (next == NzToken.Delete)
            {
                Advance();
                return new MergeMatchedDeleteClause(FromToken(whenTok), condition);
            }
            else
            {
                AddParserError("Expected UPDATE or DELETE after WHEN MATCHED THEN", Peek(), "PAR118");
                return null;
            }
        }
    }
}
