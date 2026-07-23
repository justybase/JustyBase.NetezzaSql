namespace JustyBase.NetezzaCatalogSql;

/// <summary>
/// Netezza catalog/metadata SQL used by Avalonia (PluginBase cache) and shared constants.
/// </summary>
public static partial class NetezzaCatalogSql
{
    public const string DatabasesSql = """
        SELECT 
            OBJID::INT AS OBJID
            , DEFSCHEMAID::INT AS DEFSCHEMAID
            , DATABASE
            , OWNER
            , DEFSCHEMA
        FROM 
            _v_database
        ORDER BY
            DATABASE
        """;

    public const string ViewDefinitionByObjectIdSql = """
        SELECT DEFINITION FROM _V_VIEW WHERE OBJTYPE IN ('VIEW','SYSTEM VIEW') AND OBJID = 
        """;

    public const string DataAktSql = "CREATE TABLE IF NOT EXISTS DATA_AKT (DATKA REAL);";

    public const string SessionSql = "SELECT current_sid";

    public const string CostSql = "SELECT NVL(MAX(QS_ESTCOST),0) FROM _V_QRYSTAT WHERE QS_SESSIONID = ___YYY___";

    public const string UserGroupsSql = "SELECT GROUPNAME FROM _V_GROUPUSERS WHERE GROUPNAME != 'PUBLIC'";

    public static string GetSqlTablesAndOtherObjects(string dbName)
    {
        dbName = NormalizeDatabaseIdentifier(dbName, nameof(dbName));
        string dbLiteral = EscapeSqlLiteral(dbName);
        bool noDescMode = dbName == "SYSTEM";
        bool ownerMode = false;

        string ownerOrSchema;

        if (dbName == "SYSTEM")
        {
            ownerOrSchema = """
                CASE WHEN D1.SCHEMAID = 4 THEN 'DEFINITION_SCHEMA' 
                 WHEN D1.SCHEMAID = 5 THEN 'INFORMATION_SCHEMA'
                WHEN D1.SCHEMA IS NULL OR D1.SCHEMA = '' THEN 'ADMIN' ELSE D1.SCHEMA END AS SCHEMA_X
                """;
        }
        else if (!ownerMode)
        {
            ownerOrSchema = "CASE WHEN D1.SCHEMA IS NULL OR D1.SCHEMA = '' THEN 'ADMIN' ELSE D1.SCHEMA END AS SCHEMA_X";
        }
        else
        {
            ownerOrSchema = " CASE WHEN D1.OWNER IS NULL OR D1.OWNER = '' THEN 'ADMIN' ELSE D1.OWNER END AS SCHEMA_X";
        }

        string dbWhere = $"AND D1.DBNAME = '{dbLiteral}'";
        if (noDescMode)
        {
            string systemSql =
                $"""
                    SELECT 
                        OBJID::INT AS OBJID
                        , OBJNAME
                        , D1.DESCRIPTION
                        , {ownerOrSchema}
                        , CASE OBJTYPE
                        WHEN  'SYSTEM TABLE' THEN 'TABLE'
                        WHEN 'SYSTEM VIEW' THEN 'VIEW' END 
                        , D1.OWNER
                        , D1.CREATEDATE
                    FROM SYSTEM.._V_OBJECT_DATA D1
                    WHERE OBJTYPE IN ('SYSTEM TABLE','SYSTEM VIEW')
                    AND DBNAME = 'SYSTEM'
                """;
            dbWhere += $" UNION ALL {systemSql}";
        }

        string sql =
            $"""
                SELECT 
                        D1.OBJID::INT AS OBJID
                    , COALESCE(F.FUNCTIONSIGNATURE, PR.PROCEDURESIGNATURE,D1.OBJNAME) AS OBJNAME_
                    , D1.DESCRIPTION
                    , {ownerOrSchema}
                    , CASE WHEN f.ENV like '%com.ibm.nz.fq.SqlReadLauncher%' THEN 'FLUID' ELSE D1.OBJTYPE END AS OBJTYPE_
                    , D1.OWNER
                    , D1.CREATEDATE
                FROM 
                    {dbName}.._V_OBJECT_DATA D1
                    LEFT JOIN {dbName}.._V_PROCEDURE PR ON PR.OBJID = D1.OBJID AND PR.DATABASE = '{dbLiteral}'
                    LEFT JOIN {dbName}.._V_FUNCTION F ON F.OBJID = D1.OBJID AND F.DATABASE = '{dbLiteral}'
                WHERE 
                    D1.OBJTYPE NOT IN
                    ('AGGREGATE','CONSTRAINT','DATABASE','DATATYPE','GROUP','MANAGEMENT INDEX','MANAGEMENT SEQ','MANAGEMENT TABLE',
                    'MANAGEMENT VIEW','SCHEDULER RULE','SCHEMA','SYSTEM INDEX','SYSTEM SEQ','SYSTEM TABLE','SYSTEM VIEW','USER')
                AND D1.OBJID NOT IN (4,5)
                {dbWhere}
                ORDER BY SCHEMA_X, OBJTYPE_, OBJNAME_
            """;

        if (dbName == "SYSTEM" && !noDescMode)
        {
            sql =
                """
                    SELECT 
                        OBJID::INT AS OBJID
                        , OBJNAME
                        , D1.DESCRIPTION
                        , OWNER
                        , CASE OBJTYPE
                       WHEN  'SYSTEM TABLE' THEN 'TABLE'
                       WHEN 'SYSTEM VIEW' THEN 'VIEW' END 
                        , D1.OWNER
                        , D1.CREATEDATE
                    FROM SYSTEM.._V_OBJECT_DATA D1
                    WHERE OBJTYPE IN ('SYSTEM TABLE','SYSTEM VIEW')
                    AND DBNAME = 'SYSTEM'
                """;
        }

        return sql;
    }

