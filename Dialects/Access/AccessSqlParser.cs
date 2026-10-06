using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Dialects.Access;

/// <summary>
/// Microsoft Access / Jet / ACE SQL parser. The shared recursive-descent
/// parser supplies the common SELECT, DML, DDL and expression grammar; this
/// class owns Access query modifiers, crosstabs, PARAMETERS and index syntax.
/// </summary>
public sealed class AccessSqlParser : Parser.NzSqlParser
{
    private bool _parsingPivotExpression;

    public AccessSqlParser(Token<NzToken>[] tokens) : base(tokens, strictNetezzaIntervalSyntax: false)
    {
    }

    protected override bool StopInExpression => _parsingPivotExpression;

    public override Statement? Parse()
    {
        SkipSemicolons();
        if (Peek().Kind == NzToken.Unknown)
            return null;

        if (IsWord(Peek(), "PARAMETERS"))
            return ParseParameterizedStatement();

        if (Peek().Kind == NzToken.AccessTransform)
            return ParseCrosstabStatement();

        if (Peek().Kind == NzToken.Drop && IsWord(Peek(1), "INDEX"))
            return ParseDropIndex();

        if (Peek().Kind == NzToken.Create && IsCreateIndexStart())
            return ParseCreateIndex();

        if (Peek().Kind == NzToken.Update)
            return ParseAccessUpdate();

        if (Peek().Kind == NzToken.Delete)
            return ParseAccessDelete();

        return base.Parse();
    }

    private UpdateStatement? ParseAccessUpdate()
    {
        var update = Expect(NzToken.Update);
        var targetReference = ParseTableReference();
        if (targetReference.Source.Table is not { } target)
        {
            AddParserError("Expected table after UPDATE", Peek(), "PAR115");
            return null;
        }

        if (Peek().Kind != NzToken.Set)
        {
            AddParserError("Expected SET after UPDATE table reference", Peek(), "PAR115");
            return null;
        }
        Advance();

        var setItems = new List<UpdateSetItem> { ParseUpdateSetItem() };
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

        return new UpdateStatement(
            FromToken(update), target, targetReference.Source.Alias,
            setItems, null, where,
            AccessTargetReference: targetReference);
    }

    private DeleteStatement? ParseAccessDelete()
    {
        var delete = Expect(NzToken.Delete);
        var wildcard = false;
        if (Peek().Kind == NzToken.Multiply)
        {
            wildcard = true;
            Advance();
        }

        if (Peek().Kind != NzToken.From)
        {
            AddParserError("Expected FROM after DELETE or DELETE *", Peek(), "PAR116");
            return null;
        }
        Advance();

        var targetReference = ParseTableReference();
        if (targetReference.Source.Table is not { } target)
        {
            AddParserError("Expected table after DELETE FROM", Peek(), "PAR116");
            return null;
        }

        Expression? where = null;
        if (Peek().Kind == NzToken.Where)
        {
            Advance();
            where = ParseExpression();
        }

        return new DeleteStatement(
            FromToken(delete), target, targetReference.Source.Alias, where,
            AccessTargetReference: targetReference, AccessWildcard: wildcard);
    }

    protected override SelectStatement ParseSelectStatement(WithClause? with = null)
        => ParseAccessSelectStatement(with, stopAtPivot: false);

