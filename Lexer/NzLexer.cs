using System.Text.RegularExpressions;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace JustyBase.NetezzaSqlParser.Lexer;

public static class NzLexer
{
    // Matches a keyword using word-boundary-anchored regex, case-insensitive.
    // The \b anchor prevents matching inside longer identifiers (e.g. "IN" won't match in "INT").
    private static TextParser<TextSpan> Kw(string keyword) =>
        Span.Regex(@"(?i)\b" + keyword + @"\b");

    private static readonly TokenizerBuilder<NzToken> Builder = new TokenizerBuilder<NzToken>()
        // Comments (ignored)
        .Ignore(Span.Regex(@"^--[^\n]*"))
        .Ignore(Span.Regex(@"^/\*[\s\S]*?\*/"))
        // Optional NZPLSQL loop labels (<<label>>) are structural markers.
        .Ignore(Span.Regex(@"^<<\s*[A-Za-z_][A-Za-z0-9_]*\s*>>"))

        // Multi-word keywords (requireDelimiters: false since they contain whitespace)
        .Match(Span.Regex(@"GROUP\s+BY", RegexOptions.IgnoreCase), NzToken.GroupBy, requireDelimiters: false)
        .Match(Span.Regex(@"ORDER\s+BY", RegexOptions.IgnoreCase), NzToken.OrderBy, requireDelimiters: false)
        .Match(Span.Regex(@"PARTITION\s+BY", RegexOptions.IgnoreCase), NzToken.PartitionBy, requireDelimiters: false)
        .Match(Span.Regex(@"MINUS\s+SET", RegexOptions.IgnoreCase), NzToken.MinusSet, requireDelimiters: false)

        // AtSet
        .Match(Span.Regex(@"^@SET\b", RegexOptions.IgnoreCase), NzToken.AtSet)

        // DML Keywords
        .Match(Kw("SELECT"), NzToken.Select)
        .Match(Kw("FROM"), NzToken.From)
        .Match(Kw("WHERE"), NzToken.Where)
        .Match(Kw("INSERT"), NzToken.Insert)
        .Match(Kw("INTO"), NzToken.Into)
        .Match(Kw("VALUES"), NzToken.Values)
        .Match(Kw("VALUE"), NzToken.Value)
        .Match(Kw("UPDATE"), NzToken.Update)
        .Match(Kw("SET"), NzToken.Set)
        .Match(Kw("DELETE"), NzToken.Delete)
        .Match(Kw("MATERIALIZED"), NzToken.Materialized)
        .Match(Kw("PERFORM"), NzToken.Perform)
        .Match(Kw("REVERSE"), NzToken.Reverse)
        .Match(Kw("OUT"), NzToken.Out)
        .Match(Kw("INOUT"), NzToken.Inout)
        .Match(Kw("SQLSTATE"), NzToken.Sqlstate)
        .Match(Kw("OTHERS"), NzToken.Others)

        // JOIN Keywords
        .Match(Kw("JOIN"), NzToken.Join)
        .Match(Kw("INNER"), NzToken.Inner)
        .Match(Kw("LEFT"), NzToken.Left)
        .Match(Kw("RIGHT"), NzToken.Right)
        .Match(Kw("FULL"), NzToken.Full)
        .Match(Kw("OUTER"), NzToken.Outer)
        .Match(Kw("CROSS"), NzToken.Cross)
        .Match(Kw("NATURAL"), NzToken.Natural)
        .Match(Kw("ONLY"), NzToken.Only)
        .Match(Kw("ON"), NzToken.On)

        // Logical Operators
        .Match(Kw("AND"), NzToken.And)
        .Match(Kw("OR"), NzToken.Or)
        .Match(Kw("NOT"), NzToken.Not)

        // SELECT Modifiers
        .Match(Kw("AS"), NzToken.As)
        .Match(Kw("DISTINCT"), NzToken.Distinct)
        .Match(Kw("ALL"), NzToken.All)

        // Set Operations
        .Match(Kw("UNION"), NzToken.Union)
        .Match(Kw("INTERSECT"), NzToken.Intersect)
        .Match(Kw("EXCEPT"), NzToken.Except)

        // Clauses
        .Match(Kw("HAVING"), NzToken.Having)
        .Match(Kw("LIMIT"), NzToken.Limit)
        .Match(Kw("OFFSET"), NzToken.Offset)

        // NULL handling
        .Match(Kw("NULLS"), NzToken.Nulls)
        .Match(Kw("NULL"), NzToken.Null)
        .Match(Kw("IS"), NzToken.Is)

