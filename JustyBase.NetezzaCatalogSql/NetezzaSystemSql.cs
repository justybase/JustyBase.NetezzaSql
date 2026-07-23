namespace JustyBase.NetezzaCatalogSql;

/// <summary>
/// SQL used to administer and inspect a Netezza appliance.
/// This class deliberately returns SQL only; connection handling belongs to a host.
/// </summary>
public static class NetezzaSystemSql
{
    public const string CurrentCatalogSql = "SELECT CURRENT_CATALOG";
    public const string CurrentSessionIdSql = "SELECT current_sid";
    public const string UserGroupsSql = "SELECT GROUPNAME FROM _V_GROUPUSERS WHERE GROUPNAME != 'PUBLIC'";

    public static string SetCatalog(string database)
        => $"SET CATALOG {NetezzaSqlIdentifier.Database(database)};";

    public static string GetProcedureByObjectId(int objectId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectId);
        return $"SELECT LENGTH(PROCEDURESOURCE), PROCEDURESIGNATURE, RETURNS, EXECUTEDASOWNER, DESCRIPTION, PROCEDURESOURCE, ARGUMENTS FROM _V_PROCEDURE WHERE OBJTYPE = 'PROCEDURE' AND OBJID = {objectId}";
    }

    public static string GetProceduresForDdl(string database)
    {
        database = NetezzaSqlIdentifier.Database(database);
        var databaseValue = NetezzaSqlIdentifier.UnquotedDatabase(database);
        return $"SELECT SCHEMA, PROCEDURESOURCE, PROCEDURESIGNATURE, RETURNS, EXECUTEDASOWNER, DESCRIPTION, ARGUMENTS FROM {database}.._V_PROCEDURE WHERE OBJTYPE = 'PROCEDURE' AND DATABASE = '{NetezzaSqlIdentifier.Literal(databaseValue)}'";
    }

    public static string GetViewsForDdl(string database)
    {
        database = NetezzaSqlIdentifier.Database(database);
        var databaseValue = NetezzaSqlIdentifier.UnquotedDatabase(database);
        return $"SELECT SCHEMA, VIEWNAME, DEFINITION FROM {database}.._V_VIEW WHERE OBJTYPE IN ('SYSTEM VIEW','VIEW') AND DATABASE = '{NetezzaSqlIdentifier.Literal(databaseValue)}'";
    }

    public static string GetViewDefinitionLength(int objectId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectId);
        return $"SELECT LENGTH(DEFINITION) FROM _V_VIEW WHERE OBJTYPE IN ('VIEW','SYSTEM VIEW') AND OBJID = {objectId}";
    }

    public static string GetViewDefinitionByObjectId(int objectId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectId);
        return $"SELECT DEFINITION FROM _V_VIEW WHERE OBJTYPE IN ('VIEW','SYSTEM VIEW') AND OBJID = {objectId}";
    }

    public static string GetExternalOptions(int objectId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectId);
        return $"SELECT DELIM, ENCODING, TIMESTYLE, REMOTESOURCE, SKIPROWS, MAXERRORS, ESCAPE, LOGDIR, DECIMALDELIM, QUOTEDVALUE, NULLVALUE, CRINSTRING, TRUNCSTRING, CTRLCHARS, IGNOREZERO, TIMEEXTRAZEROS, Y2BASE, FILLRECORD, COMPRESS, INCLUDEHEADER, LFINSTRING, DATESTYLE, DATEDELIM, TIMEDELIM, BOOLSTYLE, FORMAT, SOCKETBUFSIZE, RECORDDELIM, MAXROWS, REQUIREQUOTES, RECORDLENGTH, DATETIMEDELIM, REJECTFILE FROM _V_EXTERNAL WHERE RELID = {objectId}";
    }

    public static string GetExternalObjectName(int objectId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectId);
        return $"SELECT EXTOBJNAME FROM _V_EXTOBJECT WHERE OBJID = {objectId}";
    }

    public static string GetLoadProgress(long rowCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowCount);
        return $"SELECT ROUND(100.*ROWSINSERTED/{rowCount},0) AS progress, * FROM _v_load_status WHERE USERNAME = USER;";
    }

    public const string ProcedureSearchTemplate = """
        SELECT P.OBJID, P.PROCEDURE, P.OWNER, P.CREATEDATE, P.OBJTYPE, P.DESCRIPTION,
               P.RESULT, P.NUMARGS, P.ARGUMENTS, P.PROCEDURESIGNATURE, P.BUILTIN,
               P.VARARGS, P.PROCEDURESOURCE, P.SPROC, P.EXECUTEDASOWNER, P.RETURNS,
               P.DATABASE, P.DATABASEID, P.SCHEMA, P.SCHEMAID
          FROM _V_PROCEDURE P
         WHERE UPPER(P.PROCEDURESOURCE) LIKE '%SOME_TEXT%';
        """;

    public const string ViewSearchTemplate = """
        SELECT V.OBJID, V.VIEWNAME, V.OWNER, V.CREATEDATE, V.OBJTYPE, V.OBJCLASS,
               V.DESCRIPTION, V.RELHASINDEX, V.RELKIND, V.RELCHECKS, V.RELTRIGGERS,
               V.RELHASRULES, V.RELUKEYS, V.RELFKEYS, V.RELREFS, V.RELHASPKEY,
               V.RELNATTS, V.DEFINITION, V.OBJDELIM, V.DATABASE, V.OBJDB, V.SCHEMA,
               V.SCHEMAID
          FROM _V_VIEW V
         WHERE UPPER(V.DEFINITION) LIKE '%SOME_TEXT%';
        """;

    public const string ServerInformation = """
        SELECT SYSTEM_STATE AS "System State", SYSTEM_STATE_VERSION AS "System State Version",
               SYSTEM_SOFTWARE_VERSION AS "System Software Version",
               SYSTEM_STATE_VALUE AS "System State Value", SYSTEM_VERSION_FULL AS "Full Version",
               SYSTEM_CAT_VERSION AS "System Catalog Version"
          FROM _V_SYSTEM_INFO
        """;

    public const string EnvironmentInformation = """
        SELECT EVR_NAME, EVR_ENABLED, EVR_EVTYPE, EVR_EVARGS, EVR_NTTYPE, EVR_NTDEST, EVR_NTCCDEST FROM _V_EVRULE ORDER BY EVR_NAME;
        SELECT name, val FROM _V_ENVIRON ORDER BY name;
        """;

    public const string HardwareInformation = """
        SELECT SUM(extent_count) * 3 AS total_disk_space_mbytes, SUM(extents_used) * 3 AS allocated_disk_space_mbytes FROM _vt_disk_partition GROUP BY hwid, dsid;

        SELECT HW_HWID, HW_PARENT, HW_TYPE, HW_TYPETEXT, HW_STATE, HW_STATETEXT,
               HW_ROLE, HW_ROLETEXT, HW_SPAID, HW_POSITION, HW_RACKID, HW_RACKPOS, HW_SERNUM,
               HW_DISKMODEL
          FROM _V_HWCOMP
         ORDER BY HW_PARENT, HW_POSITION;

        SELECT spa.HW_SPAID, spa.HW_HWID AS "Hardware ID", spa.HW_RACKID AS "Rack", 'NULL' AS "Serial #",
               'NULL' AS "Hardware Version", 'NULL' AS "Flash Version", 'NULL' AS "App Version",
               'NULL' AS "IP Address", 'NULL' AS "MAC Address",
               nvl(spu.TotalCount, 0) AS "SPUs", nvl(spuspare.SpareCount, 0) AS "Spares",
               nvl(spufail.FailedCount, 0) AS "Failed SPUs"
          FROM _V_SPA spa
          JOIN (SELECT HW_SPAID, count(*) AS TotalCount FROM _V_SPU GROUP BY HW_SPAID) spu ON spu.HW_SPAID = spa.HW_SPAID
          LEFT JOIN (SELECT HW_SPAID, count(*) AS SpareCount FROM _V_SPU WHERE HW_ROLE = 3 GROUP BY HW_SPAID) spuspare ON spuspare.HW_SPAID = spa.HW_SPAID
          LEFT JOIN (SELECT HW_SPAID, count(*) AS FailedCount FROM _V_SPU WHERE HW_STATE = 22 GROUP BY HW_SPAID) spufail ON spufail.HW_SPAID = spa.HW_SPAID
         ORDER BY spa.HW_SPAID;

        SELECT spu.HW_SPAID, spu.HW_HWID AS "Hardware ID", spu.HW_POSITION AS "Position", spu.HW_SERNUM AS "Serial #",
               spu.HW_ROLETEXT AS "Role", spu.HW_STATETEXT AS "State",
               spu.HW_TOTALMEMORY AS "Total Memory", spu.HW_NUMFPGA AS "FPGA Count", spu.HW_FPGAVER AS "FPGA Version",
               spu.HW_HWVER AS "Hardware Version", spu.HW_IPADDRTXT AS "IP Address",
               spu.HW_MACADDRTXT AS "MAC Address", 'NULL' AS "DAC Serial #",
               'NULL' AS Flash_Version, 'NULL' AS "App Version", 'NULL' AS "Disk Serial", 'NULL' AS "Disk Model"
          FROM _V_SPU spu
         ORDER BY spu.HW_SPAID, spu.HW_POSITION, spu.HW_HWID;
        """;

    public const string Users = "SELECT * FROM _v_user;";
    public const string GroupUsers = "SELECT * FROM _v_groupusers;";
    public const string UserSecurity = "SELECT * FROM _v_user_security;";
    public const string DataSliceCount = "SELECT count(distinct DS_SUPPORTINGDISKS) FROM _v_dslice";
    public const string AllNonSystemTables = "SELECT '\"' || DATABASE || '\".' || '\"' || SCHEMA || '\".' || '\"' || TABLENAME || '\"' FROM _v_table WHERE DATABASE != 'SYSTEM'";
    public const string SearchViewsTemplate = "SELECT OBJTYPE AS \"Type\", VIEWNAME AS \"Name\", DATABASE AS \"Db\", DESCRIPTION AS \"Desc\", OWNER AS \"Schema\" FROM YYY.._V_VIEW WHERE UPPER(DEFINITION) LIKE '%X1X2X3%' AND OBJTYPE = 'VIEW' AND DATABASE = 'YYY'";
    public const string SearchProceduresTemplate = "SELECT OBJTYPE AS \"Type\", PROCEDURE AS \"Name\", DATABASE AS \"Db\", DESCRIPTION AS \"Desc\", OWNER AS \"Schema\" FROM YYY.._V_PROCEDURE WHERE UPPER(PROCEDURESOURCE) LIKE '%X1X2X3%' AND OBJTYPE = 'PROCEDURE' AND DATABASE = 'YYY'";

    public static string GetTableSizesReport(string database)
        => $"{SetCatalog(database)}\r\n\r\nSELECT OBJID, TABLENAME, OWNER, CREATEDATE, RELNATTS, ALLOCATED_BYTES::bigint AS ALLOCATED_BYTES, USED_BYTES::bigint AS USED_BYTES, SKEW, CAST(NULL AS NUMERIC) AS ROW_COUNT, ALLOCATED_BLOCKS, USED_BLOCKS, BLOCK_SIZE, USED_MIN, USED_MAX, USED_AVG, RELDISTMETHOD, MATER_COUNT, MATER_BLOCKS, MATER_BYTES, MATER_OVERHEAD FROM _V_TABLE_STORAGE_STAT WHERE UPPER(OBJTYPE) = 'TABLE' ORDER BY TABLENAME, OWNER;\r\n\r\nSELECT RELNAME, RELREFS, RELTUPLES FROM _T_CLASS;\r\n\r\nSELECT o.OBJNAME AS TABLENAME, o.OWNER, z.DSID, z.HWID, NULL AS DATA_PART, z.ALLOCATED_BLOCKS, z.USED_BLOCKS, z.ALLOCATED_BYTES::bigint AS ALLOCATED_BYTES, z.USED_BYTES::bigint AS USED_BYTES, z.SORTED_BLOCKS, z.SORTED_BYTES FROM _V_SYS_OBJECT_DSLICE_INFO z JOIN _v_object_data o ON o.objid = z.tblid WHERE o.objdb = current_db;";

    public static string GetQueryHistory(string database)
        => $"SELECT QH_SESSIONID, QH_PLANID, QH_CLIIPADDR, QH_DATABASE, QH_USER, QH_SQL, QH_TSUBMIT, QH_TSTART, QH_TEND, QH_PRITXT, QH_ESTCOST, QH_ESTDISK, QH_ESTMEM, QH_SNIPPETS, QH_SNPTSDONE, QH_RESROWS, QH_RESBYTES FROM _V_QRYHIST WHERE QH_DATABASE = '{NetezzaSqlIdentifier.Literal(NetezzaSqlIdentifier.Database(database))}'";

    public static string GetUserSessions(string database)
        => $"SELECT s.id AS \"Session ID\", s.pid AS \"PID\", s.username AS \"Login\", s.dbname AS \"Database\", s.status AS \"Status\", s.type AS \"Type\", s.conntime AS \"Connected\", s.priority AS \"Priority\", s.CID AS \"CID\", s.ipaddr AS \"Host\", s.command AS \"Command\", h.QH_PLANID AS \"Plan ID\", h.QH_TSUBMIT AS \"Submitted\", h.QH_TSTART AS \"Started\", h.QH_TEND AS \"Finished\", h.QH_ESTCOST AS \"Est. cost\", h.QH_ESTDISK AS \"Est. disk\", h.QH_ESTMEM AS \"Est. mem.\", h.QH_SNIPPETS AS \"Snippets\", h.QH_SNPTSDONE AS \"Snippets done\", h.QH_RESROWS AS \"Res. rows\", h.QH_RESBYTES AS \"Res. bytes\" FROM _V_SESSION s LEFT JOIN (SELECT h.QH_SESSIONID AS SESSION_ID, max(h.QH_PLANID) AS PLAN_ID FROM _V_QRYHIST h WHERE h.QH_SESSIONID IN (SELECT ID FROM _V_SESSION) GROUP BY h.QH_SESSIONID) p ON p.SESSION_ID = s.ID LEFT JOIN _V_QRYHIST h ON h.QH_SESSIONID = s.ID AND h.QH_PLANID = p.PLAN_ID WHERE s.dbname = '{NetezzaSqlIdentifier.Literal(NetezzaSqlIdentifier.Database(database))}'";

    public static string GetGroomTableCandidates(string database)
        => $"{SetCatalog(database)}\nSELECT CURRENT_CATALOG, OBJID, TABLENAME, SCHEMA, ALLOCATED_BYTES, USED_BYTES, 'GROOM TABLE \"' || CURRENT_CATALOG || '\".\"' || SCHEMA || '\".\"' || TABLENAME || '\" RECORDS ALL RECLAIM BACKUPSET NONE;' AS GROOM_CODE FROM _V_TABLE_STORAGE_STAT WHERE UPPER(OBJTYPE) = 'TABLE' AND OBJID IS NOT NULL ORDER BY TABLENAME, SCHEMA\n";

    public static string GetDatabaseSize(string database)
        => $"{SetCatalog(database)}\nSELECT CURRENT_CATALOG, SUM(USED_BYTES::bigint)/1024/1024 AS USED_BYTES_MB, SUM(ALLOCATED_BYTES)/1024/1024 AS ALLOCATED_BYTES, SUM(SKEW * USED_BYTES)/NULLIF(SUM(USED_BYTES),0) AS SKEW_AVG FROM _V_TABLE_STORAGE_STAT WHERE UPPER(OBJTYPE) = 'TABLE';\n";

    public static string GetDistributionWithDeletedRecords(string database, string table)
        => $"SET show_deleted_records = 1;SELECT datasliceid, COUNT(1), COUNT(NULLIF(deletexid,0)) FROM {NetezzaSqlIdentifier.Database(database)}..{NetezzaSqlIdentifier.Object(table)} GROUP BY datasliceid ORDER BY COUNT(1) DESC;SET show_deleted_records = 0;";

    public static string GetTableStorageStatistics(string table)
        => $"SELECT OBJID::BIGINT AS OBJID, SKEW::DOUBLE, CREATEDATE::DATETIME AS CREATEDATE, ALLOCATED_BYTES::BIGINT AS ALLOCATED_BYTES, USED_BYTES::BIGINT AS USED_BYTES, ALLOCATED_BLOCKS, USED_BLOCKS, BLOCK_SIZE, USED_MIN, USED_MAX, USED_AVG FROM _V_TABLE_STORAGE_STAT WHERE UPPER(OBJTYPE) = 'TABLE' AND UPPER(TABLENAME) = '{NetezzaSqlIdentifier.Literal(table.ToUpperInvariant())}' ORDER BY TABLENAME, OWNER;";

    public static string GetSequenceMetadata(string sequence)
        => $"SELECT d.type_name, s.LAST_VALUE::varchar(50) AS START, s.INCREMENT_BY::varchar(50) AS INCREMENT, s.MIN_VALUE::varchar(50) AS MIN_VALUE, s.MAX_VALUE::varchar(50) AS MAX_VALUE, CASE istrue(s.IS_CYCLED) WHEN true THEN 1 ELSE 0 END::int AS IS_CYCLED FROM {NetezzaSqlIdentifier.Object(sequence)} s JOIN _v_sys_datatype d ON d.type_oid = s.DATATYPE;";

    public static string GetFunctionInfo(string function)
        => $"SELECT OBJID::INT, ARGUMENTS || ' -> ' || RESULT AS WYN FROM _v_function WHERE FUNCTION = '{NetezzaSqlIdentifier.Literal(function)}';";

    public static string GetAggregateInfo(string aggregate)
        => $"SELECT OBJID::INT, ARGUMENTS || ' -> ' || RETURN_TYPE AS WYN FROM _v_aggregate WHERE AGGREGATE = '{NetezzaSqlIdentifier.Literal(aggregate)}';";

    public const string SequenceInfo = "SELECT SEQ_ID, 'DATA_TYPE:' || DATA_TYPE || '    DATA_TYPE: ' || DATA_TYPE || '    MIN_VALUE: ' || MIN_VALUE || '    MAX_VALUE: ' || MAX_VALUE || '    INCREMENT: ' || INCREMENT || '    CACHE_SIZE: ' || CACHE_SIZE || '    NEXT_CACHE_VAL: ' || NEXT_CACHE_VAL || '    FLAGS: ' || FLAGS AS DANE FROM _vt_sequence;";
    public const string SynonymInfo = "SELECT OBJID::INT, REFDATABASE || '..' || REFOBJNAME FROM _v_synonym;";

    public static string GetEstimatedQueryCost(int processId, int backendProcessId = -1)
    {
        if (backendProcessId >= 0)
            return $"SELECT qs_estcost FROM _v_qrystat qs JOIN _v_session s ON s.id = qs.qs_sessionid WHERE s.pid = {backendProcessId}";
        ArgumentOutOfRangeException.ThrowIfNegative(processId);
        return $"SELECT qs_estcost FROM _v_qrystat WHERE QS_SESSIONID = {processId};";
    }
}

