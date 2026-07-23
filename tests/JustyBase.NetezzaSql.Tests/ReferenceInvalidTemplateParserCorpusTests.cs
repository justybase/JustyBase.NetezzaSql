using JustyBase.NetezzaSqlParser.Caching;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ReferenceInvalidTemplateParserCorpusTests
{
    public static IEnumerable<object[]> InvalidSql =
    new object[][]
    {
        new object[] { "SELECT CASE\r\n    WHEN SALARY > 5000 THEN 'High'\r\n    WHEN SALARY > 3000 THEN 'Medium'\r\n    ELSE 'Low'\r\nFROM TESTDB..EMPLOYEES;" },
        new object[] { "SELECT CASE\r\n    WHEN 1 = 1 THEN CASE WHEN 2 = 2 THEN 'nested'\r\n    ELSE 'outer'\r\nEND;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_PROC()\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    RETURN 1;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_PROC()\r\nRETURNS INT4\r\nNZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    RETURN 1;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_PROC()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN\r\n    RETURN 1;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_PROC()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    RETURN 1;\r\nEND;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_IF()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    IF 1 = 1 THEN\r\n        RETURN 1;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_IF2()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    IF 1 = 1\r\n        RETURN 1;\r\n    END IF;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_WHILE()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nDECLARE\r\n    v_i INT4 := 0;\r\nBEGIN\r\n    WHILE v_i < 10 LOOP\r\n        v_i := v_i + 1;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_WHILE2()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nDECLARE\r\n    v_i INT4 := 0;\r\nBEGIN\r\n    WHILE v_i < 10\r\n        v_i := v_i + 1;\r\n    END LOOP;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_FOR()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    FOR i IN 1..5 LOOP\r\n        RAISE NOTICE 'i=%', i;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_RAISE()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    RAISE 'missing severity';\r\n    RETURN 1;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_ARGS p_id INT4)\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    RETURN 1;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_ARGS(p_id INT4\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    RETURN 1;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE NO_BEGIN()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nDECLARE\r\n    v_i INT4 := 0;\r\n    RETURN v_i;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE NO_END()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    RETURN 1;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_CASE_PROC()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    DECLARE v_result VARCHAR(20);\r\n    v_result := CASE WHEN 1 = 1 THEN 'yes';\r\n    RETURN 1;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_PROC()\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    RETURN 0;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_PROC()\r\nRETURNS INT4\r\nAS\r\nBEGIN_PROC\r\nBEGIN\r\n    RETURN 0;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_PROC()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    RETURN 0;\r\nEND;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_PROC()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    RETURN 0;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_PROC()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nDECLARE\r\n    v INT4;\r\nBEGIN\r\n    v := 1\r\n    RETURN v;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_WHILE()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nDECLARE\r\n    v INT4 := 0;\r\nBEGIN\r\n    WHILE v < 10\r\n        v := v + 1;\r\n    END LOOP;\r\n    RETURN v;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE BAD_IF()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    IF 1 > 0\r\n        RETURN 1;\r\n    END IF;\r\n    RETURN 0;\r\nEND;\r\nEND_PROC;" },
        new object[] { "CREATE OR REPLACE PROCEDURE TESTDB.PUBLIC.P_GOTO()\r\nRETURNS INT4\r\nLANGUAGE NZPLSQL AS\r\nBEGIN_PROC\r\nBEGIN\r\n    GOTO lbl;\r\n    RETURN 1;\r\nEND;\r\nEND_PROC;" },
    };

    [Theory]
    [MemberData(nameof(InvalidSql))]
    public void ReferenceInvalidTemplateReportsSyntaxError(string sql)
    {
        using var runtime = new ParsingRuntime();
        var result = runtime.Parse(sql);
        Assert.False(result.Valid, "Expected syntax error for SQL=" + sql);
    }
}
