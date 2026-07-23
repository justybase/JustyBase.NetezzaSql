using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace JustyBase.NetezzaSqlParser.Linter;

// ====== NZP001: Missing BEGIN_PROC / END_PROC ======
public class RuleNZP001_MissingProcDelimiters : LintRule
{
    public override string Id => "NZP001";
    public override string Name => "Missing BEGIN_PROC/END_PROC";
    public override string Description => "Procedure must have BEGIN_PROC and END_PROC delimiters";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 95; // Critical — procedure structure

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var hasBeginProc = Regex.IsMatch(sql, @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
        var hasEndProc = Regex.IsMatch(sql, @"\bEND_PROC\b", RegexOptions.IgnoreCase);

        if (!hasBeginProc)
            yield return new LintIssue(Id, $"{Id}: Missing BEGIN_PROC in procedure", DefaultSeverity,
                procMatch.Index, procMatch.Index + procMatch.Length);
        else if (!hasEndProc)
            yield return new LintIssue(Id, $"{Id}: Missing END_PROC in procedure", DefaultSeverity,
                procMatch.Index, procMatch.Index + procMatch.Length);
    }
}

// ====== NZP002: Missing LANGUAGE clause ======
public class RuleNZP002_MissingLanguage : LintRule
{
    public override string Id => "NZP002";
    public override string Name => "Missing LANGUAGE clause";
    public override string Description => "Procedure should specify LANGUAGE clause";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var after = sql[m.Index..];
            var endProc = Regex.Match(after, @"\bEND_PROC\b", RegexOptions.IgnoreCase);
            var range = endProc.Success ? after[..(endProc.Index + 8)] : after;
            if (!Regex.IsMatch(range, @"\bLANGUAGE\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: Missing LANGUAGE clause", DefaultSeverity,
                    m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZP003: Missing RETURNS clause ======
public class RuleNZP003_MissingReturns : LintRule
{
    public override string Id => "NZP003";
    public override string Name => "Missing RETURNS clause";
    public override string Description => "Procedure should specify RETURNS clause";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var after = sql[m.Index..];
            var langOrIs = Regex.Match(after, @"\b(LANGUAGE|IS)\b", RegexOptions.IgnoreCase);
            var range = langOrIs.Success ? after[..langOrIs.Index] : after;
            if (!Regex.IsMatch(range, @"\bRETURNS\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: Missing RETURNS clause", DefaultSeverity,
                    m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZP004: Unmatched BEGIN/END blocks ======
public class RuleNZP004_UnmatchedBeginEnd : LintRule
{
    public override string Id => "NZP004";
    public override string Name => "Unmatched BEGIN/END blocks";
    public override string Description => "BEGIN and END blocks must be balanced";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 95; // Critical — block structure

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var beginProc = Regex.Match(sql[procMatch.Index..], @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
        if (!beginProc.Success) yield break;

        var beginPos = procMatch.Index + beginProc.Index;
        var endProcMatch = Regex.Match(sql[beginPos..], @"\bEND_PROC\b", RegexOptions.IgnoreCase);
        var endPos = endProcMatch.Success ? beginPos + endProcMatch.Index : sql.Length;
        var body = sql[beginPos..endPos];

        int begCount = 0, endCount = 0;
        foreach (Match m in Regex.Matches(body, @"\bBEGIN\b", RegexOptions.IgnoreCase))
        {
            if (!LintHelpers.IsInsideStringOrComment(body, m.Index) && !IsBeginProc(body, m.Index))
                begCount++;
        }
        foreach (Match m in Regex.Matches(body, @"\bEND\b(?!\s*IF\b|\s*LOOP\b|\s*_PROC\b)", RegexOptions.IgnoreCase))
        {
            if (!LintHelpers.IsInsideStringOrComment(body, m.Index) && !IsEndProc(body, m.Index))
                endCount++;
        }

        if (begCount != endCount)
            yield return new LintIssue(Id,
                $"{Id}: {begCount} BEGIN vs {endCount} END", DefaultSeverity,
                beginPos, beginPos + body.Length);
    }

    private static bool IsBeginProc(string s, int idx) =>
        idx + 10 <= s.Length && s[idx..(idx + 10)].Equals("BEGIN_PROC", StringComparison.OrdinalIgnoreCase);
    private static bool IsEndProc(string s, int idx) =>
        idx + 8 <= s.Length && s[idx..(idx + 8)].Equals("END_PROC", StringComparison.OrdinalIgnoreCase);
}

// ====== NZP005: IF without END IF ======
public class RuleNZP005_IfWithoutEndIf : LintRule
{
    public override string Id => "NZP005";
    public override string Name => "IF without END IF";
    public override string Description => "IF must be closed with END IF";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 95; // Critical — control flow

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        int ifCount = 0, endIfCount = 0;
        var matches = Regex.Matches(sql[procMatch.Index..], @"\b(?:IF\b|END\s+IF\b)", RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            if (LintHelpers.IsInsideStringOrComment(sql, procMatch.Index + m.Index)) continue;
            if (m.Value.StartsWith("IF", StringComparison.OrdinalIgnoreCase))
                ifCount++;
            else
                endIfCount++;
        }

        if (ifCount > endIfCount)
            yield return new LintIssue(Id, $"{Id}: {ifCount} IF vs {endIfCount} END IF", DefaultSeverity,
                procMatch.Index, procMatch.Index + 10);
    }
}

// ====== NZP006: LOOP without END LOOP ======
public class RuleNZP006_LoopWithoutEndLoop : LintRule
{
    public override string Id => "NZP006";
    public override string Name => "LOOP without END LOOP";
    public override string Description => "LOOP must be closed with END LOOP";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 95; // Critical — control flow

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var beginProc = Regex.Match(sql[procMatch.Index..], @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
        if (!beginProc.Success) yield break;
        var body = sql[(procMatch.Index + beginProc.Index)..];

        int loopCount = 0, endLoopCount = 0;
        foreach (Match m in Regex.Matches(body, @"\b(?:LOOP\b|END\s+LOOP\b)", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(body, m.Index)) continue;
            // Exclude ALIAS FOR (the FOR keyword before LOOP in declarations)
            if (m.Value.Equals("LOOP", StringComparison.OrdinalIgnoreCase))
            {
                // Check if preceded by FOR (FOR...LOOP construct) - those are matched correctly
                loopCount++;
            }
            else
                endLoopCount++;
        }

        if (loopCount != endLoopCount)
            yield return new LintIssue(Id, $"{Id}: {loopCount} LOOP vs {endLoopCount} END LOOP", DefaultSeverity,
                procMatch.Index, procMatch.Index + 10);
    }
}

// ====== NZP007: Missing semicolons ======
public class RuleNZP007_MissingSemicolons : LintRule
{
    public override string Id => "NZP007";
    public override string Name => "Missing Semicolons";
    public override string Description => "Procedure statements should end with semicolons";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 75; // Elevated — common source of errors

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var beginProc = Regex.Match(sql[procMatch.Index..], @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
        if (!beginProc.Success) yield break;
        var start = procMatch.Index + beginProc.Index;
        var body = sql[start..];

        var endProcInBody = Regex.Match(body, @"\bEND_PROC\b", RegexOptions.IgnoreCase);
        var range = endProcInBody.Success ? body[..endProcInBody.Index] : body;

        // Check for missing semicolons: statements (SELECT, INSERT, UPDATE, DELETE, RETURN, RAISE, etc.)
        // followed by non-semicolon, non-keyword
        var checks = Regex.Matches(range,
            @"\b(SELECT|INSERT|UPDATE|DELETE|RETURN|RAISE|EXEC|EXECUTE|CALL|EXIT|ROLLBACK|COMMIT)\b[^;]*?(?=\b(?:SELECT|INSERT|UPDATE|DELETE|RETURN|RAISE|EXEC|EXECUTE|CALL|EXIT|ROLLBACK|COMMIT|BEGIN|END|LOOP|ELSE|ELSIF|WHEN|END_PROC)\b|$)",
            RegexOptions.IgnoreCase);
        foreach (Match m in checks)
        {
            if (LintHelpers.IsInsideStringOrComment(body, m.Index)) continue;
            if (!m.Value.TrimEnd().EndsWith(';'))
                yield return new LintIssue(Id, $"{Id}: Statement may be missing semicolon", DefaultSeverity,
                    start + m.Index, start + m.Index + m.Length);
        }
    }
}

// ====== NZP008: Unused variables ======
public class RuleNZP008_UnusedVariables : LintRule
{
    public override string Id => "NZP008";
    public override string Name => "Unused Variables";
    public override string Description => "Declared variable is never used";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var declareSection = Regex.Match(sql[procMatch.Index..], @"\bDECLARE\b(.+?)\bBEGIN\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!declareSection.Success) yield break;

        var declareBody = declareSection.Groups[1].Value;
        var varMatches = Regex.Matches(declareBody, @"\b(\w+)\s+(?:ALIAS\b|CONSTANT\b|\w+(?:\([^)]*\))?)",
            RegexOptions.IgnoreCase);

        foreach (Match v in varMatches)
        {
            if (!v.Groups[1].Success) continue;
            var varName = v.Groups[1].Value;
            // Skip type names and keywords
            if (varName.Equals("ALIAS", StringComparison.OrdinalIgnoreCase) ||
                varName.Equals("CONSTANT", StringComparison.OrdinalIgnoreCase) ||
                varName.Length <= 1) continue;

            var afterDeclare = sql[(procMatch.Index + declareSection.Index + declareSection.Length)..];
            if (!Regex.IsMatch(afterDeclare, $@"\b{Regex.Escape(varName)}\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: Variable '{varName}' is never used", DefaultSeverity,
                    procMatch.Index + declareSection.Index + v.Index, procMatch.Index + declareSection.Index + v.Index + varName.Length);
        }
    }
}

// ====== NZP009: Missing EXCEPTION handler ======
public class RuleNZP009_MissingExceptionHandler : LintRule
{
    public override string Id => "NZP009";
    public override string Name => "Missing EXCEPTION handler";
    public override string Description => "Procedure block should have EXCEPTION handler";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var beginProc = Regex.Match(sql[procMatch.Index..], @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
        if (!beginProc.Success) yield break;

        var bodyStart = procMatch.Index + beginProc.Index;
        var endProc = Regex.Match(sql[bodyStart..], @"\bEND_PROC\b", RegexOptions.IgnoreCase);
        var bodyEnd = endProc.Success ? bodyStart + endProc.Index : sql.Length;
        var body = sql[bodyStart..bodyEnd];

        // Check each BEGIN...END block for EXCEPTION
        int offset = 0;
        while (offset < body.Length)
        {
            var begin = Regex.Match(body[offset..], @"\bBEGIN\b", RegexOptions.IgnoreCase);
            if (!begin.Success) break;
            if (LintHelpers.IsInsideStringOrComment(body, offset + begin.Index) ||
                IsBeginProc(body, offset + begin.Index))
            {
                offset += begin.Index + 5;
                continue;
            }

            var blockStart = offset + begin.Index;
            var blockEnd = FindMatchingEnd(body, blockStart + 5);
            if (blockEnd < 0) break;

            var block = body[blockStart..blockEnd];
            if (!Regex.IsMatch(block, @"\bEXCEPTION\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: Block missing EXCEPTION handler", DefaultSeverity,
                    bodyStart + blockStart, bodyStart + blockStart + 5);

            offset = blockEnd;
        }
    }

    private static int FindMatchingEnd(string s, int start)
    {
        int depth = 1, i = start;
        while (i < s.Length && depth > 0)
        {
            if (LintHelpers.IsInsideStringOrComment(s, i)) { i++; continue; }
            var rem = s[i..];
            var beginMatch = Regex.Match(rem, @"\bBEGIN\b", RegexOptions.IgnoreCase);
            var endMatch = Regex.Match(rem, @"\bEND\b", RegexOptions.IgnoreCase);

            if (endMatch.Success && (!beginMatch.Success || endMatch.Index < beginMatch.Index))
            {
                depth--;
                i += endMatch.Index + 3;
            }
            else if (beginMatch.Success)
            {
                depth++;
                i += beginMatch.Index + 5;
            }
            else if (endMatch.Success)
            {
                depth--;
                i += endMatch.Index + 3;
            }
            else break;
        }
        return depth == 0 ? i : -1;
    }

    private static bool IsBeginProc(string s, int idx) =>
        idx + 10 <= s.Length && s[idx..(idx + 10)].Equals("BEGIN_PROC", StringComparison.OrdinalIgnoreCase);
}

// ====== NZP010: RAISE with severity ======
public class RuleNZP010_RaiseSeverity : LintRule
{
    public override string Id => "NZP010";
    public override string Name => "RAISE Severity";
    public override string Description => "RAISE should specify severity level";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    private static readonly string[] Valid = { "NOTICE", "WARNING", "ERROR", "EXCEPTION" };

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bRAISE\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var after = sql[(m.Index + 5)..].TrimStart();
            bool hasSeverity = false;
            foreach (var sev in Valid)
            {
                if (after.StartsWith(sev, StringComparison.OrdinalIgnoreCase))
                {
                    hasSeverity = true;
                    break;
                }
            }
            if (!hasSeverity)
                yield return new LintIssue(Id, $"{Id}: RAISE should have severity level (NOTICE/WARNING/ERROR/EXCEPTION)",
                    DefaultSeverity, m.Index, m.Index + 5);
        }
    }
}

// ====== NZP011: SELECT without INTO in procedure ======
public class RuleNZP011_SelectWithoutInto : LintRule
{
    public override string Id => "NZP011";
    public override string Name => "SELECT without INTO";
    public override string Description => "SELECT in procedures should use INTO";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var beginProc = Regex.Match(sql[procMatch.Index..], @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
        if (!beginProc.Success) yield break;
        var bodyStart = procMatch.Index + beginProc.Index;
        var endProc = Regex.Match(sql[bodyStart..], @"\bEND_PROC\b", RegexOptions.IgnoreCase);
        var body = endProc.Success ? sql[bodyStart..(bodyStart + endProc.Index)] : sql[bodyStart..];

        foreach (Match m in Regex.Matches(body, @"\bSELECT\b(?!\s+INTO\b)", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(body, m.Index)) continue;
            // Skip INSERT INTO ... SELECT
            var before = body[..m.Index].TrimEnd();
            if (before.EndsWith("INSERT", StringComparison.OrdinalIgnoreCase) == false &&
                before.EndsWith(")", StringComparison.OrdinalIgnoreCase) == false)
                yield return new LintIssue(Id, $"{Id}: SELECT should use INTO in procedures", DefaultSeverity,
                    bodyStart + m.Index, bodyStart + m.Index + 6);
        }
    }
}

// ====== NZP012: Incorrect ELSIF syntax ======
public class RuleNZP012_IncorrectElsif : LintRule
{
    public override string Id => "NZP012";
    public override string Name => "Incorrect ELSIF Syntax";
    public override string Description => "Use ELSIF instead of ELSEIF or ELSE IF";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 90; // Syntax error

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\b(?:ELSEIF|ELSE\s+IF)\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id,
                $"{Id}: Use ELSIF instead of '{m.Value}'", DefaultSeverity,
                m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZP013: Missing THEN after IF/ELSIF ======
public class RuleNZP013_MissingThen : LintRule
{
    public override string Id => "NZP013";
    public override string Name => "Missing THEN keyword";
    public override string Description => "IF and ELSIF statements must have THEN keyword";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 75; // Elevated — correctness

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match procMatch in Regex.Matches(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b.*?\bEND_PROC\b", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) continue;
            var begin = Regex.Match(procMatch.Value, @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
            if (!begin.Success) continue;
            var bodyStart = begin.Index + begin.Length;
            var body = procMatch.Value[bodyStart..];
            foreach (Match ifMatch in Regex.Matches(body, @"(?<!ELS|END\s)\b(IF|ELSIF)\b(?!\s+(?:NOT\s+)?EXISTS\b)", RegexOptions.IgnoreCase))
            {
                var after = body[(ifMatch.Index + ifMatch.Length)..];
                var terminator = Regex.Match(after, @"\b(ELSIF|ELSE|END\s+IF)\b|;", RegexOptions.IgnoreCase);
                var condition = terminator.Success ? after[..terminator.Index] : after;
                if (!Regex.IsMatch(condition, @"\bTHEN\b", RegexOptions.IgnoreCase))
                    yield return new LintIssue(Id, $"{Id}: {ifMatch.Groups[1].Value.ToUpperInvariant()} statement missing THEN keyword",
                        DefaultSeverity, procMatch.Index + bodyStart + ifMatch.Index,
                        procMatch.Index + bodyStart + ifMatch.Index + ifMatch.Length);
            }
        }
    }
}

// ====== NZP014: EXIT without WHEN ======
public class RuleNZP014_ExitWithoutWhen : LintRule
{
    public override string Id => "NZP014";
    public override string Name => "EXIT without WHEN";
    public override string Description => "EXIT should specify WHEN condition";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bEXIT\b(?!\s+WHEN\b)", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id,
                $"{Id}: EXIT without WHEN condition — use EXIT WHEN condition", DefaultSeverity,
                m.Index, m.Index + 4);
        }
    }
}

// ====== NZP015: Parameter naming convention (p_ prefix) ======
public class RuleNZP015_ParameterNaming : LintRule
{
    public override string Id => "NZP015";
    public override string Name => "Parameter naming convention";
    public override string Description => "Procedure parameters should use p_ prefix";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql,
            @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\s+\w+\s*\((.*?)\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var paramList = m.Groups[1].Value;
            foreach (var segment in paramList.Split(','))
            {
                var p = Regex.Match(segment.Trim(), @"^(?:(?:IN|OUT|INOUT)\s+)?(\w+)\b", RegexOptions.IgnoreCase);
                if (!p.Success) continue;
                var pName = p.Groups[1].Value;
                if (pName.Length > 0 && !pName.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase)
                    && !pName.StartsWith("p_", StringComparison.OrdinalIgnoreCase)
                    && !pName.StartsWith("v_", StringComparison.OrdinalIgnoreCase)
                    && !LintHelpers.IsInsideStringOrComment(paramList, paramList.IndexOf(pName, StringComparison.OrdinalIgnoreCase)))
                    yield return new LintIssue(Id,
                        $"{Id}: Parameter '{pName}' should use p_ prefix", DefaultSeverity,
                        m.Index + paramList.IndexOf(pName, StringComparison.OrdinalIgnoreCase),
                        m.Index + paramList.IndexOf(pName, StringComparison.OrdinalIgnoreCase) + pName.Length);
            }
        }
    }
}

// ====== NZP016: Variable naming convention (v_ prefix) ======
public class RuleNZP016_VariableNaming : LintRule
{
    public override string Id => "NZP016";
    public override string Name => "Variable naming convention";
    public override string Description => "Declared variables should use v_ prefix";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var declareSection = Regex.Match(sql[procMatch.Index..], @"\bDECLARE\b(.+?)\bBEGIN\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!declareSection.Success) yield break;

        var declareBody = declareSection.Groups[1].Value;
        foreach (Match v in Regex.Matches(declareBody, @"\b(\w+)\s+(?:ALIAS\b|CONSTANT\b)?\s*\w+(?:\([^)]*\))?",
            RegexOptions.IgnoreCase))
        {
            if (!v.Groups[1].Success) continue;
            var varName = v.Groups[1].Value;
            if (varName.Equals("ALIAS", StringComparison.OrdinalIgnoreCase) ||
                varName.Equals("CONSTANT", StringComparison.OrdinalIgnoreCase) ||
                varName.Length <= 1) continue;
            if (!varName.StartsWith("v_", StringComparison.OrdinalIgnoreCase))
                yield return new LintIssue(Id,
                    $"{Id}: Variable '{varName}' should use v_ prefix", DefaultSeverity,
                    procMatch.Index + declareSection.Index + v.Index,
                    procMatch.Index + declareSection.Index + v.Index + varName.Length);
        }
    }
}

// ====== NZP017: Unmatched CASE/END CASE ======
public class RuleNZP017_UnmatchedCaseEndCase : LintRule
{
    public override string Id => "NZP017";
    public override string Name => "Unmatched CASE/END CASE";
    public override string Description => "CASE blocks must be closed with END CASE";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 90; // Syntax error

