using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Authoring;

public static class NzHoverService
{
    private static readonly Dictionary<string, string> KeywordDocs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SELECT"] = "Retrieve rows from a table or view.",
        ["FROM"] = "Specify the table(s) to query.",
        ["WHERE"] = "Filter rows based on a condition.",
        ["INSERT"] = "Add new rows to a table.",
        ["INTO"] = "Specify the target table for INSERT.",
        ["VALUES"] = "Specify row values for INSERT or SET.",
        ["UPDATE"] = "Modify existing rows in a table.",
        ["SET"] = "Specify column assignments in UPDATE.",
        ["DELETE"] = "Remove rows from a table.",
        ["JOIN"] = "Combine rows from two tables.",
        ["INNER"] = "Return matching rows from both tables.",
        ["LEFT"] = "Return all rows from the left table.",
        ["RIGHT"] = "Return all rows from the right table.",
        ["FULL"] = "Return all rows from both tables.",
        ["CROSS"] = "Return Cartesian product of two tables.",
        ["ON"] = "Specify the join condition.",
        ["AND"] = "Combine two boolean conditions (both must be true).",
        ["OR"] = "Combine two boolean conditions (at least one must be true).",
        ["NOT"] = "Negate a boolean condition.",
        ["AS"] = "Assign an alias to a column or table.",
        ["DISTINCT"] = "Remove duplicate rows from results.",
        ["ALL"] = "Include all rows (default).",
        ["UNION"] = "Combine results of two queries (distinct).",
        ["INTERSECT"] = "Return rows common to both queries.",
        ["EXCEPT"] = "Return rows from the first query not in the second.",
        ["GROUP BY"] = "Group rows for aggregation.",
        ["ORDER BY"] = "Sort the result set.",
        ["HAVING"] = "Filter groups after aggregation.",
        ["LIMIT"] = "Restrict the number of result rows.",
        ["OFFSET"] = "Skip a number of rows before returning results.",
        ["NULL"] = "Represent missing or unknown data.",
        ["IS"] = "Test for a boolean condition (e.g., IS NULL).",
        ["LIKE"] = "Pattern match with % and _ wildcards.",
        ["ILIKE"] = "Case-insensitive pattern match.",
        ["IN"] = "Test if a value is in a list.",
        ["BETWEEN"] = "Test if a value is within a range.",
        ["EXISTS"] = "Test if a subquery returns any rows.",
        ["CASE"] = "Conditional expression.",
        ["WHEN"] = "Specify a condition in CASE.",
        ["THEN"] = "Specify the result when a CASE condition is true.",
        ["ELSE"] = "Specify the default result in CASE.",
        ["END"] = "End a CASE expression or a block.",
        ["BEGIN"] = "Start a block of statements.",
        ["DECLARE"] = "Declare a variable or cursor.",
        ["CREATE"] = "Create a new database object.",
        ["TABLE"] = "Create or reference a database table.",
        ["VIEW"] = "Create or reference a view.",
        ["DROP"] = "Remove a database object.",
        ["ALTER"] = "Modify a database object.",
        ["TRUNCATE"] = "Remove all rows from a table.",
        ["WITH"] = "Define a Common Table Expression (CTE).",
        ["RECURSIVE"] = "Allow a CTE to reference itself.",
        ["DISTRIBUTE ON"] = "Specify distribution key for a table.",
        ["ORGANIZE ON"] = "Specify organization key for a table.",
        ["EXPLAIN"] = "Show the query execution plan.",
        ["CAST"] = "Convert a value to a specified data type.",
        ["EXTRACT"] = "Extract a part of a date/time value.",
        ["OVER"] = "Define a window for window functions.",
        ["PARTITION BY"] = "Divide rows into partitions for window functions.",
        ["FETCH"] = "Retrieve rows (used with OFFSET).",
        ["FOR"] = "Used in FOR UPDATE or FOR loop.",
        ["CALL"] = "Execute a stored procedure.",
        ["RETURN"] = "Return a value from a function or procedure.",
        ["MERGE"] = "Insert, update, or delete rows based on a source.",
    };

    private static readonly HashSet<string> DataTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "INT", "INT1", "INT2", "INT4", "INT8", "INTEGER",
        "BIGINT", "SMALLINT", "BYTEINT",
        "NUMERIC", "DECIMAL", "FLOAT", "FLOAT4", "FLOAT8", "REAL", "DOUBLE", "DOUBLE PRECISION",
        "VARCHAR", "NVARCHAR", "CHAR", "NCHAR", "CHARACTER", "CHARACTER VARYING", "CHAR VARYING",
        "BOOLEAN", "BOOL",
        "DATE", "TIME", "TIMETZ", "TIMESTAMP", "TIMESTAMPTZ", "INTERVAL",
        "BINARY", "VARBINARY", "BLOB", "CLOB", "NCLOB",
        "TEXT", "NTEXT",
        "VARRAY", "RECORD", "REFTABLE",
    };

    public static SqlHoverInfo? GetHover(
        string text,
        int offset,
        ISchemaProvider? schema,
        DocumentParsingCoordinator? parsingCoordinator = null,
        string? documentUri = null)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        parsingCoordinator?.GetOrCreate(documentUri ?? "default").Parse(text);

        offset = Math.Clamp(offset, 0, Math.Max(0, text.Length - 1));

        int wordStart = offset;
        while (wordStart > 0 && IsWordChar(text[wordStart - 1]))
            wordStart--;

        int wordEnd = offset;
        while (wordEnd < text.Length && IsWordChar(text[wordEnd]))
            wordEnd++;

        if (wordStart >= wordEnd)
            return null;

        var word = text[wordStart..wordEnd];

        try
        {
            var tokens = NzLexer.Tokenize(text).ToArray();
            if (tokens.Length == 0)
                return null;

            Token<NzToken>? cursorToken = null;
            foreach (var token in tokens)
            {
                int tokenStart = token.Span.Position.Absolute;
                int tokenEnd = tokenStart + token.Span.Length;
                if (offset >= tokenStart && offset <= tokenEnd)
                {
                    cursorToken = token;
                    break;
                }
            }

            if (cursorToken is null)
                return null;

            var content = ResolveHover(cursorToken.Value, tokens, word, schema);
            return content is null ? null : new SqlHoverInfo(content, wordStart, wordEnd);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveHover(Token<NzToken> token, Token<NzToken>[] allTokens, string word, ISchemaProvider? schema)
    {
        if (token.Kind != NzToken.Identifier && token.Kind != NzToken.QuotedIdentifier)
        {
            string? keywordDoc = token.Kind switch
            {
                NzToken.GroupBy => GetKeywordDoc("GROUP BY"),
                NzToken.OrderBy => GetKeywordDoc("ORDER BY"),
                NzToken.PartitionBy => GetKeywordDoc("PARTITION BY"),
                _ => GetKeywordDoc(word),
            };

            return keywordDoc is null
                ? null
                : $"**{word.ToUpperInvariant()}**  \n{keywordDoc}";
        }

        var name = token.ToStringValue();
        if (IsDataType(name))
            return GetDataTypeDetail(name);

        if (string.Equals(name, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return $"**{name.ToUpperInvariant()}** — boolean literal";
        }

        int index = Array.FindIndex(allTokens, t => t.Span.Position.Absolute == token.Span.Position.Absolute);
        if (index < 0)
            return null;

        bool isColumnRef = index >= 2 &&
            allTokens[index - 1].Kind == NzToken.Dot &&
            allTokens[index - 2].Kind == NzToken.Identifier;

        if (isColumnRef)
        {
            var tableName = allTokens[index - 2].ToStringValue();
            return ResolveColumnRef(name, tableName, schema);
        }

        if (index >= 2 &&
            allTokens[index - 1].Kind == NzToken.Dot &&
            allTokens[index - 2].Kind == NzToken.Identifier &&
            index + 1 < allTokens.Length &&
            allTokens[index + 1].Kind == NzToken.Dot)
        {
            return $"**Schema**: {name}";
        }

        bool isFunction = index + 1 < allTokens.Length && allTokens[index + 1].Kind == NzToken.LParen;
        if (NzSignatureHelpService.TryGetSignature(name, out var signature))
        {
            return FormatFunctionDetail(name, signature);
        }

        if (isFunction)
        {
            return $"**{name.ToUpperInvariant()}()**";
        }

        if (schema is not null)
        {
            var info = schema.GetTable(null, null, name);
            if (info?.Columns is not null && info.Columns.Count > 0)
            {
                var lines = new List<string> { $"**{name}**" };
                lines.AddRange(info.Columns.Select(col => $"- `{col.Name}`"));
                return string.Join("\n", lines);
            }

            if (index >= 2 && allTokens[index - 1].Kind == NzToken.Dot && allTokens[index - 2].Kind == NzToken.Identifier)
            {
                var qualifier = allTokens[index - 2].ToStringValue();
                var qualifiedInfo = schema.GetTable(null, qualifier, name);
                if (qualifiedInfo?.Columns is not null && qualifiedInfo.Columns.Count > 0)
                {
                    var lines = new List<string> { $"**{qualifier}.{name}**" };
                    lines.AddRange(qualifiedInfo.Columns.Select(col => $"- `{col.Name}`"));
                    return string.Join("\n", lines);
                }
            }
        }

        var keywordFallback = GetKeywordDoc(name);
        return keywordFallback is null
            ? null
            : $"**{name.ToUpperInvariant()}**  \n{keywordFallback}";
    }

    private static string FormatFunctionDetail(string functionName, SqlSignatureInfo signature)
    {
        var lines = new List<string> { $"**{signature.Label}**" };
        if (!string.IsNullOrWhiteSpace(signature.Documentation))
        {
            lines.Add(signature.Documentation);
        }
        if (signature.Parameters.Length > 0)
        {
            lines.Add("");
            lines.Add("Parameters:");
            lines.AddRange(signature.Parameters.Select(parameter =>
                string.IsNullOrWhiteSpace(parameter.Documentation)
                    ? $"- `{parameter.Label}`"
                    : $"- `{parameter.Label}` — {parameter.Documentation}"));
        }
        return string.Join("\n", lines);
    }

    private static string? ResolveColumnRef(string columnName, string tableName, ISchemaProvider? schema)
    {
        if (schema is null)
            return $"`{tableName}.{columnName}`";

        var info = schema.GetTable(null, null, tableName);
        if (info?.Columns is not null)
        {
            return $"`{tableName}.{columnName}`  \nTable: **{tableName}**";
        }

        return $"`{tableName}.{columnName}`";
    }

    private static string? GetKeywordDoc(string word)
    {
        if (KeywordDocs.TryGetValue(word, out var doc))
            return doc;

        return NetezzaSqlCatalog.NetezzaKeywords.Contains(word, StringComparer.OrdinalIgnoreCase)
            ? "Netezza-specific SQL keyword or system catalog object."
            : null;
    }

    private static bool IsDataType(string name)
        => name.Length > 1 && NetezzaSqlCatalog.TryGetDataType(name, out _);

    private static string GetDataTypeDetail(string name)
    {
        var upper = name.ToUpperInvariant();
        return upper switch
        {
            "INT" or "INT4" or "INTEGER" => $"**{name}** — 32-bit signed integer (-2,147,483,648 to 2,147,483,647)",
            "INT1" or "BYTEINT" => $"**{name}** — 8-bit signed integer (-128 to 127)",
            "INT2" or "SMALLINT" => $"**{name}** — 16-bit signed integer (-32,768 to 32,767)",
            "INT8" or "BIGINT" => $"**{name}** — 64-bit signed integer (-9.22 × 10¹⁸ to 9.22 × 10¹⁸)",
            "NUMERIC" or "DECIMAL" => $"**{name}** — Exact numeric with configurable precision and scale",
            "FLOAT" or "FLOAT8" or "REAL" or "DOUBLE" or "DOUBLE PRECISION" => $"**{name}** — 64-bit floating-point",
            "FLOAT4" => $"**{name}** — 32-bit floating-point",
            "VARCHAR" => $"**{name}(n)** — Variable-length character string (max 64K)",
            "NVARCHAR" => $"**{name}(n)** — Variable-length Unicode character string",
            "CHAR" => $"**{name}(n)** — Fixed-length character string",
            "NCHAR" or "CHARACTER" => $"**{name}(n)** — Fixed-length character string",
            "CHARACTER VARYING" or "CHAR VARYING" => $"**{name}(n)** — Variable-length character string",
            "BOOLEAN" or "BOOL" => $"**{name}** — Boolean: TRUE, FALSE, or NULL",
            "DATE" => $"**{name}** — Calendar date (year, month, day)",
            "TIME" => $"**{name}** — Time of day (without time zone)",
            "TIMETZ" => $"**{name}** — Time of day (with time zone)",
            "TIMESTAMP" => $"**{name}** — Date and time (without time zone)",
            "TIMESTAMPTZ" => $"**{name}** — Date and time (with time zone)",
            "INTERVAL" => $"**{name}** — Time interval",
            "BINARY" => $"**{name}(n)** — Fixed-length binary data",
            "VARBINARY" => $"**{name}(n)** — Variable-length binary data",
            "BLOB" => $"**{name}** — Binary large object (up to 2 GB)",
            "CLOB" => $"**{name}** — Character large object (up to 2 GB)",
            "NCLOB" => $"**{name}** — Unicode character large object (up to 2 GB)",
            "TEXT" => $"**{name}** — Variable-length character string (alias for VARCHAR)",
            "NTEXT" => $"**{name}** — Variable-length Unicode character string",
            _ => $"**{name}** — Data type",
        };
    }

    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '#';
}
