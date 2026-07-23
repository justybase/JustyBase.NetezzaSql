namespace JustyBase.NetezzaSqlParser.Caching;

/// <summary>
/// Represents a single SQL statement boundary with content hash for change detection.
/// Port of statementIndex.ts from the reference TypeScript project.
/// </summary>
public readonly record struct StatementBoundary(
    int Index,
    int StartOffset,
    int EndOffset,
    string Sql,
    string ContentHash
);

/// <summary>
/// Index of all statements in a SQL document, with document-level content hash.
/// </summary>
public readonly record struct StatementIndex(
    string DocumentContentHash,
    IReadOnlyList<StatementBoundary> Statements
);

/// <summary>
/// Result of diffing two statement indexes — identifies which statements changed.
/// </summary>
public readonly record struct StatementIndexDiff(
    IReadOnlyList<int> DirtyIndices,
    int AffectedFromIndex
);

/// <summary>
/// Builds and diffs statement indexes for incremental validation.
/// Splits SQL text into individual statements by semicolons (respecting strings/comments/parens).
/// </summary>
public static class StatementIndexBuilder
{
    /// <summary>
    /// Build a statement index from full SQL text.
    /// </summary>
    public static StatementIndex BuildIndex(string fullSql)
    {
        var boundaries = SplitStatementsWithPositions(fullSql);
        var statements = boundaries.Select((b, i) => new StatementBoundary(
            i,
            b.start,
            b.end,
            fullSql[b.start..b.end],
            SimpleHash(fullSql[b.start..b.end])
        )).ToList();

        return new StatementIndex(SimpleHash(fullSql), statements);
    }

    /// <summary>
    /// Diff two statement indexes to find which statements need re-validation.
    /// Returns dirty indices and the first index affected by insertions/deletions.
    /// </summary>
    public static StatementIndexDiff DiffIndexes(StatementIndex? previous, StatementIndex next)
    {
        if (previous is null || previous.Value.Statements.Count == 0)
        {
            return new StatementIndexDiff(
                next.Statements.Select(s => s.Index).ToList(),
                0
            );
        }

        // Fast path: same content hash = no changes
        if (previous.Value.DocumentContentHash == next.DocumentContentHash)
        {
            return new StatementIndexDiff([], next.Statements.Count);
        }

        var prev = previous.Value;
        var dirty = new HashSet<int>();
        var maxLength = Math.Max(prev.Statements.Count, next.Statements.Count);
        var firstDirty = next.Statements.Count;

        for (int idx = 0; idx < maxLength; idx++)
        {
            var prevStmt = idx < prev.Statements.Count ? prev.Statements[idx] : (StatementBoundary?)null;
            var nextStmt = idx < next.Statements.Count ? next.Statements[idx] : (StatementBoundary?)null;

            if (nextStmt is null)
            {
                // Statement was removed
                firstDirty = Math.Min(firstDirty, idx);
                continue;
            }

            if (prevStmt is null || prevStmt.Value.ContentHash != nextStmt.Value.ContentHash)
            {
                dirty.Add(idx);
                firstDirty = Math.Min(firstDirty, idx);
            }
        }

        // If statement count changed, all remaining statements after firstDirty need re-validation
        // because semantic context may have shifted
        if (prev.Statements.Count != next.Statements.Count)
        {
            for (int idx = firstDirty; idx < next.Statements.Count; idx++)
            {
                dirty.Add(idx);
            }
        }

        return new StatementIndexDiff(
            dirty.OrderBy(i => i).ToList(),
            firstDirty
        );
    }

    /// <summary>
    /// Split SQL text into statement boundaries by semicolons,
    /// respecting string literals, comments, and balanced parentheses.
    /// </summary>
    private static List<(int start, int end)> SplitStatementsWithPositions(string sql)
    {
        var boundaries = new List<(int start, int end)>();
        if (string.IsNullOrEmpty(sql))
            return boundaries;

        int currentStart = 0;
        int parenDepth = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            // Track string literals
            if (inSingleQuote)
            {
                if (c == '\'' && next == '\'') { i++; } // escaped quote ''
                else if (c == '\'') inSingleQuote = false;
                continue;
            }
            if (inDoubleQuote)
            {
                if (c == '"') inDoubleQuote = false;
                continue;
            }

            // Track comments
            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (c == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }

            // Start comments/strings
            if (c == '-' && next == '-') { inLineComment = true; i++; continue; }
            if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }
            if (c == '\'') { inSingleQuote = true; continue; }
            if (c == '"') { inDoubleQuote = true; continue; }

            // Track parentheses (for subqueries in FROM etc.)
            if (c == '(') { parenDepth++; continue; }
            if (c == ')') { parenDepth--; continue; }

            // Semicolon at depth 0 = statement boundary
            if (c == ';' && parenDepth == 0)
            {
                var trimmed = sql[currentStart..i].Trim();
                if (trimmed.Length > 0)
                {
                    boundaries.Add((currentStart, i));
                }
                currentStart = i + 1;
            }
        }

        // Last statement (no trailing semicolon)
        var lastTrimmed = sql[currentStart..].Trim();
        if (lastTrimmed.Length > 0)
        {
            boundaries.Add((currentStart, sql.Length));
        }

        return boundaries;
    }

    /// <summary>
    /// Fast non-cryptographic hash for content comparison.
    /// Uses FNV-1a 64-bit hash.
    /// </summary>
    public static string SimpleHash(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        unchecked
        {
            ulong hash = 14695981039346656037; // FNV-1a offset basis
            foreach (char c in input)
            {
                hash ^= c;
                hash *= 1099511628211; // FNV-1a prime
            }
            return hash.ToString("x16");
        }
    }
}
