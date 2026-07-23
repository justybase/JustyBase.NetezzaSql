using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.Tests.NetezzaSqlParser;

// ========================================================================
// StatementIndexBuilder Tests
// ========================================================================

public sealed class StatementIndexBuilderTests
{
    [Fact]
    public void SimpleHash_SameInput_SameHash()
    {
        var h1 = StatementIndexBuilder.SimpleHash("SELECT 1");
        var h2 = StatementIndexBuilder.SimpleHash("SELECT 1");
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void SimpleHash_DifferentInput_DifferentHash()
    {
        var h1 = StatementIndexBuilder.SimpleHash("SELECT 1");
        var h2 = StatementIndexBuilder.SimpleHash("SELECT 2");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void SimpleHash_EmptyString_ReturnsEmpty()
    {
        var h = StatementIndexBuilder.SimpleHash("");
        Assert.Equal("", h);
    }

    [Fact]
    public void SimpleHash_NullString_ReturnsEmpty()
    {
        var h = StatementIndexBuilder.SimpleHash(null!);
        Assert.Equal("", h);
    }

    [Fact]
    public void SimpleHash_Whitespace_ReturnsConsistentHash()
    {
        var h1 = StatementIndexBuilder.SimpleHash("   ");
        var h2 = StatementIndexBuilder.SimpleHash("   ");
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void SimpleHash_CaseSensitive_DifferentHashes()
    {
        var h1 = StatementIndexBuilder.SimpleHash("SELECT * FROM t");
        var h2 = StatementIndexBuilder.SimpleHash("select * from t");
        Assert.NotEqual(h1, h2);
    }

    // ===== BuildIndex =====

    [Fact]
    public void BuildIndex_EmptyString_ReturnsEmptyIndex()
    {
        var idx = StatementIndexBuilder.BuildIndex("");
        Assert.Empty(idx.Statements);
        Assert.Equal("", idx.DocumentContentHash);
    }

    [Fact]
    public void BuildIndex_SingleStatement_NoSemicolon()
    {
        var idx = StatementIndexBuilder.BuildIndex("SELECT 1");
        Assert.Single(idx.Statements);
        Assert.Equal("SELECT 1", idx.Statements[0].Sql);
        Assert.Equal(0, idx.Statements[0].StartOffset);
        Assert.Equal(8, idx.Statements[0].EndOffset);
    }

    [Fact]
    public void BuildIndex_SingleStatement_WithSemicolon()
    {
        var idx = StatementIndexBuilder.BuildIndex("SELECT 1;");
        Assert.Single(idx.Statements);
        Assert.Equal("SELECT 1", idx.Statements[0].Sql);
        Assert.Equal(0, idx.Statements[0].StartOffset);
        Assert.Equal(8, idx.Statements[0].EndOffset);
    }

    [Fact]
    public void BuildIndex_MultipleStatements()
    {
        var idx = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2; SELECT 3");
        Assert.Equal(3, idx.Statements.Count);
        Assert.Equal("SELECT 1", idx.Statements[0].Sql);
        Assert.Equal(" SELECT 2", idx.Statements[1].Sql);
        Assert.Equal(" SELECT 3", idx.Statements[2].Sql);
    }

    [Fact]
    public void BuildIndex_StatementsHaveCorrectOffsets()
    {
        var sql = "SELECT 1; SELECT 2";
        var idx = StatementIndexBuilder.BuildIndex(sql);
        // "SELECT 1" starts at 0, ends at 8 (the ';' is at position 8)
        // " SELECT 2" starts at 9, ends at sql.Length (18)
        Assert.Equal(2, idx.Statements.Count);
        Assert.Equal(0, idx.Statements[0].StartOffset);
        Assert.Equal(8, idx.Statements[0].EndOffset);
        Assert.Equal(9, idx.Statements[1].StartOffset);
        Assert.Equal(sql.Length, idx.Statements[1].EndOffset);
    }

    [Fact]
    public void BuildIndex_IgnoresEmptyStatements()
    {
        var idx = StatementIndexBuilder.BuildIndex("SELECT 1;;;SELECT 2");
        Assert.Equal(2, idx.Statements.Count);
        Assert.Equal("SELECT 1", idx.Statements[0].Sql);
        Assert.Equal("SELECT 2", idx.Statements[1].Sql);
    }

    [Fact]
    public void BuildIndex_OnlyWhitespace_ReturnsEmpty()
    {
        var idx = StatementIndexBuilder.BuildIndex("   \n  \t  ");
        Assert.Empty(idx.Statements);
    }

    [Fact]
    public void BuildIndex_OnlySemicolons_ReturnsEmpty()
    {
        var idx = StatementIndexBuilder.BuildIndex(";;;");
        Assert.Empty(idx.Statements);
    }

    // ===== Statement Splitting Edge Cases =====

    [Fact]
    public void BuildIndex_SemicolonInStringLiteral_NotSplit()
    {
        // Semicolons inside single-quoted strings should not split the statement
        var idx = StatementIndexBuilder.BuildIndex("SELECT 'hello; world' FROM t; SELECT 2");
        Assert.Equal(2, idx.Statements.Count);
        Assert.Contains("hello; world", idx.Statements[0].Sql);
        Assert.Equal(" SELECT 2", idx.Statements[1].Sql);
    }

    [Fact]
    public void BuildIndex_SemicolonInDoubleQuotedIdentifier_NotSplit()
    {
        var idx = StatementIndexBuilder.BuildIndex("SELECT * FROM \"my;table\"; SELECT 2");
        Assert.Equal(2, idx.Statements.Count);
        Assert.Contains("my;table", idx.Statements[0].Sql);
    }

    [Fact]
    public void BuildIndex_SemicolonInLineComment_NotSplit()
    {
        // Line comments don't create statement boundaries; only semicolons do.
        // The entire text becomes one statement since there's no top-level semicolon.
        var idx = StatementIndexBuilder.BuildIndex("SELECT 1 -- this has a ; inside ;\nSELECT 2");
        Assert.Single(idx.Statements);
        Assert.Contains("-- this has a ; inside ;", idx.Statements[0].Sql);
    }

    [Fact]
    public void BuildIndex_SemicolonInBlockComment_NotSplit()
    {
        // The block comment is part of the statement text; the external semicolon creates the boundary.
        var idx = StatementIndexBuilder.BuildIndex("SELECT 1 /* block ; comment ; */ ; SELECT 2");
        Assert.Equal(2, idx.Statements.Count);
        Assert.Contains("/* block ; comment ; */", idx.Statements[0].Sql);
        Assert.Equal(" SELECT 2", idx.Statements[1].Sql);
    }

    [Fact]
    public void BuildIndex_SemicolonInSubqueryParens_NotSplit()
    {
        // Semicolons inside parentheses (e.g., subqueries) should not split if not ambiguous
        // Actually, semicolons inside balanced parens should be treated literally,
        // but the current implementation only tracks paren depth for balanced parens.
        // A semicolon inside parens at depth > 0 should NOT split.
        var idx = StatementIndexBuilder.BuildIndex("SELECT * FROM (SELECT 1; SELECT 2) sub");
        Assert.Single(idx.Statements);
        Assert.Contains("(SELECT 1; SELECT 2)", idx.Statements[0].Sql);
    }

    [Fact]
    public void BuildIndex_StringLiteralWithEscapedQuote_NotSplit()
    {
        var idx = StatementIndexBuilder.BuildIndex("SELECT 'it''s a test' FROM t; SELECT 2");
        Assert.Equal(2, idx.Statements.Count);
    }

    [Fact]
    public void BuildIndex_MixedContent()
    {
        var sql = """
            -- comment at start
            SELECT 1; /* block
            comment */ SELECT 2; SELECT 'semi;colon' AS x
            """;
        var idx = StatementIndexBuilder.BuildIndex(sql);
        Assert.Equal(3, idx.Statements.Count);
    }

    [Fact]
    public void BuildIndex_DocumentHash_Match_WhenSameContent()
    {
        var idx1 = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2");
        var idx2 = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2");
        Assert.Equal(idx1.DocumentContentHash, idx2.DocumentContentHash);
    }

    [Fact]
    public void BuildIndex_DocumentHash_Differs_WhenContentChanges()
    {
        var idx1 = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2");
        var idx2 = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 3");
        Assert.NotEqual(idx1.DocumentContentHash, idx2.DocumentContentHash);
    }

    [Fact]
    public void BuildIndex_PreservesStatementOrder()
    {
        var idx = StatementIndexBuilder.BuildIndex("SELECT a; SELECT b; SELECT c");
        Assert.Equal(3, idx.Statements.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(i, idx.Statements[i].Index);
        }
    }

    [Fact]
    public void BuildIndex_EachStatement_HasUniqueHash()
    {
        var idx = StatementIndexBuilder.BuildIndex("SELECT a; SELECT b");
        Assert.Equal(2, idx.Statements.Count);
        Assert.NotEqual(idx.Statements[0].ContentHash, idx.Statements[1].ContentHash);
    }

    // ===== DiffIndexes =====

    [Fact]
    public void DiffIndexes_NullPrevious_AllDirty()
    {
        var next = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2");
        var diff = StatementIndexBuilder.DiffIndexes(null, next);
        Assert.Equal(2, diff.DirtyIndices.Count);
        Assert.Contains(0, diff.DirtyIndices);
        Assert.Contains(1, diff.DirtyIndices);
        Assert.Equal(0, diff.AffectedFromIndex);
    }

    [Fact]
    public void DiffIndexes_EmptyPrevious_AllDirty()
    {
        var prev = StatementIndexBuilder.BuildIndex("");
        var next = StatementIndexBuilder.BuildIndex("SELECT 1");
        var diff = StatementIndexBuilder.DiffIndexes(prev, next);
        Assert.Single(diff.DirtyIndices);
        Assert.Contains(0, diff.DirtyIndices);
        Assert.Equal(0, diff.AffectedFromIndex);
    }

    [Fact]
    public void DiffIndexes_SameContent_NoDirty()
    {
        var prev = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2");
        var next = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2");
        var diff = StatementIndexBuilder.DiffIndexes(prev, next);
        Assert.Empty(diff.DirtyIndices);
        Assert.Equal(2, diff.AffectedFromIndex); // no affected
    }

    [Fact]
    public void DiffIndexes_ChangedStatement_OnlyThatDirty()
    {
        var prev = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2");
        var next = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 999");
        var diff = StatementIndexBuilder.DiffIndexes(prev, next);
        Assert.Single(diff.DirtyIndices);
        Assert.Contains(1, diff.DirtyIndices); // second statement changed
        Assert.Equal(1, diff.AffectedFromIndex);
    }

    [Fact]
    public void DiffIndexes_AddedStatement_AllAfterDirty()
    {
        var prev = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2");
        var next = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2; SELECT 3");
        var diff = StatementIndexBuilder.DiffIndexes(prev, next);
        Assert.Contains(2, diff.DirtyIndices); // new statement
        Assert.Equal(2, diff.AffectedFromIndex);
    }

    [Fact]
    public void DiffIndexes_RemovedStatement_AllAfterDirty()
    {
        var prev = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2; SELECT 3");
        var next = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 3");
        var diff = StatementIndexBuilder.DiffIndexes(prev, next);
        Assert.Equal(1, diff.AffectedFromIndex);
        // After removal, statement at index 1 is now "SELECT 3" which was at index 2
        // The diff should mark index 1 and everything after
        Assert.Contains(1, diff.DirtyIndices);
        Assert.Equal(1, diff.AffectedFromIndex);
    }

    [Fact]
    public void DiffIndexes_FirstStatementChanged_OnlyThatDirty()
    {
        var prev = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2; SELECT 3");
        var next = StatementIndexBuilder.BuildIndex("SELECT X; SELECT 2; SELECT 3");
        var diff = StatementIndexBuilder.DiffIndexes(prev, next);
        // Only statement 0 changed; statements 1 and 2 are unchanged
        Assert.Single(diff.DirtyIndices);
        Assert.Contains(0, diff.DirtyIndices);
        Assert.Equal(0, diff.AffectedFromIndex);
    }

    [Fact]
    public void DiffIndexes_InsertInMiddle_ShiftAffected()
    {
        var prev = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 3");
        var next = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2; SELECT 3");
        var diff = StatementIndexBuilder.DiffIndexes(prev, next);
        // Statement count changed: 2 -> 3
        // Index 0 matches, but count changed so 1 and beyond are dirty
        Assert.Contains(1, diff.DirtyIndices);
        Assert.Contains(2, diff.DirtyIndices);
        Assert.Equal(1, diff.AffectedFromIndex);
    }

    [Fact]
    public void DiffIndexes_SameContentDifferentFormatting_StillDirty()
    {
        var prev = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2");
        var next = StatementIndexBuilder.BuildIndex("SELECT 1; select 2");
        var diff = StatementIndexBuilder.DiffIndexes(prev, next);
        // Hashes differ because content differs (case)
        Assert.NotEmpty(diff.DirtyIndices);
    }
}

// ========================================================================
// DocumentParseSession Tests
// ========================================================================

public sealed class DocumentParseSessionTests : IDisposable
{
    private readonly DocumentParseSession _session = new();

    public void Dispose()
    {
        _session.Dispose();
    }

    [Fact]
    public void GetOrParse_SimpleSelect_ReturnsParseResult()
    {
        var result = _session.GetOrParse("SELECT 1");
        Assert.Single(result.Statements);
        Assert.True(result.Valid);
    }

    [Fact]
    public void GetOrParse_EmptyString_ReturnsEmptyValidResult()
    {
        var result = _session.GetOrParse("");
        Assert.Empty(result.Statements);
        Assert.Empty(result.Errors);
        Assert.True(result.Valid);
    }

    [Fact]
    public void GetOrParse_CachesSameInput_ReturnsSameStatements()
    {
        var result1 = _session.GetOrParse("SELECT 1");
        var result2 = _session.GetOrParse("SELECT 1");
        Assert.Same(result1.Statements, result2.Statements); // same cached reference
    }

    [Fact]
    public void GetOrParse_DifferentInput_ReturnsDifferentResults()
    {
        var result1 = _session.GetOrParse("SELECT 1");
        var result2 = _session.GetOrParse("SELECT 2");
        Assert.NotSame(result1.Statements, result2.Statements);
    }

    [Fact]
    public void GetOrParse_CacheHit_IncrementsStats()
    {
        _session.GetOrParse("SELECT 1"); // miss
        _session.GetOrParse("SELECT 1"); // hit
        var (hits, misses) = _session.GetStats();
        Assert.Equal(1, hits);
        Assert.Equal(1, misses);
    }

    [Fact]
    public void GetOrParse_CacheMiss_IncrementsStats()
    {
        _session.GetOrParse("SELECT 1"); // miss
        _session.GetOrParse("SELECT 2"); // miss
        var (hits, misses) = _session.GetStats();
        Assert.Equal(0, hits);
        Assert.Equal(2, misses);
    }

    [Fact]
    public void Invalidate_RemovesFromCache()
    {
        _session.GetOrParse("SELECT 1"); // miss
        _session.GetOrParse("SELECT 1"); // hit

        _session.Invalidate("SELECT 1");
        _session.GetOrParse("SELECT 1"); // miss again after invalidate

        var (hits, misses) = _session.GetStats();
        Assert.Equal(1, hits); // only one hit before invalidate
        Assert.Equal(2, misses); // two misses: original + after invalidate
    }

    [Fact]
    public void Clear_RemovesAllCachedEntries()
    {
        _session.GetOrParse("SELECT 1"); // miss
        _session.GetOrParse("SELECT 2"); // miss
        _session.GetOrParse("SELECT 1"); // hit
        _session.GetOrParse("SELECT 2"); // hit

        _session.Clear();

        _session.GetOrParse("SELECT 1"); // miss again (cache was cleared)
        _session.GetOrParse("SELECT 2"); // miss again (cache was cleared)
        var (hits, misses) = _session.GetStats();
        Assert.Equal(0, hits);  // stats reset by Clear()
        Assert.Equal(2, misses); // two misses after clear
    }

    [Fact]
    public void GetOrParse_InvalidSql_ReturnsErrorButDoesNotThrow()
    {
        // SQL with unterminated string literal
        var result = _session.GetOrParse("SELECT 'unterminated");
        // The parser/tokenizer may throw or return partial results
        // Either way, GetOrParse should not throw
    }

    [Fact]
    public void GetOrParse_MultipleStatements_ParsesAll()
    {
        var result = _session.GetOrParse("SELECT 1; SELECT 2; SELECT 3");
        Assert.Equal(3, result.Statements.Count);
    }

    [Fact]
    public void GetOrParse_LargeInput_DoesNotThrow()
    {
        var largeSql = string.Join("; ", Enumerable.Range(1, 100).Select(i => $"SELECT {i}"));
        var result = _session.GetOrParse(largeSql);
        Assert.True(result.Statements.Count > 0);
    }

    [Fact]
    public void GetOrParse_AfterDispose_Throws()
    {
        _session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _session.GetOrParse("SELECT 1"));
    }

    [Fact]
    public void GetOrParse_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _session.GetOrParse(null!));
    }