    private SelectStatement ParseAccessSelectStatement(WithClause? with, bool stopAtPivot)
    {
        var select = Expect(NzToken.Select);
        var distinct = false;
        var all = false;
        var distinctRow = false;
        AccessTopClause? top = null;

        // Access accepts the modifier and TOP in either order in commonly
        // emitted SQL. Accepting both forms keeps editing tolerant while the
        // formatter emits the canonical modifier-then-TOP order.
        var readingModifiers = true;
        while (readingModifiers)
        {
            switch (Peek().Kind)
            {
                case NzToken.Distinct:
                    distinct = true;
                    Advance();
                    break;
                case NzToken.All:
                    all = true;
                    Advance();
                    break;
                case NzToken.AccessDistinctRow:
                    distinctRow = true;
                    Advance();
                    break;
                case NzToken.AccessTop:
                    top = ParseTopClause();
                    break;
                default:
                    readingModifiers = false;
                    break;
            }
        }

        var items = ParseSelectList();
        var hasInto = false;
        if (Peek().Kind == NzToken.Into)
        {
            hasInto = true;
            Advance();
            while (IsContextualIdentifier(Peek().Kind))
            {
                Advance();
                if (Peek().Kind == NzToken.Comma)
                    Advance();
                else
                    break;
            }
        }

        IReadOnlyList<TableReference>? from = null;
        Expression? where = null;
        IReadOnlyList<Expression>? groupBy = null;
        Expression? having = null;
        IReadOnlyList<OrderByItem>? orderBy = null;
        string? externalDatabase = null;

        if (Peek().Kind == NzToken.From)
        {
            Advance();
            from = ParseTableReferences();
            if (Peek().Kind == NzToken.In)
            {
                Advance();
                externalDatabase = ParseExternalDatabase();
            }
        }

        if (Peek().Kind == NzToken.Where)
        {
            Advance();
            where = ParseExpression();
        }

        if (Peek().Kind == NzToken.GroupBy)
        {
            Advance();
            groupBy = ParseExpressionList();
        }

        if (Peek().Kind == NzToken.Having)
        {
            Advance();
            having = ParseExpression();
        }

        if (Peek().Kind == NzToken.OrderBy)
        {
            Advance();
            orderBy = ParseOrderByItems();
        }

        if (!stopAtPivot && Peek().Kind is NzToken.Limit or NzToken.Offset or NzToken.Fetch)
        {
            AddParserError("Access SQL uses TOP instead of LIMIT/OFFSET/FETCH", Peek(), "ACC001");
            ConsumeUntilStatementBoundary();
        }

        var withOwnerAccess = !stopAtPivot && TryParseOwnerAccessOption();

        var setOps = new List<SetOperation>();
        var compound = new List<SelectStatement>();
        if (!stopAtPivot)
        {
            while (IsSetOperationStart())
            {
                var opToken = Advance();
                var opType = opToken.Kind switch
                {
                    NzToken.Union => SetOperationType.Union,
                    NzToken.Intersect => SetOperationType.Intersect,
                    NzToken.Except or NzToken.MinusSet => SetOperationType.Except,
                    _ => SetOperationType.Except
                };
                var opAll = false;
                if (Peek().Kind == NzToken.All)
                {
                    opAll = true;
                    Advance();
                }
                else if (Peek().Kind == NzToken.Distinct)
                {
                    Advance();
                }

                setOps.Add(new SetOperation(FromToken(opToken), opType, opAll));
                if (Peek().Kind == NzToken.LParen)
                {
                    Advance();
                    WithClause? nestedWith = null;
                    if (Peek().Kind == NzToken.With)
                        nestedWith = ParseWithClause();
                    compound.Add(ParseAccessSelectStatement(nestedWith, stopAtPivot: false));
                    Expect(NzToken.RParen);
                }
                else
                {
                    WithClause? nestedWith = null;
                    if (Peek().Kind == NzToken.With)
                        nestedWith = ParseWithClause();
                    compound.Add(ParseAccessSelectStatement(nestedWith, stopAtPivot: false));
                }
            }
        }

        AccessQueryOptions? accessOptions = null;
        if (top is not null || externalDatabase is not null || withOwnerAccess)
        {
            accessOptions = new AccessQueryOptions(
                FromToken(select), top, externalDatabase, withOwnerAccess);
        }

        return new SelectStatement(
            FromToken(select),
            distinct || all || distinctRow ? new SelectModifier(distinct, all, distinctRow) : null,
            items,
            from,
            where,
            groupBy,
            having,
            orderBy,
            null,
            setOps.Count == 0 ? null : setOps,
            compound.Count == 0 ? null : compound,
            with,
            hasInto,
            null,
            null,
            null,
            null,
            accessOptions);
    }

    private AccessTopClause ParseTopClause()
    {
        var top = Expect(NzToken.AccessTop);
        Expression count;
        if (Peek().Kind == NzToken.NumberLiteral)
        {
            var number = Advance();
            count = new Literal(FromToken(number), LiteralKind.Number, number.ToStringValue());
        }
        else if (Peek().Kind is NzToken.Parameter or NzToken.AccessNamedParameter)
        {
            var parameter = Advance();
            count = parameter.Kind == NzToken.Parameter
                ? new ParameterExpression(FromToken(parameter))
                : new ParameterExpression(FromToken(parameter), parameter.ToStringValue());
        }
        else
        {
            count = ParseExpression();
        }
        var percent = false;
        if (Peek().Kind == NzToken.AccessPercent)
        {
            percent = true;
            Advance();
        }
        return new AccessTopClause(FromToken(top), count, percent);
    }

    private string ParseExternalDatabase()
    {
        var token = Peek();
        if (token.Kind is NzToken.StringLiteral or NzToken.AccessDateLiteral)
            return Advance().ToStringValue();

        return ExpectNameToken().ToStringValue();
    }

