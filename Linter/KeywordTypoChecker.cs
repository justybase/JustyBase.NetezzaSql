namespace JustyBase.NetezzaSqlParser.Linter;

public sealed class KeywordTypoChecker
{
    private readonly Dictionary<string, string> _knownKeywords;

    public KeywordTypoChecker()
    {
        _knownKeywords = BuildKeywordMap();
    }

    /// <summary>Returns the best keyword suggestion for a possible typo, or null.</summary>
    public string? CheckTypo(string input)
    {
        if (string.IsNullOrEmpty(input) || input.Length < 2)
            return null;

        var upper = input.ToUpperInvariant();

        // Exact match is not a typo
        if (_knownKeywords.ContainsKey(upper))
            return null;

        string? bestMatch = null;
        var bestDistance = int.MaxValue;

        foreach (var (keyword, canonical) in _knownKeywords)
        {
            var dist = DamerauLevenshtein(upper, keyword);

            // Short words (2-4 chars): max distance 1
            // Medium words (5-7 chars): max distance 2
            // Long words (8+ chars): max distance 3
            var maxDistance = keyword.Length switch
            {
                <= 4 => 1,
                <= 7 => 2,
                _ => 3
            };

            if (dist <= maxDistance && dist < bestDistance)
            {
                bestDistance = dist;
                bestMatch = canonical;
            }
        }

        return bestMatch;
    }

    /// <summary>Damerau-Levenshtein distance with adjacent transpositions.</summary>
    private static int DamerauLevenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = (b[j - 1] == a[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[n, m];
    }

    private static Dictionary<string, string> BuildKeywordMap()
    {
        // All known SQL and NZPLSQL keywords, mapped to their canonical form
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[][] groups =
        [
            // DML
            ["SELECT"], ["FROM"], ["WHERE"], ["INSERT"], ["INTO"], ["VALUES"], ["VALUE"],
            ["UPDATE"], ["SET"], ["DELETE"], ["MATERIALIZED"], ["PERFORM"], ["REVERSE"],
            ["OUT"], ["INOUT"], ["SQLSTATE"], ["OTHERS"],
            // JOIN
            ["JOIN"], ["INNER"], ["LEFT"], ["RIGHT"], ["FULL"], ["OUTER"],
            ["CROSS"], ["NATURAL"], ["ONLY"], ["ON"],
            // Logical
            ["AND"], ["OR"], ["NOT"],
            // SELECT modifiers
            ["AS"], ["DISTINCT"], ["ALL"],
            // Set operations
            ["UNION"], ["INTERSECT"], ["EXCEPT"],
            // Clauses
            ["HAVING"], ["LIMIT"], ["OFFSET"],
            // NULL
            ["NULLS"], ["NULL"], ["IS"],
            // Pattern matching
            ["ILIKE"], ["LIKE"], ["ESCAPE"], ["IN"], ["BETWEEN"], ["EXISTS"],
            // CASE
            ["CASE"], ["WHEN"], ["THEN"], ["ELSIF"], ["IF"], ["ELSE"], ["END"],
            // NZPLSQL
            ["NZPLSQL"], ["BEGIN_PROC"], ["END_PROC"], ["BEGIN"], ["DECLARE"],
            ["EXCEPTION"], ["RETURN"], ["ALIAS"], ["CONSTANT"], ["LOOP"],
            ["WHILE"], ["EXIT"], ["RAISE"], ["NOTICE"], ["DEBUG"], ["ERROR"],
            ["ROLLBACK"], ["COMMIT"], ["CALL"], ["IMMEDIATE"], ["USING"],
            // DCL
            ["GRANT"], ["REVOKE"], ["TO"], ["PUBLIC"], ["TYPE"],
            ["CASCADE"], ["RESTRICT"], ["SAMEAS"], ["HASH"],
            ["DEFERRABLE"], ["INITIALLY"],
            // DDL
            ["CREATE"], ["REPLACE"], ["DATABASE"], ["SCHEMA"], ["TABLE"],
            ["SEQUENCE"], ["SESSION"], ["SYNONYM"], ["USER"], ["PROCEDURE"],
            ["TEMPORARY"], ["TEMP"], ["DROP"], ["TRUNCATE"], ["EXPLAIN"],
            ["VERBOSE"], ["DISTRIBUTION"], ["PLANTEXT"], ["PLANGRAPH"],
            ["ALTER"], ["SHOW"], ["COPY"], ["LOCK"], ["MERGE"], ["MATCHED"],
            ["REINDEX"], ["RESET"], ["EXTERNAL"], ["VIEWS"], ["VIEW"],
            ["COMMENT"], ["RENAME"], ["MODIFY"], ["PRIVILEGES"], ["DEFERRED"],
            ["MATCH"], ["ACTION"], ["WITHIN"], ["HISTORY"], ["CONFIGURATION"],
            ["SCHEDULER"], ["RULE"], ["WARNING"], ["COLUMN"], ["ADD"],
            ["CONSTRAINT"], ["PRIMARY"], ["KEY"], ["FOREIGN"], ["REFERENCES"],
            ["UNIQUE"], ["CHECK"], ["GLOBAL"], ["RETURNS"], ["LANGUAGE"],
            ["EXECUTE"], ["EXEC"], ["OWNER"], ["CALLER"], ["REFTABLE"],
            ["VARARGS"], ["VARRAY"], ["AUTOCOMMIT"],
            // CTE
            ["WITH"], ["FINAL"], ["RECURSIVE"],
            // Netezza-specific
            ["DISTRIBUTE"], ["RANDOM"], ["ORGANIZE"], ["GROOM"],
            ["VERSIONS"], ["RECORDS"], ["PAGES"], ["READY"], ["START"],
            ["RECLAIM"], ["BACKUPSET"], ["DEFAULT"], ["NONE"],
            ["GENERATE"], ["NEXT"], ["EXPRESS"], ["STATISTICS"], ["FOR"], ["OF"],
            // ORDER BY / FETCH
            ["ASC"], ["DESC"], ["FETCH"], ["FIRST"],
            // Quantified
            ["ANY"], ["SOME"],
            // Window
            ["OVER"], ["ROWS"], ["RANGE"], ["GROUPS"], ["CURRENT"], ["ROW"],
            ["UNBOUNDED"], ["PRECEDING"], ["FOLLOWING"], ["FILTER"],
            ["EXCLUDE"], ["TIES"],
            // Expressions
            ["EXTRACT"], ["CAST"],
            // GroupBy / OrderBy (multi-word)
            ["GROUP"], ["ORDER"], ["BY"], ["PARTITION"],
            // Additional
            ["MINUS"], ["ATSET"], ["@SET"],
            ["EXECUTE IMMEDIATE"]
        ];
        foreach (var g in groups)
        {
            var canonical = g[0];
            foreach (var alias in g)
                map[alias.ToUpperInvariant()] = canonical;
        }
        return map;
    }
}