    public override IEnumerable<LintIssue> Check(string sql)
    {
        // Walk each procedure body (between BEGIN_PROC and END_PROC). In procedure
        // context the standalone CASE statement (not CASE WHEN expression) must be
        // closed with END CASE. Distinguishing a statement CASE from an expression
        // CASE is heuristic via regex; the typical pattern is "CASE <var>" or
        // "CASE" on its own line followed by "WHEN <val> THEN" lines.
        foreach (Match procMatch in Regex.Matches(sql,
            @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b.*?\bEND_PROC\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) continue;
            var body = procMatch.Value;

            int caseCount = 0, endCaseCount = 0;
            int firstUnmatchedCase = -1;
            int depth = 0;

            // Track both searched CASE expressions and procedural CASE blocks.
            foreach (Match m in Regex.Matches(body, @"\bCASE\b", RegexOptions.IgnoreCase))
            {
                if (LintHelpers.IsInsideStringOrComment(body, m.Index)) continue;
                caseCount++;
                if (depth == 0) firstUnmatchedCase = procMatch.Index + m.Index;
                depth++;
            }
            foreach (Match m in Regex.Matches(body, @"\bEND\b(?!\s+(?:IF|LOOP|PROC)\b)", RegexOptions.IgnoreCase))
            {
                if (LintHelpers.IsInsideStringOrComment(body, m.Index)) continue;
                if (depth == 0) continue; // stray END CASE
                depth--;
                endCaseCount++;
            }

            if (depth > 0 && firstUnmatchedCase >= 0)
            {
                yield return new LintIssue(Id,
                    $"{Id}: {depth} unmatched CASE — expected END CASE",
                    DefaultSeverity, firstUnmatchedCase, firstUnmatchedCase + 4);
            }
            else if (endCaseCount > caseCount)
            {
                yield return new LintIssue(Id,
                    $"{Id}: {endCaseCount - caseCount} stray END CASE without matching CASE",
                    DefaultSeverity, procMatch.Index, procMatch.Index + 10);
            }
        }
    }
}

// ====== NZP018: SQL Injection risk ======
public class RuleNZP018_SqlInjectionRisk : LintRule
{
    public override string Id => "NZP018";
    public override string Name => "SQL Injection Risk";
    public override string Description => "EXECUTE IMMEDIATE with concatenation may risk SQL injection";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 85; // Security — elevated above Warning default 70

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bEXECUTE\s+IMMEDIATE\b.*?\|\|", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id,
                $"{Id}: EXECUTE IMMEDIATE with string concatenation may risk SQL injection. Use USING clause or parameterized queries instead.",
                DefaultSeverity, m.Index, m.Index + Math.Min(m.Length, 50));
        }
    }
}