        // Pattern matching
        .Match(Kw("ILIKE"), NzToken.Ilike)
        .Match(Kw("LIKE"), NzToken.Like)
        .Match(Kw("ESCAPE"), NzToken.Escape)
        .Match(Kw("IN"), NzToken.In)
        .Match(Kw("BETWEEN"), NzToken.Between)
        .Match(Kw("EXISTS"), NzToken.Exists)

        // CASE expression
        .Match(Kw("CASE"), NzToken.Case)
        .Match(Kw("WHEN"), NzToken.When)
        .Match(Kw("THEN"), NzToken.Then)
        .Match(Kw("ELSIF"), NzToken.Elsif)
        .Match(Kw("IF"), NzToken.If)
        .Match(Kw("ELSE"), NzToken.Else)
        .Match(Kw("END"), NzToken.End)

        // NZPLSQL
        .Match(Kw("NZPLSQL"), NzToken.Nzplsql)
        .Match(Kw("BEGIN_PROC"), NzToken.BeginProc)
        .Match(Kw("END_PROC"), NzToken.EndProc)
        .Match(Kw("BEGIN"), NzToken.Begin)
        .Match(Kw("DECLARE"), NzToken.Declare)
        .Match(Kw("EXCEPTION"), NzToken.Exception)
        .Match(Kw("RETURN"), NzToken.Return)
        .Match(Kw("ALIAS"), NzToken.Alias)
        .Match(Kw("CONSTANT"), NzToken.Constant)
        .Match(Kw("LOOP"), NzToken.Loop)
        .Match(Kw("WHILE"), NzToken.While)
        .Match(Kw("EXIT"), NzToken.Exit)
        .Match(Kw("RAISE"), NzToken.Raise)
        .Match(Kw("NOTICE"), NzToken.Notice)
        .Match(Kw("DEBUG"), NzToken.Debug)
        .Match(Kw("ERROR"), NzToken.Error1)
        .Match(Kw("ROLLBACK"), NzToken.Rollback)
        .Match(Kw("COMMIT"), NzToken.Commit)
        .Match(Kw("CALL"), NzToken.Call)
        .Match(Kw("IMMEDIATE"), NzToken.Immediate)
        .Match(Kw("USING"), NzToken.Using)

        // DCL
        .Match(Kw("GRANT"), NzToken.Grant)
        .Match(Kw("REVOKE"), NzToken.Revoke)
        .Match(Kw("TO"), NzToken.To)
        .Match(Kw("PUBLIC"), NzToken.Public)
        .Match(Kw("TYPE"), NzToken.Type)
        .Match(Kw("CASCADE"), NzToken.Cascade)
        .Match(Kw("RESTRICT"), NzToken.Restrict)
        .Match(Kw("SAMEAS"), NzToken.SameAs)
        .Match(Kw("HASH"), NzToken.Hash)
        .Match(Kw("DEFERRABLE"), NzToken.Deferrable)
        .Match(Kw("INITIALLY"), NzToken.Initially)

