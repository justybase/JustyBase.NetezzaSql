using System.Text;

namespace JustyBase.NetezzaSql.Tests.Performance;

/// <summary>
/// Deterministic ~1 MB SQL fixtures for parser performance budgets. Fixtures are
/// generated in memory (stable seed, no locale dependence) so the repository
/// does not carry large SQL files.
/// </summary>
public static class PerfFixtureFactory
{
    /// <summary>Target fixture size in bytes.</summary>
    public const int TargetBytes = 1_048_576;

    public const string Ddl = "DDL";
    public const string Dml = "DML";
    public const string Complex = "Complex";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>Returns the generated fixture for <paramref name="kind"/> (DDL, DML or Complex).</summary>
    public static string Get(string kind)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(kind, out var cached))
                return cached;

            var sql = kind switch
            {
                Ddl => BuildDdlFixture(),
                Dml => BuildDmlFixture(),
                Complex => BuildComplexFixture(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown perf fixture kind.")
            };

            Cache[kind] = sql;
            return sql;
        }
    }

    private static string BuildDdlFixture()
    {
        var builder = new StringBuilder(TargetBytes + 4096);
        var index = 0;
        while (builder.Length < TargetBytes)
        {
            builder.Append("CREATE TABLE PERF_DDL.TABLE_").Append(index)
                .Append(" (ID INT4 NOT NULL, NAME VARCHAR(50), AMOUNT NUMERIC(12,2), CREATED_AT TIMESTAMP, STATUS CHAR(1));\n");
            builder.Append("ALTER TABLE PERF_DDL.TABLE_").Append(index)
                .Append(" ADD COLUMN EXTRA_").Append(index).Append(" VARCHAR(100);\n");
            builder.Append("COMMENT ON TABLE PERF_DDL.TABLE_").Append(index)
                .Append(" IS 'perf fixture ").Append(index).Append("';\n");
            index++;
        }

        return builder.ToString();
    }

    private static string BuildDmlFixture()
    {
        var builder = new StringBuilder(TargetBytes + 4096);
        var index = 0;
        while (builder.Length < TargetBytes)
        {
            builder.Append("INSERT INTO PERF_DML.TABLE_").Append(index)
                .Append(" (ID, NAME, AMOUNT, CREATED_AT, STATUS) VALUES (")
                .Append(index).Append(", 'name_").Append(index).Append("', ").Append(index)
                .Append(".25, DATE '2024-01-01', 'A');\n");
            builder.Append("UPDATE PERF_DML.TABLE_").Append(index)
                .Append(" SET NAME = 'updated_").Append(index)
                .Append("', AMOUNT = AMOUNT + 1 WHERE ID = ").Append(index).Append(";\n");
            builder.Append("DELETE FROM PERF_DML.TABLE_").Append(index)
                .Append(" WHERE ID = ").Append(index).Append(" AND STATUS = 'D';\n");
            builder.Append("INSERT INTO PERF_DML.SUMMARY_").Append(index)
                .Append(" (ID, TOTAL) SELECT ID, SUM(AMOUNT) FROM PERF_DML.TABLE_").Append(index)
                .Append(" GROUP BY ID;\n");
            index++;
        }

        return builder.ToString();
    }

    private static string BuildComplexFixture()
    {
        var builder = new StringBuilder(TargetBytes + 4096);
        var index = 0;
        while (builder.Length < TargetBytes)
        {
            builder.Append("WITH CTE_").Append(index).Append(" AS (SELECT ID, NAME, SUM(AMOUNT) OVER (PARTITION BY STATUS ORDER BY ID) AS RUNNING_TOTAL FROM PERF_CMP.TABLE_")
                .Append(index).Append(" WHERE CREATED_AT >= DATE '2024-01-01')\n");
            builder.Append("SELECT C.ID, CASE WHEN C.RUNNING_TOTAL > 1000 THEN 'HIGH' WHEN C.RUNNING_TOTAL > 100 THEN 'MID' ELSE 'LOW' END AS BUCKET, ")
                .Append("(SELECT COUNT(*) FROM PERF_CMP.DETAIL_").Append(index).Append(" D WHERE D.ID = C.ID) AS DETAIL_COUNT ")
                .Append("FROM CTE_").Append(index).Append(" C JOIN PERF_CMP.OTHER_").Append(index)
                .Append(" O ON O.ID = C.ID AND O.STATUS IN ('A', 'B') ")
                .Append("WHERE EXISTS (SELECT 1 FROM PERF_CMP.AUDIT_").Append(index)
                .Append(" A WHERE A.ID = C.ID) ORDER BY C.ID;\n");
            index++;
        }

        return builder.ToString();
    }
}
