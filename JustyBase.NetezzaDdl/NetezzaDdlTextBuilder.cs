using System.Text;
using JustyBase.NetezzaDdl.Models;

namespace JustyBase.NetezzaDdl;

public sealed class NetezzaDdlTextBuilder
{
    public string BuildCreateTable(NetezzaTableDdlInput input, IReadOnlyList<string>? distOverride = null)
    {
        var sb = new StringBuilder();
        AppendCreateTable(sb, input, distOverride);
        return sb.ToString();
    }

    public NetezzaDdlBuildResult AppendCreateTable(
        StringBuilder sb,
        NetezzaTableDdlInput input,
        IReadOnlyList<string>? distOverride = null)
    {
        if (input.Columns.Count == 0)
        {
            sb.AppendLine($"-- Table {input.Database}.{input.Schema}.{input.TableName} has no columns or was not found");
            return new NetezzaDdlBuildResult([], []);
        }

        var (cleanDatabase, cleanSchema, cleanTableName) = NetezzaNameHelper.GetCleanedNames(
            input.Database,
            input.Schema,
            input.OverrideTableName ?? input.TableName);

        sb.AppendLine($"CREATE TABLE {cleanDatabase}.{cleanSchema}.{cleanTableName} ");
        sb.AppendLine("(");

        var columnLines = input.Columns.Select(column =>
        {
            string line = $"{NetezzaNameHelper.QuoteNameIfNeeded(column.Name)} {column.FullTypeName}";
            if (column.NotNull)
                line += " NOT NULL";
            if (!string.IsNullOrWhiteSpace(column.ColDefault))
                line += $" DEFAULT {column.ColDefault}";
            return line;
        }).ToList();

        sb.Append("    ");
        sb.Append(string.Join(",\n    ", columnLines));

        var distributeColumns = ResolveDistributeColumns(input.DistributeColumns, distOverride);
        if (distributeColumns.Count > 0)
        {
            var cleanDist = string.Join(", ", distributeColumns.Select(NetezzaNameHelper.QuoteNameIfNeeded));
            sb.AppendLine($"\n)\nDISTRIBUTE ON ({cleanDist})");
        }
        else
        {
            sb.AppendLine("\n)\nDISTRIBUTE ON RANDOM");
        }

        if (input.OrganizeColumns is { Count: > 0 })
        {
            var cleanOrg = string.Join(", ", input.OrganizeColumns.Select(NetezzaNameHelper.QuoteNameIfNeeded));
            sb.AppendLine($"ORGANIZE ON ({cleanOrg})");
        }

        sb.AppendLine(";");

        if (!string.IsNullOrEmpty(input.MiddleCode))
            sb.AppendLine(input.MiddleCode);

        AppendKeys(sb, cleanDatabase, cleanSchema, cleanTableName, input.Keys);
        AppendTableAndColumnComments(sb, cleanDatabase, cleanSchema, cleanTableName, input.Columns, input.TableComment);

        if (!string.IsNullOrEmpty(input.EndingCode))
            sb.AppendLine(input.EndingCode);

        return new NetezzaDdlBuildResult(
            DistributeColumns: distributeColumns,
            OrganizeColumns: input.OrganizeColumns?.ToList() ?? []);
    }

    public NetezzaDdlBuildResult AppendRecreateTable(StringBuilder sb, NetezzaTableDdlInput input)
    {
        string tempName = NetezzaNameHelper.RandomTempName("TMP_");
        string tempName2;
        do
        {
            tempName2 = NetezzaNameHelper.RandomTempName("TMP_");
        } while (tempName2 == tempName);

        var (cleanDatabase, cleanSchema, cleanTableName) = NetezzaNameHelper.GetCleanedNames(
            input.Database,
            input.Schema,
            input.TableName);

        var middleCode = new StringBuilder();
        middleCode.AppendLine($"INSERT INTO {cleanDatabase}.{cleanSchema}.{tempName} SELECT * FROM {cleanDatabase}.{cleanSchema}.{cleanTableName};");
        middleCode.AppendLine($"ALTER TABLE {cleanDatabase}.{cleanSchema}.{tempName} SET PRIVILEGES TO {cleanDatabase}.{cleanSchema}.{cleanTableName};");
        middleCode.AppendLine($"ALTER TABLE {cleanDatabase}.{cleanSchema}.{cleanTableName} RENAME TO {cleanDatabase}.{cleanSchema}.{tempName2};");
        middleCode.AppendLine($"ALTER TABLE {cleanDatabase}.{cleanSchema}.{tempName} RENAME TO {cleanDatabase}.{cleanSchema}.{cleanTableName};");

        string owner = NetezzaNameHelper.QuoteNameIfNeeded(input.TableOwner ?? input.Schema);
        middleCode.AppendLine($"ALTER TABLE {cleanDatabase}.{cleanSchema}.{cleanTableName} OWNER TO {owner};");
        middleCode.AppendLine($"DROP TABLE {cleanDatabase}.{cleanSchema}.{tempName2};");

        var createInput = input with
        {
            OverrideTableName = tempName,
            MiddleCode = middleCode.ToString(),
            EndingCode = $"GENERATE EXPRESS STATISTICS ON {cleanDatabase}.{cleanSchema}.{cleanTableName};{Environment.NewLine}"
        };

        return AppendCreateTable(sb, createInput);
    }