// ====== NZP019: Optional parameter without DEFAULT ======
public class RuleNZP019_MissingDefault : LintRule
{
    public override string Id => "NZP019";
    public override string Name => "Optional parameter without DEFAULT";
    public override string Description => "Optional parameters should specify DEFAULT value";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql,
            @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\s+\w+\s*\((.*?)\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var paramList = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(paramList)) continue;

            var paramSpecs = paramList.Split(',');
            foreach (var p in paramSpecs)
            {
                var trimmed = p.Trim();
                if (trimmed.Length == 0) continue;
                // Skip OUT/INOUT parameters
                if (Regex.IsMatch(trimmed, @"\bOUT\b|\bINOUT\b", RegexOptions.IgnoreCase)) continue;
                // If it's a parameter name followed by type (not preceded by DEFAULT or =)
                var pMatch = Regex.Match(trimmed, @"^\s*(\w+)\s+\w+", RegexOptions.IgnoreCase);
                if (pMatch.Success && !Regex.IsMatch(trimmed, @"\bDEFAULT\b|:=", RegexOptions.IgnoreCase))
                {
                    var pName = pMatch.Groups[1].Value;
                    if (!LintHelpers.IsInsideStringOrComment(sql, m.Index + trimmed.IndexOf(pName, StringComparison.OrdinalIgnoreCase)))
                        yield return new LintIssue(Id,
                            $"{Id}: Parameter '{pName}' has no DEFAULT value", DefaultSeverity,
                            m.Index + m.Groups[1].Index + paramList.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase),
                            m.Index + m.Groups[1].Index + paramList.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) + pName.Length);
                }
            }
        }
    }
}

