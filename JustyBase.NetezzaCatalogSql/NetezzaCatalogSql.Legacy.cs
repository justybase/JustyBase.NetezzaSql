namespace JustyBase.NetezzaCatalogSql;

/// <summary>
/// Legacy WinForms-specific catalog SQL (schema+owner columns, single-pass column load with dist/org).
/// </summary>
public static partial class NetezzaCatalogSql
{
    public const string LegacyTableKeysSql = """
        SELECT 
            X.OBJID::INT AS OBJID
            , X.CONSTRAINTNAME
            , X.CONTYPE
            , X.CONSEQ
            , X.ATTNAME
            , X.PKOBJID::INT
            , X.PKATTNAME
            , X.UPDT_TYPE
            , X.DEL_TYPE
        FROM 
            _V_RELATION_KEYDATA X
        WHERE 
            X.OBJID NOT IN (4,5)
        """;

    public static string GetLegacyBazyTabeleSql(string dbName, bool ownerMode = true, bool noDescMode = false)
    {
        dbName = NormalizeDatabaseIdentifier(dbName, nameof(dbName));
        string dbLiteral = EscapeSqlLiteral(dbName);

        const string schemaExpr = "CASE WHEN D1.SCHEMA IS NULL OR D1.SCHEMA = '' THEN 'ADMIN' ELSE D1.SCHEMA END";
        const string ownerExpr = "CASE WHEN D1.OWNER IS NULL OR D1.OWNER = '' THEN 'ADMIN' ELSE D1.OWNER END";

        string systemSql = $"""
            SELECT 
                OBJID::INT
                , OBJDB::INT AS OBJDB
                , OBJNAME
                , D1.DESCRIPTION
                , {schemaExpr}
                , {ownerExpr}
                , CASE OBJTYPE
               WHEN  'SYSTEM TABLE' THEN 'TABLE'
               WHEN 'SYSTEM VIEW' THEN 'VIEW' END 
            FROM SYSTEM.._V_OBJECT_DATA D1
            WHERE OBJTYPE IN ('SYSTEM TABLE','SYSTEM VIEW')
            AND DBNAME = 'SYSTEM'
            """;

        string dbWhere = $"AND D1.DBNAME = '{dbLiteral}'";
        if (noDescMode)
            dbWhere = $" UNION ALL {systemSql}";

        string sql =
            $"""
            SELECT 
                 D1.OBJID::INT
                , D1.OBJDB::INT AS OBJDB
                , D1.OBJNAME
                , D1.DESCRIPTION
                , {schemaExpr}
                , {ownerExpr}
                , D1.OBJTYPE
            FROM 
                {dbName}.._V_OBJECT_DATA D1
            WHERE 
                D1.OBJTYPE NOT IN
                ('AGGREGATE','CONSTRAINT','DATABASE','DATATYPE','GROUP','MANAGEMENT INDEX','MANAGEMENT SEQ','MANAGEMENT TABLE',
                'MANAGEMENT VIEW','SCHEDULER RULE','SCHEMA','SYSTEM INDEX','SYSTEM SEQ','SYSTEM TABLE','SYSTEM VIEW','USER')
            AND D1.OBJID NOT IN (4,5)
            {dbWhere}
            """;

        if (dbName == "SYSTEM" && !noDescMode)
        {
            sql = $"""
                SELECT 
                    OBJID::INT
                    , OBJDB::INT AS OBJDB
                    , OBJNAME
                    , D1.DESCRIPTION
                    , {schemaExpr}
                    , {ownerExpr}
                    , CASE OBJTYPE
                   WHEN  'SYSTEM TABLE' THEN 'TABLE'
                   WHEN 'SYSTEM VIEW' THEN 'VIEW' END 
                FROM SYSTEM.._V_OBJECT_DATA D1
                WHERE OBJTYPE IN ('SYSTEM TABLE','SYSTEM VIEW')
                AND DBNAME = 'SYSTEM'
                """;
        }

        return sql;
    }

    public static string GetLegacyObjectColumnsSql(string dbName)
    {
        dbName = NormalizeDatabaseIdentifier(dbName, nameof(dbName));

        return $"""
            SELECT 
                X.ATTNUM::SMALLINT as ATTNUM
                , X.OBJID::INT AS OBJID
                , X.OBJDB::INT AS OBJDB
                , X.ATTNAME 
                , X.DESCRIPTION
                , X.FORMAT_TYPE
                , X.ATTNOTNULL::BOOL AS ATTNOTNULL
                , DM.DISTSEQNO::BYTEINT
                , DS.ORGSEQNO::BYTEINT
                , X.COLDEFAULT
            FROM
                {dbName}.._V_RELATION_COLUMN X
                LEFT JOIN {dbName}.._V_TABLE_DIST_MAP DM ON DM.OBJID = X.OBJID
                    AND DM.ATTNUM = X.ATTNUM
                    AND DM.DATABASE = '{EscapeSqlLiteral(dbName)}'
                LEFT JOIN {dbName}.._V_TABLE_ORGANIZE_COLUMN DS ON DS.OBJID = X.OBJID
                    AND DS.ATTNUM = X.ATTNUM
            WHERE
                X.TYPE IN ('TABLE','VIEW','EXTERNAL TABLE', 'SEQUENCE','SYSTEM VIEW','SYSTEM TABLE')
                AND X.OBJID NOT IN (4,5)
                AND X.DATABASE = '{EscapeSqlLiteral(dbName)}'
            ORDER BY 
                X.OBJDB, X.OBJID, X.ATTNUM
            """;
    }

