using JustyBase.ImportExport.Import;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Live proof: CSV → DatabaseTypeChooser.Infer → CREATE → pipe INSERT → SELECT equality.
/// Soft-skips without NZ_DEV_*; pipe topology soft-skips unless NZ_REQUIRE_PIPE=1.
/// </summary>
public sealed class NetezzaLiveImportRoundTripTests
{
    public static TheoryData<LiveImportCase> Cases()
    {
        var data = new TheoryData<LiveImportCase>
        {
            SimpleTypes(),
            NullableAndEmptyVarchar(),
            EscapingTabNewlineBackslash(),
            HardQuotedMultilineCsv(),
            AdversarialPayloads(),
            MixedColumnFallsBackToVarchar(),
            LeadingZerosStayVarchar()
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "Live")]
    public Task RoundTrip_csv_infer_create_insert_select(LiveImportCase importCase)
        => LiveImportRoundTripRunner.RunAsync(importCase);

    private static LiveImportCase SimpleTypes()
        => new(
            Name: "simple_types",
            CsvText: """
                     id,flag,amount,when_dt,label
                     1,true,1.50,2020-01-02 03:04:05,hello
                     2,false,2.00,2020-02-03 04:05:06,world
                     """,
            ColumnNames: ["id", "flag", "amount", "when_dt", "label"],
            ExpectedRows:
            [
                Row(("id", "1"), ("flag", "true"), ("amount", "1.50"), ("when_dt", "2020-01-02 03:04:05"), ("label", "hello")),
                Row(("id", "2"), ("flag", "false"), ("amount", "2.00"), ("when_dt", "2020-02-03 04:05:06"), ("label", "world"))
            ],
            ExpectedInferredTypes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "BIGINT",
                ["flag"] = "NVARCHAR(20)",
                ["amount"] = "NUMERIC(16,2)",
                ["when_dt"] = "DATETIME",
                ["label"] = "NVARCHAR(20)"
            });

    private static LiveImportCase NullableAndEmptyVarchar()
        => new(
            Name: "nullable_empty",
            CsvText: """
                     id,note
                     1,
                     2,present
                     """,
            ColumnNames: ["id", "note"],
            ExpectedRows:
            [
                Row(("id", "1"), ("note", null)),
                Row(("id", "2"), ("note", "present"))
            ],
            ExpectedInferredTypes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "BIGINT",
                ["note"] = "NVARCHAR(20)"
            },
            CsvOptions: new CsvImportOptions(HasHeader: true, NullValue: ""),
            NullValue: "");

    private static LiveImportCase EscapingTabNewlineBackslash()
        => new(
            Name: "escaping",
            CsvText: "id,txt\n1,\"contains\tdelimiter\nvalue\"\n2,\"a\\b\"\n",
            ColumnNames: ["id", "txt"],
            ExpectedRows:
            [
                Row(("id", "1"), ("txt", "contains\tdelimiter\nvalue")),
                Row(("id", "2"), ("txt", "a\\b"))
            ],
            ExpectedInferredTypes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "BIGINT",
                ["txt"] = "NVARCHAR(29)"
            });

    private static LiveImportCase HardQuotedMultilineCsv()
        => new(
            Name: "hard_quoted_csv",
            CsvText: "id,name,note\n1,\"Ada \"\"Lovelace\"\"\",\"line1\nline2\"\n2,\"Bob, Jr.\",plain\n",
            ColumnNames: ["id", "name", "note"],
            ExpectedRows:
            [
                Row(("id", "1"), ("name", "Ada \"Lovelace\""), ("note", "line1\nline2")),
                Row(("id", "2"), ("name", "Bob, Jr."), ("note", "plain"))
            ],
            ExpectedInferredTypes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "BIGINT",
                ["name"] = "NVARCHAR(20)",
                ["note"] = "NVARCHAR(20)"
            });

    private static LiveImportCase AdversarialPayloads()
        => new(
            Name: "adversarial",
            CsvText: """
                     id,payload
                     1,'; DROP TABLE x;--
                     2,-- comment
                     3,<script>alert(1)</script>
                     4,emoji ✅ café
                     """,
            ColumnNames: ["id", "payload"],
            ExpectedRows:
            [
                Row(("id", "1"), ("payload", "'; DROP TABLE x;--")),
                Row(("id", "2"), ("payload", "-- comment")),
                Row(("id", "3"), ("payload", "<script>alert(1)</script>")),
                Row(("id", "4"), ("payload", "emoji ✅ café"))
            ],
            ExpectedInferredTypes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "BIGINT",
                ["payload"] = "NVARCHAR(30)"
            });

    private static LiveImportCase MixedColumnFallsBackToVarchar()
        => new(
            Name: "mixed_falls_back_varchar",
            CsvText: """
                     id,mixed
                     1,1
                     2,2
                     3,x
                     """,
            ColumnNames: ["id", "mixed"],
            ExpectedRows:
            [
                Row(("id", "1"), ("mixed", "1")),
                Row(("id", "2"), ("mixed", "2")),
                Row(("id", "3"), ("mixed", "x"))
            ],
            ExpectedInferredTypes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "BIGINT",
                ["mixed"] = "NVARCHAR(20)"
            });

    private static LiveImportCase LeadingZerosStayVarchar()
        => new(
            Name: "leading_zeros_varchar",
            CsvText: """
                     id,code
                     1,001
                     2,002
                     3,-5
                     """,
            ColumnNames: ["id", "code"],
            ExpectedRows:
            [
                Row(("id", "1"), ("code", "001")),
                Row(("id", "2"), ("code", "002")),
                Row(("id", "3"), ("code", "-5"))
            ],
            ExpectedInferredTypes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "BIGINT",
                ["code"] = "NVARCHAR(20)"
            });

    private static IReadOnlyDictionary<string, string?> Row(params (string Key, string? Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
}
