using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Parser;

public sealed partial class PostgreSqlSqlParser
{
    private bool PostgreSqlWordIs(string word) => Peek().ToStringValue().Equals(word, StringComparison.OrdinalIgnoreCase);

    private Token<NzToken> ExpectPostgreSqlWord(string word)
    {
        var token = Peek();
        if (!PostgreSqlWordIs(word)) AddParserError($"Expected {word}", token, "PAR001");
        if (token.Kind != NzToken.Unknown) Advance();
        return token;
    }

    private IReadOnlyList<Token<NzToken>> ParsePostgreSqlBalancedTokens()
    {
        var start = _pos;
        Expect(NzToken.LParen);
        var depth = 1;
        while (depth > 0 && Peek().Kind != NzToken.Unknown)
        {
            var token = Advance();
            if (token.Kind == NzToken.LParen) depth++;
            else if (token.Kind == NzToken.RParen) depth--;
            else if (token.Kind == NzToken.PostgreSqlInvalidLiteral)
                AddParserError("Unterminated PostgreSQL literal", token, "PAR110");
        }
        if (depth != 0) AddParserError("Unclosed PostgreSQL clause", Peek(), "PAR107");
        return _tokens[start.._pos];
    }

    private IReadOnlyList<string> ParsePostgreSqlIdentifierList()
    {
        Expect(NzToken.LParen);
        var names = new List<string> { StripQuotes(ExpectNameToken().ToStringValue()) };
        while (Peek().Kind == NzToken.Comma) { Advance(); names.Add(StripQuotes(ExpectNameToken().ToStringValue())); }
        Expect(NzToken.RParen);
        return names;
    }

    protected override string? ParseCteMaterialization()
    {
        if (Peek().Kind == NzToken.Not) { Advance(); Expect(NzToken.Materialized); return "NOT MATERIALIZED"; }
        if (Peek().Kind == NzToken.Materialized) { Advance(); return "MATERIALIZED"; }
        return null;
    }

    protected override Expression ParseDialectPostfix(Expression expression)
    {
        if (Peek().Kind != NzToken.LBracket) return expression;
        var start = Advance();
        var index = ParseExpression();
        Expect(NzToken.RBracket);
        return new ArraySubscriptExpression(FromToken(start), expression, index);
    }

    protected override Expression ParseDialectComparison(Expression expression)
    {
        while (Peek().Kind is NzToken.PostgreSqlJsonExists or NzToken.PostgreSqlJsonExistsAny or NzToken.PostgreSqlJsonExistsAll)
        {
            var op = Advance();
            var kind = op.Kind switch
            {
                NzToken.PostgreSqlJsonExists => BinaryOperator.JsonExists,
                NzToken.PostgreSqlJsonExistsAny => BinaryOperator.JsonExistsAny,
                _ => BinaryOperator.JsonExistsAll,
            };
            expression = new BinaryExpression(FromToken(op), kind, expression, ParseAdditive(), op.ToStringValue());
        }
        return expression;
    }

    private Statement RejectPostgreSqlCommand(string message)
    {
        var token = Peek();
        AddParserError(message, token, "PAR001");
        ParseCommandTail();
        return new PostgreSqlCommandStatement(FromToken(token), "unsupported", []);
    }

    private Statement ParsePostgreSqlDrop()
    {
        var token = Advance();
        var kind = Peek();
        if (kind.Kind == NzToken.Synonym)
            AddParserError("DROP SYNONYM is not PostgreSQL syntax", kind, "PAR001");
        Advance();
        var ifExists = Peek().Kind == NzToken.If;
        if (ifExists) { Advance(); Expect(NzToken.Exists); }
        var targets = new List<TableName> { ParseTableName().Table };
        while (Peek().Kind == NzToken.Comma) { Advance(); targets.Add(ParseTableName().Table); }
        var behavior = PostgreSqlWordIs("CASCADE") || PostgreSqlWordIs("RESTRICT") ? Advance().ToStringValue() : null;
        return new DropStatement(FromToken(token), kind.ToStringValue().ToUpperInvariant(), targets, ifExists)
            { IfExistsBeforeTarget = true, DropBehavior = behavior };
    }

