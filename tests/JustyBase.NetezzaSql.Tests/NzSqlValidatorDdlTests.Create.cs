using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorDdlTests
{
    [Fact]
    public void CreateTable_MultipleColumnTypes()
    {
        ExpectValid(
            "CREATE TABLE MY_TABLE (ID INT4, NAME VARCHAR(100), AMOUNT NUMERIC(12,2), FLAG BOOLEAN, CREATED DATE) DISTRIBUTE ON (ID);",
            _schema);
    }


    [Fact]
    public void CreateTable_AllIntegerTypes()
    {
        ExpectValid(
            "CREATE TABLE INT_TYPES (C1 INT1, C2 BYTEINT, C3 INT2, C4 SMALLINT, C5 INT4, C6 INTEGER, C7 INT8, C8 BIGINT);",
            _schema);
    }


    [Fact]
    public void CreateTable_FloatTypes()
    {
        ExpectValid(
            "CREATE TABLE FLOAT_TYPES (C1 FLOAT4, C2 REAL, C3 FLOAT8, C4 DOUBLE PRECISION, C5 FLOAT);",
            _schema);
    }


    [Fact]
    public void CreateTable_NcharNvarcharTypes()
    {
        ExpectValid(
            "CREATE TABLE NCHAR_TYPES (C1 NCHAR(10), C2 NVARCHAR(200));",
            _schema);
    }


    [Fact]
    public void CreateTable_DistributeOnRandom()
    {
        ExpectValid("CREATE TABLE T1 (ID INT4) DISTRIBUTE ON RANDOM;", _schema);
    }


    [Fact]
    public void CreateTable_DistributeOnColumnsWithOrganize()
    {
        ExpectValid(
            "CREATE TABLE T1 (ID INT4, NAME VARCHAR(10)) DISTRIBUTE ON (ID) ORGANIZE ON (NAME);",
            _schema);
    }


    [Fact]
    public void CreateTempTable_WithDdlColumns()
    {
        ExpectValid("CREATE TEMP TABLE TMP (ID INT4, VAL VARCHAR(50));", _schema);
    }


    [Fact]
    public void CreateTemporaryTable()
    {
        ExpectValid("CREATE TEMPORARY TABLE TMP2 (ID INT8);", _schema);
    }


    [Fact]
    public void CreateGlobalTempTable()
    {
        ExpectValid("CREATE GLOBAL TEMP TABLE GTT (ID INT4, DATA TEXT);", _schema);
    }




    [Fact]
    public void CreateTable_InvalidDataType()
    {
        ExpectErrorCode("CREATE TABLE T1 (ID FOOBARBAZ);", "SQL013");
        ExpectErrorCode("CREATE TABLE TESTDB..BAD_TYPE (ID FAKE_TYPE);", "SQL013");
    }


    [Fact]
    public void CreateTable_ExcessTypeParamFixed()
    {
        ExpectErrorCode("CREATE TABLE T1 (ID INT4(10));", "SQL014");
        ExpectErrorCode("CREATE TABLE TESTDB..BAD_PARAMS (ID INT4(10,2));", "SQL014");
    }


    [Fact]
    public void CreateTable_ExcessTypeParamVarchar()
    {
        ExpectErrorCode("CREATE TABLE T1 (C VARCHAR(10,2));", "SQL014");
    }


    [Fact]
    public void CreateTable_MissingColumnName()
    {
        ExpectSyntaxError("CREATE TABLE T1 (INT4);", _schema);
    }


    [Fact]
    public void CreateTable_MissingType()
    {
        ExpectSyntaxError("CREATE TABLE T1 (ID);", _schema);
    }


    [Fact]
    public void CreateTable_EmptyColumnDefList()
    {
        ExpectSyntaxError("CREATE TABLE t1 ();", _schema);
    }




    [Fact]
    public void CreateTempTableAs()
    {
        ExpectValid("CREATE TEMP TABLE T_TMP AS (SELECT 1 AS COL);", _schema);
    }


    [Fact]
    public void CreateTempTableAsSelect()
    {
        ExpectValid(
            "CREATE TEMP TABLE TMP_DATA AS SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY > 1000;",
            _schema);
    }


    [Fact]
    public void CreateView_AsSelect()
    {
        ExpectValid(
            "CREATE VIEW V_EMP AS SELECT EMPLOYEE_ID, FIRST_NAME FROM TESTDB..EMPLOYEES;",
            _schema);
    }


    [Fact]
    public void CreateOrReplaceView()
    {
        ExpectValid(
            "CREATE OR REPLACE VIEW V_EMP AS SELECT * FROM TESTDB..EMPLOYEES;",
            _schema);
    }


    [Fact]
    public void CreateView_WithQualifiedName()
    {
        ExpectValid("CREATE VIEW TESTDB.PUBLIC.V_EMP AS SELECT 1 AS X;", _schema);
    }


    [Fact]
    public void CreateView_WithColumnAliases()
    {
        ExpectValid(
            "CREATE VIEW V_EMP (EMP_ID, EMP_NAME) AS SELECT EMPLOYEE_ID, FIRST_NAME FROM TESTDB..EMPLOYEES;",
            _schema);
    }


    [Fact]
    public void CreateView_WithParenthesizedSelect()
    {
        ExpectValid(
            "CREATE VIEW TEST_VIEW AS (SELECT * FROM TESTDB..EMPLOYEES);",
            _schema);
    }




    [Fact]
    public void CreateView_MissingAs()
    {
        ExpectSyntaxError("CREATE VIEW V_EMP SELECT 1;", _schema);
    }


    [Fact]
    public void CreateView_MissingSelectAfterAs()
    {
        ExpectSyntaxError("CREATE VIEW V_EMP AS;", _schema);
    }


    [Fact]
    public void CreateView_InvalidAliasListSyntax()
    {
        ExpectSyntaxError("CREATE VIEW V_EMP (ID,) AS SELECT 1 AS ID;", _schema);
    }




    [Fact]
    public void DropTable()
    {
        ExpectValid("DROP TABLE TESTDB.PUBLIC.EMPLOYEES;", _schema);
    }


    [Fact]
    public void DropTableIfExists()
    {
        // Live Netezza rejects DROP IF EXISTS (live evidence 2026-10-06).
        ExpectSyntaxError("DROP TABLE TESTDB.PUBLIC.EMPLOYEES IF EXISTS;", _schema);
    }


    [Fact]
    public void DropTable_MultipleTargets()
    {
        ExpectValid("DROP TABLE TESTDB.PUBLIC.EMPLOYEES, TESTDB.PUBLIC.DEPARTMENTS;", _schema);
    }


    [Fact]
    public void CreateSynonym_FullyQualified()
    {
        ExpectValid("CREATE SYNONYM JUST_DATA.ADMIN.DIMDATE_AAA FOR JUST_DATA_2.ADMIN.DIMDATE;", _schema);
    }


    [Fact]
    public void CreateSynonym_SchemaQualified()
    {
        ExpectValid("CREATE SYNONYM ADMIN.MY_SYN FOR OTHER_SCHEMA.MY_TABLE;", _schema);
    }


    [Fact]
    public void CreateSynonym_Unqualified()
    {
        ExpectValid("CREATE SYNONYM MY_SYN FOR MY_TABLE;", _schema);
    }


    [Fact]
    public void CreateSynonym_DbDotDotTable()
    {
        ExpectValid("CREATE SYNONYM DB1.SCH1.SYN1 FOR DB2..TAB2;", _schema);
    }




    [Fact]
    public void DropTableIf_Incomplete()
    {
        ExpectSyntaxError("DROP TABLE T1 IF;", _schema);
    }


    [Fact]
    public void DropTable_MissingObjectName()
    {
        ExpectSyntaxError("DROP TABLE;", _schema);
    }




    [Fact]
    public void TruncateTable()
    {
        ExpectValid("TRUNCATE TABLE TESTDB.PUBLIC.EMPLOYEES;", _schema);
    }


    [Fact]
    public void Truncate_WithoutTableKeyword()
    {
        ExpectValid("TRUNCATE TESTDB.PUBLIC.EMPLOYEES;", _schema);
    }




    [Fact]
    public void AlterTable_AddConstraintPrimaryKey()
    {
        ExpectValid(
            "ALTER TABLE TESTDB.PUBLIC.EMPLOYEES ADD CONSTRAINT PK_EMP PRIMARY KEY (EMPLOYEE_ID);",
            _schema);
    }


    [Fact]
    public void AlterTable_RenameTo()
    {
        ExpectValid(
            "ALTER TABLE TESTDB.PUBLIC.EMPLOYEES RENAME TO EMPLOYEES_OLD;",
            _schema);
    }


    [Fact]
    public void AlterTable_AddColumn()
    {
        ExpectValid(
            "ALTER TABLE TESTDB.PUBLIC.EMPLOYEES ADD COLUMN EMAIL VARCHAR(200);",
            _schema);
    }


    [Fact]
    public void AlterTable_DropColumnCascade()
    {
        ExpectValid(
            "ALTER TABLE TESTDB.PUBLIC.EMPLOYEES DROP COLUMN STATUS CASCADE;",
            _schema);
    }


    [Fact]
    public void CommentOn_Table()
    {
        ExpectValid(
            "COMMENT ON TABLE TESTDB.PUBLIC.EMPLOYEES IS 'Main employee table';",
            _schema);
    }


    [Fact]
    public void GroomTable_Versions()
    {
        ExpectValid("GROOM TABLE TESTDB.PUBLIC.EMPLOYEES VERSIONS;", _schema);
    }


    [Fact]
    public void GroomTable_RecordsAll()
    {
        ExpectValid("GROOM TABLE TESTDB.PUBLIC.EMPLOYEES RECORDS ALL;", _schema);
    }


    [Fact]
    public void GroomTable_PagesStartReclaim()
    {
        ExpectValid(
            "GROOM TABLE TESTDB.PUBLIC.EMPLOYEES PAGES START RECLAIM BACKUPSET DEFAULT;",
            _schema);
    }




    [Fact]
    public void GenerateStatistics_OnTable()
    {
        ExpectValid("GENERATE STATISTICS ON TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void GenerateExpressStatistics_ForTable()
    {
        ExpectValid("GENERATE EXPRESS STATISTICS FOR TABLE TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void Grant_SimpleOnTable()
    {
        ExpectValid("GRANT SELECT ON my_table TO admin", _schema);
    }


    [Fact]
    public void CreateUser_Simple()
    {
        ExpectValid("CREATE USER testuser", _schema);
    }


    [Fact]
    public void CreateUser_WithPassword()
    {
        ExpectValid("CREATE USER testuser WITH PASSWORD 'secret123'", _schema);
    }


    [Fact]
    public void CreateUser_MultipleClauses()
    {
        ExpectValid(
            "CREATE USER testuser WITH PASSWORD 'secret123' IN GROUP dev_team DEFPRIORITY NORMAL ROWSETLIMIT 1000",
            _schema);
    }


    [Fact]
    public void CreateUser_WithNullPassword()
    {
        ExpectValid("CREATE USER testuser WITH PASSWORD NULL", _schema);
    }




    [Fact]
    public void CreateTableIfNotExists_WithColumns()
    {
        ExpectValid(
            "CREATE TABLE IF NOT EXISTS my_table (id INTEGER, name VARCHAR(100))",
            _schema);
    }


    [Fact]
    public void CreateTempTableIfNotExists()
    {
        ExpectValid(
            "CREATE TEMP TABLE IF NOT EXISTS tmp_data (id INT, val FLOAT)",
            _schema);
    }


    [Fact]
    public void CreateTableIfNotExists_Ctas()
    {
        ExpectValid(
            "CREATE TABLE IF NOT EXISTS new_table AS SELECT * FROM old_table",
            _schema);
    }




    [Fact]
    public void CreateExternalTable_Sameas()
    {
        ExpectValid(
            "CREATE EXTERNAL TABLE ext_emp SAMEAS emp USING (DATAOBJECT ('/tmp/emp.dat'))",
            _schema);
    }


    [Fact]
    public void CreateExternalTable_SchemaQualifiedSameas()
    {
        ExpectValid(
            "CREATE EXTERNAL TABLE ext_emp SAMEAS myschema.emp USING (DATAOBJECT ('/tmp/emp.dat'))",
            _schema);
    }


    [Fact]
    public void CreateExternalTable_ColumnDefs()
    {
        ExpectValid(
            "CREATE EXTERNAL TABLE ext_data (id INT, name VARCHAR(100)) USING (DATAOBJECT ('/tmp/data.csv') FORMAT TEXT)",
            _schema);
    }


    [Fact]
    public void CreateExternalTable_Simple()
    {
        ExpectValid("CREATE EXTERNAL TABLE ext_table SAMEAS source_table", _schema);
    }


    [Fact]
    public void CreateExternalTable_ValidOptions()
    {
        ExpectValid(
            "CREATE EXTERNAL TABLE ext_ok (id INT4, name VARCHAR(20), created_at TIMESTAMP) USING (DATAOBJECT ('/tmp/data.csv') FORMAT TEXT DELIMITER '|' ENCODING 'INTERNAL' TIMESTYLE '24HOUR' REMOTESOURCE 'JDBC' MAXERRORS 1 LOGDIR '/tmp' QUOTEDVALUE 'NO' NULLVALUE 'NULL' COMPRESS FALSE DATESTYLE 'YMD' DATEDELIM '-' TIMEDELIM ':' BOOLSTYLE '1_0' SOCKETBUFSIZE 8388608 RECORDDELIM '\\n' DATETIMEDELIM ' ');",
            _schema);
    }




    [Fact]
    public void CreateExternalTable_InvalidColumnDataType()
    {
        ExpectErrorCode(
            "CREATE EXTERNAL TABLE ext_bad_types (col1 WRONG_TYPE_INTEGER, col2 WRONG_TYPE_VARCHAR(10)) USING (DATAOBJECT ('/tmp/data.csv') FORMAT TEXT);",
            "SQL013");
    }


    [Fact]
    public void CreateExternalTable_UnknownOptionName()
    {
        var result = Validate(
            "CREATE EXTERNAL TABLE ext_bad_opts (id INT4) USING (DATAOBJECT ('/tmp/data.csv') WRONG_OPTION_NAME 'abc' FORMAT TEXT);",
            _schema);
        Assert.True(result.Errors.Count > 0);
    }


    [Fact]
    public void CreateExternalTable_InvalidOptionValues()
    {
        var result = Validate(
            "CREATE EXTERNAL TABLE ext_bad_values (id INT4) USING (DATAOBJECT ('/tmp/data.csv') FORMAT 'BAD_FORMAT' QUOTEDVALUE 'MAYBE');",
            _schema);
        Assert.True(result.Errors.Count > 0);
    }




    [Fact]
    public void TableLevelPrimaryKeyConstraint()
    {
        ExpectValid(
            "CREATE TABLE t (id INT, name VARCHAR(50), PRIMARY KEY (id))",
            _schema);
    }


    [Fact]
    public void TableLevelUniqueConstraint()
    {
        ExpectValid(
            "CREATE TABLE t (id INT, email VARCHAR(200), UNIQUE (email))",
            _schema);
    }


    [Fact]
    public void TableLevelForeignKeyConstraint()
    {
        ExpectValid(
            "CREATE TABLE t (id INT, dept_id INT, FOREIGN KEY (dept_id) REFERENCES departments (id))",
            _schema);
    }


    [Fact]
    public void NamedTableConstraint()
    {
        ExpectValid(
            "CREATE TABLE t (id INT, name VARCHAR(50), CONSTRAINT pk_t PRIMARY KEY (id))",
            _schema);
    }


    [Fact]
    public void DropTableIfExists_Simple()
    {
        // Live Netezza rejects DROP IF EXISTS (live evidence 2026-10-06).
        ExpectSyntaxError("DROP TABLE my_table IF EXISTS", _schema);
    }


    [Fact]
    public void DropTable_Multiple()
    {
        ExpectValid("DROP TABLE t1, t2, t3", _schema);
    }


    [Fact]
    public void DropExternalTable()
    {
        ExpectValid("DROP EXTERNAL TABLE ext_data", _schema);
    }


    [Fact]
    public void GroomTable_Basic()
    {
        ExpectValid("GROOM TABLE my_table", _schema);
    }


    [Fact]
    public void GroomTable_RecordsReady()
    {
        ExpectValid("GROOM TABLE my_table RECORDS READY", _schema);
    }


    [Fact]
    public void GroomTable_PagesAll()
    {
        ExpectValid("GROOM TABLE my_table PAGES ALL", _schema);
    }


    [Fact]
    public void GroomTable_PagesStart()
    {
        ExpectValid("GROOM TABLE my_table PAGES START", _schema);
    }


    [Fact]
    public void GroomTable_Versions_Extended()
    {
        ExpectValid("GROOM TABLE my_table VERSIONS", _schema);
    }


    [Fact]
    public void GroomTable_ReclaimBackupsetNone()
    {
        ExpectValid("GROOM TABLE my_table RECORDS ALL RECLAIM BACKUPSET NONE", _schema);
    }


    [Fact]
    public void GroomTable_ReclaimBackupsetDefault()
    {
        ExpectValid("GROOM TABLE my_table RECLAIM BACKUPSET DEFAULT", _schema);
    }


    [Fact]
    public void GroomTable_SchemaQualified()
    {
        ExpectValid("GROOM TABLE mydb.myschema.my_table VERSIONS", _schema);
    }




    [Fact]
    public void TruncateTable_Simple()
    {
        ExpectValid("TRUNCATE TABLE my_table", _schema);
    }


    [Fact]
    public void Truncate_WithoutTableKeyword_Simple()
    {
        ExpectValid("TRUNCATE my_table", _schema);
    }


    [Fact]
    public void GenerateStatistics_OnTable_Extended()
    {
        ExpectValid("GENERATE STATISTICS ON my_table", _schema);
    }


    [Fact]
    public void GenerateStatistics_OnTableWithColumns()
    {
        ExpectValid("GENERATE STATISTICS ON my_table (col1, col2)", _schema);
    }


    [Fact]
    public void GenerateExpressStatistics_OnTable()
    {
        ExpectValid("GENERATE EXPRESS STATISTICS ON TESTDB..EMPLOYEES", _schema);
    }


    [Fact]
    public void CreateTable_NotNullConstraints()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..NN_TABLE (ID INT4 NOT NULL, NAME VARCHAR(100) NOT NULL);",
            _schema);
    }


    [Fact]
    public void CreateTable_DefaultValues()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..DEF_TABLE (ID INT4 DEFAULT 0, STATUS VARCHAR(20) DEFAULT 'ACTIVE');",
            _schema);
    }


    [Fact]
    public void CreateTable_DefaultAndNotNull()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..DEF_NN_TABLE (ID INT4 DEFAULT 1 NOT NULL, FLAG VARCHAR(5) NOT NULL DEFAULT 'Y');",
            _schema);
    }


    [Fact]
    public void CreateTable_NamedColumnConstraintWithInterval()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..INTERVAL_TABLE (ID INT4, LEN INTERVAL HOUR TO MINUTE);",
            _schema);
    }


    [Fact]
    public void CreateTable_NamedNotNullAndDefault()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..NAMED_CONSTRAINTS (ID INT4 CONSTRAINT NN_ID NOT NULL, FLAG CHAR(1) CONSTRAINT DF_FLAG DEFAULT 'Y');",
            _schema);
    }


    [Fact]
    public void CreateTable_FunctionalDefaultExpression()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..DEF_FUNC_TABLE (CREATED_AT TIMESTAMP DEFAULT NOW(), CODE INT4 DEFAULT (1 + 2));",
            _schema);
    }


    [Fact]
    public void CreateTable_QuotedFunctionalDefaultExpression()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..DEF_FUNC2 (ID INT4, TS VARCHAR(10) DEFAULT \"timestamp\"('NOW(0)'::\"VARCHAR\"));",
            _schema);
    }


    [Fact]
    public void CreateTable_WithBigint()
    {
        ExpectValid("CREATE TABLE TESTDB..BIG_TABLE (ID INT8, BIG_VAL BIGINT);", _schema);
    }


    [Fact]
    public void CreateTable_WithBoolean()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..BOOL_TABLE (ID INT4, IS_ACTIVE BOOLEAN, FLAG BOOL);",
            _schema);
    }


    [Fact]
    public void CreateTable_WithDateAndTimestamp()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..DT_TABLE (ID INT4, CREATED_AT TIMESTAMP, BIRTH_DATE DATE, EVENT_TIME TIME);",
            _schema);
    }


    [Fact]
    public void CreateTable_WithNumericPrecision()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..NUM_TABLE (ID INT4, AMOUNT NUMERIC(18, 4), RATE DECIMAL(5,2));",
            _schema);
    }


    [Fact]
    public void CreateSequence()
    {
        ExpectValid("CREATE SEQUENCE TESTDB..MY_SEQ;", _schema);
    }


    [Fact]
    public void AlterTable_RenameColumn()
    {
        ExpectValid(
            "ALTER TABLE TESTDB..EMPLOYEES RENAME COLUMN FIRST_NAME TO GIVEN_NAME;",
            _schema);
    }




    [Fact]
    public void CreateTable_WithoutTableName()
    {
        ExpectSyntaxError("CREATE TABLE (ID INT4);", _schema);
    }


    [Fact]
    public void CreateTable_WithoutColumnDefsOrAs()
    {
        ExpectSyntaxError("CREATE TABLE TESTDB..NEW_TABLE;", _schema);
    }


    [Fact]
    public void CreateTable_MissingColumnType_Additional()
    {
        ExpectSyntaxError(
            "CREATE TABLE TESTDB..NEW_TABLE (ID, NAME VARCHAR(100));",
            _schema);
    }


    [Fact]
    public void CreateView_WithoutAs()
    {
        ExpectSyntaxError(
            "CREATE VIEW TESTDB..V SELECT * FROM TESTDB..EMPLOYEES;",
            _schema);
    }


    [Fact]
    public void AlterTable_WithoutAction()
    {
        ExpectSyntaxError("ALTER TABLE T1;", _schema);
    }




    [Fact]
    public void CreateTable_MissingColumnType_Extended()
    {
        ExpectSyntaxError("CREATE TABLE t (col1)", _schema);
    }


    [Fact]
    public void CreateTable_UnclosedParenthesis()
    {
        ExpectSyntaxError("CREATE TABLE t (id INT, name VARCHAR(100)", _schema);
    }


    [Fact]
    public void CreateTable_DuplicateComma()
    {
        ExpectSyntaxError("CREATE TABLE t (id INT,, name VARCHAR(100))", _schema);
    }


    [Fact]
    public void Select_ReservedKeywordAsTableName()
    {
        ExpectErrorCode("SELECT * FROM FROM", "PAR003");
    }


    [Fact]
    public void LockTableCommand()
    {
        ExpectValid("LOCK TABLE TESTDB..EMPLOYEES IN EXCLUSIVE MODE;", _schema);
    }


    [Fact]
    public void AlterTable_AddColumnWithNotNull()
    {
        ExpectValid(
            "ALTER TABLE TESTDB..EMPLOYEES ADD COLUMN EMAIL VARCHAR(255) NOT NULL;",
            _schema);
    }


    [Fact]
    public void AlterTable_DropColumn()
    {
        // Live Netezza requires RESTRICT or CASCADE (live evidence 2026-10-06).
        ExpectSyntaxError("ALTER TABLE TESTDB..EMPLOYEES DROP COLUMN STATUS;", _schema);
        ExpectValid("ALTER TABLE TESTDB..EMPLOYEES DROP COLUMN STATUS CASCADE;", _schema);
    }


    [Fact]
    public void AlterTable_OwnerTo()
    {
        ExpectValid("ALTER TABLE TESTDB..EMPLOYEES OWNER TO ADMIN;", _schema);
    }
}