// ====== NZP020: Implicit type conversion ======
public class RuleNZP020_ImplicitConversion : LintRule
{
    public override string Id => "NZP020";
    public override string Name => "Implicit type conversion";
    public override string Description => "Consider explicit CAST for type conversions";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        // Look for concat with non-strings: expr || 'text' where expr is numeric
        // Simple heuristic: variable || string without explicit cast
        foreach (Match m in Regex.Matches(sql, @"\b([A-Za-z_]\w*|\d+(?:\.\d+)?)\s*\|\|\s*(?:'[^']*'|\d+(?:\.\d+)?)", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var varName = m.Groups[1].Value;
            if (!Regex.IsMatch(m.Value, @"\bCAST\s*\(", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id,
                    $"{Id}: Consider explicit CAST for variable '{varName}' in string concatenation",
                    DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZP022: OUT parameter without assignment ======
public class RuleNZP022_OutParamAssignment : LintRule
{
    public override string Id => "NZP022";
    public override string Name => "OUT parameter without assignment";
    public override string Description => "OUT parameters should be assigned before RETURN";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var beginProc = Regex.Match(sql[procMatch.Index..], @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
        if (!beginProc.Success) yield break;
        var bodyStart = procMatch.Index + beginProc.Index;
        var endProc = Regex.Match(sql[bodyStart..], @"\bEND_PROC\b", RegexOptions.IgnoreCase);
        var bodyEnd = endProc.Success ? bodyStart + endProc.Index : sql.Length;
        var body = sql[bodyStart..bodyEnd];

        // Find OUT parameters from signature
        var sigMatch = Regex.Match(sql[procMatch.Index..],
            @"\bPROCEDURE\s+\w+\s*\((.*?)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!sigMatch.Success) yield break;

        var paramList = sigMatch.Groups[1].Value;
        foreach (Match p in Regex.Matches(paramList, @"\b(OUT|INOUT)\b.*?\b(\w+)\b", RegexOptions.IgnoreCase))
        {
            if (!p.Groups[2].Success) continue;
            var outParam = p.Groups[2].Value;
            // Check if this parameter is assigned in the body
            if (!Regex.IsMatch(body, $@"\b{Regex.Escape(outParam)}\s*:=", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id,
                    $"{Id}: OUT parameter '{outParam}' is never assigned", DefaultSeverity,
                    procMatch.Index + sigMatch.Index + p.Groups[2].Index,
                    procMatch.Index + sigMatch.Index + p.Groups[2].Index + outParam.Length);
        }
    }
}

// ====== NZP023: Unclosed cursor ======
public class RuleNZP023_UnclosedCursor : LintRule
{
    public override string Id => "NZP023";
    public override string Name => "Unclosed cursor";
    public override string Description => "OPEN cursor should be followed by CLOSE cursor";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        // Find OPEN cursor statements
        foreach (Match open in Regex.Matches(sql, @"\bOPEN\s+(\w+)\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, open.Index)) continue;
            var cursorName = open.Groups[1].Value;
            // Skip FOR loops (auto-close)
            if (IsForLoopCursor(sql, open.Index)) continue;

            // Look for matching CLOSE after this OPEN
            var after = sql[(open.Index + open.Length)..];
            if (!Regex.IsMatch(after, $@"\bCLOSE\s+{Regex.Escape(cursorName)}\b", RegexOptions.IgnoreCase))
            {
                yield return new LintIssue(Id,
                    $"{Id}: Cursor '{cursorName}' is opened but may not be closed", DefaultSeverity,
                    open.Index, open.Index + open.Length);
            }
        }
    }

    private static bool IsForLoopCursor(string sql, int openPos)
    {
        // Check if OPEN is actually part of FOR cursor IN (SELECT...) construct
        int searchStart = Math.Max(0, openPos - 20);
        var before = sql[searchStart..openPos];
        return Regex.IsMatch(before, @"\bFOR\b", RegexOptions.IgnoreCase);
    }
}

// ====== NZP024: Missing RETURN statement ======
public class RuleNZP024_MissingReturn : LintRule
{
    public override string Id => "NZP024";
    public override string Name => "Missing RETURN statement";
    public override string Description => "Procedure with RETURNS type must have RETURN statement";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var returns = Regex.Match(sql, @"\bRETURNS\s+(\w+)", RegexOptions.IgnoreCase);
        if (!returns.Success) yield break;

        var begin = Regex.Match(sql, @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
        if (!begin.Success) yield break;
        var end = Regex.Match(sql[(begin.Index + begin.Length)..], @"\bEND_PROC\b", RegexOptions.IgnoreCase);
        var bodyStart = begin.Index + begin.Length;
        var body = end.Success ? sql[bodyStart..(bodyStart + end.Index)] : sql[bodyStart..];
        if (!Regex.IsMatch(body, @"\bRETURN\b", RegexOptions.IgnoreCase))
        {
            yield return new LintIssue(Id,
                $"{Id}: Procedure declares RETURNS {returns.Groups[1].Value} but has no RETURN statement",
                DefaultSeverity, bodyStart, bodyStart + Math.Min(20, body.Length));
        }
    }
}

// ====== NZP025: Transaction control in procedure ======
public class RuleNZP025_TransactionControl : LintRule
{
    public override string Id => "NZP025";
    public override string Name => "Transaction control in procedure";
    public override string Description => "COMMIT/ROLLBACK inside procedure may affect outer transactions";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var beginProc = Regex.Match(sql[procMatch.Index..], @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
        if (!beginProc.Success) yield break;

        var searchStart = procMatch.Index + beginProc.Index;

        foreach (Match m in Regex.Matches(sql[searchStart..], @"\b(COMMIT|ROLLBACK)\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, searchStart + m.Index)) continue;
            yield return new LintIssue(Id,
                $"{Id}: {m.Value} inside procedure — consider impact on outer transaction",
                DefaultSeverity, searchStart + m.Index, searchStart + m.Index + m.Length);
        }
    }
}

// ====== NZP026: Use PERFORM for discarded results ======
public class RuleNZP026_PerformInsteadOfSelect : LintRule
{
    public override string Id => "NZP026";
    public override string Name => "Use PERFORM for discarded results";
    public override string Description => "Use PERFORM instead of SELECT when discarding results in procedure";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var beginProc = Regex.Match(sql[procMatch.Index..], @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
        if (!beginProc.Success) yield break;
        var bodyStart = procMatch.Index + beginProc.Index;
        var endProc = Regex.Match(sql[bodyStart..], @"\bEND_PROC\b", RegexOptions.IgnoreCase);
        var body = endProc.Success ? sql[bodyStart..(bodyStart + endProc.Index)] : sql[bodyStart..];

        foreach (Match m in Regex.Matches(body, @"\bSELECT\s+\w+\([^)]*\)\s*(?!INTO\b)", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(body, m.Index)) continue;
            // This is a function call via SELECT that's discarded — suggest PERFORM
            yield return new LintIssue(Id,
                $"{Id}: Use PERFORM instead of SELECT when discarding function results",
                DefaultSeverity, bodyStart + m.Index, bodyStart + m.Index + m.Length);
        }
    }
}

// ====== NZP027: Missing EXECUTE AS clause ======
public class RuleNZP027_MissingExecuteAs : LintRule
{
    public override string Id => "NZP027";
    public override string Name => "Missing EXECUTE AS clause";
    public override string Description => "Procedure should specify EXECUTE AS OWNER or CALLER";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var after = sql[m.Index..];
            var endProc = Regex.Match(after, @"\bEND_PROC\b", RegexOptions.IgnoreCase);
            var range = endProc.Success ? after[..endProc.Index] : after;

            if (!Regex.IsMatch(range, @"\bEXECUTE\s+AS\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id,
                    $"{Id}: Procedure should specify EXECUTE AS OWNER or EXECUTE AS CALLER",
                    DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZP028: VARRAY without EXTEND ======
public class RuleNZP028_VarrayWithoutExtend : LintRule
{
    public override string Id => "NZP028";
    public override string Name => "VARRAY without EXTEND";
    public override string Description => "VARRAY should be extended before use";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        // Find VARRAY declarations
        var declareSection = Regex.Match(sql[procMatch.Index..], @"\bDECLARE\b(.+?)\bBEGIN\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!declareSection.Success) yield break;

        var declareBody = declareSection.Groups[1].Value;
        foreach (Match v in Regex.Matches(declareBody, @"\b(\w+)\s+VARRAY\b", RegexOptions.IgnoreCase))
        {
            if (!v.Groups[1].Success) continue;
            var varrayName = v.Groups[1].Value;
            var after = sql[(procMatch.Index + declareSection.Index + declareSection.Length)..];
            var endProc = Regex.Match(after, @"\bEND_PROC\b", RegexOptions.IgnoreCase);
            var body = endProc.Success ? after[..endProc.Index] : after;

            if (!Regex.IsMatch(body, $@"\b{Regex.Escape(varrayName)}\s*\.\s*EXTEND\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id,
                    $"{Id}: VARRAY '{varrayName}' declared but never extended with .EXTEND()",
                    DefaultSeverity,
                    procMatch.Index + declareSection.Index + v.Index,
                    procMatch.Index + declareSection.Index + v.Index + varrayName.Length);
        }
    }
}

// ====== NZP029: Deep exception nesting ======
public class RuleNZP029_DeepExceptionNesting : LintRule
{
    public override string Id => "NZP029";
    public override string Name => "Deep exception nesting";
    public override string Description => "More than 3 nested BEGIN/EXCEPTION blocks";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var procMatch = Regex.Match(sql, @"\bCREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b", RegexOptions.IgnoreCase);
        if (!procMatch.Success || LintHelpers.IsInsideStringOrComment(sql, procMatch.Index)) yield break;

        var beginProc = Regex.Match(sql[procMatch.Index..], @"\bBEGIN_PROC\b", RegexOptions.IgnoreCase);
        if (!beginProc.Success) yield break;

        var bodyStart = procMatch.Index + beginProc.Index;
        var endProc = Regex.Match(sql[bodyStart..], @"\bEND_PROC\b", RegexOptions.IgnoreCase);
        var bodyEnd = endProc.Success ? bodyStart + endProc.Index : sql.Length;
        var body = sql[bodyStart..bodyEnd];

        // Count nesting depth of BEGIN/EXCEPTION blocks
        int maxDepth = 0, depth = 0;
        foreach (Match m in Regex.Matches(body, @"\b(BEGIN|EXCEPTION|END)\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(body, m.Index)) continue;
            var val = m.Value;
            if (val.Equals("BEGIN", StringComparison.OrdinalIgnoreCase)) depth++;
            else if (val.Equals("EXCEPTION", StringComparison.OrdinalIgnoreCase)) depth++;
            else if (val.Equals("END", StringComparison.OrdinalIgnoreCase)) depth--;
            maxDepth = Math.Max(maxDepth, depth);
        }

        if (maxDepth > 3)
            yield return new LintIssue(Id,
                $"{Id}: Deep exception nesting ({maxDepth} levels). Consider refactoring.",
                DefaultSeverity, bodyStart, bodyStart + Math.Min(10, body.Length));
    }
}

// ====== NZP030: Use named exceptions ======
public class RuleNZP030_NamedExceptions : LintRule
{
    public override string Id => "NZP030";
    public override string Name => "Use named exceptions";
    public override string Description => "Use named exceptions instead of SQLSTATE codes";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"WHEN\s+SQLSTATE\s+'(\d+)'", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id,
                $"{Id}: Use named exception instead of SQLSTATE '{m.Groups[1].Value}'",
                DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

// ====== Extended all-rules registry ======
public static class NzLintRulesExtensions
{
    public static IReadOnlyList<LintRule> ProcedureRules { get; } = new LintRule[]
    {
        new RuleNZP001_MissingProcDelimiters(),
        new RuleNZP002_MissingLanguage(),
        new RuleNZP003_MissingReturns(),
        new RuleNZP004_UnmatchedBeginEnd(),
        new RuleNZP005_IfWithoutEndIf(),
        new RuleNZP006_LoopWithoutEndLoop(),
        new RuleNZP007_MissingSemicolons(),
        new RuleNZP008_UnusedVariables(),
        new RuleNZP009_MissingExceptionHandler(),
        new RuleNZP010_RaiseSeverity(),
        new RuleNZP011_SelectWithoutInto(),
        new RuleNZP012_IncorrectElsif(),
        new RuleNZP013_MissingThen(),

        // NZP014: EXIT without WHEN
        new RuleNZP014_ExitWithoutWhen(),
        // NZP015: Parameter naming convention (p_ prefix)
        new RuleNZP015_ParameterNaming(),
        // NZP016: Variable naming convention (v_ prefix)
        new RuleNZP016_VariableNaming(),
        // NZP017: Unmatched CASE/END CASE
        new RuleNZP017_UnmatchedCaseEndCase(),
        // NZP018: SQL Injection risk (EXECUTE IMMEDIATE with ||)
        new RuleNZP018_SqlInjectionRisk(),
        // NZP019: Optional parameter without DEFAULT
        new RuleNZP019_MissingDefault(),
        // NZP020: Implicit type conversion
        new RuleNZP020_ImplicitConversion(),
        // NZP022: OUT parameter without assignment
        new RuleNZP022_OutParamAssignment(),
        // NZP023: Unclosed cursor
        new RuleNZP023_UnclosedCursor(),
        new RuleNZP024_MissingReturn(),
        // NZP024: Missing RETURN (duplicate — NZP013 covers this)
        // NZP025: Transaction control in procedure
        new RuleNZP025_TransactionControl(),
        // NZP026: Use PERFORM for discarded results
        new RuleNZP026_PerformInsteadOfSelect(),
        // NZP027: Missing EXECUTE AS clause
        new RuleNZP027_MissingExecuteAs(),
        // NZP028: VARRAY without EXTEND
        new RuleNZP028_VarrayWithoutExtend(),
        // NZP029: Deep exception nesting
        new RuleNZP029_DeepExceptionNesting(),
        // NZP030: Use named exceptions
        new RuleNZP030_NamedExceptions(),
    };
}
