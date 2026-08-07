namespace JustyBase.Ai.Services;

public sealed class NetezzaReferenceProvider
{
    // Netezza-specific optimization rules for AI prompts
    private const string NetezzaOptimizationRules = """
NETEZZA OPTIMIZATION RULES TO APPLY:

1. Eliminate SELECT *
   Replace SELECT * with explicit column lists. Include only columns actually used in output or subsequent operations. This reduces I/O dramatically in Netezza's columnar architecture.

2. Push Filters into Subqueries
   Move WHERE conditions from outer query into CTEs/subqueries. Filter data as early as possible before joins and aggregations to reduce intermediate result sizes.

3. Align JOINs with Distribution Keys
   Check if join columns match table distribution keys. If not, consider redistributing smaller table or add distribution key to join condition when possible to avoid broadcast operations.

4. Replace Correlated Subqueries
   Convert correlated subqueries to JOINs or window functions. Correlated subqueries execute per row - JOINs and analytics leverage parallel processing across SPUs.

5. Simplify DISTINCT and GROUP BY
   Remove unnecessary DISTINCT operations. Ensure GROUP BY uses distribution keys when possible. Consider if aggregation is truly needed or if EXISTS/window functions could replace it.

6. Optimize Window Functions
   Add PARTITION BY on distribution key in window functions. Use ORDER BY only when necessary. Limit window frame size (ROWS BETWEEN) to minimum required range.

7. Use UNION ALL Instead of UNION
   Replace UNION with UNION ALL when duplicates don't matter. UNION performs implicit DISTINCT which causes expensive data redistribution and sorting.

8. Avoid Functions on Join/WHERE Columns
   Remove functions from join and filter columns: change WHERE YEAR(date_col) = 2024 to WHERE date_col BETWEEN '2024-01-01' AND '2024-12-31'. Functions prevent zone map usage.

9. Split Complex Queries with TEMP Tables
   Break multi-join, multi-aggregation queries into steps using CREATE TEMP TABLE AS SELECT. Distribute temp tables on appropriate keys for subsequent joins to control execution plan.

10. Use GROOM TABLE for Data Reorganization
    GROOM TABLE reorganizes data on disk for better zone map utilization. Use GROOM TABLE table_name RECLUSTER for optimal performance on frequently updated tables.

11. Create Zone Maps with ORGANIZE ON
    Use ORGANIZE ON (column_name) during table creation to create zone maps. CREATE ZONEMAP is the command for zone map creation on frequently filtered columns. Best for columns with high cardinality and range queries.

12. Use CTAS for Materialized Views
    CREATE TABLE AS SELECT (CTAS) is more efficient than INSERT INTO for creating materialized views. Distribute CTAS tables on join or aggregation columns.

13. Use Temporary Tables for Complex Processing
    CREATE GLOBAL TEMP TABLE or CREATE TEMP TABLE for intermediate results. temporary tables are automatically dropped at session end and can be distributed for optimal join performance. Use TEMP or TEMPORARY keyword for temporary table creation.

14. Date Functions Optimization
    Use date arithmetic instead of functions on columns. Instead of WHERE YEAR(date_col) = 2024, use WHERE date_col >= '2024-01-01' AND date_col < '2025-01-01'.

15. String Functions Optimization
    Avoid functions on string columns in WHERE clauses. Use LIKE patterns efficiently. Consider using VARCHAR(n) instead of CHAR(n) for variable-length data. Common string functions like SUBSTR, CONCAT, and TRIM are available in Netezza.

16. Aggregate Functions Best Practices
    Use GROUP BY with distribution keys when possible. Consider using DISTRIBUTE ON key for aggregation tables. Use COUNT(*) instead of COUNT(column) when nulls don't matter. SUM, AVG, MIN, and MAX are standard aggregate functions available.

17. Anti-Join Patterns Warning
    Avoid NOT IN with nullable columns (returns NULL if any NULL exists). Use NOT EXISTS or LEFT JOIN WHERE IS NULL instead for better performance. Anti-join patterns should be carefully implemented to avoid unexpected results.

18. Subquery Optimization
    Correlated subqueries are inefficient in Netezza. subquery performance can be improved by converting to JOINs or using window functions. Push predicates into subqueries before joining.

19. Data Type Recommendations
    Use INTEGER for whole numbers, NUMERIC(p,s) for decimals. Use VARCHAR(n) for variable-length strings. Avoid CHAR(n) unless fixed length is required. Use DATE for dates, TIMESTAMP for date+time.

20. NULL Handling
    Use IS NULL / IS NOT NULL for NULL checks. COALESCE(column, default_value) for NULL replacement. Be aware that NULL != NULL in comparisons.

NETEZZA SQL NAMING CONVENTIONS:
- Three-part name: DATABASE.SCHEMA.OBJECT (e.g., SALES.ADMIN.CUSTOMERS)
- Two-part name: SCHEMA.OBJECT or DATABASE..OBJECT (Netezza-specific syntax)
- Object types: TABLE, VIEW, PROCEDURE, FUNCTION, SYNONYM
- Maximum identifier length: 128 characters
- Use UPPERCASE for unquoted identifiers (case-insensitive)
- Use quoted identifiers for case-sensitive names: "MixedCase"
""";

