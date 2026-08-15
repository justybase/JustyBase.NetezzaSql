using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Parser;

public sealed partial class SqliteSqlParser
{
    // ====== CREATE dispatch ======

    protected override Statement? ParseCreate()
    {
        // CREATE VIRTUAL TABLE name USING module(...)
        if (Peek().Kind == NzToken.Create && Peek(1).Kind == NzToken.SqliteVirtual)
        {
            var createTok = Advance();
            Advance(); // VIRTUAL
            Expect(NzToken.Table);
            var ifNotExists = ParseIfNotExists();
            var (table, _) = ParseTableName();
            var tokens = new List<Token<NzToken>>();
            while (Peek().Kind is not (NzToken.Semicolon or NzToken.Unknown))
                tokens.Add(Advance());
            if (tokens.Count == 0)
                AddParserError("CREATE VIRTUAL TABLE requires a USING module clause", Peek(), "PAR001");
            return new SqliteCreateVirtualTableStatement(FromToken(createTok), table, tokens, ifNotExists);
        }

        // CREATE [TEMP|TEMPORARY] TRIGGER [IF NOT EXISTS] name ...
        if (Peek().Kind == NzToken.Create &&
            Peek(1).Kind is NzToken.Temp or NzToken.Temporary &&
            IsSqliteTriggerKeyword(Peek(2)))
        {
            var createTok = Advance();
            return ParseSqliteCreateTrigger(createTok, temporary: true);
        }
        if (Peek().Kind == NzToken.Create && IsSqliteTriggerKeyword(Peek(1)))
        {
            var createTok = Advance();
            return ParseSqliteCreateTrigger(createTok, temporary: false);
        }

        // SQLite has no sequences, schemas, databases, procedures, synonyms,
        // users or external tables — report them and let the base consume.
        if (Peek().Kind == NzToken.Create &&
            Peek(1).Kind is NzToken.Sequence or NzToken.Schema or NzToken.Database
            or NzToken.Procedure or NzToken.Synonym or NzToken.User or NzToken.External)
        {
            AddParserError($"CREATE {Peek(1).Kind} is not supported by SQLite", Peek(1), "PAR001");
        }

        return base.ParseCreate();
    }

    private static bool IsSqliteTriggerKeyword(Token<NzToken> token) =>
        token.Kind == NzToken.Identifier &&
        token.ToStringValue().Equals("TRIGGER", StringComparison.OrdinalIgnoreCase);

    private bool ParseIfNotExists()
    {
        if (Peek().Kind != NzToken.If)
            return false;
        Advance();
        Expect(NzToken.Not);
        Expect(NzToken.Exists);
        return true;
    }

    private Statement ParseSqliteCreateTrigger(Token<NzToken> createTok, bool temporary)
    {
        // CREATE [TEMP|TEMPORARY] TRIGGER [IF NOT EXISTS] name ...
        var prefixTokens = new List<Token<NzToken>>();
        if (temporary)
            prefixTokens.Add(Advance());
        Advance(); // TRIGGER (identifier)
        bool ifNotExists = ParseIfNotExists();

        var (trigger, _) = ParseTableName();

        // The trigger body is a BEGIN ... END block whose internal statements
        // carry their own semicolons, so consume through the matching END.
        // CASE expressions inside the body (for example a WHEN clause) also
        // terminate with END tokens and must not end the block prematurely.
        var bodyTokens = new List<Token<NzToken>>();
        var blockDepth = 0;
        var caseDepth = 0;
        while (Peek().Kind != NzToken.Unknown)
        {
            var token = Advance();
            if (token.Kind == NzToken.Case)
            {
                caseDepth++;
                bodyTokens.Add(token);
                continue;
            }
            if (token.Kind == NzToken.End)
            {
                if (caseDepth > 0)
                {
                    caseDepth--;
                    bodyTokens.Add(token);
                    continue;
                }
                if (blockDepth == 0)
                    break;
                blockDepth--;
                bodyTokens.Add(token);
                continue;
            }
            if (token.Kind == NzToken.Begin)
                blockDepth++;
            bodyTokens.Add(token);
        }

        var all = prefixTokens;
        all.AddRange(bodyTokens);
        return new SqliteCreateTriggerStatement(FromToken(createTok), trigger, all, ifNotExists);
    }

