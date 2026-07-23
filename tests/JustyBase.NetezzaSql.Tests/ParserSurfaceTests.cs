using JustyBase.NetezzaSqlParser.Caching;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ParserSurfaceTests
{
    public static IEnumerable<object[]> Statements =>
    [
        ["SELECT id FROM orders WHERE id = 1;"],
        ["WITH c AS (SELECT id FROM orders) SELECT id FROM c;"],
        ["INSERT INTO orders (id) VALUES (1);"],
        ["UPDATE orders SET id = 2 WHERE id = 1;"],
        ["DELETE FROM orders WHERE id = 1;"],
        ["MERGE INTO orders USING source ON orders.id = source.id WHEN MATCHED THEN UPDATE SET id = source.id;"],
        ["CREATE TABLE orders (id INTEGER) DISTRIBUTE ON RANDOM;"],
        ["CREATE TABLE orders AS SELECT id FROM source DISTRIBUTE ON (id);"],
        ["CREATE VIEW order_view AS SELECT id FROM orders;"],
        ["CREATE EXTERNAL TABLE ext (id INTEGER) USING (DATAOBJECT('/tmp/a'));"],
        ["CREATE SEQUENCE order_seq START WITH 1;"],
        ["CREATE SYNONYM order_alias FOR orders;"],
        ["DROP TABLE orders IF EXISTS;"],
        ["ALTER TABLE orders ADD COLUMN status INTEGER;"],
        ["TRUNCATE TABLE orders;"],
        ["EXPLAIN VERBOSE SELECT id FROM orders;"],
        ["GROOM TABLE orders RECORDS;"],
        ["GENERATE EXPRESS STATISTICS ON orders;"],
        ["COMMENT ON TABLE orders IS 'orders';"],
        ["GRANT SELECT ON orders TO PUBLIC;"],
        ["REVOKE SELECT ON orders FROM PUBLIC;"],
        ["COMMIT;"],
        ["ROLLBACK;"],
        ["CALL refresh_orders();"],
        ["BEGIN;"],
        ["SET SCHEMA 'PUBLIC';"],
        ["SELECT 1 MINUS SET SELECT 2;"]
    ];

    [Theory]
    [MemberData(nameof(Statements))]
    public void Parse_SupportedTopLevelStatement_IsValid(string sql)
    {
        using var runtime = new ParsingRuntime();
        var result = runtime.Parse(sql);

        var errors = string.Join("; ", result.Errors.Select(error => error.Code + ": " + error.Message));
        Assert.True(result.Valid, errors);
        Assert.NotEmpty(result.Statements);
    }
}