    private AccessCrosstabStatement ParseCrosstabStatement()
    {
        var transform = Expect(NzToken.AccessTransform);
        var transformItems = ParseSelectList();
        var rowQuery = ParseAccessSelectStatement(null, stopAtPivot: true);
        var pivot = Expect(NzToken.AccessPivot);

        Expression pivotExpression;
        _parsingPivotExpression = true;
        try
        {
            pivotExpression = ParseExpression();
        }
        finally
        {
            _parsingPivotExpression = false;
        }

        IReadOnlyList<Expression>? inValues = null;
        if (Peek().Kind == NzToken.In)
        {
            Advance();
            Expect(NzToken.LParen);
            if (Peek().Kind != NzToken.RParen)
                inValues = ParseExpressionList();
            Expect(NzToken.RParen);
        }

        var withOwnerAccess = TryParseOwnerAccessOption();
        AccessQueryOptions? options = withOwnerAccess
            ? new AccessQueryOptions(FromToken(transform), null, null, true)
            : null;

        return new AccessCrosstabStatement(
            FromToken(transform), transformItems, rowQuery,
            pivotExpression, inValues, options);
    }

    private AccessParameterizedStatement? ParseParameterizedStatement()
    {
        var parametersToken = Advance();
        var declarations = new List<AccessParameterDeclaration>();

        while (Peek().Kind is not NzToken.Semicolon and not NzToken.Unknown)
        {
            var nameToken = ExpectNameToken();
            var type = ParseDataType();
            declarations.Add(new AccessParameterDeclaration(
                FromToken(nameToken), StripQuotes(nameToken.ToStringValue()), type));

            if (Peek().Kind != NzToken.Comma)
                break;
            Advance();
        }

        if (Peek().Kind == NzToken.Semicolon)
            Advance();

        var body = Parse();
        if (body is null)
            return null;

        var clause = new AccessParametersClause(FromToken(parametersToken), declarations);
        return new AccessParameterizedStatement(FromToken(parametersToken), clause, body);
    }

    private bool TryParseOwnerAccessOption()
    {
        if (Peek().Kind != NzToken.With || !IsWord(Peek(1), "OWNERACCESS") || !IsWord(Peek(2), "OPTION"))
            return false;

        Advance();
        Advance();
        Advance();
        return true;
    }

    private bool IsCreateIndexStart()
    {
        if (Peek().Kind != NzToken.Create)
            return false;
        if (IsWord(Peek(1), "INDEX"))
            return true;
        return Peek(1).Kind == NzToken.Unique && IsWord(Peek(2), "INDEX");
    }

    private AccessCreateIndexStatement ParseCreateIndex()
    {
        var create = Expect(NzToken.Create);
        var unique = false;
        if (Peek().Kind == NzToken.Unique)
        {
            unique = true;
            Advance();
        }

        ExpectAccessWord("INDEX");
        var name = ExpectNameToken();
        ExpectAccessWord("ON");
        var (table, _) = ParseTableName();

        var columns = new List<string>();
        var indexColumns = new List<AccessIndexColumn>();
        Expect(NzToken.LParen);
        if (Peek().Kind != NzToken.RParen)
        {
            var columnToken = ExpectNameToken();
            var column = new AccessIndexColumn(
                StripQuotes(columnToken.ToStringValue()), IdentifierQuote(columnToken.Kind));
            indexColumns.Add(column);
            columns.Add(column.Name);
            indexColumns[^1] = indexColumns[^1] with { Direction = ParseIndexColumnDirection() };
            while (Peek().Kind == NzToken.Comma)
            {
                Advance();
                columnToken = ExpectNameToken();
                column = new AccessIndexColumn(
                    StripQuotes(columnToken.ToStringValue()), IdentifierQuote(columnToken.Kind));
                indexColumns.Add(column);
                columns.Add(column.Name);
                indexColumns[^1] = indexColumns[^1] with { Direction = ParseIndexColumnDirection() };
            }
        }
        Expect(NzToken.RParen);

        var options = CaptureTailTokens();
        return new AccessCreateIndexStatement(
            FromToken(create), StripQuotes(name.ToStringValue()), table, unique, columns, options,
            IdentifierQuote(name.Kind), indexColumns);
    }

