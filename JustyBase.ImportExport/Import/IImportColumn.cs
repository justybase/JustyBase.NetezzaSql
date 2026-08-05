namespace JustyBase.ImportExport.Import;

/// <summary>
/// Neutral column description of an import job. Hosts map their own type model
/// (e.g. <c>DbTypeWithSize</c>) onto this surface; DDL rendering for a target
/// dialect is provided by <see cref="RenderDdl"/>.
/// </summary>
public interface IImportColumn
{
    string Name { get; }

    ImportColumnKind Kind { get; }

    /// <summary>NVARCHAR length or NUMERIC precision depending on <see cref="Kind"/>.</summary>
    int LengthOrPrecision { get; }

    int Scale { get; }

    bool IsNullable { get; }

    string RenderDdl(DatabaseKind databaseKind);
}

/// <summary>Default <see cref="IImportColumn"/> implementation (hosts may override rendering).</summary>
public sealed record ImportColumn(
    string Name,
    ImportColumnKind Kind,
    int LengthOrPrecision = 0,
    int Scale = 0,
    bool IsNullable = true) : IImportColumn
{
    public string RenderDdl(DatabaseKind databaseKind) => Kind switch
    {
        ImportColumnKind.Integer => databaseKind == DatabaseKind.Oracle ? "INTEGER" : "BIGINT",
        ImportColumnKind.Numeric => databaseKind == DatabaseKind.Oracle
            ? $"NUMBER ({LengthOrPrecision},{Scale})"
            : $"NUMERIC({LengthOrPrecision},{Scale})",
        ImportColumnKind.Nvarchar => $"{TextTypeName(databaseKind)}({LengthOrPrecision})",
        ImportColumnKind.Date => "DATE",
        ImportColumnKind.TimeStamp => "TIMESTAMP",
        ImportColumnKind.Boolean => "BOOL",
        _ => $"{TextTypeName(databaseKind)}(255)"
    };

    private static string TextTypeName(DatabaseKind databaseKind) => databaseKind switch
    {
        DatabaseKind.Netezza or DatabaseKind.NetezzaOdbc or DatabaseKind.MsSql => "NVARCHAR",
        DatabaseKind.Db2 or DatabaseKind.PostgreSql => "VARCHAR",
        DatabaseKind.Oracle => "VARCHAR2",
        _ => "TEXT"
    };
}