    public static string GetSqlOfColumns(string dbName)
    {
        dbName = NormalizeDatabaseIdentifier(dbName, nameof(dbName));
        string dbLiteral = EscapeSqlLiteral(dbName);

        return
            $"""
            SELECT 
                    X.OBJID::INT AS OBJID
                    , X.ATTNAME
                    , X.DESCRIPTION
                    , CASE WHEN X.ATTNOTNULL THEN X.FORMAT_TYPE || ' NOT NULL'  ELSE X.FORMAT_TYPE END
                    , X.ATTNOTNULL::BOOL AS ATTNOTNULL
                    , X.COLDEFAULT
                FROM
                    {dbName}.._V_RELATION_COLUMN X
                WHERE
                    X.TYPE IN ('TABLE','VIEW','EXTERNAL TABLE', 'SEQUENCE','SYSTEM VIEW','SYSTEM TABLE')
                    AND X.OBJID NOT IN (4,5)
                    AND DATABASE = '{dbLiteral}'
                ORDER BY 
                    X.OBJID, X.ATTNUM
            """;
    }

    /// <summary>Lists schemas from the database-local Netezza schema view.</summary>
    public static string GetSchemasSql(string database)
    {
        database = NormalizeDatabaseIdentifier(database, nameof(database));
        return $"SELECT SCHEMA FROM {database}.._V_SCHEMA WHERE DATABASE = '{EscapeSqlLiteral(database)}' ORDER BY SCHEMA";
    }

    /// <summary>Lists object types available in a database.</summary>
    public static string GetObjectTypesSql(string database)
    {
        database = NormalizeDatabaseIdentifier(database, nameof(database));
        return $"SELECT DISTINCT OBJTYPE FROM {database}.._V_OBJECT_DATA WHERE DBNAME = '{EscapeSqlLiteral(database)}' ORDER BY OBJTYPE";
    }

    /// <summary>
    /// Returns storage and skew metadata used when deciding whether a table
    /// should be reorganized or redistributed.
    /// </summary>
    public static string GetTableStorageStatsSql(
        string database,
        string? schema = null,
        string? tableName = null)
    {
        database = NormalizeDatabaseIdentifier(database, nameof(database));
        var predicates = new List<string> { $"DATABASE = '{EscapeSqlLiteral(database)}'" };
        if (!string.IsNullOrWhiteSpace(schema))
            predicates.Add($"SCHEMA = '{EscapeSqlLiteral(schema)}'");
        if (!string.IsNullOrWhiteSpace(tableName))
            predicates.Add($"TABLENAME = '{EscapeSqlLiteral(tableName)}'");

        return $"""
            SELECT DATABASE, SCHEMA, TABLENAME, OBJID::INT AS OBJID,
                   SKEW, ALLOCATED_BYTES, USED_BYTES, ALLOCATED_BLOCKS,
                   USED_BLOCKS, BLOCK_SIZE, USED_MIN, USED_MAX, USED_AVG
            FROM {database}.._V_TABLE_STORAGE_STAT
            WHERE {string.Join(" AND ", predicates)}
            ORDER BY SCHEMA, TABLENAME
            """;
    }