        // DDL
        .Match(Kw("CREATE"), NzToken.Create)
        .Match(Kw("REPLACE"), NzToken.Replace)
        .Match(Kw("DATABASE"), NzToken.Database)
        .Match(Kw("SCHEMA"), NzToken.Schema)
        .Match(Kw("TABLE"), NzToken.Table)
        .Match(Kw("SEQUENCE"), NzToken.Sequence)
        .Match(Kw("SESSION"), NzToken.Session)
        .Match(Kw("SYNONYM"), NzToken.Synonym)
        .Match(Kw("USER"), NzToken.User)
        .Match(Kw("PROCEDURE"), NzToken.Procedure)
        .Match(Kw("TEMPORARY"), NzToken.Temporary)
        .Match(Kw("TEMP"), NzToken.Temp)
        .Match(Kw("DROP"), NzToken.Drop)
        .Match(Kw("TRUNCATE"), NzToken.Truncate)
        .Match(Kw("EXPLAIN"), NzToken.Explain)
        .Match(Kw("VERBOSE"), NzToken.Verbose)
        .Match(Kw("DISTRIBUTION"), NzToken.Distribution)
        .Match(Kw("PLANTEXT"), NzToken.Plantext)
        .Match(Kw("PLANGRAPH"), NzToken.Plangraph)
        .Match(Kw("ALTER"), NzToken.Alter)
        .Match(Kw("SHOW"), NzToken.Show)
        .Match(Kw("COPY"), NzToken.Copy)
        .Match(Kw("LOCK"), NzToken.Lock)
        .Match(Kw("MERGE"), NzToken.Merge)
        .Match(Kw("MATCHED"), NzToken.Matched)
        .Match(Kw("REINDEX"), NzToken.Reindex)
        .Match(Kw("RESET"), NzToken.Reset)
        .Match(Kw("EXTERNAL"), NzToken.External)
        .Match(Kw("VIEWS"), NzToken.Views)
        .Match(Kw("VIEW"), NzToken.View)
        .Match(Kw("COMMENT"), NzToken.Comment)
        .Match(Kw("RENAME"), NzToken.Rename)
        .Match(Kw("MODIFY"), NzToken.Modify)
        .Match(Kw("PRIVILEGES"), NzToken.Privileges)
        .Match(Kw("DEFERRED"), NzToken.Deferred)
        .Match(Kw("MATCH"), NzToken.Match)
        .Match(Kw("ACTION"), NzToken.Action)
        .Match(Kw("WITHIN"), NzToken.Within)
        .Match(Kw("HISTORY"), NzToken.History)
        .Match(Kw("CONFIGURATION"), NzToken.Configuration)
        .Match(Kw("SCHEDULER"), NzToken.Scheduler)
        .Match(Kw("RULE"), NzToken.Rule)
        .Match(Kw("NOT NULL"), NzToken.NotNull, requireDelimiters: false)
        .Match(Kw("WARNING"), NzToken.Warning)
        .Match(Kw("COLUMN"), NzToken.Column)
        .Match(Kw("ADD"), NzToken.Add)
        .Match(Kw("CONSTRAINT"), NzToken.Constraint)
        .Match(Kw("PRIMARY"), NzToken.Primary)
        .Match(Kw("KEY"), NzToken.Key)
        .Match(Kw("FOREIGN"), NzToken.Foreign)
        .Match(Kw("REFERENCES"), NzToken.References)
        .Match(Kw("UNIQUE"), NzToken.Unique)
        .Match(Kw("CHECK"), NzToken.Check)
        .Match(Kw("GLOBAL"), NzToken.Global)
        .Match(Kw("RETURNS"), NzToken.Returns)
        .Match(Kw("LANGUAGE"), NzToken.Language)
        .Match(Kw("EXECUTE"), NzToken.Execute)
        .Match(Kw("EXEC"), NzToken.Exec)
        .Match(Kw("OWNER"), NzToken.Owner)
        .Match(Kw("CALLER"), NzToken.Caller)
        .Match(Kw("REFTABLE"), NzToken.RefTable)
        .Match(Kw("VARARGS"), NzToken.Varargs)
        .Match(Kw("VARRAY"), NzToken.Varray)
        .Match(Kw("AUTOCOMMIT"), NzToken.Autocommit)

        // CTE
        .Match(Kw("WITH"), NzToken.With)
        .Match(Kw("FINAL"), NzToken.Final)
        .Match(Kw("RECURSIVE"), NzToken.Recursive)

        // Netezza-specific
        .Match(Kw("DISTRIBUTE"), NzToken.Distribute)
        .Match(Kw("RANDOM"), NzToken.Random)
        .Match(Kw("ORGANIZE"), NzToken.Organize)
        .Match(Kw("GROOM"), NzToken.Groom)
        .Match(Kw("VERSIONS"), NzToken.Versions)
        .Match(Kw("RECORDS"), NzToken.Records)
        .Match(Kw("PAGES"), NzToken.Pages)
        .Match(Kw("READY"), NzToken.Ready)
        .Match(Kw("START"), NzToken.Start)
        .Match(Kw("RECLAIM"), NzToken.Reclaim)
        .Match(Kw("BACKUPSET"), NzToken.Backupset)
        .Match(Kw("DEFAULT"), NzToken.Default)
        .Match(Kw("NONE"), NzToken.None)
        .Match(Kw("GENERATE"), NzToken.Generate)
        .Match(Kw("NEXT"), NzToken.Next)
        .Match(Kw("EXPRESS"), NzToken.Express)
        .Match(Kw("STATISTICS"), NzToken.Statistics)
        .Match(Kw("FOR"), NzToken.For)
        .Match(Kw("OF"), NzToken.Of)

        // ORDER BY / FETCH
        .Match(Kw("ASC"), NzToken.Asc)
        .Match(Kw("DESC"), NzToken.Desc)
        .Match(Kw("FETCH"), NzToken.Fetch)
        .Match(Kw("FIRST"), NzToken.First)

        // Quantified comparisons
        .Match(Kw("ANY"), NzToken.Any)
        .Match(Kw("SOME"), NzToken.Some)