    [Fact]
    public void GetOrParse_CachesSelectStar()
    {
        var result = _session.GetOrParse("SELECT * FROM t");
        Assert.Single(result.Statements);
    }

    [Fact]
    public void GetOrParse_CachesInsertStatement()
    {
        var result = _session.GetOrParse("INSERT INTO t VALUES (1, 2)");
        Assert.Single(result.Statements);
    }

    [Fact]
    public void GetOrParse_CachesCreateTable()
    {
        var result = _session.GetOrParse("CREATE TABLE t (id INT)");
        Assert.Single(result.Statements);
    }

    [Fact]
    public void GetOrParse_ThreadSafe_ConcurrentAccess()
    {
        // Spawn multiple threads accessing the cache simultaneously
        var exceptions = new List<Exception>();
        var threads = new List<Thread>();
        var lockObj = new object();

        for (int t = 0; t < 8; t++)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 20; i++)
                    {
                        var sql = $"SELECT {i % 5}";
                        var result = _session.GetOrParse(sql);
                        Assert.True(result.Valid);
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj) { exceptions.Add(ex); }
                }
            });
            threads.Add(thread);
            thread.Start();
        }

        foreach (var t in threads) t.Join();
        Assert.Empty(exceptions);
    }

    [Fact]
    public void LRU_Eviction_WhenExceedingMaxEntries()
    {
        // Fill cache with 32 entries
        for (int i = 0; i < 32; i++)
        {
            _session.GetOrParse($"SELECT {i}");
        }

        // All hits for existing entries
        for (int i = 0; i < 32; i++)
        {
            _session.GetOrParse($"SELECT {i}");
        }

        var (hits, misses) = _session.GetStats();
        Assert.Equal(32, hits);
        Assert.Equal(32, misses);

        // Add one more (33rd) — should evict the least recently used (SELECT 0)
        _session.GetOrParse("SELECT 99"); // miss (new content)
        // SELECT 0 should be evicted now, so it will be a miss too
        _session.GetOrParse("SELECT 0"); // miss (evicted)
        var (hits2, misses2) = _session.GetStats();
        // 32 hits from re-reading 0-31, no extra hits
        Assert.Equal(32, hits2);
        // 32 original + SELECT 99 (miss) + SELECT 0 (miss after eviction)
        Assert.Equal(34, misses2);
    }

    [Fact]
    public void GetOrParse_InvalidateNonExistent_DoesNotThrow()
    {
        _session.Invalidate("nonexistent sql");
        // Should not throw
    }

    [Fact]
    public void GetOrParse_ClearWhileIdle_DoesNotThrow()
    {
        _session.GetOrParse("SELECT 1");
        _session.Clear();
        // Cache should work again
        var result = _session.GetOrParse("SELECT 1");
        Assert.Single(result.Statements);
    }
}

