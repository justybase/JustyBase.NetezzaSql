using JustyBase.NetezzaSqlParser.Caching;

namespace JustyBase.NetezzaSql.Tests;

public sealed class NetezzaReferenceSyntaxTests
{
    public static IEnumerable<object[]> Statements =>
    [
        ["SELECT GROUP_CONCAT(name SEPARATOR ',') FROM users;"],
        ["SELECT GROUP_CONCAT_SORT(name ORDER BY name) FROM users;"],
        ["SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY amount) FROM sales;"],
        ["SELECT PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY amount) FROM sales;"],
        ["SELECT HASH4(customer_id), HASH8(customer_id) FROM customers;"],
        ["CREATE TABLE fact (id INTEGER) DISTRIBUTE ON HASH (id) ORGANIZE ON (id);"],
    ];

    [Theory]
    [MemberData(nameof(Statements))]
    public void ReferenceSyntax_ParsesWithoutDiagnostics(string sql)
    {
        using var runtime = new ParsingRuntime();
        var result = runtime.Parse(sql);

        var errors = string.Join("; ", result.Errors.Select(error => error.Code + ": " + error.Message));
        Assert.True(result.Valid, errors);
    }
}
