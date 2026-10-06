using System.Text.RegularExpressions;
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
    // ====== CREATE PROCEDURE (NZPLSQL) ======

    private Statement ParseCreateProcedure(Token<NzToken> createTok, bool orReplace)
    {
        Expect(NzToken.Procedure);

        // Procedure name
        var (procName, _) = ParseTableName();

        // Arguments: (param1 type1, param2 type2, ...) or VARARGS
        Expect(NzToken.LParen);
        IReadOnlyList<ProcedureParameter>? parameters = null;
        if (Peek().Kind != NzToken.RParen)
        {
            parameters = ParseProcedureArguments();
        }
        Expect(NzToken.RParen);

        // Signature: RETURNS / EXECUTE AS / LANGUAGE in any order
        var (returns, executeAs, hasReturns, hasLanguage) = ParseProcedureSignatureSpec();

        if (!hasReturns)
            _errors.Add(new ValidationError("RETURNS clause is required in CREATE PROCEDURE", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));
        if (!hasLanguage)
            _errors.Add(new ValidationError("LANGUAGE clause is required in CREATE PROCEDURE", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));

        // Netezza requires AS; IS is rejected (procedure matrix hdr_is_rejected).
        if (Peek().Kind == NzToken.As)
        {
            Advance();
        }
        else if (Peek().Kind == NzToken.Is)
        {
            _errors.Add(new ValidationError(
                "IS is not valid after a procedure signature; use AS",
                "error", SourcePosition.FromToken(Peek()), "PAR001"));
            Advance();
        }
        else
        {
            _errors.Add(new ValidationError("Expected AS after procedure signature", "error",
                SourcePosition.FromToken(Peek()), "PARSE001"));
        }

        // Procedure body
        var body = ParseProcedureBody();

        return new CreateProcedureStatement(
            FromToken(createTok), procName, orReplace, parameters, returns, executeAs, "NZPLSQL", body);
    }

    private IReadOnlyList<ProcedureParameter> ParseProcedureArguments()
    {
        var parameters = new List<ProcedureParameter>();
        if (Peek().Kind == NzToken.Varargs)
        {
            Advance();
            return parameters;
        }

        var first = ParseProcedureArgument();
        if (first is not null) parameters.Add(first);
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            var parameter = ParseProcedureArgument();
            if (parameter is not null) parameters.Add(parameter);
        }
        return parameters;
    }

    private ProcedureParameter? ParseProcedureArgument()
    {
        var mode = ProcedureParameterMode.In;
        Token<NzToken>? modeToken = null;
        if (Peek().Kind == NzToken.In)
        {
            Advance();
        }
        else if (Peek().Kind == NzToken.Out || (Peek().Kind == NzToken.Identifier &&
            string.Equals(Peek().ToStringValue(), "OUT", StringComparison.OrdinalIgnoreCase)))
        {
            mode = ProcedureParameterMode.Out;
            modeToken = Advance();
        }
        else if (Peek().Kind == NzToken.Inout || (Peek().Kind == NzToken.Identifier &&
            string.Equals(Peek().ToStringValue(), "INOUT", StringComparison.OrdinalIgnoreCase)))
        {
            mode = ProcedureParameterMode.InOut;
            modeToken = Advance();
        }

        if (modeToken is { } unsupportedMode)
            AddParserError("Netezza procedure arguments are input-only; OUT and INOUT are not supported.", unsupportedMode, "PAR001");

        string? parameterName = null;
        SourcePosition? parameterPosition = null;
        if (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
        {
            var next = Peek(1).Kind;
            // Check if this identifier is followed by another identifier (type name) or by a type keyword
            if (next is NzToken.Identifier or NzToken.QuotedIdentifier
                or NzToken.NumberLiteral)
            {
                // Named argument: name type
                parameterPosition = SourcePosition.FromToken(Peek());
                parameterName = ParseIdentifier();
                ParseDataType();
            }
            else
            {
                // Type only (unnamed argument)
                ParseDataType();
            }
        }
        else
        {
            ParseDataType();
        }

        return parameterName is null || parameterPosition is null
            ? null
            : new ProcedureParameter(parameterPosition, parameterName, mode);
    }

    private (string? Returns, ExecuteAs? ExecuteAs, bool HasReturns, bool HasLanguage) ParseProcedureSignatureSpec()
    {
        var executeAs = default(ExecuteAs?);
        string? returns = null;

        // Parse RETURNS / EXECUTE AS / LANGUAGE in any order
        // At minimum we need to consume until AS or IS
        bool hasReturns = false;
        bool hasLanguage = false;

        while (Peek().Kind != NzToken.As && Peek().Kind != NzToken.Is
            && Peek().Kind != NzToken.BeginProc && Peek().Kind != NzToken.StringLiteral
            && Peek().Kind != NzToken.Semicolon && Peek().Kind != NzToken.Unknown)
        {
            var k = Peek().Kind;

            if (k == NzToken.Returns && !hasReturns)
            {
                hasReturns = true;
                Advance();
                returns = ParseProcedureReturnType();
            }
            else if (k == NzToken.Execute)
            {
                executeAs = ParseExecuteAsClause();
            }
            else if (k == NzToken.Language && !hasLanguage)
            {
                hasLanguage = true;
                Advance();
                if (Peek().Kind == NzToken.Nzplsql)
                {
                    Advance();
                }
                else if (Peek().Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
                {
                    var languageToken = Peek();
                    var language = languageToken.ToStringValue();
                    Advance();
                    // Only LANGUAGE NZPLSQL is accepted (procedure matrix hdr_bad_language_rejected).
                    if (!string.Equals(language, "NZPLSQL", StringComparison.OrdinalIgnoreCase))
                    {
                        _errors.Add(new ValidationError(
                            $"LANGUAGE {language} is not supported; only LANGUAGE NZPLSQL is accepted",
                            "error", SourcePosition.FromToken(languageToken), "PAR001"));
                    }
                }
                else
                {
                    Expect(NzToken.Identifier);
                }
            }
            else
            {
                // Skip unexpected tokens (allows flexible ordering)
                Advance();
            }
        }

        return (returns, executeAs, hasReturns, hasLanguage);
    }

    private string ParseProcedureReturnType()
    {
        if (Peek().Kind == NzToken.RefTable)
        {
            Advance();
            Expect(NzToken.LParen);
            var (reftable, _) = ParseTableName();
            Expect(NzToken.RParen);
            return $"REFTABLE({FormatTableName(reftable)})";
        }

        // Regular return type
        var type = ParseDataType();
        return type.Name;
    }

    private ExecuteAs ParseExecuteAsClause()
    {
        Expect(NzToken.Execute);
        Expect(NzToken.As);
        if (Peek().Kind == NzToken.Owner)
        {
            Advance();
            return ExecuteAs.Owner;
        }
        Expect(NzToken.Caller);
        return ExecuteAs.Caller;
    }

    // ====== Procedure Body ======

    private ProcedureBody ParseProcedureBody()
    {
        var startTok = Peek();

        // Body can be a string literal (inline SQL) or a BEGIN_PROC...END_PROC block
        if (Peek().Kind == NzToken.StringLiteral)
        {
            var bodyToken = Advance(); // consume the string body
            var bodyText = bodyToken.ToStringValue() ?? string.Empty;
            if (StringBodyHasUnclosedCase(bodyText))
            {
                _errors.Add(new ValidationError(
                    "CASE expression in a string-body procedure must end with END keyword",
                    "error", SourcePosition.FromToken(bodyToken), "SQL041"));
            }
            return new ProcedureBody(SourcePosition.FromToken(startTok), null,
                new List<ProcedureStatement>(), null);
        }

        Expect(NzToken.BeginProc);

        // Parse declarations + statements + exception handlers
        var declarations = new List<VariableDeclaration>();
        var statements = new List<ProcedureStatement>();
        var exceptionHandlers = new List<ExceptionHandler>();

        // Skip optional semicolons
        while (Peek().Kind == NzToken.Semicolon) Advance();

        if (Peek().Kind == NzToken.Declare)
        {
            declarations.AddRange(ParseVariableDeclarations());
            // Expect BEGIN after DECLARE
            if (Peek().Kind == NzToken.Begin)
                Advance();
            else
            {
                _errors.Add(new ValidationError("Expected BEGIN after DECLARE in procedure block", "error",
                    SourcePosition.FromToken(Peek()), "PARSE001"));
            }
            SkipSemicolons();
            ParseProcedureStatementsInto(statements);
            SkipSemicolons();
            // Optional EXCEPTION block
            if (Peek().Kind == NzToken.Exception)
            {
                exceptionHandlers.AddRange(ParseExceptionBlock());
            }
            Expect(NzToken.End);
            if (Peek().Kind == NzToken.Semicolon) Advance();
        }
        else if (Peek().Kind == NzToken.Begin)
        {
            Advance(); // BEGIN
            SkipSemicolons();
            ParseProcedureStatementsInto(statements);
            SkipSemicolons();
            // Optional EXCEPTION block
            if (Peek().Kind == NzToken.Exception)
            {
                exceptionHandlers.AddRange(ParseExceptionBlock());
            }
            Expect(NzToken.End);
            if (Peek().Kind == NzToken.Semicolon) Advance();
        }
        else
        {
            // Direct statements without wrapper block
            ParseProcedureStatementsInto(statements);
        }

        // Optional trailing semicolons before END_PROC
        SkipSemicolons();
        Expect(NzToken.EndProc);

        return new ProcedureBody(
            SourcePosition.FromToken(startTok),
            declarations.Count > 0 ? declarations : null,
            statements,
            exceptionHandlers.Count > 0 ? exceptionHandlers : null);
    }

    private static bool StringBodyHasUnclosedCase(string body)
    {
        var opens = Regex.Matches(body, @"\b(?:BEGIN|CASE|IF|LOOP)\b", RegexOptions.IgnoreCase).Count;
        var closes = Regex.Matches(body, @"\bEND\b", RegexOptions.IgnoreCase).Count;
        var cases = Regex.Matches(body, @"\bCASE\b", RegexOptions.IgnoreCase).Count;
        return cases > 0 && opens > closes;
    }

    private ProcedureBlockStatement ParseProcedureBlock()
    {
        var startTok = Peek();
        var declarations = new List<VariableDeclaration>();
        var statements = new List<ProcedureStatement>();
        var exceptionHandlers = new List<ExceptionHandler>();

        if (Peek().Kind == NzToken.Declare)
        {
            declarations.AddRange(ParseVariableDeclarations());
        }

        Expect(NzToken.Begin);
        SkipSemicolons();
        ParseProcedureStatementsInto(statements);
        SkipSemicolons();

        if (Peek().Kind == NzToken.Exception)
        {
            exceptionHandlers.AddRange(ParseExceptionBlock());
        }

        Expect(NzToken.End);

        var body = new ProcedureBody(
            SourcePosition.FromToken(startTok),
            declarations.Count > 0 ? declarations : null,
            statements,
            exceptionHandlers.Count > 0 ? exceptionHandlers : null);

        return new ProcedureBlockStatement(SourcePosition.FromToken(startTok), body);
    }

    // ====== Procedure Statements ======

    private void ParseProcedureStatementsInto(List<ProcedureStatement> stmts)
    {
        bool hadSeparator = true;
        while (true)
        {
            if (Peek().Kind == NzToken.Semicolon)
            {
                hadSeparator = true;
                Advance();
                continue;
            }
            if (!IsProcedureStatementStart(Peek().Kind)) break;

            if (!hadSeparator)
            {
                _errors.Add(new ValidationError("Expected ';' before statement", "error",
                    SourcePosition.FromToken(Peek()), "PARSE001"));
            }
            hadSeparator = false;

            var s = ParseProcedureStatement();
            if (s is null) break;
            stmts.Add(s);
            if (Peek().Kind == NzToken.Semicolon) { Advance(); hadSeparator = true; }
        }
    }

    private bool IsProcedureStatementStart(NzToken k)
    {
        return k == NzToken.If || k == NzToken.Loop || k == NzToken.While || k == NzToken.For
            || k == NzToken.Return || k == NzToken.Exit || k == NzToken.Raise
            || k == NzToken.Execute || k == NzToken.Call
            || k == NzToken.Begin || k == NzToken.Declare
            || k == NzToken.Commit || k == NzToken.Rollback
            || k == NzToken.Select || k == NzToken.With
            || k == NzToken.Insert || k == NzToken.Update || k == NzToken.Delete
            || k == NzToken.Create || k == NzToken.Drop || k == NzToken.Alter
            || k == NzToken.Truncate || k == NzToken.Groom || k == NzToken.Generate
            || k == NzToken.Grant || k == NzToken.Revoke || k == NzToken.Comment
            || k == NzToken.Autocommit || k == NzToken.Perform
            || k == NzToken.LessThan
            || k == NzToken.Identifier;
    }

    private ProcedureStatement? ParseProcedureStatement()
    {
        var k = Peek().Kind;

        if (k == NzToken.LessThan && Peek(1).Kind == NzToken.LessThan)
        {
            // Consume an optional <<label>> prefix, then parse the loop/for.
            while (Peek().Kind != NzToken.Loop && Peek().Kind != NzToken.For
                && Peek().Kind != NzToken.Unknown && Peek().Kind != NzToken.Semicolon)
                Advance();
            return ParseProcedureStatement();
        }

        switch (k)
        {
            case NzToken.Perform:
            {
                var perform = Advance();
                AddParserError("PERFORM is not supported in Netezza NZPLSQL.", perform, "PAR001");
                while (Peek().Kind is not (NzToken.Semicolon or NzToken.Unknown)) Advance();
                return new ProcedureSqlStatement(SourcePosition.FromToken(perform), new SetStatement(SourcePosition.FromToken(perform)));
            }
            case NzToken.If: return ParseProcedureIf();
            case NzToken.Loop: return ParseProcedureLoop();
            case NzToken.While: return ParseProcedureWhile();
            case NzToken.For: return ParseProcedureFor();
            case NzToken.Return: return ParseProcedureReturn();
            case NzToken.Exit: return ParseProcedureExit();
            case NzToken.Raise: return ParseProcedureRaise();
            case NzToken.Call: return ParseProcedureCall();
            case NzToken.Begin:
            case NzToken.Declare: return ParseProcedureBlock();
            case NzToken.Commit: Advance(); return new ProcedureCommitStatement(SourcePosition.FromToken(Peek()));
            case NzToken.Rollback: Advance(); return new ProcedureRollbackStatement(SourcePosition.FromToken(Peek()));
            case NzToken.Execute:
                if (Peek(1).Kind == NzToken.Immediate)
                    return ParseProcedureExecuteImmediate();
                return ParseProcedureCall();
            case NzToken.Identifier:
                {
                    if (string.Equals(Peek().ToStringValue(), "PERFORM", StringComparison.OrdinalIgnoreCase))
                    {
                        var tok = Advance();
                        AddParserError("PERFORM is not supported in Netezza NZPLSQL.", tok, "PAR001");
                        ParseExpression();
                        return new ProcedureCallStatement(SourcePosition.FromToken(tok), new TableName("PERFORM"), null);
                    }
                    if (Peek(1).Kind == NzToken.LParen)
                    {
                        // Array element assignment: arr(1) := value
                        var arrName = Advance().ToStringValue();
                        Advance(); // (
                        ParseExpression(); // index
                        Expect(NzToken.RParen);
                        if (Peek().Kind == NzToken.Assign)
                        {
                            Advance();
                            ParseExpression(); // value
                        }
                        return new AssignmentStatement(SourcePosition.FromToken(Peek()), arrName, new Literal(SourcePosition.FromToken(Peek()), LiteralKind.Null, "NULL"));
                    }
                    // Record field assignment: rec.field := value
                    if (Peek(1).Kind == NzToken.Dot)
                    {
                        if (Peek(3).Kind == NzToken.LParen)
                        {
                            var method = Advance().ToStringValue();
                            Advance(); Advance(); Advance();
                            var depth = 1;
                            while (depth > 0 && Peek().Kind != NzToken.Unknown)
                            {
                                if (Peek().Kind == NzToken.LParen) depth++;
                                else if (Peek().Kind == NzToken.RParen) depth--;
                                Advance();
                            }
                            return new ProcedureCallStatement(SourcePosition.FromToken(Peek()), new TableName(method), null);
                        }
                        var recName = Advance().ToStringValue();
                        Advance(); // Dot
                        var fieldName = Expect(NzToken.Identifier).ToStringValue();
                        Expect(NzToken.Assign);
                        var value = ParseExpression();
                        return new AssignmentStatement(SourcePosition.FromToken(Peek()), $"{recName}.{fieldName}", value);
                    }
                    if (Peek(1).Kind == NzToken.Assign)
                    {
                        var varName = Advance().ToStringValue();
                        return ParseAssignmentStatement(varName);
                    }
                    return null;
                }

            default:
                return ParseSqlAsProcedureStatement();
        }
    }

    // ====== Variable Declarations ======

    private List<VariableDeclaration> ParseVariableDeclarations()
    {
        var decls = new List<VariableDeclaration>();
        Expect(NzToken.Declare);

        while (true)
        {
            SkipSemicolons();
            if (!IsVariableDeclarationStart()) break;
            decls.Add(ParseVariableDeclaration());
            if (Peek().Kind == NzToken.Semicolon) Advance();
        }

        return decls;
    }

    private bool IsVariableDeclarationStart()
    {
        var k = Peek().Kind;
        return k is NzToken.Identifier or NzToken.QuotedIdentifier
            or NzToken.Owner or NzToken.Start;
    }

    private VariableDeclaration ParseVariableDeclaration()
    {
        var nameTok = Peek();
        var pos = SourcePosition.FromToken(nameTok);
        var name = ParseIdentifier();

        // ALIAS FOR $n / identifier
        if (Peek().Kind == NzToken.Alias)
        {
            Advance();
            Expect(NzToken.For);
            var aliasFor = Peek().ToStringValue();
            Advance(); // consume the $n, $identifier, number, or identifier
            return new VariableDeclaration(pos, name,
                new DataTypeInfo(pos, "ALIAS", null), true, false, aliasFor);
        }

        // CONSTANT
        bool constant = false;
        if (Peek().Kind == NzToken.Constant)
        {
            constant = true;
            Advance();
        }

        // Type — handle VARRAY and anchored types specially
        DataTypeInfo dataType;
        if (TryParseAnchoredType(out var anchoredType))
        {
            dataType = anchoredType;
        }
        else if (Peek().Kind == NzToken.Varray)
        {
            Advance();
            IReadOnlyList<string>? varrayParams = null;
            if (Peek().Kind == NzToken.LParen)
            {
                Advance();
                var argList = new List<string> { Expect(NzToken.NumberLiteral).ToStringValue() };
                Expect(NzToken.RParen);
                varrayParams = argList;
            }
            Expect(NzToken.Of);
            ParseDataType(); // consume the element type
            dataType = new DataTypeInfo(pos, "VARRAY", varrayParams);
        }
        else
        {
            dataType = ParseDataType();
        }

        // NOT NULL
        if (Peek().Kind == NzToken.Not && Peek(1).Kind == NzToken.Null)
        {
            Advance(); Advance();
        }

        // := expression or DEFAULT expression (initializer)
        if (Peek().Kind == NzToken.Assign)
        {
            Advance();
            ParseExpression(); // consume initializer value
        }
        else if (Peek().Kind == NzToken.Default)
        {
            Advance();
            ParseExpression(); // consume default value
        }

        return new VariableDeclaration(pos, name, dataType, false, constant, null);
    }

    /// <summary>
    /// Parses anchored declaration types: <c>table.column%TYPE</c> and
    /// <c>table%ROWTYPE</c> (procedure matrix decl_percent_type / decl_rowtype).
    /// </summary>
    private bool TryParseAnchoredType(out DataTypeInfo dataType)
    {
        dataType = null!;
        var index = 0;
        var parts = new List<string>();
        while (Peek(index).Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
        {
            parts.Add(Peek(index).ToStringValue());
            index++;
            if (Peek(index).Kind == NzToken.Dot)
            {
                index++;
                continue;
            }
            break;
        }

        if (parts.Count == 0 || Peek(index).Kind != NzToken.Modulo)
            return false;

        index++;
        string suffix;
        if (Peek(index).Kind == NzToken.Type)
        {
            suffix = "TYPE";
        }
        else if (Peek(index).Kind == NzToken.Identifier
                 && string.Equals(Peek(index).ToStringValue(), "ROWTYPE", StringComparison.OrdinalIgnoreCase))
        {
            suffix = "ROWTYPE";
        }
        else
        {
            return false;
        }

        index++;
        var position = SourcePosition.FromToken(Peek());
        for (var consumed = 0; consumed < index; consumed++)
            Advance();

        dataType = new DataTypeInfo(position, $"{string.Join('.', parts)}%{suffix}", null);
        return true;
    }

    // ====== Assignment Statement ======

    private AssignmentStatement ParseAssignmentStatement(string variable)
    {
        var assignTok = Expect(NzToken.Assign);
        var value = ParseExpression();
        return new AssignmentStatement(SourcePosition.FromToken(assignTok), variable, value);
    }

    // ====== @SET Statement ======

    private Statement ParseAtSetStatement()
    {
        var tok = Advance(); // @SET
        if (Peek().Kind == NzToken.Identifier) Advance(); // variable name
        if (Peek().Kind == NzToken.EqualsOp)
        {
            Advance();
            ParseExpression(); // value
        }
        SkipSemicolons();
        return new VariableSetStatement(SourcePosition.FromToken(tok), new Literal(SourcePosition.FromToken(tok), LiteralKind.Null, "NULL"));
    }

    // ====== AUTOCOMMIT Statement ======

    private ProcedureStatement ParseAutocommitStatement()
    {
        var tok = Advance(); // AUTOCOMMIT
        if (Peek().Kind == NzToken.Identifier &&
            string.Equals(Peek().ToStringValue(), "TRX", StringComparison.OrdinalIgnoreCase))
            Advance();
        if (Peek().Kind == NzToken.On) Advance();
        else if (Peek().Kind == NzToken.Identifier) Advance();
        SkipSemicolons();
        return new ProcedureSqlStatement(SourcePosition.FromToken(tok), new SetStatement(SourcePosition.FromToken(tok)));
    }

    // ====== RETURN Statement ======

    private ProcedureReturnStatement ParseProcedureReturn()
    {
        var retTok = Expect(NzToken.Return);

        Expression? value = null;
        if (Peek().Kind != NzToken.Semicolon
            && Peek().Kind != NzToken.End
            && Peek().Kind != NzToken.EndProc
            && Peek().Kind != NzToken.Exception
            && Peek().Kind != NzToken.Else
            && Peek().Kind != NzToken.Elsif
            && Peek().Kind != NzToken.Then
            && Peek().Kind != NzToken.When)
        {
            value = ParseExpression();
        }

        return new ProcedureReturnStatement(SourcePosition.FromToken(retTok), value);
    }

    // ====== IF Statement ======

    private ProcedureIfStatement ParseProcedureIf()
    {
        var ifTok = Expect(NzToken.If);
        var condition = ParseExpression();
        Expect(NzToken.Then);

        var thenStmts = new List<ProcedureStatement>();
        ParseProcedureStatementsInto(thenStmts);

        var elsifClauses = new List<ProcedureElsif>();
        while (Peek().Kind == NzToken.Elsif)
        {
            var elsifTok = Advance();
            var elsifCond = ParseExpression();
            Expect(NzToken.Then);
            var elsifStmts = new List<ProcedureStatement>();
            ParseProcedureStatementsInto(elsifStmts);
            elsifClauses.Add(new ProcedureElsif(SourcePosition.FromToken(elsifTok), elsifCond, elsifStmts));
        }

        List<ProcedureStatement>? elseStmts = null;
        if (Peek().Kind == NzToken.Else)
        {
            Advance();
            elseStmts = new List<ProcedureStatement>();
            ParseProcedureStatementsInto(elseStmts);
        }

        Expect(NzToken.End);
        Expect(NzToken.If);

        return new ProcedureIfStatement(
            SourcePosition.FromToken(ifTok), condition,
            thenStmts, elsifClauses.Count > 0 ? elsifClauses : null, elseStmts);
    }

    // ====== LOOP Statement ======

    private ProcedureLoopStatement ParseProcedureLoop()
    {
        var loopTok = Expect(NzToken.Loop);
        var stmts = new List<ProcedureStatement>();
        ParseProcedureStatementsInto(stmts);
        Expect(NzToken.End);
        Expect(NzToken.Loop);
        return new ProcedureLoopStatement(SourcePosition.FromToken(loopTok), stmts);
    }

    // ====== WHILE Statement ======

    private ProcedureWhileStatement ParseProcedureWhile()
    {
        var whileTok = Expect(NzToken.While);
        var condition = ParseExpression();
        Expect(NzToken.Loop);
        var stmts = new List<ProcedureStatement>();
        ParseProcedureStatementsInto(stmts);
        Expect(NzToken.End);
        Expect(NzToken.Loop);
        return new ProcedureWhileStatement(SourcePosition.FromToken(whileTok), condition, stmts);
    }

    // ====== FOR Statement (range loop) ======

    private ProcedureForStatement ParseProcedureFor()
    {
        var forTok = Expect(NzToken.For);
        var variable = ParseIdentifier();
        Expect(NzToken.In);

        // FOR ... IN (SELECT ...)  or  FOR ... IN EXECUTE ...
        if (Peek().Kind is NzToken.Select or NzToken.With)
        {
            WithClause? forWith = null;
            if (Peek().Kind == NzToken.With) forWith = ParseWithClause();
            var forQuery = ParseSelectStatement(forWith);
            Expect(NzToken.Loop);
            var stmts = new List<ProcedureStatement>();
            ParseProcedureStatementsInto(stmts);
            Expect(NzToken.End);
            Expect(NzToken.Loop);
            return new ProcedureForStatement(SourcePosition.FromToken(forTok), variable,
                null, null, null, forQuery, null, stmts);
        }
        else if (Peek().Kind == NzToken.Execute)
        {
            Advance();
            var sql = ParseExpression();
            Expect(NzToken.Loop);
            var stmts = new List<ProcedureStatement>();
            ParseProcedureStatementsInto(stmts);
            Expect(NzToken.End);
            Expect(NzToken.Loop);
            return new ProcedureForStatement(SourcePosition.FromToken(forTok), variable,
                null, null, null, null, sql, stmts);
        }

        // Range loop: FOR i IN expr..expr LOOP
        if (Peek().Kind == NzToken.Reverse || (Peek().Kind == NzToken.Identifier &&
            string.Equals(Peek().ToStringValue(), "REVERSE", StringComparison.OrdinalIgnoreCase)))
            Advance();
        var from = ParseExpression();
        Expect(NzToken.Dot);
        Expect(NzToken.Dot);
        var to = ParseExpression();

        Expect(NzToken.Loop);
        var rangeStmts = new List<ProcedureStatement>();
        ParseProcedureStatementsInto(rangeStmts);
        Expect(NzToken.End);
        Expect(NzToken.Loop);

        return new ProcedureForStatement(
            SourcePosition.FromToken(forTok), variable,
            from, to, null, null, null, rangeStmts);
    }

    // ====== EXIT Statement ======

    private ProcedureExitStatement ParseProcedureExit()
    {
        var exitTok = Expect(NzToken.Exit);
        Expression? when = null;
        // EXIT may optionally name a loop label before the WHEN condition.
        if (Peek().Kind == NzToken.Identifier)
            Advance();
        if (Peek().Kind == NzToken.When)
        {
            Advance();
            when = ParseExpression();
        }
        return new ProcedureExitStatement(SourcePosition.FromToken(exitTok), when);
    }

    // ====== RAISE Statement ======

    private ProcedureRaiseStatement ParseProcedureRaise()
    {
        var raiseTok = Expect(NzToken.Raise);

        RaiseLevel level;
        if (Peek().Kind == NzToken.Exception)
        {
            Advance();
            level = RaiseLevel.Exception;
        }
        else if (Peek().Kind == NzToken.Notice)
        {
            Advance();
            level = RaiseLevel.Notice;
        }
        else if (Peek().Kind == NzToken.Debug)
        {
            Advance();
            level = RaiseLevel.Debug;
        }
        else
        {
            // Only DEBUG, NOTICE and EXCEPTION severities exist
            // (procedure matrix raise_warning_rejected / raise_error_rejected).
            _errors.Add(new ValidationError(
                "Expected NOTICE, DEBUG or EXCEPTION after RAISE; WARNING and ERROR severities are not supported",
                "error", SourcePosition.FromToken(Peek()), "PARSE001"));
            level = RaiseLevel.Notice;

            if (Peek().Kind is NzToken.Warning or NzToken.Error1
                || (Peek().Kind == NzToken.Identifier
                    && (IsWord(Peek(), "WARNING") || IsWord(Peek(), "LOG") || IsWord(Peek(), "ERROR"))))
            {
                Advance();
            }
        }

        // RAISE requires a message string (verified live; procedure matrix raise_no_message_rejected).
        Expression message;
        if (Peek().Kind is NzToken.Semicolon or NzToken.End or NzToken.EndProc)
        {
            _errors.Add(new ValidationError(
                "RAISE requires a message string",
                "error", SourcePosition.FromToken(Peek()), "PARSE001"));
            message = new Literal(SourcePosition.FromToken(raiseTok), LiteralKind.Null, "NULL");
            return new ProcedureRaiseStatement(SourcePosition.FromToken(raiseTok), level, message);
        }

        if (Peek().Kind != NzToken.StringLiteral)
            AddParserError("Netezza RAISE requires a string literal message.", Peek(), "PAR001");
        message = ParseExpression();
        // Skip comma-separated additional expressions
        while (Peek().Kind == NzToken.Comma)
        {
            Advance();
            if (Peek().Kind != NzToken.Semicolon
                && Peek().Kind != NzToken.End
                && Peek().Kind != NzToken.EndProc)
            {
                var argument = Peek();
                var variableToken = argument.Kind is NzToken.Identifier or NzToken.QuotedIdentifier
                    or NzToken.DollarIdentifier or NzToken.DollarNumber or NzToken.BracedVariable
                    or NzToken.BracesOnlyVariable;
                var nextKind = Peek(1).Kind;
                var simpleVariable = variableToken && nextKind is
                    NzToken.Comma or NzToken.Semicolon or NzToken.End or NzToken.EndProc or NzToken.Unknown;
                var simpleRecordField = variableToken && nextKind == NzToken.Dot
                    && Peek(2).Kind is NzToken.Identifier or NzToken.QuotedIdentifier
                    && Peek(3).Kind is NzToken.Comma or NzToken.Semicolon or NzToken.End or NzToken.EndProc or NzToken.Unknown;
                if (!simpleVariable && !simpleRecordField)
                    AddParserError("Netezza RAISE format arguments must be simple variable references.", argument, "PAR001");
                ParseExpression();
            }
            else
                break;
        }

        return new ProcedureRaiseStatement(SourcePosition.FromToken(raiseTok), level, message);
    }

    private static bool IsWord(Superpower.Model.Token<NzToken> token, string word) =>
        string.Equals(token.ToStringValue(), word, StringComparison.OrdinalIgnoreCase);

    // ====== EXECUTE IMMEDIATE Statement ======

    private ProcedureExecuteImmediateStatement ParseProcedureExecuteImmediate()
    {
        var execTok = Expect(NzToken.Execute);
        Expect(NzToken.Immediate);
        var sql = ParseExpression();

        // Optional USING clause — consume expressions but store as strings
        IReadOnlyList<string>? usingParams = null;
        if (Peek().Kind == NzToken.Using)
        {
            var usingToken = Advance();
            AddParserError("USING parameters are not supported by Netezza EXECUTE IMMEDIATE.", usingToken, "PAR001");
            var usingList = new List<string>();
            var firstExpr = ParseExpression();
            usingList.Add(firstExpr.ToString() ?? "");
            while (Peek().Kind == NzToken.Comma)
            {
                Advance();
                var expr = ParseExpression();
                usingList.Add(expr.ToString() ?? "");
            }
            usingParams = usingList;
        }

        return new ProcedureExecuteImmediateStatement(SourcePosition.FromToken(execTok), sql, usingParams);
    }

    // ====== CALL Statement (procedure body) ======

    private ProcedureCallStatement ParseProcedureCall()
    {
        var tok = Advance(); // CALL or EXECUTE
        if (Peek().Kind == NzToken.Procedure) Advance();
        var (proc, _) = ParseTableName();

        IReadOnlyList<Expression>? arguments = null;
        if (Peek().Kind == NzToken.LParen)
        {
            Advance();
            var args = new List<Expression>();
            if (Peek().Kind != NzToken.RParen)
            {
                args.Add(ParseExpression());
                while (Peek().Kind == NzToken.Comma)
                {
                    Advance();
                    args.Add(ParseExpression());
                }
            }
            Expect(NzToken.RParen);
            arguments = args;
        }

        return new ProcedureCallStatement(SourcePosition.FromToken(tok), proc, arguments);
    }

    // ====== EXCEPTION Block ======

    private List<ExceptionHandler> ParseExceptionBlock()
    {
        var handlers = new List<ExceptionHandler>();
        Expect(NzToken.Exception);

        while (Peek().Kind == NzToken.When)
        {
            handlers.Add(ParseWhenClause());
        }

        // EXCEPTION requires at least one WHEN handler (procedure matrix exc_without_handler_rejected).
        if (handlers.Count == 0)
        {
            _errors.Add(new ValidationError(
                "EXCEPTION requires at least one WHEN handler",
                "error", SourcePosition.FromToken(Peek()), "PARSE001"));
        }

        return handlers;
    }

    private ExceptionHandler ParseWhenClause()
    {
        var whenTok = Expect(NzToken.When);
        var condition = ParseIdentifier();
        if (!string.Equals(condition, "OTHERS", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(condition, "TRANSACTION_ABORTED", StringComparison.OrdinalIgnoreCase))
            AddParserError("Unsupported Netezza NZPLSQL exception condition.", whenTok, "PAR001");
        if (Peek().Kind == NzToken.StringLiteral)
        {
            AddParserError("SQLSTATE exception conditions are not supported in Netezza NZPLSQL.", Peek(), "PAR001");
            Advance(); // SQLSTATE 'xxxxx' condition literal
        }

        Expect(NzToken.Then);

        var stmts = new List<ProcedureStatement>();
        ParseProcedureStatementsInto(stmts);

        return new ExceptionHandler(SourcePosition.FromToken(whenTok), condition, stmts);
    }

    // ====== SQL Statement Wrapper ======

    private ProcedureSqlStatement? ParseSqlAsProcedureStatement()
    {
        var startTok = Peek();
        Statement? sqlStmt = null;

        switch (startTok.Kind)
        {
            case NzToken.Select:
            case NzToken.With:
                sqlStmt = ParseSelect();
                break;
            case NzToken.Insert:
                sqlStmt = ParseInsert();
                break;
            case NzToken.Update:
                sqlStmt = ParseUpdate();
                break;
            case NzToken.Delete:
                sqlStmt = ParseDelete();
                break;
            case NzToken.Create:
                sqlStmt = ParseCreate();
                break;
            case NzToken.Drop:
                sqlStmt = ParseDrop();
                break;
            case NzToken.Alter:
                sqlStmt = ParseAlterTable();
                break;
            case NzToken.Truncate:
                sqlStmt = ParseTruncate();
                break;
            case NzToken.Groom:
                sqlStmt = ParseGroom();
                break;
            case NzToken.Generate:
                sqlStmt = ParseGenerateStatistics();
                break;
            case NzToken.Grant:
                sqlStmt = ParseGrant();
                break;
            case NzToken.Revoke:
                sqlStmt = ParseRevoke();
                break;
            case NzToken.Comment:
                sqlStmt = ParseCommentOn();
                break;
            default:
            {
                // Generic fallback: consume until semicolon or terminator
                var startPos = SourcePosition.FromToken(startTok);
                while (Peek().Kind != NzToken.Semicolon
                    && Peek().Kind != NzToken.Unknown
                    && Peek().Kind != NzToken.End
                    && Peek().Kind != NzToken.EndProc
                    && Peek().Kind != NzToken.Exception
                    && Peek().Kind != NzToken.Else
                    && Peek().Kind != NzToken.Elsif
                    && Peek().Kind != NzToken.When)
                {
                    if (Peek().Kind == NzToken.LParen)
                    {
                        Advance();
                        var depth = 1;
                        while (depth > 0 && Peek().Kind != NzToken.Unknown)
                        {
                            if (Peek().Kind == NzToken.LParen) depth++;
                            else if (Peek().Kind == NzToken.RParen) depth--;
                            Advance();
                        }
                    }
                    else
                    {
                        Advance();
                    }
                }
                sqlStmt = new SetStatement(startPos);
                break;
            }
        }

        return sqlStmt is not null ? new ProcedureSqlStatement(sqlStmt.Position, sqlStmt) : null;
    }
}