    /// <summary>Returns object descriptions with the required database filter.</summary>
    public static string GetObjectDescriptionsSql(string database, string? schema = null)
    {
        database = NormalizeDatabaseIdentifier(database, nameof(database));
        var predicates = new List<string> { $"DBNAME = '{EscapeSqlLiteral(database)}'" };
        if (!string.IsNullOrWhiteSpace(schema))
            predicates.Add($"SCHEMA = '{EscapeSqlLiteral(schema)}'");

        return $"""
            SELECT OBJID::INT AS OBJID, OBJTYPE, OBJNAME, SCHEMA, OWNER, DESCRIPTION
            FROM {database}.._V_OBJECT_DATA
            WHERE {string.Join(" AND ", predicates)}
              AND DESCRIPTION IS NOT NULL
            ORDER BY SCHEMA, OBJTYPE, OBJNAME
            """;
    }

    public static string GetProceduresSql(string database, string objectFilterName)
    {
        database = NormalizeDatabaseIdentifier(database);
        string whereOnSpecificObject = "";
        if (!string.IsNullOrEmpty(objectFilterName))
            whereOnSpecificObject = $" AND PROCEDURESIGNATURE = '{EscapeSqlLiteral(objectFilterName)}'";

        return
            $"""
                SELECT SCHEMA,PROCEDURESOURCE,OBJID::INT 
                    ,RETURNS, EXECUTEDASOWNER, DESCRIPTION, PROCEDURESIGNATURE, ARGUMENTS, NULL AS LANGUAGE
                FROM {database}.._V_PROCEDURE
                    WHERE DATABASE = '{EscapeSqlLiteral(database)}'{whereOnSpecificObject}
                    ORDER BY 1,2,3;
                """;
    }

    public static string GetSynonymSql(string database)
    {
        database = NormalizeDatabaseIdentifier(database);
        return $"SELECT SCHEMA,SYNONYM_NAME, REFOBJNAME, REFDATABASE, REFSCHEMA, DESCRIPTION FROM {database}.._V_SYNONYM WHERE DATABASE = '{EscapeSqlLiteral(database)}'";
    }

    public static string GetViewsSql(string database, string objectFilterName)
    {
        database = NormalizeDatabaseIdentifier(database);
        string whereOnSpecificObject = "";
        if (!string.IsNullOrEmpty(objectFilterName))
            whereOnSpecificObject = $" AND VIEWNAME = '{EscapeSqlLiteral(objectFilterName)}'";

        return
            $"""
                SELECT SCHEMA,VIEWNAME, DEFINITION FROM {database}.._V_VIEW
                    WHERE DATABASE = '{EscapeSqlLiteral(database)}'{whereOnSpecificObject}
                    ORDER BY 1,2,3;
            """;
    }

    public static string GetExternalTableSql(string database)
    {
        database = NormalizeDatabaseIdentifier(database);
        return
            $"""
            SELECT 
                    E1.SCHEMA
                    , E1.TABLENAME
                    , E2.EXTOBJNAME
                    , E2.OBJID::INT
                    , E1.DELIM
                    , E1.ENCODING
                    , E1.TIMESTYLE
                    , E1.REMOTESOURCE
                    , E1.SKIPROWS
                    , E1.MAXERRORS
                    , E1.ESCAPE
                    , E1.LOGDIR
                    , E1.DECIMALDELIM
                    , E1.QUOTEDVALUE
                    , E1.NULLVALUE
                    , E1.CRINSTRING
                    , E1.TRUNCSTRING
                    , E1.CTRLCHARS
                    , E1.IGNOREZERO
                    , E1.TIMEEXTRAZEROS
                    , E1.Y2BASE
                    , E1.FILLRECORD
                    , E1.COMPRESS
                    , E1.INCLUDEHEADER
                    , E1.LFINSTRING
                    , E1.DATESTYLE
                    , E1.DATEDELIM
                    , E1.TIMEDELIM
                    , E1.BOOLSTYLE
                    , E1.FORMAT
                    , E1.SOCKETBUFSIZE
                    , E1.RECORDDELIM
                    , E1.MAXROWS
                    , E1.REQUIREQUOTES
                    , E1.RECORDLENGTH
                    , E1.DATETIMEDELIM
                    , E1.NULLINDICATOR
                    , E1.REJECTFILE 
                FROM 
                    {database}.._V_EXTERNAL E1
                    JOIN {database}.._V_EXTOBJECT E2 ON E1.DATABASE = E2.DATABASE
                        AND E1.SCHEMA = E2.SCHEMA
                        AND E1.TABLENAME = E2.TABLENAME
                WHERE 
                    E1.DATABASE = '{EscapeSqlLiteral(database)}';
            """;
    }