    public string BuildRecreateTable(NetezzaTableDdlInput input)
    {
        var sb = new StringBuilder();
        AppendRecreateTable(sb, input);
        return sb.ToString();
    }

    public void AppendCreateExternal(StringBuilder sb, NetezzaExternalDdlInput input)
    {
        var (cleanDatabase, cleanSchema, cleanTableName) = NetezzaNameHelper.GetCleanedNames(
            input.Database,
            input.Schema,
            input.TableName);

        sb.AppendLine($"CREATE EXTERNAL TABLE {cleanDatabase}.{cleanSchema}.{cleanTableName}");
        sb.AppendLine("(");
        sb.AppendLine(string.Join($",{Environment.NewLine}", input.Columns.Select(o =>
            $"    {NetezzaNameHelper.QuoteNameIfNeeded(o.Name)} {o.FullTypeName}{(o.NotNull ? " NOT NULL" : string.Empty)}")));
        sb.AppendLine(")");
        sb.AppendLine("USING");
        sb.AppendLine("(");

        var options = input.Options;
        if (!string.IsNullOrEmpty(options.DataObject))
            sb.AppendLine($"    DATAOBJECT('{NetezzaNameHelper.EscapeLiteral(options.DataObject)}')");
        if (!string.IsNullOrEmpty(options.Delimiter))
            sb.AppendLine($"    DELIMITER '{NetezzaNameHelper.EscapeLiteral(options.Delimiter)}'");
        if (!string.IsNullOrEmpty(options.Encoding))
            sb.AppendLine($"    ENCODING '{NetezzaNameHelper.EscapeLiteral(options.Encoding)}'");
        if (!string.IsNullOrEmpty(options.Timestyle))
            sb.AppendLine($"    TIMESTYLE '{NetezzaNameHelper.EscapeLiteral(options.Timestyle)}'");
        if (!string.IsNullOrEmpty(options.RemoteSource))
            sb.AppendLine($"    REMOTESOURCE '{NetezzaNameHelper.EscapeLiteral(options.RemoteSource)}'");
        if (options.SkipRows is not null)
            sb.AppendLine($"    SKIPROWS {options.SkipRows}");
        if (options.MaxErrors is not null)
            sb.AppendLine($"    MAXERRORS {options.MaxErrors}");
        if (!string.IsNullOrEmpty(options.EscapeChar))
            sb.AppendLine($"    ESCAPECHAR '{NetezzaNameHelper.EscapeLiteral(options.EscapeChar)}'");
        if (!string.IsNullOrEmpty(options.DecimalDelim))
            sb.AppendLine($"    DECIMALDELIM '{NetezzaNameHelper.EscapeLiteral(options.DecimalDelim)}'");
        if (!string.IsNullOrEmpty(options.LogDir))
            sb.AppendLine($"    LOGDIR '{NetezzaNameHelper.EscapeLiteral(options.LogDir)}'");
        if (!string.IsNullOrEmpty(options.QuotedValue))
            sb.AppendLine($"    QUOTEDVALUE '{NetezzaNameHelper.EscapeLiteral(options.QuotedValue)}'");
        if (!string.IsNullOrEmpty(options.NullValue))
            sb.AppendLine($"    NULLVALUE '{NetezzaNameHelper.EscapeLiteral(options.NullValue)}'");
        if (options.CrInString is not null)
            sb.AppendLine($"    CRINSTRING {FormatBool(options.CrInString.Value)}");
        if (options.TruncString is not null)
            sb.AppendLine($"    TRUNCSTRING {FormatBool(options.TruncString.Value)}");
        if (options.CtrlChars is not null)
            sb.AppendLine($"    CTRLCHARS {FormatBool(options.CtrlChars.Value)}");
        if (options.IgnoreZero is not null)
            sb.AppendLine($"    IGNOREZERO {FormatBool(options.IgnoreZero.Value)}");
        if (options.TimeExtraZeros is not null)
            sb.AppendLine($"    TIMEEXTRAZEROS {FormatBool(options.TimeExtraZeros.Value)}");
        if (options.Y2Base is not null)
            sb.AppendLine($"    Y2BASE {options.Y2Base}");
        if (options.FillRecord is not null)
            sb.AppendLine($"    FILLRECORD {FormatBool(options.FillRecord.Value)}");
        if (!string.IsNullOrEmpty(options.Compress))
            sb.AppendLine($"    COMPRESS {options.Compress}");
        if (options.IncludeHeader is not null)
            sb.AppendLine($"    INCLUDEHEADER {FormatBool(options.IncludeHeader.Value)}");
        if (options.LfInString is not null)
            sb.AppendLine($"    LFINSTRING {FormatBool(options.LfInString.Value)}");
        if (!string.IsNullOrEmpty(options.DateStyle))
            sb.AppendLine($"    DATESTYLE '{NetezzaNameHelper.EscapeLiteral(options.DateStyle)}'");
        if (!string.IsNullOrEmpty(options.DateDelim))
            sb.AppendLine($"    DATEDELIM '{NetezzaNameHelper.EscapeLiteral(options.DateDelim)}'");
        if (!string.IsNullOrEmpty(options.TimeDelim))
            sb.AppendLine($"    TIMEDELIM '{NetezzaNameHelper.EscapeLiteral(options.TimeDelim)}'");
        if (!string.IsNullOrEmpty(options.BoolStyle))
            sb.AppendLine($"    BOOLSTYLE '{NetezzaNameHelper.EscapeLiteral(options.BoolStyle)}'");
        if (!string.IsNullOrEmpty(options.Format))
            sb.AppendLine($"    FORMAT '{NetezzaNameHelper.EscapeLiteral(options.Format)}'");
        if (options.SocketBufSize is not null)
            sb.AppendLine($"    SOCKETBUFSIZE {options.SocketBufSize}");
        if (!string.IsNullOrEmpty(options.RecordDelim))
            sb.AppendLine($"    RECORDDELIM '{NetezzaNameHelper.EscapeLiteral(options.RecordDelim)}'");
        if (options.MaxRows is not null)
            sb.AppendLine($"    MAXROWS {options.MaxRows}");
        if (options.RequireQuotes is not null)
            sb.AppendLine($"    REQUIREQUOTES {FormatBool(options.RequireQuotes.Value)}");
        if (!string.IsNullOrEmpty(options.RecordLength))
            sb.AppendLine($"    RECORDLENGTH {options.RecordLength}");
        if (!string.IsNullOrEmpty(options.DateTimeDelim))
            sb.AppendLine($"    DATETIMEDELIM '{NetezzaNameHelper.EscapeLiteral(options.DateTimeDelim)}'");
        if (!string.IsNullOrEmpty(options.RejectFile))
            sb.AppendLine($"    REJECTFILE '{NetezzaNameHelper.EscapeLiteral(options.RejectFile)}'");

        sb.AppendLine(");");
    }

