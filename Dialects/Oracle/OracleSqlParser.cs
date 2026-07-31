using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Parser;

/// <summary>
/// Oracle dialect parser. Port of src/dialects/oracle/sql/parser.ts from the
/// reference TypeScript project. Extends the shared Netezza parser with
/// anonymous PL/SQL blocks, CREATE FUNCTION/PROCEDURE/PACKAGE/TRIGGER program
/// units (kept as offset-stable token sequences), hierarchical queries,
/// PIVOT/UNPIVOT, RETURNING INTO, bind variables, qualified function calls and
/// database links, while rejecting Netezza-only syntax (LIMIT, DB..TABLE,
/// DISTRIBUTE ON, ORGANIZE ON, EXTERNAL TABLE, GROOM, GENERATE STATISTICS).
/// </summary>
public partial class OracleSqlParser : NzSqlParser
{
    public OracleSqlParser(Token<NzToken>[] tokens) : base(tokens)
    {
    }

    // ====== Top-Level Dispatch ======

    public override Statement? Parse()
    {
        SkipSemicolons();
        if (_pos >= _tokens.Length)
            return null;
        var k = Peek().Kind;

        Statement? result;
        if (k is NzToken.Declare or NzToken.Begin)
            result = ParseOracleAnonymousBlock();
        else if (k == NzToken.Create)
            result = ParseOracleCreateOrFallback();
        else if (k is NzToken.Groom or NzToken.Generate)
        {
            AddParserError($"{k} is Netezza-only syntax and is not supported in Oracle",
                Peek(), "PAR001");
            Advance();
            result = null;
        }
        else
        {
            result = base.Parse();
        }

        if (result is null)
            SynchronizeStatement();
        return result;
    }