    public static string GetKeysSql(string databaseName)
    {
        databaseName = NormalizeDatabaseIdentifier(databaseName, nameof(databaseName));
        return
            $"""
             SELECT 
                    X.SCHEMA
                    , X.RELATION
                    , X.CONSTRAINTNAME
                    , X.CONTYPE
                    , X.ATTNAME
                    , X.PKDATABASE
                    , X.PKSCHEMA
                    , X.PKRELATION
                    , X.PKATTNAME
                	, X.UPDT_TYPE
                	, X.DEL_TYPE
                FROM 
                    {databaseName}.._V_RELATION_KEYDATA X
                WHERE 
                    X.OBJID NOT IN (4,5)
                    AND X.DATABASE = '{EscapeSqlLiteral(databaseName)}'
                ORDER BY
                    X.SCHEMA, X.RELATION, X.CONSEQ
            """;
    }

    public static string GetDistributeSql(string databaseName)
    {
        databaseName = NormalizeDatabaseIdentifier(databaseName, nameof(databaseName));
        return
            $"""
            SELECT 
                    SCHEMA
                    , TABLENAME
                    , DISTATTNUM
                    , ATTNAME
                FROM 
                    {databaseName}.._V_TABLE_DIST_MAP
                WHERE 
                    DATABASE = '{EscapeSqlLiteral(databaseName)}'
                ORDER BY 
                    SCHEMA, TABLENAME, DISTSEQNO
            """;
    }

    public static string GetOrganizeSql(string databaseName)
    {
        databaseName = NormalizeDatabaseIdentifier(databaseName, nameof(databaseName));
        return
            $"""
            SELECT 
                    SCHEMA
                    , TABLENAME
                    , ATTNUM
                    , ATTNAME
                FROM 
                    {databaseName}.._V_TABLE_ORGANIZE_COLUMN
                ORDER BY 
                    SCHEMA, TABLENAME, ORGSEQNO;
            """;
    }

    public static string GetDescSql(string dbName)
    {
        dbName = NormalizeDatabaseIdentifier(dbName, nameof(dbName));

        return $"SELECT OBJID::INT, DESCRIPTION FROM {dbName}.._V_OBJECT_DATA WHERE DESCRIPTION IS NOT NULL AND DBNAME = '{EscapeSqlLiteral(dbName)}' AND OBJID NOT IN (4,5,6,7)";
    }

    public static string GetLegacyProcSql(string database)
    {
        database = NormalizeDatabaseIdentifier(database);
        return $"SELECT SCHEMA,OWNER,PROCEDURE,PROCEDURESOURCE,DESCRIPTION FROM {database}.._V_PROCEDURE WHERE DATABASE = '{EscapeSqlLiteral(database)}'";
    }

    public static string GetLegacySynonymSql(string database)
    {
        database = NormalizeDatabaseIdentifier(database);
        return $"SELECT SCHEMA,OWNER,SYNONYM_NAME,REFOBJNAME,DESCRIPTION FROM {database}.._V_SYNONYM WHERE DATABASE = '{EscapeSqlLiteral(database)}'";
    }

    public static string GetLegacyViewSql(string database)
    {
        database = NormalizeDatabaseIdentifier(database);
        return $"SELECT SCHEMA,OWNER,VIEWNAME,DEFINITION,DESCRIPTION FROM {database}.._V_VIEW WHERE OBJTYPE IN ('SYSTEM VIEW','VIEW') AND DATABASE = '{EscapeSqlLiteral(database)}'";
    }

    public static string GetLegacyExternalSql(string database)
    {
        database = NormalizeDatabaseIdentifier(database);
        string databaseLiteral = EscapeSqlLiteral(database);
        return $"SELECT E.SCHEMA,E.OWNER,E.TABLENAME,E.EXTOBJNAME,T.DESCRIPTION\r\nFROM {database}.._V_EXTOBJECT E\r\nJOIN {database}.._V_TABLE T ON T.OBJID = E.OBJID\r\nWHERE E.DATABASE = '{databaseLiteral}' AND T.DATABASE = '{databaseLiteral}'";
    }
}
