using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

/// <summary>
/// Microsoft SQL Server (T-SQL) dialect parser. Port of src/dialects/mssql/sql/parser.ts.
/// Extends the shared Netezza parser with TOP / OUTPUT / CROSS|OUTER APPLY /
/// bracketed identifiers / @variables / thin BEGIN...END / GO batches and
/// thin CREATE PROCEDURE units, while rejecting Netezza-only syntax.
/// Deep T-SQL (TRY-CATCH nesting, CLR, Service Broker) stays out of scope.
/// </summary>
public partial class MssqlSqlParser : NzSqlParser
{
    public MssqlSqlParser(Token<NzToken>[] tokens) : base(tokens)
    {
    }

    protected override bool SupportsEmptyQualifiedNameSegment => false;

    public override Statement? Parse()
    {
        SkipSemicolons();

        // GO batch separators are transparent: skip them and parse the next
        // real statement (or finish when only GO remains).
        while (Peek().Kind == NzToken.MssqlGo)
        {
            Advance();
            SkipSemicolons();
        }

        if (_pos >= _tokens.Length)
            return null;
        var k = Peek().Kind;

        Statement? result;
        if (k == NzToken.Create)
            result = ParseMssqlCreateOrFallback();
        else if (k is NzToken.Groom or NzToken.Generate)
        {
            AddParserError($"{k} is Netezza-only syntax and is not supported in MSSQL",
                Peek(), "PAR001");
            Advance();
            result = null;
        }
        else if (k is NzToken.Declare or NzToken.Set or NzToken.Begin)
        {
            // DECLARE @x INT, SET @x = 1 and thin BEGIN...END are opaque tails.
            result = ParseCommandTailFallback(Peek());
        }
        else
        {
            result = base.Parse();
        }

        if (result is null)
            SynchronizeStatement();
        return result;
    }

    private Statement? ParseMssqlCreateOrFallback()
    {
        var look = Peek(1).Kind == NzToken.Or ? 3 : 1;
        var obj = Peek(look).Kind;

        if (obj == NzToken.External)
        {
            AddParserError("CREATE EXTERNAL TABLE is Netezza-only syntax and is not supported in MSSQL",
                Peek(look), "PAR001");
            Advance();
            return null;
        }

        if (StartsMssqlProcedure())
            return ParseMssqlProcedureUnit();

        return base.Parse();
    }

    private bool StartsMssqlProcedure()
    {
        if (Peek().Kind != NzToken.Create)
            return false;
        if (Peek(1).Kind is NzToken.Procedure or NzToken.MssqlProc)
            return true;
        return Peek(1).Kind == NzToken.Or
            && Peek(2).Kind == NzToken.Replace
            && Peek(3).Kind is NzToken.Procedure or NzToken.MssqlProc;
    }

    // CREATE [OR REPLACE] PROCEDURE|PROC name [(@p INT = 1 OUTPUT, ...)]
    // [WITH RECOMPILE|ENCRYPTION] [EXECUTE AS ...] AS <body>
    // Body is either a thin BEGIN...END block or a single statement tail.
    private MssqlProcedureUnitStatement? ParseMssqlProcedureUnit()
    {
        var startTok = Peek();
        var startIdx = _pos;

        // Header until BEGIN, AS-only body or statement end.
        while (Peek().Kind != NzToken.Begin && Peek().Kind != NzToken.Unknown
               && Peek().Kind != NzToken.Semicolon && Peek().Kind != NzToken.MssqlGo)
            Advance();

        if (Peek().Kind == NzToken.Begin)
        {
            Advance(); // BEGIN
            var depth = 1;
            var done = false;
            while (!done && Peek().Kind != NzToken.Unknown)
            {
                if (Peek().Kind == NzToken.Begin)
                {
                    depth++;
                    Advance();
                }
                else if (Peek().Kind == NzToken.End)
                {
                    depth--;
                    // END TRY never closes the unit (a BEGIN CATCH may follow);
                    // END CATCH / bare END close it only at the outer level.
                    if (Peek(1).Kind == NzToken.MssqlTry)
                    {
                        Advance(); // END
                        Advance(); // TRY
                    }
                    else if (Peek(1).Kind == NzToken.MssqlCatch)
                    {
                        Advance(); // END
                        Advance(); // CATCH
                        if (depth == 0)
                            done = true;
                    }
                    else
                    {
                        Advance(); // END
                        if (depth == 0)
                            done = true;
                    }
                }
                else
                {
                    Advance();
                }
            }

            if (depth != 0)
            {
                AddParserError("Unterminated MSSQL PROCEDURE unit (expected END)", startTok, "PAR001");
                return null;
            }
        }
        else
        {
            // Body is a single statement without BEGIN..END; consume to the end
            // of the statement, leaving GO separators for Parse() to skip.
            while (Peek().Kind != NzToken.Semicolon && Peek().Kind != NzToken.Unknown
                   && Peek().Kind != NzToken.MssqlGo)
                Advance();
        }

        var tokens = _tokens[startIdx.._pos].ToArray();

        // Name: skip CREATE [OR REPLACE] [PROCEDURE|PROC] <name> (dotted allowed).
        var namePos = 1;
        if (tokens.Length > 3 && tokens[1].Kind == NzToken.Or)
            namePos = 3;
        TableName name = new("procedure");
        if (namePos + 1 < tokens.Length &&
            tokens[namePos].Kind is NzToken.Procedure or NzToken.MssqlProc &&
            tokens[namePos + 1].Kind is NzToken.Identifier or NzToken.QuotedIdentifier
                or NzToken.MssqlBracketedIdentifier)
        {
            var parts = new List<string> { StripMssqlName(tokens[namePos + 1].ToStringValue()) };
            var i = namePos + 2;
            while (i + 1 < tokens.Length && tokens[i].Kind == NzToken.Dot &&
                   tokens[i + 1].Kind is NzToken.Identifier or NzToken.QuotedIdentifier
                       or NzToken.MssqlBracketedIdentifier)
            {
                parts.Add(StripMssqlName(tokens[i + 1].ToStringValue()));
                i += 2;
            }
            name = parts.Count switch
            {
                1 => new TableName(parts[0]),
                2 => new TableName(parts[1], Schema: parts[0]),
                _ => new TableName(parts[^1], Schema: parts.Count >= 2 ? parts[^2] : null)
            };
        }

        return new MssqlProcedureUnitStatement(FromToken(startTok), name, tokens);
    }

    /// <summary>Strips T-SQL quoting: [name], "name" and @variable prefixes.</summary>
    internal static string StripMssqlName(string value)
    {
        if (value.Length >= 2 && value[0] == '[' && value[^1] == ']')
            return value[1..^1].Replace("]]", "]", StringComparison.Ordinal);
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        if (value.Length >= 1 && value[0] == '@')
            return value[1..];
        return value;
    }
}