    public static string GetLegacyOneTableSqlOwner(string tablename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablename, nameof(tablename));
        if (!tablename.StartsWith('"'))
            tablename = tablename.ToUpperInvariant();

        return $"""
            SELECT 
                D1.OBJID::INT AS OBJID
                , D1.OBJDB::INT AS OBJDB
                , D1.OBJNAME
                , NULL AS OPIS_TABELI
                , CASE WHEN D1.OWNER IS NULL OR D1.OWNER = '' THEN 'ADMIN' ELSE D1.OWNER  END 
                , D1.OBJTYPE
            FROM 
                _V_OBJ_RELATION_XDB D1
            WHERE
                D1.OBJNAME = '{EscapeSqlLiteral(tablename)}'
                AND D1.OBJID NOT IN (4,5)
            """;
    }

    public static string GetLegacyOneTableSqlSchema(string tablename, bool schemaOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablename, nameof(tablename));
        if (!tablename.StartsWith('"'))
            tablename = tablename.ToUpperInvariant();

        string ownerOrSchema = schemaOn
            ? " CASE WHEN D1.SCHEMA IS NULL OR D1.SCHEMA = '' THEN 'ADMIN' ELSE D1.SCHEMA  END "
            : " CASE WHEN D1.OWNER IS NULL OR D1.OWNER = '' THEN 'ADMIN' ELSE D1.OWNER END ";

        return $"""
            SELECT 
                D1.OBJID::INTEGER AS OBJID
                , D1.OBJDB::INT
                , D1.OBJNAME
                , NULL AS OPIS_TABELI
                , {ownerOrSchema}
                , D1.OBJTYPE
            FROM 
                _V_OBJ_RELATION_XDB D1
            WHERE
                D1.OBJNAME = '{EscapeSqlLiteral(tablename)}'
                AND D1.OBJID NOT IN (4,5)
            """;
    }

    public static string GetLegacySearchInSchemaSql(string dbName, string txtToSearch)
    {
        dbName = NormalizeDatabaseIdentifier(dbName, nameof(dbName));
        ArgumentNullException.ThrowIfNull(txtToSearch);

        string search = EscapeSqlLiteral(txtToSearch.ToUpperInvariant());
        return $"""
            SELECT DISTINCT 
                 D1.OBJID::INT
                , D1.OBJDB::INT AS OBJDB
                , D1.OBJNAME
                , D1.DESCRIPTION
                , R.SCHEMA
                , D1.OBJTYPE
            FROM 
                {dbName}.._V_OBJECT_DATA D1
                JOIN {dbName}.._V_RELATION_COLUMN R ON D1.OBJID = R.OBJID
                    AND R.DATABASE = '{EscapeSqlLiteral(dbName)}'
            WHERE 
                D1.OBJTYPE NOT IN 
                ('AGGREGATE','CONSTRAINT','DATABASE','DATATYPE','GROUP','MANAGEMENT INDEX','MANAGEMENT SEQ','MANAGEMENT TABLE',
                'MANAGEMENT VIEW','SCHEDULER RULE','SCHEMA','SYSTEM INDEX','SYSTEM SEQ','SYSTEM TABLE','SYSTEM VIEW','USER')
                AND D1.OBJID NOT IN (4,5)
                AND D1.DBNAME = '{EscapeSqlLiteral(dbName)}'
                AND (UPPER(D1.OBJNAME) LIKE '%{search}%' OR UPPER(D1.DESCRIPTION) LIKE '%{search}%'
                    OR UPPER(R.ATTNAME) LIKE '%{search}%' OR UPPER(R.DESCRIPTION) LIKE '%{search}%'
            )
            """;
    }

    public static string GetLegacyFulidesSql(string databaseName, int databaseId)
    {
        databaseName = NormalizeDatabaseIdentifier(databaseName, nameof(databaseName));
        return $"""
            SELECT f.OWNER, f.FUNCTION, NVL(f.DESCRIPTION, '') as description, min(OBJID)
            FROM  {databaseName}.._V_FUNCTION f
            WHERE f.ENV like '%com.ibm.nz.fq.SqlReadLauncher%'
            AND DATABASEID = {databaseId}
            group by 1,2,3
            """;
    }
}