    private Statement ParsePostgreSqlDo()
    {
        var start = _pos;
        var token = Advance();
        Expect(NzToken.StringLiteral);
        if (Peek().Kind == NzToken.Language) { Advance(); ExpectNameToken(); }
        return new PostgreSqlCommandStatement(FromToken(token), "do", _tokens[start.._pos]);
    }

    private Statement ParsePostgreSqlExplain()
    {
        var start = _pos;
        var token = Advance();
        var parenthesized = Peek().Kind == NzToken.LParen;
        if (parenthesized) Advance();
        var count = 0;
        var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "ANALYZE", "VERBOSE", "COSTS", "SETTINGS", "BUFFERS", "WAL", "TIMING", "SUMMARY", "FORMAT", "GENERIC_PLAN", "SERIALIZE", "MEMORY" };
        while (Peek().Kind != NzToken.Unknown)
        {
            var word = Peek().ToStringValue();
            if (!parenthesized && !options.Contains(word)) break;
            if (parenthesized && Peek().Kind == NzToken.RParen) break;
            var option = Advance();
            count++;
            if (!options.Contains(word)) AddParserError("Unknown PostgreSQL EXPLAIN option", option, "PAR001");
            if (word.Equals("FORMAT", StringComparison.OrdinalIgnoreCase))
            {
                if (!(PostgreSqlWordIs("TEXT") || PostgreSqlWordIs("XML") || PostgreSqlWordIs("JSON") || PostgreSqlWordIs("YAML")))
                    AddParserError("Invalid EXPLAIN format", Peek(), "PAR001");
                if (Peek().Kind != NzToken.Unknown) Advance();
            }
            else if (PostgreSqlWordIs("TRUE") || PostgreSqlWordIs("FALSE") || PostgreSqlWordIs("ON") || PostgreSqlWordIs("OFF") || Peek().Kind == NzToken.NumberLiteral)
                Advance();
            if (parenthesized)
            {
                if (Peek().Kind != NzToken.Comma) break;
                Advance();
            }
        }
        if (parenthesized)
        {
            if (count == 0) AddParserError("EXPLAIN option list cannot be empty", Peek(), "PAR001");
            Expect(NzToken.RParen);
        }
        if (Peek().Kind is NzToken.Plangraph or NzToken.Plantext or NzToken.Distribution)
            return RejectPostgreSqlCommand("Netezza EXPLAIN options are not PostgreSQL syntax");
        Parse();
        return new PostgreSqlCommandStatement(FromToken(token), "explain", _tokens[start.._pos]);
    }

    protected override Statement? ParseCreate()
    {
        var offset = Peek(1).Kind == NzToken.Or ? 3 : 1;
        var kind = Peek(offset).ToStringValue().ToUpperInvariant();
        if (kind == "SYNONYM") return RejectPostgreSqlCommand("CREATE SYNONYM is not PostgreSQL syntax");
        if (kind is "FUNCTION" or "PROCEDURE") return ParsePostgreSqlRoutine();
        if (kind is "TYPE" or "DOMAIN" or "INDEX" or "UNIQUE") return ParsePostgreSqlDefinition();
        return base.ParseCreate();
    }

    private Statement ParsePostgreSqlRoutine()
    {
        var start = _pos;
        var create = Advance();
        if (Peek().Kind == NzToken.Or) { Advance(); Expect(NzToken.Replace); }
        var kind = Advance().ToStringValue();
        ParseTableName();
        ParsePostgreSqlBalancedTokens();
        var body = false;
        while (Peek().Kind is not (NzToken.Unknown or NzToken.Semicolon))
        {
            if (Peek().Kind == NzToken.As)
            {
                Advance();
                Expect(NzToken.StringLiteral);
                body = true;
            }
            else if (Peek().Kind == NzToken.Begin)
            {
                Advance();
                ExpectPostgreSqlWord("ATOMIC");
                while (Peek().Kind is not (NzToken.End or NzToken.Unknown))
                {
                    var before = _pos;
                    Parse();
                    SkipSemicolons();
                    if (_pos <= before) break;
                }
                Expect(NzToken.End);
                body = true;
                break;
            }
            else if (Peek().Kind == NzToken.PostgreSqlInvalidLiteral)
            {
                AddParserError("Unterminated routine body", Advance(), "PAR110");
            }
            else if (Peek().Kind == NzToken.LParen) ParsePostgreSqlBalancedTokens();
            else Advance();
        }
        if (!body) AddParserError("Routine body is required", create, "PAR001");
        return new PostgreSqlCommandStatement(FromToken(create), "create", _tokens[start.._pos]);
    }

    private Statement ParsePostgreSqlDefinition()
    {
        var start = _pos;
        var create = Advance();
        if (Peek().Kind == NzToken.Unique) Advance();
        var kind = Advance().ToStringValue().ToUpperInvariant();
        ParseTableName();
        if (kind == "TYPE")
        {
            Expect(NzToken.As);
            if (PostgreSqlWordIs("ENUM"))
            {
                Advance(); Expect(NzToken.LParen);
                if (Peek().Kind != NzToken.RParen)
                {
                    Expect(NzToken.StringLiteral);
                    while (Peek().Kind == NzToken.Comma) { Advance(); Expect(NzToken.StringLiteral); }
                }
                Expect(NzToken.RParen);
            }
            else ParsePostgreSqlBalancedTokens();
        }
        else if (kind == "DOMAIN")
        {
            Expect(NzToken.As); ParseDataType();
            if (Peek().Kind == NzToken.Check) { Advance(); ParsePostgreSqlBalancedTokens(); }
        }
        else
        {
            Expect(NzToken.On); ParseTableName();
            Expect(NzToken.LParen);
            ParseExpression();
            if (PostgreSqlWordIs("ASC") || PostgreSqlWordIs("DESC")) Advance();
            if (Peek().Kind == NzToken.Nulls) { Advance(); Advance(); }
            while (Peek().Kind == NzToken.Comma) { Advance(); ParseExpression(); }
            Expect(NzToken.RParen);
            if (PostgreSqlWordIs("INCLUDE")) { Advance(); ParsePostgreSqlIdentifierList(); }
            if (Peek().Kind == NzToken.Where) { Advance(); ParseExpression(); }
        }
        return new PostgreSqlCommandStatement(FromToken(create), "create", _tokens[start.._pos]);
    }

    protected override bool IsDialectColumnClauseStart() => PostgreSqlWordIs("GENERATED");

    protected override bool TryParseDialectColumnClause()
    {
        if (!IsDialectColumnClauseStart()) return false;
        var start = _pos;
        Advance();
        if (PostgreSqlWordIs("ALWAYS")) Advance();
        else { ExpectPostgreSqlWord("BY"); Expect(NzToken.Default); }
        Expect(NzToken.As);
        if (PostgreSqlWordIs("IDENTITY"))
        {
            Advance();
            if (Peek().Kind == NzToken.LParen) ParsePostgreSqlBalancedTokens();
        }
        else
        {
            Expect(NzToken.LParen); ParseExpression(); Expect(NzToken.RParen);
            if (PostgreSqlWordIs("STORED") || PostgreSqlWordIs("VIRTUAL")) Advance();
            else AddParserError("Generated expression requires STORED or VIRTUAL", Peek(), "PAR001");
        }
        CurrentDialectColumnTokens?.AddRange(_tokens[start.._pos]);
        return true;
    }

    protected override (DistributeClause? Distribute, OrganizeClause? Organize) ParseTableStorageClauses() => (null, null);

    protected override IReadOnlyList<Token<NzToken>>? ParseDialectTableOptions()
    {
        var start = _pos;
        if (Peek().Kind == NzToken.PartitionBy)
        {
            Advance();
            if (!(PostgreSqlWordIs("RANGE") || PostgreSqlWordIs("LIST") || PostgreSqlWordIs("HASH")))
                AddParserError("Unknown partition method", Peek(), "PAR001");
            Advance();
            Expect(NzToken.LParen); ParseExpressionList(); Expect(NzToken.RParen);
        }
        return _pos == start ? null : _tokens[start.._pos];
    }

    private FunctionCall ParsePostgreSqlJsonTable(Token<NzToken> name)
    {
        var start = _pos - 1;
        Expect(NzToken.LParen);
        var source = ParseExpression(); Expect(NzToken.Comma); var path = ParseExpression();
        ExpectPostgreSqlWord("COLUMNS"); Expect(NzToken.LParen);
        var columns = new List<string>();
        do
        {
            columns.Add(PostgreSqlName(ExpectNameToken())); ParseDataType();
            ExpectPostgreSqlWord("PATH"); Expect(NzToken.StringLiteral);
            if (Peek().Kind != NzToken.Comma) break;
            Advance();
        } while (true);
        Expect(NzToken.RParen); Expect(NzToken.RParen);
        return new FunctionCall(FromToken(name), "JSON_TABLE", null, false, false, new[] { source, path }, null, null) { PostgreSqlTokens = _tokens[start.._pos], ResultColumnNames = columns };
    }

    private Statement ParsePostgreSqlMerge()
    {
        var merge = Advance(); Expect(NzToken.Into);
        var (target, _) = ParseTableName(); var alias = ParseAliasName();
        Expect(NzToken.Using); var source = ParseTableSource(); Expect(NzToken.On); var on = ParseExpression();
        var clauses = new List<MergeClause>();
        while (Peek().Kind == NzToken.When)
        {
            var when = Advance();
            var unmatched = Peek().Kind == NzToken.Not;
            if (unmatched) Advance();
            ExpectPostgreSqlWord("MATCHED");
            var bySource = false;
            if (PostgreSqlWordIs("BY"))
            {
                Advance();
                bySource = PostgreSqlWordIs("SOURCE");
                if (!(PostgreSqlWordIs("SOURCE") || PostgreSqlWordIs("TARGET")) || !unmatched)
                    AddParserError("BY SOURCE/TARGET requires NOT MATCHED", Peek(), "PAR001");
                Advance();
            }
            Expression? condition = null;
            if (Peek().Kind == NzToken.And) { Advance(); condition = ParseExpression(); }
            Expect(NzToken.Then);
            if (Peek().Kind == NzToken.Delete)
            {
                if (unmatched && !bySource) AddParserError("NOT MATCHED BY TARGET cannot DELETE", Peek(), "PAR001");
                Advance(); clauses.Add(new MergeMatchedDeleteClause(FromToken(when), condition) { NotMatchedBySource = bySource });
            }
            else if (Peek().Kind == NzToken.Update)
            {
                if (unmatched && !bySource) AddParserError("NOT MATCHED BY TARGET cannot UPDATE", Peek(), "PAR001");
                Advance(); Expect(NzToken.Set);
                var items = new List<UpdateSetItem> { ParsePostgreSqlSetItem() };
                while (Peek().Kind == NzToken.Comma) { Advance(); items.Add(ParsePostgreSqlSetItem()); }
                clauses.Add(new MergeMatchedUpdateClause(FromToken(when), condition, items, null) { NotMatchedBySource = bySource });
            }
            else if (Peek().Kind == NzToken.Insert)
            {
                if (!unmatched || bySource) AddParserError("INSERT requires NOT MATCHED BY TARGET", Peek(), "PAR001");
                Advance(); var columns = Peek().Kind == NzToken.LParen ? ParsePostgreSqlIdentifierList() : null;
                Expect(NzToken.Values); Expect(NzToken.LParen); var values = ParseExpressionList(); Expect(NzToken.RParen);
                clauses.Add(new MergeNotMatchedInsertClause(FromToken(when), condition, columns, values));
            }
            else { AddParserError("Expected MERGE action", Peek(), "PAR001"); break; }
        }
        if (clauses.Count == 0) AddParserError("MERGE requires a WHEN action", merge, "PAR001");
        var returning = ParseReturningClause();
        return new MergeStatement(FromToken(merge), target, alias, source, on, clauses) { Returning = returning };
    }
}