    private AccessIndexColumnDirection ParseIndexColumnDirection()
    {
        if (Peek().Kind == NzToken.Asc)
        {
            Advance();
            return AccessIndexColumnDirection.Ascending;
        }
        if (Peek().Kind == NzToken.Desc)
        {
            Advance();
            return AccessIndexColumnDirection.Descending;
        }

        return AccessIndexColumnDirection.Unspecified;
    }

    private AccessDropIndexStatement ParseDropIndex()
    {
        var drop = Expect(NzToken.Drop);
        ExpectAccessWord("INDEX");
        var name = ExpectNameToken();
        ExpectAccessWord("ON");
        var (table, _) = ParseTableName();
        return new AccessDropIndexStatement(
            FromToken(drop), StripQuotes(name.ToStringValue()), table, IdentifierQuote(name.Kind));
    }

    protected override bool IsDialectColumnClauseStart() =>
        IsWord(Peek(), "AUTOINCREMENT")
        || IsWord(Peek(), "IDENTITY")
        || IsWord(Peek(), "COLLATE")
        || IsWord(Peek(), "WITH");

    protected override bool TryParseDialectColumnClause()
    {
        if (CurrentDialectColumnTokens is null)
            return false;

        if (IsWord(Peek(), "AUTOINCREMENT") || IsWord(Peek(), "IDENTITY"))
        {
            CurrentDialectColumnTokens.Add(Advance());
            if (Peek().Kind == NzToken.LParen)
            {
                var depth = 0;
                do
                {
                    var token = Advance();
                    CurrentDialectColumnTokens.Add(token);
                    if (token.Kind == NzToken.LParen)
                        depth++;
                    else if (token.Kind == NzToken.RParen)
                        depth--;
                }
                while (depth > 0 && Peek().Kind is not NzToken.Unknown and not NzToken.Semicolon);
            }
            return true;
        }

        if (IsWord(Peek(), "COLLATE"))
        {
            CurrentDialectColumnTokens.Add(Advance());
            if (Peek().Kind.IsIdentifierLike())
                CurrentDialectColumnTokens.Add(Advance());
            return true;
        }

        if (IsWord(Peek(), "WITH"))
        {
            CurrentDialectColumnTokens.Add(Advance());
            if (IsWord(Peek(), "COMPRESSION") || IsWord(Peek(), "COMP"))
                CurrentDialectColumnTokens.Add(Advance());
            return true;
        }

        return false;
    }

    protected override DataTypeInfo ParseDataType()
    {
        var first = ExpectNameToken();
        var nameParts = new List<string> { StripQuotes(first.ToStringValue()) };

        while (Peek().Kind.IsIdentifierLike() && !IsAccessTypeBoundary(Peek()))
            nameParts.Add(Advance().ToStringValue());

        IReadOnlyList<string>? parameters = null;
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var values = new List<string>();
            while (Peek().Kind is not NzToken.RParen and not NzToken.Unknown)
            {
                if (Peek().Kind == NzToken.Comma)
                {
                    Advance();
                    continue;
                }
                values.Add(Advance().ToStringValue());
            }
            Expect(NzToken.RParen);
            parameters = values.Count == 0 ? null : values;
        }

        return new DataTypeInfo(FromToken(first), string.Join(" ", nameParts), parameters);
    }

    private static bool IsAccessTypeBoundary(Token<NzToken> token) =>
        IsWord(token, "AUTOINCREMENT")
        || IsWord(token, "IDENTITY")
        || IsWord(token, "COLLATE")
        || IsWord(token, "WITH")
        || IsWord(token, "DEFAULT")
        || IsWord(token, "CONSTRAINT")
        || token.Kind is NzToken.Not or NzToken.Null or NzToken.Primary or NzToken.Unique
            or NzToken.References or NzToken.Check or NzToken.Comma or NzToken.RParen;

    private IReadOnlyList<Token<NzToken>>? CaptureTailTokens()
    {
        var tokens = new List<Token<NzToken>>();
        while (Peek().Kind is not NzToken.Semicolon and not NzToken.Unknown)
            tokens.Add(Advance());
        return tokens.Count == 0 ? null : tokens;
    }

    private void ConsumeUntilStatementBoundary()
    {
        while (Peek().Kind is not NzToken.Semicolon and not NzToken.Unknown)
            Advance();
    }

    private Token<NzToken> ExpectAccessWord(string word)
    {
        if (IsWord(Peek(), word))
            return Advance();

        AddParserError($"Expected {word} in Access statement", Peek(), "ACC001");
        return Peek();
    }

    private static bool IsWord(Token<NzToken> token, string word) =>
        string.Equals(token.ToStringValue(), word, StringComparison.OrdinalIgnoreCase);
}