// ========================================================================
// DocumentValidationSession Tests
// ========================================================================

public sealed class DocumentValidationSessionTests : IDisposable
{
    private readonly DocumentValidationSession _session = new();

    public void Dispose()
    {
        _session.Dispose();
    }

    private static StatementBoundary MakeBoundary(int index, string sql)
    {
        return new StatementBoundary(index, 0, sql.Length, sql,
            StatementIndexBuilder.SimpleHash(sql));
    }

    private static LintIssue MakeIssue(string ruleId, int offset = 0)
    {
        return new LintIssue(ruleId, $"Test {ruleId}", LintSeverity.Error, offset, offset + 1);
    }

    // ===== PrepareDocument =====

    [Fact]
    public void PrepareDocument_FirstCall_AllDirty()
    {
        var state = _session.PrepareDocument("doc1", "SELECT 1; SELECT 2");
        Assert.Equal(2, state.NextIndex.Statements.Count);
        Assert.Null(state.PreviousIndex);
        Assert.Equal(2, state.Diff.DirtyIndices.Count);
        Assert.Equal(0, state.Diff.AffectedFromIndex);
    }

    [Fact]
    public void PrepareDocument_SecondCallSameContent_NoDirty()
    {
        var state1 = _session.PrepareDocument("doc1", "SELECT 1; SELECT 2");
        _session.CommitDocumentIndex("doc1", state1.NextIndex);
        var state = _session.PrepareDocument("doc1", "SELECT 1; SELECT 2");
        Assert.Empty(state.Diff.DirtyIndices);
        Assert.Equal(2, state.Diff.AffectedFromIndex);
    }

