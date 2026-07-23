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
    // ====== CREATE Statement Dispatcher ======

    private Statement? ParseCreate()
    {
        var createTok = Expect(NzToken.Create);

        bool orReplace = false;
        if (Peek().Kind == NzToken.Or)
        {
            Advance();
            Expect(NzToken.Replace);
            orReplace = true;
        }

        if (Peek().Kind == NzToken.Materialized
            || (Peek().Kind == NzToken.Identifier
                && string.Equals(Peek().ToStringValue(), "MATERIALIZED", StringComparison.OrdinalIgnoreCase)))
        {
            Advance();
            Expect(NzToken.View);
            var (view, _) = ParseTableName();
            Expect(NzToken.As);
            if (Peek().Kind == NzToken.LParen) Advance();
            if (Peek().Kind is NzToken.Semicolon or NzToken.Unknown)
            {
                AddParserError("Materialized view query must contain a SELECT statement",
                    Peek(), "PAR121");
            }
            var query = ParseSelectStatement();
            if (Peek().Kind == NzToken.RParen) Advance();
            return new CreateViewStatement(FromToken(createTok), view, orReplace, null, query);
        }

        return Peek().Kind switch
        {
            NzToken.Table or NzToken.Temp or NzToken.Temporary or NzToken.Global => ParseCreateTable(createTok, orReplace),
            NzToken.View => ParseCreateView(createTok, orReplace),
            NzToken.External => ParseCreateExternalTable(createTok),
            NzToken.Procedure => ParseCreateProcedure(createTok, orReplace),
            NzToken.Sequence => ParseCreateSequence(createTok),
            NzToken.Synonym => ParseCreateSynonym(createTok),
            NzToken.User => ParseCreateUser(createTok),
            NzToken.Database => ParseCreateDatabase(createTok),
            _ when IsGroupKeyword() => ParseCreateGroup(createTok),
            _ => ParseCommandTailFallback(createTok)
        };
    }

    // ====== CREATE TABLE ======

    private CreateTableStatement ParseCreateTable(Token<NzToken> createTok, bool orReplace)
    {
        bool temporary = false;
        bool global = false;

        if (Peek().Kind == NzToken.Global)
        {
            global = true;
            Advance();
        }

        if (Peek().Kind is NzToken.Temp or NzToken.Temporary)
        {
            temporary = true;
            Advance();
        }

        Expect(NzToken.Table);

        bool ifNotExists = false;
        if (Peek().Kind == NzToken.If)
        {
            Advance();
            Expect(NzToken.Not);
            Expect(NzToken.Exists);
            ifNotExists = true;
        }

        var (table, _) = ParseTableName();
        IReadOnlyList<ColumnDefinition>? columns = null;
        IReadOnlyList<TableConstraint>? constraints = null;
        SelectStatement? asSelect = null;

        if (Peek().Kind == NzToken.As)
        {
            Advance();
            // CTAS: AS (SELECT ...) or AS SELECT ...
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                WithClause? nestedWith = null;
                if (Peek().Kind == NzToken.With)
                    nestedWith = ParseWithClause();
                asSelect = ParseSelectStatement(nestedWith);
                Expect(NzToken.RParen);
            }
            else
            {
                WithClause? nestedWith = null;
                if (Peek().Kind == NzToken.With)
                    nestedWith = ParseWithClause();
                asSelect = ParseSelectStatement(nestedWith);
            }
        }
        else if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var colList = ParseColumnDefinitionList();
            Expect(NzToken.RParen);
            columns = colList.Columns;
            constraints = colList.Constraints;
        }
        else
        {
            AddParserError("Expected AS or ( after CREATE TABLE", Peek(), "PAR121");
            return new CreateTableStatement(FromToken(createTok), table, ifNotExists, temporary, global, null, null, null, null, null);
        }

        var distribute = ParseDistributeClause();
        var organize = ParseOrganizeClause();

        return new CreateTableStatement(FromToken(createTok), table, ifNotExists, temporary, global,
            columns, constraints, asSelect, distribute, organize);
    }

    private ColumnDefList ParseColumnDefinitionList()
    {
        var columns = new List<ColumnDefinition>();
        var constraints = new List<TableConstraint>();

        if (Peek().Kind == NzToken.RParen)
        {
            AddParserError("Empty column definition list is not allowed", Peek(), "PAR119");
            return new ColumnDefList(columns, constraints);
        }

        ParseOneColumnOrConstraint(columns, constraints);
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            ParseOneColumnOrConstraint(columns, constraints);
        }

        return new ColumnDefList(columns, constraints);
    }

    private void ParseOneColumnOrConstraint(List<ColumnDefinition> columns, List<TableConstraint> constraints)
    {
        if (IsTableConstraintStart())
        {
            var tc = ParseTableConstraint();
            if (tc is not null) constraints.Add(tc);
            return;
        }
        var col = ParseColumnDefinition();
        columns.Add(col);
    }

    private bool IsTableConstraintStart()
    {
        var k = Peek().Kind;
        if (k == NzToken.Primary || k == NzToken.Unique || k == NzToken.Foreign || k == NzToken.Check)
            return true;
        if (k == NzToken.Constraint)
        {
            var afterId = Peek(2).Kind;
            return afterId == NzToken.Primary || afterId == NzToken.Unique || afterId == NzToken.Foreign || afterId == NzToken.Check;
        }
        return false;
    }

    private ColumnDefinition ParseColumnDefinition()
    {
        var nameTok = ExpectNameToken();
        var colPos = FromToken(nameTok);
        var colName = nameTok.ToStringValue();

        var dataType = ParseDataType();

        var notNull = false;
        Expression? defaultValue = null;
        IReadOnlyList<ColumnConstraint>? constraints = null;
        var consList = new List<ColumnConstraint>();

        while (IsColumnConstraintStart())
        {
            if (Peek().Kind == NzToken.Constraint)
            {
                Advance();
                var consName = ExpectNameToken().ToStringValue();
                var cc = ParseColumnConstraintKind();
                if (cc is not null)
                {
                    consList.Add(new NamedColumnConstraint(cc.Position, consName, cc));
                }
            }
            else if (Peek().Kind == NzToken.Not && Peek(1).Kind == NzToken.Null)
            {
                Advance(); Advance();
                notNull = true;
                consList.Add(new NotNullConstraint(colPos));
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
            else
            {
                var cc = ParseColumnConstraintKind();
                if (cc is not null) consList.Add(cc);
            }
        }

        if (consList.Count > 0) constraints = consList;
        return new ColumnDefinition(colPos, colName, dataType, notNull, defaultValue, constraints);
    }

    private bool IsColumnConstraintStart()
    {
        var k = Peek().Kind;
        if (k == NzToken.Constraint || k == NzToken.Default || k == NzToken.Primary
            || k == NzToken.Unique || k == NzToken.References)
            return true;
        if (k == NzToken.Not && Peek(1).Kind == NzToken.Null)
            return true;
        if (k == NzToken.Null)
            return true;
        return false;
    }

    private ColumnConstraint? ParseColumnConstraintKind()
    {
        var k = Peek().Kind;
        var pos = SourcePosition.FromToken(Peek());

        if (k == NzToken.Primary)
        {
            Advance();
            Expect(NzToken.Key);
            return new PrimaryKeyColumnConstraint(pos);
        }
        if (k == NzToken.Unique)
        {
            Advance();
            return new UniqueColumnConstraint(pos);
        }
        if (k == NzToken.References)
        {
            Advance();
            var (refTable, _) = ParseTableName();
            IReadOnlyList<string>? refCols = null;
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                refCols = new List<string> { ExpectNameToken().ToStringValue() };
                while (Peek().Kind == NzToken.Comma)
                {
                    Advance();
                    ((List<string>)refCols).Add(ExpectNameToken().ToStringValue());
                }
                Expect(NzToken.RParen);
            }
            return new ReferencesConstraint(pos, refTable, refCols);
        }

        return null;
    }

    private TableConstraint? ParseTableConstraint()
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
            IReadOnlyList<string>? cols = null;
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                cols = ParseIdentifierList();
                Expect(NzToken.RParen);
            }
            return new PrimaryKeyConstraint(pos, cols, name);
        }

        if (k == NzToken.Unique)
        {
            Advance();
            IReadOnlyList<string>? cols = null;
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                cols = ParseIdentifierList();
                Expect(NzToken.RParen);
            }
            return new UniqueConstraint(pos, cols, name);
        }

        if (k == NzToken.Foreign)
        {
            Advance();
            Expect(NzToken.Key);
            IReadOnlyList<string>? cols = null;
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                cols = ParseIdentifierList();
                Expect(NzToken.RParen);
            }
            Expect(NzToken.References);
            var (refTable, _) = ParseTableName();
            IReadOnlyList<string>? refCols = null;
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                refCols = ParseIdentifierList();
                Expect(NzToken.RParen);
            }
            return new ForeignKeyConstraint(pos, cols, refTable, refCols, name);
        }

        if (k == NzToken.Check)
        {
            Advance();
            Expect(NzToken.LParen);
            var expr = ParseExpression();
            Expect(NzToken.RParen);
            return new CheckConstraint(pos, expr);
        }

        return null;
    }

    private IReadOnlyList<string> ParseIdentifierList()
    {
        var list = new List<string> { ExpectNameToken().ToStringValue() };
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            list.Add(ExpectNameToken().ToStringValue());
        }
        return list;
    }

    private DistributeClause? ParseDistributeClause()
    {
        if (Peek().Kind != NzToken.Distribute) return null;
        var tok = Advance();
        Expect(NzToken.On);

        if (Peek().Kind == NzToken.Random)
        {
            Advance();
            return new DistributeClause(FromToken(tok), true, null);
        }

        if (Peek().Kind == NzToken.Hash)
        {
            Advance();
        }

        Expect(NzToken.LParen);
        var cols = ParseIdentifierList();
        Expect(NzToken.RParen);
        return new DistributeClause(FromToken(tok), false, cols);
    }

    private OrganizeClause? ParseOrganizeClause()
    {
        if (Peek().Kind != NzToken.Organize) return null;
        var tok = Advance();
        Expect(NzToken.On);

        if (Peek().Kind == NzToken.None)
        {
            Advance();
            return new OrganizeClause(FromToken(tok), new List<string>());
        }

        Expect(NzToken.LParen);
        var cols = ParseIdentifierList();
        Expect(NzToken.RParen);
        return new OrganizeClause(FromToken(tok), cols);
    }

    // ====== CREATE VIEW ======

    private CreateViewStatement ParseCreateView(Token<NzToken> createTok, bool orReplace)
    {
        Expect(NzToken.View);
        var (view, _) = ParseTableName();

        IReadOnlyList<string>? columnAliases = null;
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            columnAliases = ParseIdentifierList();
            Expect(NzToken.RParen);
        }

        Expect(NzToken.As);

        SelectStatement query;
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            WithClause? nestedWith = null;
            if (Peek().Kind == NzToken.With)
                nestedWith = ParseWithClause();
            query = ParseSelectStatement(nestedWith);
            Expect(NzToken.RParen);
        }
        else
        {
            WithClause? nestedWith = null;
            if (Peek().Kind == NzToken.With)
                nestedWith = ParseWithClause();
            query = ParseSelectStatement(nestedWith);
        }

        return new CreateViewStatement(FromToken(createTok), view, orReplace, columnAliases, query);
    }

    // ====== CREATE EXTERNAL TABLE ======

    private CreateExternalTableStatement ParseCreateExternalTable(Token<NzToken> createTok)
    {
        Expect(NzToken.External);
        Expect(NzToken.Table);

        var (table, _) = ParseTableName();

        // SAMEAS or column definitions
        TableName? sameAs = null;
        IReadOnlyList<ColumnDefinition>? columns = null;

        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var colList = ParseColumnDefinitionList();
            Expect(NzToken.RParen);
            columns = colList.Columns;
        }

        if (Peek().Kind == NzToken.SameAs)
        {
            Advance();
            var (sa, _) = ParseTableName();
            sameAs = sa;
        }

        // USING clause — consume as command tail for now
        IReadOnlyList<ExternalTableOption>? options = null;
        if (Peek().Kind == NzToken.Using)
        {
            Advance();
            Expect(NzToken.LParen);
            options = ParseExternalTableOptions();
            Expect(NzToken.RParen);
        }

        return new CreateExternalTableStatement(FromToken(createTok), table, sameAs, columns, options);
    }

    private IReadOnlyList<ExternalTableOption> ParseExternalTableOptions()
    {
        var options = new List<ExternalTableOption>();
        while (Peek().Kind != NzToken.RParen && Peek().Kind != NzToken.Semicolon && Peek().Kind != NzToken.Unknown)
        {
            options.Add(ParseExternalTableOption());
        }
        return options;
    }

    private ExternalTableOption ParseExternalTableOption()
    {
        var nameTok = ExpectNameToken();
        var pos = FromToken(nameTok);
        var name = nameTok.ToStringValue();

        ExternalOptionValue? value = null;
        if (Peek().Kind == NzToken.StringLiteral)
        {
            value = new ExternalStringValue(FromToken(Peek()), Advance().ToStringValue());
        }
        else if (Peek().Kind == NzToken.NumberLiteral)
        {
            if (long.TryParse(Peek().ToStringValue(), out var num))
                value = new ExternalNumberValue(FromToken(Peek()), num);
            else
                value = new ExternalNumberValue(FromToken(Peek()), 0);
            Advance();
        }
        else if (Peek().Kind == NzToken.Identifier)
        {
            value = new ExternalIdentifierValue(FromToken(Peek()), Advance().ToStringValue());
        }
        else if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            if (Peek().Kind == NzToken.StringLiteral)
                value = new ExternalStringValue(FromToken(Peek()), Advance().ToStringValue());
            Expect(NzToken.RParen);
        }

        return new ExternalTableOption(pos, name, value);
    }

    // ====== CREATE SEQUENCE ======

    private CreateSequenceStatement ParseCreateSequence(Token<NzToken> createTok)
    {
        Expect(NzToken.Sequence);
        var (seq, _) = ParseTableName();
        ParseCommandTail(); // optional AS, START WITH, etc.
        return new CreateSequenceStatement(FromToken(createTok), seq);
    }

    // ====== CREATE SYNONYM ======

    private Statement ParseCreateSynonym(Token<NzToken> createTok)
    {
        Expect(NzToken.Synonym);
        var (synonym, _) = ParseTableName();
        if (Peek().Kind == NzToken.For)
        {
            Advance();
            ParseTableName();
        }
        ParseCommandTail();
        return new SetStatement(FromToken(createTok));
    }

    // ====== CREATE USER / DATABASE / GROUP ======

    private Statement ParseCreateUser(Token<NzToken> createTok)
    {
        Expect(NzToken.User);
        ExpectNameToken();
        ParseCommandTail();
        return new SetStatement(FromToken(createTok));
    }

    private Statement ParseCreateDatabase(Token<NzToken> createTok)
    {
        Expect(NzToken.Database);
        ExpectNameToken();
        ParseCommandTail();
        return new SetStatement(FromToken(createTok));
    }

    private Statement ParseCreateGroup(Token<NzToken> createTok)
    {
        // GROUP token is tokenized as Identifier; caller has matched it via IsGroupKeyword()
        if (Peek().Kind == NzToken.Identifier &&
            string.Equals(Peek().ToStringValue(), "GROUP", StringComparison.OrdinalIgnoreCase))
            Advance();
        ExpectNameToken();
        ParseCommandTail();
        return new SetStatement(FromToken(createTok));
    }

    // ====== DROP ======

    private DropStatement? ParseDrop()
    {
        var dropTok = Expect(NzToken.Drop);

        string objectType;
        var typeTokKind = Peek().Kind;

        if (typeTokKind == NzToken.External)
        {
            Advance();
            Expect(NzToken.Table);
            objectType = "EXTERNAL TABLE";
        }
        else
        {
            objectType = typeTokKind switch
            {
                NzToken.Table => "TABLE",
                NzToken.View => "VIEW",
                NzToken.Views => "VIEW",
                NzToken.Procedure => "PROCEDURE",
                NzToken.Database => "DATABASE",
                _ when IsGroupKeyword() => "GROUP",
                NzToken.Schema => "SCHEMA",
                NzToken.Sequence => "SEQUENCE",
                NzToken.Session => "SESSION",
                NzToken.Synonym => "SYNONYM",
                NzToken.User => "USER",
                NzToken.Public => "PUBLIC",
                NzToken.Type => "TYPE",
                _ => "TABLE"
            };
            if (typeTokKind != NzToken.Unknown)
                Advance();
        }

        var targets = new List<TableName>();

        // Netezza syntax: DROP TABLE [IF EXISTS] table_name [, ...]
        // Also supported: DROP TABLE table_name [, ...] IF EXISTS
        bool ifExists = false;
        if (Peek().Kind == NzToken.If)
        {
            Advance();
            if (Peek().Kind == NzToken.Exists)
            {
                Advance();
                ifExists = true;
            }
            else
            {
                _errors.Add(new ValidationError("Expected EXISTS after IF in DROP", "error",
                    SourcePosition.FromToken(Peek()), "PARSE001"));
                return null;
            }
        }

        targets.Add(ParseDropTarget());

        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            targets.Add(ParseDropTarget());
        }

        // Also handle IF EXISTS after table names (Netezza form)
        if (!ifExists && Peek().Kind == NzToken.If)
        {
            Advance();
            if (Peek().Kind == NzToken.Exists)
            {
                Advance();
                ifExists = true;
            }
            else
            {
                _errors.Add(new ValidationError("Expected EXISTS after IF in DROP", "error",
                    SourcePosition.FromToken(Peek()), "PARSE001"));
            }
        }

        if (!ifExists)
        {
            ParseCommandTail();
        }

        return new DropStatement(FromToken(dropTok), objectType, targets, ifExists);
    }

    private TableName ParseDropTarget()
    {
        if (Peek().Kind == NzToken.NumberLiteral)
        {
            var num = Peek().ToStringValue() ?? "0";
            Advance(); // session number, used with DROP SESSION
            return new TableName(num);
        }
        var (name, _) = ParseTableName();
        return name;
    }

    // ====== ALTER TABLE ======

    private AlterTableStatement ParseAlterTable()
    {
        var alterTok = Expect(NzToken.Alter);
        var objKind = Peek().Kind;

        // ALTER TABLE/VIEW/DATABASE/SEQUENCE/USER etc.
        if (objKind is NzToken.Table or NzToken.View or NzToken.Database
            or NzToken.Sequence or NzToken.User or NzToken.Schema)
        {
            Advance();
        }

        var (target, _) = ParseTableName();

        if (Peek().Kind == NzToken.Semicolon || Peek().Kind == NzToken.Unknown)
        {
            _errors.Add(new ValidationError("ALTER requires an action clause (e.g. ADD, DROP, RENAME, OWNER TO)", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));
        }

        var actionTokens = new List<Token<NzToken>>();
        while (Peek().Kind != NzToken.Semicolon && Peek().Kind != NzToken.Unknown)
            actionTokens.Add(Advance());

        var actions = actionTokens.Count == 0
            ? Array.Empty<AlterTableAction>()
            : new[] { CreateAlterAction(alterTok, actionTokens) };

        return new AlterTableStatement(FromToken(alterTok), target, actions);
    }

    private AlterTableAction CreateAlterAction(Token<NzToken> alterToken, IReadOnlyList<Token<NzToken>> tokens)
    {
        var raw = string.Join(" ", tokens.Select(t => t.ToStringValue() ?? t.Kind.ToString()));
        var first = tokens[0].Kind;
        var second = tokens.Count > 1 ? tokens[1].Kind : NzToken.Unknown;
        var position = FromToken(tokens[0]);
        if (first == NzToken.Add && second == NzToken.Column) return new AddColumnAlterAction(position, raw);
        if (first == NzToken.Add && second == NzToken.Constraint) return new AddConstraintAlterAction(position, raw);
        if (first == NzToken.Alter && second == NzToken.Column) return new AlterColumnAlterAction(position, raw);
        if (first == NzToken.Drop && second == NzToken.Column) return new DropColumnAlterAction(position, raw);
        if (first == NzToken.Drop && second == NzToken.Constraint) return new DropConstraintAlterAction(position, raw);
        if (first == NzToken.Modify && second == NzToken.Column) return new ModifyColumnAlterAction(position, raw);
        if (first == NzToken.Rename && second == NzToken.Column) return new RenameColumnAlterAction(position, raw);
        if (first == NzToken.Rename && second == NzToken.To) return new RenameToAlterAction(position, raw);
        if (first == NzToken.Owner && second == NzToken.To) return new OwnerToAlterAction(position, raw);
        if (first == NzToken.Set && second == NzToken.Privileges) return new SetPrivilegesAlterAction(position, raw);
        if (first == NzToken.Organize) return new OrganizeOnAlterAction(position, raw);
        if (first is NzToken.Cascade or NzToken.Restrict) return new CascadeAlterAction(position, raw);
        return new UnknownAlterAction(position, raw);
    }

    // ====== TRUNCATE ======

    private TruncateStatement ParseTruncate()
    {
        var truncTok = Expect(NzToken.Truncate);
        if (Peek().Kind == NzToken.Table) Advance();
        var (table, _) = ParseTableName();
        ParseCommandTail();
        return new TruncateStatement(FromToken(truncTok), table);
    }

    // ====== EXPLAIN ======

    private Statement ParseExplain()
    {
        var explainTok = Expect(NzToken.Explain);

        // Consume EXPLAIN options: VERBOSE, DISTRIBUTION, PLANTEXT, PLANGRAPH
        while (Peek().Kind is NzToken.Verbose or NzToken.Distribution
            or NzToken.Plantext or NzToken.Plangraph or NzToken.Identifier)
        {
            Advance();
        }

        // Now parse the actual statement being explained
        var inner = Parse();
        return inner ?? new SetStatement(FromToken(explainTok));
    }

    // ====== COMMENT ON ======

    private CommentStatement ParseCommentOn()
    {
        var commentTok = Expect(NzToken.Comment);
        Expect(NzToken.On);

        bool isColumn = false;
        string objectType;

        if (Peek().Kind == NzToken.Column)
        {
            isColumn = true;
            objectType = "COLUMN";
            Advance();
        }
        else
        {
            objectType = Peek().Kind switch
            {
                NzToken.Table => "TABLE",
                NzToken.View => "VIEW",
                NzToken.Procedure => "PROCEDURE",
                _ => "TABLE"
            };
            // Need to advance past the object type token unless it's an identifier (meaning "TABLE" implied)
            if (Peek().Kind is NzToken.Table or NzToken.View or NzToken.Procedure)
                Advance();
            else
                objectType = "TABLE";
        }

        var (obj, _) = ParseTableName();

        if (objectType == "PROCEDURE" && Peek().Kind == NzToken.LParen)
        {
            Advance();
            while (Peek().Kind != NzToken.RParen && Peek().Kind != NzToken.Unknown)
                Advance();
            Expect(NzToken.RParen);
        }

        string? columnName = null;
        if (isColumn && Peek().Kind == NzToken.Dot)
        {
            Advance();
            columnName = ExpectNameToken().ToStringValue();
        }

        Expect(NzToken.Is);
        var comment = Expect(NzToken.StringLiteral).ToStringValue();

        return new CommentStatement(FromToken(commentTok), objectType, obj, columnName, comment);
    }

    // ====== GRANT / REVOKE ======

    private GrantStatement ParseGrant()
    {
        var tok = Expect(NzToken.Grant);
        if (Peek().Kind is NzToken.Semicolon or NzToken.Unknown)
        {
            _errors.Add(new ValidationError(
                "Expected statement content after GRANT", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));
        }
        else
        {
            ParseCommandTail();
        }
        return new GrantStatement(FromToken(tok));
    }

    private RevokeStatement ParseRevoke()
    {
        var tok = Expect(NzToken.Revoke);
        if (Peek().Kind is NzToken.Semicolon or NzToken.Unknown)
        {
            _errors.Add(new ValidationError(
                "Expected statement content after REVOKE", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));
        }
        else
        {
            ParseCommandTail();
        }
        return new RevokeStatement(FromToken(tok));
    }

    // ====== CALL / EXECUTE ======

    private CallStatement ParseCall()
    {
        var tok = Advance(); // CALL, EXEC, or EXECUTE
        if (Peek().Kind == NzToken.Procedure) Advance();
        var (proc, _) = ParseTableName();

        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            while (Peek().Kind != NzToken.RParen && Peek().Kind != NzToken.Semicolon && Peek().Kind != NzToken.Unknown)
            {
                ParseExpression();
                if (Peek().Kind == NzToken.Comma) Advance();
            }
            Expect(NzToken.RParen);
        }

        return new CallStatement(FromToken(tok), proc);
    }

    // ====== COMMIT / ROLLBACK ======

    private CommitStatement ParseCommit()
    {
        var tok = Expect(NzToken.Commit);
        return new CommitStatement(FromToken(tok));
    }

    private RollbackStatement ParseRollback()
    {
        var tok = Expect(NzToken.Rollback);
        return new RollbackStatement(FromToken(tok));
    }

    // ====== GROOM TABLE ======

    private GroomStatement ParseGroom()
    {
        var groomTok = Expect(NzToken.Groom);
        Expect(NzToken.Table);
        var (table, _) = ParseTableName();

        GroomMode? mode = null;
        GroomReclaim? reclaim = null;

        var k = Peek().Kind;
        if (k == NzToken.Versions)
        {
            Advance();
            mode = new GroomMode(FromToken(groomTok), GroomModeKind.Versions);
        }
        else if (k == NzToken.Records)
        {
            Advance();
            mode = new GroomMode(FromToken(groomTok), GroomModeKind.Records);
            if (Peek().Kind is NzToken.All or NzToken.Ready) Advance();
        }
        else if (k == NzToken.Pages)
        {
            Advance();
            mode = new GroomMode(FromToken(groomTok), GroomModeKind.Pages);
            if (Peek().Kind is NzToken.All or NzToken.Start) Advance();
        }

        if (Peek().Kind == NzToken.Reclaim)
        {
            Advance();
            Expect(NzToken.Backupset);
            if (Peek().Kind is NzToken.None or NzToken.Default) Advance();
            reclaim = new GroomReclaim(SourcePosition.FromToken(groomTok), true, false);
        }

        return new GroomStatement(FromToken(groomTok), table, mode, reclaim);
    }

    // ====== GENERATE STATISTICS ======

    private GenerateStatisticsStatement ParseGenerateStatistics()
    {
        var genTok = Expect(NzToken.Generate);

        bool express = false;
        if (Peek().Kind == NzToken.Express)
        {
            express = true;
            Advance();
        }

        Expect(NzToken.Statistics);

        IReadOnlyList<string>? columns = null;
        TableName? table = null;

        if (Peek().Kind == NzToken.On)
        {
            Advance();
            var (t, _) = ParseTableName();
            table = t;

            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                columns = ParseIdentifierList();
                Expect(NzToken.RParen);
            }
        }
        else if (Peek().Kind == NzToken.For)
        {
            Advance();
            Expect(NzToken.Table);
            var (t, _) = ParseTableName();
            table = t;
        }

        return new GenerateStatisticsStatement(FromToken(genTok),
            table ?? new TableName(""),
            express, columns);
    }

    private record ColumnDefList(IReadOnlyList<ColumnDefinition> Columns, IReadOnlyList<TableConstraint> Constraints);
}
