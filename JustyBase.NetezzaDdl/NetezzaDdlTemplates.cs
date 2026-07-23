namespace JustyBase.NetezzaDdl;

/// <summary>
/// Static DDL/SQL templates for Netezza maintenance operations.
/// </summary>
public static class NetezzaDdlTemplates
{
    public static string GetDeletedRecordsSql(string qualifiedTable, IReadOnlyList<string> columnNames, Func<string, string> quoteName)
    {
        var colList = string.Join("\r\n    , ", columnNames.Select(quoteName));
        return $"""
            SET show_deleted_records = 1;
            SELECT T1.createxid, T1.deletexid, T1.* FROM {qualifiedTable}  T1 WHERE deletexid != 0;
            SET show_deleted_records = 0;
            """;
    }

    public static string GetGrantSelectSql(string qualifiedTable)
        => $"GRANT SELECT ON {qualifiedTable} TO SOME_OWNER?;\r\n--https://www.ibm.com/docs/en/netezza?topic=npsscr-grant-2";

    public static string GetOrganizeTemplateSql(string qualifiedTable)
        => $"ALTER TABLE {qualifiedTable} ORGANIZE ON (<COL1>, <COL2>);\r\n--https://www.ibm.com/docs/en/netezza?topic=tables-select-organizing-keys";

    public static string GetGroomSql(string qualifiedTable)
        => $"""
            GROOM TABLE {qualifiedTable} RECORDS ALL RECLAIM BACKUPSET NONE;
            --GROOM TABLE {qualifiedTable} VERSIONS;
            --https://www.ibm.com/docs/en/netezza?topic=databases-groom-tables
            """;

    public static string GetGenerateStatsSql(string qualifiedTable)
        => $"GENERATE EXPRESS STATISTICS ON {qualifiedTable};\r\n--https://www.ibm.com/docs/en/netezza?topic=reference-generate-express-statistics";

    public static string GetAddTableCommentTemplateSql(string qualifiedTable)
        => $"COMMENT ON TABLE {qualifiedTable} IS 'some comment';";

    public static string GetCheckDistributeSql(string cleanDatabase, string cleanSchema, string cleanTable, string tableNameUpper)
    {
        string distQ1 =
            $"""
            SET SHOW_DELETED_RECORDS = 1;
                SELECT 
                    DATASLICEID, COUNT(1), COUNT(NULLIF(DELETEXID,0)) 
                FROM 
                    {cleanDatabase}.{cleanSchema}.{cleanTable} 
                GROUP BY 
                    DATASLICEID 
                ORDER BY 
                    COUNT(1) DESC;
                SET SHOW_DELETED_RECORDS = 0;
            """;

        string distQ2 =
            $"""
            SELECT
                    OBJID::BIGINT AS OBJID
                    , SKEW::DOUBLE
                    , CREATEDATE::DATETIME AS CREATEDATE
                    , ALLOCATED_BYTES::BIGINT AS ALLOCATED_BYTES
                    , USED_BYTES::BIGINT as USED_BYTES
                    , ALLOCATED_BLOCKS
                    , USED_BLOCKS
                    , BLOCK_SIZE
                    , USED_MIN
                    , USED_MAX
                    , USED_AVG
                FROM
                    {cleanDatabase}.{cleanSchema}._V_TABLE_STORAGE_STAT
                WHERE
                    UPPER(OBJTYPE) = 'TABLE' 
                    AND UPPER(TABLENAME) = '{tableNameUpper}'
                ORDER BY
                    TABLENAME, OWNER;
            """;

        return distQ1 + Environment.NewLine + Environment.NewLine + distQ2;
    }

    public const string CreateProcedurePattern = """
        CREATE OR REPLACE PROCEDURE SAMPLE_PROC(TEXT) 
        RETURNS TEXT
        EXECUTE AS CALLER
        LANGUAGE NZPLSQL AS
        BEGIN_PROC
            DECLARE PARAM_ALIAS ALIAS FOR $1;
            DECLARE SID INTEGER;
            DECLARE RESULT TEXT;
        BEGIN
            SID := CURRENT_SID;
            RESULT := 'HELLO ' || PARAM_ALIAS || 'SID IS ' || SID;
            RETURN RESULT;


            EXCEPTION
            WHEN OTHERS THEN
                ROLLBACK;
                RAISE EXCEPTION  'Procedure failed: %', sqlerrm;
                --RAISE NOTICE 'Caught error, continuing %', sqlerrm;
        END;
        END_PROC;
        """;

    public static string GetCreateFluidSampleSql(string qualifiedTable)
        => $"SELECT * FROM TABLE WITH FINAL ({qualifiedTable}('', '', 'SELECT *  FROM SOME_TABLE'))";
}