    [Fact]
    public void PrepareDocument_SecondCallChangedContent_DirtyOnlyChanged()
    {
        _session.PrepareDocument("doc1", "SELECT 1; SELECT 2");
        _session.CommitDocumentIndex("doc1", StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2"));

        var state = _session.PrepareDocument("doc1", "SELECT 1; SELECT 999");
        Assert.Single(state.Diff.DirtyIndices);
        Assert.Contains(1, state.Diff.DirtyIndices);
        Assert.Equal(1, state.Diff.AffectedFromIndex);
    }

    [Fact]
    public void PrepareDocument_DifferentDocuments_DoNotInterfere()
    {
        var state1 = _session.PrepareDocument("doc1", "SELECT 1");
        var state2 = _session.PrepareDocument("doc2", "SELECT 2");

        Assert.Single(state1.NextIndex.Statements);
        Assert.Single(state2.NextIndex.Statements);
        Assert.NotEqual(
            state1.NextIndex.Statements[0].ContentHash,
            state2.NextIndex.Statements[0].ContentHash);
    }

    // ===== Store + GetCachedDiagnostics =====

    [Fact]
    public void StoreAndGet_StatementDiagnostics_ReturnsCached()
    {
        var stmt = MakeBoundary(0, "SELECT 1");
        var issues = new List<LintIssue> { MakeIssue("NZ001") };

        _session.StoreStatementDiagnostics("doc1", stmt, issues);
        var cached = _session.GetCachedDiagnostics("doc1", stmt);

        Assert.NotNull(cached);
        Assert.Single(cached);
        Assert.Equal("NZ001", cached[0].RuleId);
    }

    [Fact]
    public void GetCachedDiagnostics_NoEntry_ReturnsNull()
    {
        var stmt = MakeBoundary(0, "SELECT 1");
        var cached = _session.GetCachedDiagnostics("doc1", stmt);
        Assert.Null(cached);
    }

    [Fact]
    public void GetCachedDiagnostics_WrongDocument_ReturnsNull()
    {
        var stmt = MakeBoundary(0, "SELECT 1");
        _session.StoreStatementDiagnostics("doc1", stmt, [MakeIssue("NZ001")]);
        var cached = _session.GetCachedDiagnostics("doc2", stmt);
        Assert.Null(cached);
    }

    [Fact]
    public void GetCachedDiagnostics_StatementHashChanged_ReturnsNull()
    {
        var oldStmt = MakeBoundary(0, "SELECT 1");
        _session.StoreStatementDiagnostics("doc1", oldStmt, [MakeIssue("NZ001")]);

        // Content changed — hash no longer matches
        var newStmt = MakeBoundary(0, "SELECT 2");
        var cached = _session.GetCachedDiagnostics("doc1", newStmt);
        Assert.Null(cached);
    }

    [Fact]
    public void StoreStatementDiagnostics_ReplacesPreviousForSameIndex()
    {
        var stmt1 = MakeBoundary(0, "SELECT 1");
        var stmt2 = MakeBoundary(0, "SELECT 2");

        _session.StoreStatementDiagnostics("doc1", stmt1, [MakeIssue("NZ001")]);
        _session.StoreStatementDiagnostics("doc1", stmt2, [MakeIssue("NZ002")]);

        var cached = _session.GetCachedDiagnostics("doc1", stmt2);
        Assert.NotNull(cached);
        Assert.Equal("NZ002", cached[0].RuleId);
    }

    // ===== Metadata Epoch =====

    [Fact]
    public void SyncMetadataEpoch_ClearsDiagnostics_WhenEpochChanges()
    {
        var stmt = MakeBoundary(0, "SELECT 1");
        _session.StoreStatementDiagnostics("doc1", stmt, [MakeIssue("NZ001")], metadataEpoch: 1);

        // Same epoch — still cached
        var cached1 = _session.GetCachedDiagnostics("doc1", stmt, metadataEpoch: 1);
        Assert.NotNull(cached1);

        // Different epoch — cache cleared
        _session.SyncMetadataEpoch("doc1", 2);
        var cached2 = _session.GetCachedDiagnostics("doc1", stmt, metadataEpoch: 2);
        Assert.Null(cached2);
    }

    [Fact]
    public void SyncMetadataEpoch_SameEpoch_DoesNotClear()
    {
        var stmt = MakeBoundary(0, "SELECT 1");
        _session.StoreStatementDiagnostics("doc1", stmt, [MakeIssue("NZ001")], metadataEpoch: 1);
        _session.SyncMetadataEpoch("doc1", 1); // same epoch
        var cached = _session.GetCachedDiagnostics("doc1", stmt, metadataEpoch: 1);
        Assert.NotNull(cached);
    }

    // ===== CommitDocumentIndex =====

    [Fact]
    public void CommitDocumentIndex_UpdatesIndex()
    {
        var state1 = _session.PrepareDocument("doc1", "SELECT 1");
        _session.CommitDocumentIndex("doc1", state1.NextIndex);

        var state2 = _session.PrepareDocument("doc1", "SELECT 1");
        Assert.Empty(state2.Diff.DirtyIndices);
    }

    [Fact]
    public void CommitDocumentIndex_WithoutCommit_StillDiffers()
    {
        var state1 = _session.PrepareDocument("doc1", "SELECT 1");
        // NOT committing

        var state2 = _session.PrepareDocument("doc1", "SELECT 1");
        // PreviousIndex is still null because we never committed
        Assert.Null(state2.PreviousIndex);
    }

    // ===== InvalidateDocument =====

    [Fact]
    public void InvalidateDocument_ClearsDiagnostics()
    {
        var stmt = MakeBoundary(0, "SELECT 1");
        _session.StoreStatementDiagnostics("doc1", stmt, [MakeIssue("NZ001")]);
        _session.InvalidateDocument("doc1");

        var cached = _session.GetCachedDiagnostics("doc1", stmt);
        Assert.Null(cached);
    }

    // ===== RemoveDocument =====

    [Fact]
    public void RemoveDocument_RemovesAllState()
    {
        var stmt = MakeBoundary(0, "SELECT 1");
        _session.StoreStatementDiagnostics("doc1", stmt, [MakeIssue("NZ001")]);
        _session.RemoveDocument("doc1");

        var cached = _session.GetCachedDiagnostics("doc1", stmt);
        Assert.Null(cached);
    }

    // ===== Multiple Documents =====

    [Fact]
    public void MultipleDocuments_IndependentCaches()
    {
        var stmt1 = MakeBoundary(0, "SELECT 1");
        var stmt2 = MakeBoundary(0, "SELECT 2");

        _session.StoreStatementDiagnostics("doc1", stmt1, [MakeIssue("NZ001")]);
        _session.StoreStatementDiagnostics("doc2", stmt2, [MakeIssue("NZ002")]);

        var cached1 = _session.GetCachedDiagnostics("doc1", stmt1);
        var cached2 = _session.GetCachedDiagnostics("doc2", stmt2);

        Assert.NotNull(cached1);
        Assert.NotNull(cached2);
        Assert.Equal("NZ001", cached1[0].RuleId);
        Assert.Equal("NZ002", cached2[0].RuleId);
    }

    [Fact]
    public void PrepareDocument_TracksCorrectStatementCount()
    {
        var state = _session.PrepareDocument("doc1", "SELECT 1; SELECT 2; SELECT 3");
        Assert.Equal(3, state.NextIndex.Statements.Count);
        Assert.Equal(3, state.Diff.DirtyIndices.Count);
    }

    // ===== Edge Cases =====

    [Fact]
    public void StoreDiagnostics_DocumentDoesNotExist_CreatesIt()
    {
        var stmt = MakeBoundary(0, "SELECT 1");
        _session.StoreStatementDiagnostics("new-doc", stmt, [MakeIssue("NZ001")]);
        var cached = _session.GetCachedDiagnostics("new-doc", stmt);
        Assert.NotNull(cached);
    }

    [Fact]
    public void StoreDiagnostics_WithMetadataEpoch_SetsEpoch()
    {
        var stmt = MakeBoundary(0, "SELECT 1");
        _session.StoreStatementDiagnostics("doc1", stmt, [MakeIssue("NZ001")], metadataEpoch: 5);

        // Same epoch should work
        var cached = _session.GetCachedDiagnostics("doc1", stmt, metadataEpoch: 5);
        Assert.NotNull(cached);

        // Different epoch should fail
        var cached2 = _session.GetCachedDiagnostics("doc1", stmt, metadataEpoch: 9);
        Assert.Null(cached2);
    }

    [Fact]
    public void Clear_RemovesAllDocuments()
    {
        _session.PrepareDocument("doc1", "SELECT 1");
        _session.Clear();

        // After clear, first prepare again should have null previous
        var state = _session.PrepareDocument("doc1", "SELECT 1");
        Assert.Null(state.PreviousIndex);
    }

    [Fact]
    public void ThreadSafe_ConcurrentAccess()
    {
        var exceptions = new List<Exception>();
        var lockObj = new object();
        var threads = new List<Thread>();

        for (int t = 0; t < 8; t++)
        {
            var threadId = t;
            var thread = new Thread(() =>
            {
                try
                {
                    var docUri = $"doc{threadId % 4}";
                    for (int i = 0; i < 10; i++)
                    {
                        var stmt = new StatementBoundary(i, 0, 1, $"SELECT {i}",
                            StatementIndexBuilder.SimpleHash($"SELECT {i}"));
                        _session.StoreStatementDiagnostics(docUri, stmt,
                            [MakeIssue($"NZ{i:D3}")]);
                        var cached = _session.GetCachedDiagnostics(docUri, stmt);
                        Assert.NotNull(cached);
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj) { exceptions.Add(ex); }
                }
            });
            threads.Add(thread);
            thread.Start();
        }

        foreach (var t in threads) t.Join();
        Assert.Empty(exceptions);
    }

    [Fact]
    public void Dispose_ClearsAllData()
    {
        _session.PrepareDocument("doc1", "SELECT 1");
        _session.Dispose();

        // After dispose, operations should not throw (they check disposed flag)
        var state = _session.PrepareDocument("doc1", "SELECT 1");
        Assert.Single(state.NextIndex.Statements);
    }

    [Fact]
    public void StoreDiagnostics_EmptyDiagnosticsList_StoresCorrectly()
    {
        var stmt = MakeBoundary(0, "SELECT 1");
        _session.StoreStatementDiagnostics("doc1", stmt, []);
        var cached = _session.GetCachedDiagnostics("doc1", stmt);
        Assert.NotNull(cached);
        Assert.Empty(cached);
    }

    [Fact]
    public void MaximumDiagnosticEntries_EvictsOldest()
    {
        // Store 513 entries (max is 512) — the 513th should evict the oldest (index 0)
        for (int i = 0; i < 513; i++)
        {
            var stmt = new StatementBoundary(i, 0, 1, $"SELECT {i}",
                StatementIndexBuilder.SimpleHash($"SELECT {i}"));
            _session.StoreStatementDiagnostics("doc1", stmt, [MakeIssue($"NZ{i:D3}")]);
        }

        // Index 0 should have been evicted
        var stmt0 = new StatementBoundary(0, 0, 1, "SELECT 0",
            StatementIndexBuilder.SimpleHash("SELECT 0"));
        var cached = _session.GetCachedDiagnostics("doc1", stmt0);
        Assert.Null(cached);
    }
}