    // ====== CREATE TABLE ======

    protected override CreateTableStatement ParseCreateTable(Token<NzToken> createTok, bool orReplace)
    {
        bool temporary = false;
        if (Peek().Kind is NzToken.Temp or NzToken.Temporary)
        {
            temporary = true;
            Advance();
        }

        Expect(NzToken.Table);
        var ifNotExists = ParseIfNotExists();
        var (table, _) = ParseTableName();

        IReadOnlyList<ColumnDefinition>? columns = null;
        IReadOnlyList<TableConstraint>? constraints = null;
        SelectStatement? asSelect = null;

        if (Peek().Kind == NzToken.As)
        {
            // CREATE TABLE ... AS SELECT
            Advance();
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                asSelect = ParseSelectStatement();
                Expect(NzToken.RParen);
            }
            else
            {
                asSelect = ParseSelectStatement();
            }
        }
        else if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var colList = SqliteParseColumnDefinitionList();
            Expect(NzToken.RParen);
            columns = colList.Columns;
            constraints = colList.Constraints;
        }
        else
        {
            AddParserError("Expected AS or ( after CREATE TABLE", Peek(), "PAR121");
            return new CreateTableStatement(FromToken(createTok), table, ifNotExists,
                temporary, false, null, null, null, null, null, null);
        }

        if (Peek().Kind is NzToken.Distribute or NzToken.Organize)
        {
            AddParserError($"{Peek().Kind} is Netezza-only syntax and is not supported in SQLite",
                Peek(), "PAR001");
            while (Peek().Kind is not (NzToken.Semicolon or NzToken.Unknown))
                Advance();
            return new CreateTableStatement(FromToken(createTok), table, ifNotExists,
                temporary, false, columns, constraints, asSelect, null, null, null);
        }

        // WITHOUT ROWID | STRICT tail (SQLite allows a comma-separated pair).
        List<Token<NzToken>>? tail = null;
        while (Peek().Kind is NzToken.SqliteWithout or NzToken.SqliteStrict or NzToken.Comma)
        {
            tail ??= new List<Token<NzToken>>();
            tail.Add(Advance());
            if (Peek().Kind == NzToken.Identifier &&
                Peek().ToStringValue().Equals("ROWID", StringComparison.OrdinalIgnoreCase))
                tail.Add(Advance());
        }

        return new CreateTableStatement(FromToken(createTok), table, ifNotExists, temporary, false,
            columns, constraints, asSelect, null, null, tail);
    }

    // ====== SQLite column / constraint grammar ======

    private (IReadOnlyList<ColumnDefinition> Columns, IReadOnlyList<TableConstraint> Constraints)
        SqliteParseColumnDefinitionList()
    {
        var columns = new List<ColumnDefinition>();
        var constraints = new List<TableConstraint>();

        if (Peek().Kind == NzToken.RParen)
        {
            AddParserError("Empty column definition list is not allowed", Peek(), "PAR119");
            return (columns, constraints);
        }

        SqliteParseOneColumnOrConstraint(columns, constraints);
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            SqliteParseOneColumnOrConstraint(columns, constraints);
        }

        return (columns, constraints);
    }

    private void SqliteParseOneColumnOrConstraint(
        List<ColumnDefinition> columns,
        List<TableConstraint> constraints)
    {
        if (SqliteIsTableConstraintStart())
        {
            var tc = SqliteParseTableConstraint();
            if (tc is null)
                return;
            // Table-level PRIMARY KEY / UNIQUE may carry an ON CONFLICT clause.
            var conflictTokens = new List<Token<NzToken>>();
            SqliteConsumeOnConflict(conflictTokens);
            constraints.Add(tc switch
            {
                PrimaryKeyConstraint pk => pk with
                {
                    SqliteConflictTokens = conflictTokens.Count == 0 ? null : conflictTokens,
                },
                UniqueConstraint unique => unique with
                {
                    SqliteConflictTokens = conflictTokens.Count == 0 ? null : conflictTokens,
                },
                _ => tc,
            });
            return;
        }

        columns.Add(SqliteParseColumnDefinition());
    }

    private bool SqliteIsTableConstraintStart()
    {
        var k = Peek().Kind;
        if (k is NzToken.Primary or NzToken.Unique or NzToken.Foreign or NzToken.Check)
            return true;
        if (k == NzToken.Constraint)
            return Peek(2).Kind is NzToken.Primary or NzToken.Unique or NzToken.Foreign or NzToken.Check;
        return false;
    }

    private ColumnDefinition SqliteParseColumnDefinition()
    {
        var nameTok = ExpectNameToken();
        var colPos = FromToken(nameTok);
        var colName = nameTok.ToStringValue();

        var dataType = ParseDataType();

        var notNull = false;
        Expression? defaultValue = null;
        var consList = new List<ColumnConstraint>();
        var attributeTokens = new List<Token<NzToken>>();

        while (SqliteIsColumnConstraintStart())
        {
            if (Peek().Kind == NzToken.Constraint)
            {
                Advance();
                var consName = ExpectNameToken().ToStringValue();
                var cc = SqliteParseColumnConstraintKind(attributeTokens);
                if (cc is not null)
                    consList.Add(new NamedColumnConstraint(cc.Position, consName, cc));
            }
            else if (Peek().Kind == NzToken.Not && Peek(1).Kind == NzToken.Null)
            {
                Advance();
                Advance();
                notNull = true;
                consList.Add(new NotNullConstraint(colPos));
                SqliteConsumeOnConflict(attributeTokens);
            }
            else if (Peek().Kind == NzToken.Null)
            {
                Advance();
                consList.Add(new NullConstraint(colPos));
            }
            else if (Peek().Kind == NzToken.Default)
            {
                Advance();
                defaultValue = ParseExpression();
                consList.Add(new DefaultConstraint(colPos, defaultValue));
            }
            else if (Peek().Kind == NzToken.SqliteGenerated || Peek().Kind == NzToken.As)
            {
                SqliteConsumeGeneratedColumn(attributeTokens);
            }
            else if (Peek().Kind == NzToken.SqliteCollate)
            {
                SqliteConsumeCollate(attributeTokens);
            }
            else
            {
                var cc = SqliteParseColumnConstraintKind(attributeTokens);
                if (cc is not null)
                    consList.Add(cc);
                else
                {
                    // Unrecognized clause start (e.g. a stray ASC/DESC or a
                    // malformed referential tail): consume one token so the
                    // loop always makes progress and reports a diagnostic.
                    AddParserError($"Unexpected column clause {Peek().Kind}", Peek(), "PAR001");
                    attributeTokens.Add(Advance());
                }
            }
        }

        return new ColumnDefinition(colPos, colName, dataType, notNull, defaultValue,
            consList.Count == 0 ? null : consList,
            attributeTokens.Count == 0 ? null : attributeTokens);
    }

    private bool SqliteIsColumnConstraintStart()
    {
        var k = Peek().Kind;
        if (k is NzToken.Constraint or NzToken.Default or NzToken.Primary or NzToken.Unique
            or NzToken.References or NzToken.Check or NzToken.SqliteAutoincrement
            or NzToken.SqliteCollate or NzToken.SqliteGenerated or NzToken.As
            or NzToken.Asc or NzToken.Desc or NzToken.Match or NzToken.Deferrable)
            return true;
        if (k == NzToken.Not && (Peek(1).Kind == NzToken.Null || Peek(1).Kind == NzToken.Deferrable))
            return true;
        if (k == NzToken.Null)
            return true;
        if (k == NzToken.On && Peek(1).Kind is NzToken.Delete or NzToken.Update or NzToken.SqliteConflict)
            return true;
        return false;
    }

    private ColumnConstraint? SqliteParseColumnConstraintKind(List<Token<NzToken>> attributeTokens)
    {
        var k = Peek().Kind;
        var pos = SourcePosition.FromToken(Peek());

        if (k == NzToken.Primary)
        {
            Advance();
            Expect(NzToken.Key);
            // PRIMARY KEY [ASC|DESC] [ON CONFLICT ...] [AUTOINCREMENT]
            if (Peek().Kind is NzToken.Asc or NzToken.Desc)
                attributeTokens.Add(Advance());
            SqliteConsumeOnConflict(attributeTokens);
            if (Peek().Kind == NzToken.SqliteAutoincrement)
                attributeTokens.Add(Advance());
            return new PrimaryKeyColumnConstraint(pos);
        }

        if (k == NzToken.Unique)
        {
            Advance();
            SqliteConsumeOnConflict(attributeTokens);
            return new UniqueColumnConstraint(pos);
        }

        if (k == NzToken.Check)
        {
            Advance();
            Expect(NzToken.LParen);
            var expr = ParseExpression();
            Expect(NzToken.RParen);
            return new CheckColumnConstraint(pos, expr);
        }

        if (k == NzToken.References)
        {
            Advance();
            var (refTable, _) = ParseTableName();
            IReadOnlyList<string>? refCols = null;
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                refCols = SqliteParseNameList();
                Expect(NzToken.RParen);
            }
            SqliteConsumeReferentialTail(attributeTokens);
            return new ReferencesConstraint(pos, refTable, refCols);
        }

        return null;
    }

    private TableConstraint? SqliteParseTableConstraint()
    {
        var pos = SourcePosition.FromToken(Peek());
        string? name = null;

        if (Peek().Kind == NzToken.Constraint)
        {
            Advance();
            name = ExpectNameToken().ToStringValue();
            pos = SourcePosition.FromToken(Peek());
        }

        var k = Peek().Kind;

        if (k == NzToken.Primary)
        {
            Advance();
            Expect(NzToken.Key);
            Expect(NzToken.LParen);
            var cols = ParseIdentifierList();
            Expect(NzToken.RParen);
            return new PrimaryKeyConstraint(pos, cols, name);
        }

        if (k == NzToken.Unique)
        {
            Advance();
            Expect(NzToken.LParen);
            var cols = ParseIdentifierList();
            Expect(NzToken.RParen);
            return new UniqueConstraint(pos, cols, name);
        }

        if (k == NzToken.Check)
        {
            Advance();
            Expect(NzToken.LParen);
            var expr = ParseExpression();
            Expect(NzToken.RParen);
            return new CheckConstraint(pos, expr);
        }

        if (k == NzToken.Foreign)
        {
            Advance();
            Expect(NzToken.Key);
            Expect(NzToken.LParen);
            var cols = ParseIdentifierList();
            Expect(NzToken.RParen);
            Expect(NzToken.References);
            var (refTable, _) = ParseTableName();
            IReadOnlyList<string>? refCols = null;
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                refCols = SqliteParseNameList();
                Expect(NzToken.RParen);
            }
            var tailTokens = new List<Token<NzToken>>();
            SqliteConsumeReferentialTail(tailTokens);
            return new ForeignKeyConstraint(pos, cols, refTable, refCols, name,
                tailTokens.Count == 0 ? null : tailTokens);
        }

        return null;
    }

    private IReadOnlyList<string> SqliteParseNameList()
    {
        var list = new List<string> { ExpectNameToken().ToStringValue() };
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            list.Add(ExpectNameToken().ToStringValue());
        }
        return list;
    }

    /// <summary>Consumes ON DELETE / ON UPDATE / MATCH / DEFERRABLE referential tails.</summary>
    private void SqliteConsumeReferentialTail(List<Token<NzToken>> into)
    {
        while (true)
        {
            var k = Peek().Kind;
            if (k == NzToken.On && Peek(1).Kind is NzToken.Delete or NzToken.Update)
            {
                into.Add(Advance());
                into.Add(Advance());
                var action = Peek().Kind;
                if (action == NzToken.Set)
                {
                    into.Add(Advance()); // SET
                    into.Add(Advance()); // NULL | DEFAULT
                }
                else if (action is NzToken.Cascade or NzToken.Restrict)
                {
                    into.Add(Advance());
                }
                else if (action == NzToken.Identifier &&
                         Peek().ToStringValue().Equals("NO", StringComparison.OrdinalIgnoreCase) &&
                         Peek(1).Kind == NzToken.Action)
                {
                    into.Add(Advance()); // NO
                    into.Add(Advance()); // ACTION
                }
                else
                {
                    AddParserError("Expected SET NULL, SET DEFAULT, CASCADE, RESTRICT or NO ACTION after ON DELETE/UPDATE",
                        Peek(), "PAR001");
                }
            }
            else if (k == NzToken.Match)
            {
                into.Add(Advance());
                if (Peek().Kind is not (NzToken.Comma or NzToken.RParen or NzToken.Unknown))
                    into.Add(Advance()); // collation name
            }
            else if (k == NzToken.Deferrable)
            {
                into.Add(Advance());
                SqliteConsumeInitially(into);
            }
            else if (k == NzToken.Not && Peek(1).Kind == NzToken.Deferrable)
            {
                into.Add(Advance());
                into.Add(Advance());
                SqliteConsumeInitially(into);
            }
            else
            {
                break;
            }
        }
    }

    private void SqliteConsumeInitially(List<Token<NzToken>> into)
    {
        if (Peek().Kind != NzToken.Initially)
            return;
        into.Add(Advance()); // INITIALLY
        if (Peek().Kind is not (NzToken.Comma or NzToken.RParen or NzToken.Unknown))
            into.Add(Advance()); // DEFERRED | IMMEDIATE
    }

    /// <summary>Consumes ON CONFLICT REPLACE|ROLLBACK|ABORT|FAIL|IGNORE.</summary>
    private void SqliteConsumeOnConflict(List<Token<NzToken>> into)
    {
        if (Peek().Kind != NzToken.On)
            return;
        into.Add(Advance()); // ON
        var conflict = Expect(NzToken.SqliteConflict); // advances past CONFLICT
        into.Add(conflict);
        if (IsContextualIdentifier(Peek().Kind) || Peek().Kind is NzToken.Replace or NzToken.Rollback)
            into.Add(Advance()); // REPLACE | ROLLBACK | ABORT | FAIL | IGNORE
        else
            AddParserError("Expected REPLACE, ROLLBACK, ABORT, FAIL or IGNORE after ON CONFLICT", Peek(), "PAR001");
    }

    /// <summary>Consumes COLLATE name.</summary>
    private void SqliteConsumeCollate(List<Token<NzToken>> into)
    {
        into.Add(Advance()); // COLLATE
        if (Peek().Kind is not (NzToken.Comma or NzToken.RParen or NzToken.Unknown))
            into.Add(Advance()); // collation name
    }

    /// <summary>Consumes GENERATED ALWAYS AS (expr) [VIRTUAL|STORED] or AS (expr) [STORED].</summary>
    private void SqliteConsumeGeneratedColumn(List<Token<NzToken>> into)
    {
        if (Peek().Kind == NzToken.SqliteGenerated)
        {
            into.Add(Advance()); // GENERATED
            if (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
                into.Add(Advance()); // ALWAYS
            if (Peek().Kind == NzToken.As)
                into.Add(Advance()); // AS
        }
        else
        {
            into.Add(Advance()); // AS (shorthand form)
        }

        if (Peek().Kind == NzToken.LParen)
        {
            var depth = 0;
            while (Peek().Kind != NzToken.Unknown)
            {
                var token = Advance();
                into.Add(token);
                if (token.Kind == NzToken.LParen) depth++;
                else if (token.Kind == NzToken.RParen)
                {
                    depth--;
                    if (depth == 0)
                        break;
                }
            }
        }
        else
        {
            AddParserError("Expected ( expression ) after GENERATED/AS", Peek(), "PAR001");
        }

        if (Peek().Kind == NzToken.SqliteVirtual)
        {
            into.Add(Advance());
        }
        else if (Peek().Kind == NzToken.Identifier &&
                 Peek().ToStringValue().Equals("STORED", StringComparison.OrdinalIgnoreCase))
        {
            into.Add(Advance());
        }
    }
}