    // NZPLSQL Stored Procedure documentation for AI prompts
    private const string NzplsqlProcedureReference = """
NZPLSQL STORED PROCEDURE REFERENCE (IBM Netezza):

PROCEDURE STRUCTURE (PREFERRED FORMAT):
```sql
CREATE [OR REPLACE] PROCEDURE database.schema.procedure_name(parameters)
RETURNS return_type
EXECUTE AS CALLER
LANGUAGE NZPLSQL
AS
BEGIN_PROC
  [DECLARE
    -- Variable declarations
    variable_name data_type [:= default_value];
    variable_name table_name%ROWTYPE;  -- Row type from table
    variable_name table_name.column%TYPE;  -- Column type
  ]
  BEGIN
    -- Statements
    [EXCEPTION WHEN OTHERS THEN
      -- Exception handling
      ROLLBACK;  -- Required for TRANSACTION_ABORTED
      RAISE NOTICE 'Error: %', SQLERRM;
    ]
  END;
END_PROC;
```

KEY SYNTAX RULES:
- ALWAYS use EXECUTE AS CALLER (preferred) - runs with caller's permissions
- Use BEGIN_PROC / END_PROC to wrap the procedure body (NOT just BEGIN/END)
- DECLARE section comes BEFORE the inner BEGIN block
- Variables are initialized to NULL by default
- Use := for assignment (not =)
- Statements end with semicolon (;)
- Labels use <<label_name>> syntax

EXECUTE AS OPTIONS:
- EXECUTE AS CALLER (PREFERRED) - procedure runs with the permissions of the calling user
- EXECUTE AS OWNER - procedure runs with the permissions of the procedure owner

VARIABLE DECLARATIONS:
```sql
DECLARE
  v_count INTEGER;
  v_name VARCHAR(100) := 'default';
  v_rate NUMERIC(10,2) NOT NULL := 0.0;
  v_const CONSTANT INTEGER := 100;
  v_row my_table%ROWTYPE;  -- Inherits structure from table
  v_id my_table.id%TYPE;   -- Inherits type from column
```

CONTROL STRUCTURES:
- IF condition THEN ... [ELSIF condition THEN ...] [ELSE ...] END IF;
- CASE expression WHEN value THEN ... [ELSE ...] END CASE;
- LOOP ... EXIT [WHEN condition]; END LOOP;
- WHILE condition LOOP ... END LOOP;
- FOR i IN 1..10 LOOP ... END LOOP;
- FOR record IN SELECT ... LOOP ... END LOOP;

DYNAMIC SQL (EXECUTE IMMEDIATE):
```sql
EXECUTE IMMEDIATE 'INSERT INTO ' || table_name || ' VALUES (1, 2)';
EXECUTE IMMEDIATE 'UPDATE ' || quote_ident(col) || ' SET x = ' || quote_literal(val);
```
- Use quote_ident() for identifiers (table/column names)
- Use quote_literal() for string values

RETURNING RESULT SETS:
```sql
CREATE PROCEDURE my_proc() RETURNS REFTABLE(reference_table)
LANGUAGE NZPLSQL AS
BEGIN_PROC
BEGIN
  EXECUTE IMMEDIATE 'INSERT INTO ' || REFTABLENAME || ' SELECT * FROM source';
  RETURN REFTABLE;
END;
END_PROC;
```

EXCEPTION HANDLING:
```sql
BEGIN
  -- statements that may fail
EXCEPTION
  WHEN TRANSACTION_ABORTED THEN
    ROLLBACK;
    RAISE ERROR 'Transaction failed: %', SQLERRM;
  WHEN OTHERS THEN
    RAISE NOTICE 'Error caught: %', SQLERRM;
END;
```

BUILT-IN VARIABLES:
- FOUND: TRUE if last query returned rows
- ROW_COUNT: Number of rows affected by last statement
- SQLERRM: Error message text in exception handler
- REFTABLENAME: Name of temp table for REFTABLE procedures

IMPORTANT NOTES:
- Netezza does NOT support nested transactions (no SAVEPOINT)
- BEGIN/END in NZPLSQL is for grouping, NOT transaction control
- Use CALL procedure_name() or EXECUTE PROCEDURE procedure_name() to invoke
- Procedures run within the caller's transaction context

For complex scenarios or detailed syntax rules, refer to IBM Netezza NZPLSQL documentation:
https://www.ibm.com/docs/en/netezza?topic=grammar-nzplsql-structure
""";

    public string GetNetezzaReference(string topic = "all")
    {
        return topic.ToLowerInvariant() switch
        {
            "optimization" => NetezzaOptimizationRules,
            "nzplsql" => NzplsqlProcedureReference,
            _ => $"{NetezzaOptimizationRules}{Environment.NewLine}{Environment.NewLine}{NzplsqlProcedureReference}"
        };
    }
}