internal static class NetezzaSqlIdentifier
{
    public static string Database(string database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var normalized = database.StartsWith('"') ? database : database.ToUpperInvariant();
        if (normalized.StartsWith('"'))
        {
            if (normalized.Length < 2 || !normalized.EndsWith('"'))
                throw new ArgumentException("A quoted database identifier must have matching quotes.", nameof(database));
            return normalized;
        }

        if (!char.IsLetter(normalized[0]) && normalized[0] != '_')
            throw new ArgumentException("Database identifiers must start with a letter or underscore.", nameof(database));
        if (normalized.Any(c => !char.IsLetterOrDigit(c) && c is not '_' and not '$' and not '#'))
            throw new ArgumentException("Database identifier contains an invalid character.", nameof(database));
        return normalized;
    }

    public static string Literal(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    public static string UnquotedDatabase(string database)
        => database.StartsWith('"')
            ? database[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
            : database;

    public static string Object(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.StartsWith('"') ? value : value.ToUpperInvariant();
        if (normalized.StartsWith('"'))
        {
            if (normalized.Length < 2 || !normalized.EndsWith('"'))
                throw new ArgumentException("A quoted object identifier must have matching quotes.", nameof(value));
            return normalized;
        }

        if (!char.IsLetter(normalized[0]) && normalized[0] != '_')
            throw new ArgumentException("Object identifiers must start with a letter or underscore.", nameof(value));
        if (normalized.Any(c => !char.IsLetterOrDigit(c) && c is not '_' and not '$' and not '#'))
            throw new ArgumentException("Object identifier contains an invalid character.", nameof(value));
        return normalized;
    }
}