    private Statement? ParseOracleCreateOrFallback()
    {
        // Peek() is CREATE. Look past the optional OR REPLACE to find the object kind.
        var look = Peek(1).Kind == NzToken.Or ? 3 : 1;
        var obj = Peek(look).Kind;

        if (obj == NzToken.External)
        {
            AddParserError("CREATE EXTERNAL TABLE is Netezza-only syntax and is not supported in Oracle",
                Peek(look), "PAR001");
            Advance(); // consume CREATE; the caller synchronizes the tail
            return null;
        }

        if (obj == NzToken.Procedure)
            return ParseOracleProgramUnit(OracleProgramUnitKind.Procedure);

        if (obj == NzToken.Identifier)
        {
            var text = Peek(look).ToStringValue();
            if (text.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
                return ParseOracleProgramUnit(OracleProgramUnitKind.Function);
            if (text.Equals("PACKAGE", StringComparison.OrdinalIgnoreCase))
                return ParseOracleProgramUnit(OracleProgramUnitKind.Package);
            if (text.Equals("TRIGGER", StringComparison.OrdinalIgnoreCase))
                return ParseOracleProgramUnit(OracleProgramUnitKind.Trigger);
        }

        return base.Parse();
    }

    // ====== Anonymous PL/SQL Blocks ======

    private OracleAnonymousBlockStatement? ParseOracleAnonymousBlock()
    {
        var startTok = Peek();
        var startIdx = _pos;
        if (Peek().Kind == NzToken.Declare)
            Advance();
        // The scanner starts at DECLARE (or directly at BEGIN) and consumes the
        // declaration section — including nested routine members — up to the
        // block BEGIN, which it counts as the outermost opener so the matching
        // END closes the block at depth 1.

        var endPos = ScanOracleBlockEnd();
        if (endPos < 0)
        {
            AddParserError("Unterminated anonymous PL/SQL block", startTok, "PAR001");
            return null;
        }

        var tokens = _tokens[startIdx..(endPos + 1)];
        while (_pos < endPos) Advance();
        Advance(); // END

        // Optional block name after END: END block_name;
        if (IsOracleNameToken(Peek().Kind))
            Advance();

        return new OracleAnonymousBlockStatement(FromToken(startTok), tokens);
    }

    // ====== CREATE FUNCTION / PROCEDURE / PACKAGE / TRIGGER Units ======

    private OracleProgramUnitStatement? ParseOracleProgramUnit(OracleProgramUnitKind kind)
    {
        var startTok = Peek();
        var startIdx = _pos;
        Expect(NzToken.Create);
        if (Peek().Kind == NzToken.Or)
        {
            Advance();
            Expect(NzToken.Replace);
        }
        var kw = Peek();
        if (kw.Kind is not (NzToken.Identifier or NzToken.Procedure))
        {
            AddParserError($"Expected FUNCTION, PROCEDURE, PACKAGE or TRIGGER, got {DescribeToken(kw.Kind)}",
                kw, "PAR001");
            return null;
        }
        Advance(); // FUNCTION / PROCEDURE / PACKAGE / TRIGGER

        var effectiveKind = kind;
        if (kind == OracleProgramUnitKind.Package &&
            Peek().Kind == NzToken.Identifier &&
            string.Equals(Peek().ToStringValue(), "BODY", StringComparison.OrdinalIgnoreCase))
        {
            effectiveKind = OracleProgramUnitKind.PackageBody;
            Advance();
        }

        // The name may be a single OracleQualifiedFunction token when the
        // qualified name is directly followed by the argument list.
        TableName name;
        if (Peek().Kind == NzToken.OracleQualifiedFunction)
        {
            var t = Advance();
            var full = t.ToStringValue();
            var lastDot = full.LastIndexOf('.');
            name = lastDot > 0
                ? new TableName(full[(lastDot + 1)..], Schema: full[..lastDot])
                : new TableName(full);
        }
        else
        {
            (name, _) = ParseTableName();
        }

        // Skip the unit header (argument list, RETURN clause, trigger header)
        // up to AS/IS (body start) or BEGIN (trigger form without AS/IS).
        while (Peek().Kind is not (NzToken.Unknown or NzToken.Semicolon))
        {
            var hk = Peek().Kind;
            if (hk is NzToken.As or NzToken.Is) { Advance(); break; }
            if (hk == NzToken.Begin) break;
            Advance();
        }

        var endPos = ScanOracleBlockEnd();
        if (endPos < 0)
        {
            AddParserError($"Unterminated Oracle {kind.ToString().ToLowerInvariant()} unit",
                startTok, "PAR001");
            return null;
        }

        var tokens = _tokens[startIdx..(endPos + 1)];
        while (_pos < endPos) Advance();
        Advance(); // END

        // Optional unit name after END: END pkg;
        if (IsOracleNameToken(Peek().Kind))
            Advance();

        return new OracleProgramUnitStatement(FromToken(startTok), effectiveKind, name, tokens);
    }

    /// <summary>
    /// Returns the index of the END token that closes the outermost block, or
    /// -1 when the block is unterminated. Counts BEGIN/IF/LOOP/CASE block
    /// openers; the closing END may be followed by a block name (END IF,
    /// END LOOP, END pkg). Routine members (FUNCTION/PROCEDURE at member
    /// level) are consumed whole — header, optional body, and their own
    /// closing END — so they cannot close the outer unit.
    /// </summary>
    private int ScanOracleBlockEnd()
    {
        var depth = 0;
        while (Peek().Kind != NzToken.Unknown)
        {
            var k = Peek().Kind;
            if (k == NzToken.End)
            {
                var next = Peek(1).Kind;
                if (next is NzToken.If or NzToken.Loop or NzToken.Case)
                {
                    // END IF / END LOOP / END CASE closes the matching opener;
                    // the suffix keyword is a label, not a new opener.
                    depth--;
                    Advance(); // END
                    Advance(); // IF / LOOP / CASE
                    continue;
                }
                // The END that drops the depth from 1 to 0 closes the outermost
                // opener (the block's BEGIN was not consumed by the caller).
                if (depth <= 1)
                    return _pos;
                depth--;
                Advance();
                continue;
            }
            if (k is NzToken.Begin or NzToken.If or NzToken.Loop or NzToken.Case)
            {
                depth++;
            }
            else if (depth == 0 && IsRoutineMemberStart())
            {
                ScanOracleRoutineMember();
                // Member scanner leaves _pos on the closing END (or ';' for specs).
                if (Peek().Kind == NzToken.End)
                {
                    Advance(); // END
                    // Optional member label: END value;
                    if (IsOracleNameToken(Peek().Kind))
                        Advance();
                }
                else if (Peek().Kind == NzToken.Semicolon)
                {
                    Advance();
                }
                continue;
            }
            Advance();
        }
        return -1;
    }

    private bool IsRoutineMemberStart()
    {
        var k = Peek().Kind;
        var isRoutineKeyword = k == NzToken.Procedure
            || (k == NzToken.Identifier &&
                Peek().ToStringValue().Equals("FUNCTION", StringComparison.OrdinalIgnoreCase));
        if (!isRoutineKeyword)
            return false;
        // Routine names may be keyword tokens (e.g. VALUE → NzToken.Value).
        return IsOracleNameToken(Peek(1).Kind);
    }

    private static bool IsOracleNameToken(NzToken kind) =>
        kind is NzToken.Identifier or NzToken.QuotedIdentifier or NzToken.OracleQualifiedFunction
            or NzToken.Value or NzToken.Public or NzToken.Owner or NzToken.Hash or NzToken.Start
            or NzToken.Replace or NzToken.Out or NzToken.Inout or NzToken.Perform or NzToken.Reverse
            or NzToken.Warning or NzToken.Within;

    /// <summary>
    /// Consumes a routine member: the FUNCTION/PROCEDURE keyword, its header
    /// (argument list, RETURN type) up to AS/IS or ';', and — when a body is
    /// present — the whole body block through its own closing END. Leaves _pos
    /// on the member's closing END (or ';' for spec-only members).
    /// </summary>
    private void ScanOracleRoutineMember()
    {
        Advance(); // FUNCTION / PROCEDURE

        // Header: skip until AS/IS (body start), BEGIN (trigger-style), or
        // ';' (spec member without body).
        while (Peek().Kind is not (NzToken.Unknown or NzToken.Semicolon))
        {
            var hk = Peek().Kind;
            if (hk is NzToken.As or NzToken.Is) { Advance(); break; }
            if (hk == NzToken.Begin) break;
            Advance();
        }

        if (Peek().Kind == NzToken.Semicolon)
            return;

        // Body: declarations (possibly with nested routines) followed by the
        // body BEGIN, scanned with the same block-balancing rules.
        ScanOracleBlockEnd();
    }
}