    public string BuildCreateExternal(NetezzaExternalDdlInput input)
    {
        var sb = new StringBuilder();
        AppendCreateExternal(sb, input);
        return sb.ToString();
    }

    public void AppendCreateView(StringBuilder sb, NetezzaViewDdlInput input)
    {
        var (cleanDatabase, cleanSchema, cleanViewName) = NetezzaNameHelper.GetCleanedNames(
            input.Database,
            input.Schema,
            input.ViewName);

        sb.AppendLine($"CREATE OR REPLACE VIEW {cleanDatabase}.{cleanSchema}.{cleanViewName} AS ");
        sb.AppendLine(input.ViewDefinition);

        var comment = NetezzaNameHelper.EscapeComment(input.ViewComment);
        if (comment is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"COMMENT ON VIEW {cleanDatabase}.{cleanSchema}.{cleanViewName} IS '{comment}';");
        }
    }

    public string BuildCreateView(NetezzaViewDdlInput input)
    {
        var sb = new StringBuilder();
        AppendCreateView(sb, input);
        return sb.ToString();
    }

    public void AppendCreateProcedure(StringBuilder sb, NetezzaProcedureDdlInput input)
    {
        var (database, schema, procedure) = NetezzaNameHelper.GetCleanedNames(
            input.Database, input.Schema, input.ProcedureName);
        var arguments = string.IsNullOrWhiteSpace(input.Arguments) ? "()" : input.Arguments.Trim();
        if (!arguments.StartsWith('(') || !arguments.EndsWith(')'))
            arguments = $"({arguments})";

        sb.AppendLine($"CREATE OR REPLACE PROCEDURE {database}.{schema}.{procedure}{arguments}");
        sb.AppendLine($"RETURNS {input.Returns}");
        sb.AppendLine(input.ExecuteAsOwner ? "EXECUTE AS OWNER" : "EXECUTE AS CALLER");
        sb.AppendLine("LANGUAGE NZPLSQL AS");
        sb.AppendLine("BEGIN_PROC");
        sb.AppendLine(input.ProcedureSource);
        sb.AppendLine("END_PROC;");

        var comment = NetezzaNameHelper.EscapeComment(input.Description);
        if (comment is not null)
            sb.AppendLine($"COMMENT ON PROCEDURE {procedure} IS '{comment}';");
    }

    public string BuildCreateProcedure(NetezzaProcedureDdlInput input)
    {
        var sb = new StringBuilder();
        AppendCreateProcedure(sb, input);
        return sb.ToString();
    }

    public void AppendCreateSynonym(StringBuilder sb, NetezzaSynonymDdlInput input)
    {
        var (database, schema, synonym) = NetezzaNameHelper.GetCleanedNames(
            input.Database, input.Owner ?? input.Schema, input.SynonymName);
        var target = string.Join('.', input.ReferencedObject.Split('.')
            .Select(NetezzaNameHelper.QuoteNameIfNeeded));

        sb.AppendLine($"CREATE SYNONYM {database}.{schema}.{synonym} FOR {target};");
        var comment = NetezzaNameHelper.EscapeComment(input.Description);
        if (comment is not null)
            sb.AppendLine($"COMMENT ON SYNONYM {synonym} IS '{comment}';");
    }

    public string BuildCreateSynonym(NetezzaSynonymDdlInput input)
    {
        var sb = new StringBuilder();
        AppendCreateSynonym(sb, input);
        return sb.ToString();
    }

    private static List<string> ResolveDistributeColumns(
        IReadOnlyList<string>? fromInput,
        IReadOnlyList<string>? distOverride)
    {
        if (distOverride is not null)
            return distOverride.Count > 0 ? distOverride.ToList() : [];

        return fromInput?.ToList() ?? [];
    }

    private static void AppendKeys(
        StringBuilder sb,
        string cleanDatabase,
        string cleanSchema,
        string cleanTableName,
        IReadOnlyList<NetezzaKeyDdl>? keys)
    {
        if (keys is null || keys.Count == 0)
            return;

        foreach (var key in keys)
        {
            string cleanKeyName = NetezzaNameHelper.QuoteNameIfNeeded(key.KeyName);
            var colList = key.ColumnNames.Select(NetezzaNameHelper.QuoteNameIfNeeded);

            switch (key.KeyType)
            {
                case 'f':
                {
                    var refCols = key.ReferencedPkColumnNames?.Select(NetezzaNameHelper.QuoteNameIfNeeded)
                        ?? Enumerable.Empty<string>();
                    string pkDatabase = NetezzaNameHelper.QuoteNameIfNeeded(key.PkDatabase!);
                    string pkSchema = NetezzaNameHelper.QuoteNameIfNeeded(key.PkSchema!);
                    string pkRelation = NetezzaNameHelper.QuoteNameIfNeeded(key.PkRelation!);
                    sb.AppendLine(
                        $"ALTER TABLE {cleanDatabase}.{cleanSchema}.{cleanTableName} ADD CONSTRAINT {cleanKeyName} FOREIGN KEY ({string.Join(", ", colList)}) REFERENCES {pkDatabase}.{pkSchema}.{pkRelation}({string.Join(", ", refCols)}) ON DELETE {key.OnDelete} ON UPDATE {key.OnUpdate};");
                    break;
                }
                case 'p':
                    sb.AppendLine(
                        $"ALTER TABLE {cleanDatabase}.{cleanSchema}.{cleanTableName} ADD CONSTRAINT {cleanKeyName} PRIMARY KEY ({string.Join(", ", colList)});");
                    break;
                case 'u':
                    sb.AppendLine(
                        $"ALTER TABLE {cleanDatabase}.{cleanSchema}.{cleanTableName} ADD CONSTRAINT {cleanKeyName} UNIQUE ({string.Join(", ", colList)});");
                    break;
            }
        }
    }

    private static void AppendTableAndColumnComments(
        StringBuilder sb,
        string cleanDatabase,
        string cleanSchema,
        string cleanTableName,
        IReadOnlyList<NetezzaColumnDdl> columns,
        string? tableComment)
    {
        var escapedTableComment = NetezzaNameHelper.EscapeComment(tableComment);
        if (escapedTableComment is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"COMMENT ON TABLE {cleanDatabase}.{cleanSchema}.{cleanTableName} IS '{escapedTableComment}';");
        }

        foreach (var column in columns)
        {
            var escaped = NetezzaNameHelper.EscapeComment(column.Description);
            if (escaped is null)
                continue;

            string cleanColumn = NetezzaNameHelper.QuoteNameIfNeeded(column.Name);
            sb.AppendLine($"COMMENT ON COLUMN {cleanDatabase}.{cleanSchema}.{cleanTableName}.{cleanColumn} IS '{escaped}';");
        }
    }

    private static string FormatBool(bool value) => value ? "true" : "false";
}

public sealed record NetezzaDdlBuildResult(
    IReadOnlyList<string> DistributeColumns,
    IReadOnlyList<string> OrganizeColumns);