        // Window functions
        .Match(Kw("OVER"), NzToken.Over)
        .Match(Kw("ROWS"), NzToken.Rows)
        .Match(Kw("RANGE"), NzToken.Range)
        .Match(Kw("GROUPS"), NzToken.Groups)
        .Match(Kw("CURRENT"), NzToken.Current)
        .Match(Kw("ROW"), NzToken.Row)
        .Match(Kw("UNBOUNDED"), NzToken.Unbounded)
        .Match(Kw("PRECEDING"), NzToken.Preceding)
        .Match(Kw("FOLLOWING"), NzToken.Following)
        .Match(Kw("FILTER"), NzToken.Filter)
        .Match(Kw("EXCLUDE"), NzToken.Exclude)
        .Match(Kw("TIES"), NzToken.Ties)

        // Expressions / built-ins
        .Match(Kw("EXTRACT"), NzToken.Extract)
        .Match(Kw("CAST"), NzToken.Cast)

        // Operators (longer patterns first, anchored with ^ to prevent forward search)
        .Match(Span.Regex(@"^(?:!=|<>)"), NzToken.NotEquals)
        .Match(Span.EqualTo("<="), NzToken.LessThanEquals)
        .Match(Span.EqualTo(">="), NzToken.GreaterThanEquals)
        .Match(Span.EqualTo("||"), NzToken.Concat)
        .Match(Span.EqualTo("::"), NzToken.DoubleColon)
        .Match(Span.EqualTo(":="), NzToken.Assign)

        .Match(Span.EqualTo("="), NzToken.EqualsOp)
        .Match(Span.EqualTo("<"), NzToken.LessThan)
        .Match(Span.EqualTo(">"), NzToken.GreaterThan)
        .Match(Span.EqualTo("+"), NzToken.Plus)
        .Match(Span.EqualTo("-"), NzToken.Minus)
        .Match(Span.EqualTo("*"), NzToken.Multiply)
        .Match(Span.EqualTo("/"), NzToken.Divide)
        .Match(Span.EqualTo("%"), NzToken.Modulo)
        .Match(Span.EqualTo("^"), NzToken.Caret)

        .Match(Span.EqualTo("."), NzToken.Dot)
        .Match(Span.EqualTo(","), NzToken.Comma)
        .Match(Span.EqualTo(";"), NzToken.Semicolon)
        .Match(Span.EqualTo("("), NzToken.LParen)
        .Match(Span.EqualTo(")"), NzToken.RParen)
        .Match(Span.EqualTo("["), NzToken.LBracket)
        .Match(Span.EqualTo("]"), NzToken.RBracket)

        // Parameter
        .Match(Span.EqualTo("?"), NzToken.Parameter)

        // Variables (longer patterns first, all anchored with ^)
        .Match(Span.Regex(@"^\$\{[a-zA-Z_][a-zA-Z0-9_]*\}"), NzToken.BracedVariable)
        .Match(Span.Regex(@"^\{[a-zA-Z_][a-zA-Z0-9_]*\}"), NzToken.BracesOnlyVariable)
        .Match(Span.Regex(@"^\$\d+"), NzToken.DollarNumber)
        .Match(Span.Regex(@"^\$[a-zA-Z_][a-zA-Z0-9_]*"), NzToken.DollarIdentifier)

        // Quoted identifier
        .Match(Span.Regex(@"^""[^""]*"""), NzToken.QuotedIdentifier)

        // String literal (single-quoted, with '' escaping)
        .Match(Span.Regex(@"^'([^']|'')*'"), NzToken.StringLiteral)

        // Number literal
        .Match(Span.Regex(@"^\d+(\.\d+)?([eE][+-]?\d+)?"), NzToken.NumberLiteral)

        // Regular identifier (must be last to catch anything not matched by keywords)
        .Match(Span.Regex(@"^\p{L}[_\p{L}\p{N}]*"), NzToken.Identifier)

        // Whitespace (ignored, must be last)
        .Ignore(Span.Regex(@"^\s+"));

    public static Tokenizer<NzToken> Instance { get; } = Builder.Build();

    public static TokenList<NzToken> Tokenize(string input)
    {
        return Instance.Tokenize(input);
    }

    public static bool TryTokenize(string input, out TokenList<NzToken> result)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Trim().Length == 0)
        {
            result = new TokenList<NzToken>(Array.Empty<Token<NzToken>>());
            return true;
        }

        try
        {
            result = Instance.Tokenize(input);
            return result.Any();
        }
        catch (Superpower.ParseException)
        {
            // The Try API must not leak tokenizer parse failures for input
            // containing unsupported characters or malformed tokens.
            result = new TokenList<NzToken>(Array.Empty<Token<NzToken>>());
            return false;
        }
    }
}
