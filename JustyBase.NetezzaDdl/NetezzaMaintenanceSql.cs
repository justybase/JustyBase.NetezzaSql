namespace JustyBase.NetezzaDdl;

/// <summary>
/// Builds Netezza GROOM / GENERATE STATISTICS SQL (Legacy GroomForm / Avalonia maintenance dialog).
/// </summary>
public static class NetezzaMaintenanceSql
{
    public static readonly string[] GroomModes =
    [
        "RECORDS ALL",
        "RECORDS READY",
        "PAGES ALL",
        "PAGES START",
        "VERSIONS"
    ];

    public static readonly string[] BackupsetPresets = ["DEFAULT", "NONE"];

    public static string BuildGroom(string qualifiedTable, string mode, string? backupset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedTable);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);

        var backup = FormatBackupset(backupset);
        return $"GROOM TABLE {qualifiedTable} {mode.Trim()} RECLAIM BACKUPSET {backup};";
    }

    public static string BuildGenerateStats(string qualifiedTable, bool express, string? columns = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedTable);

        if (express)
            return $"GENERATE EXPRESS STATISTICS ON {qualifiedTable};";

        var cols = columns?.Trim();
        if (string.IsNullOrEmpty(cols))
            return $"GENERATE STATISTICS ON {qualifiedTable};";

        return $"GENERATE STATISTICS ON {qualifiedTable} ({cols});";
    }

    public static string FormatBackupset(string? backupset)
    {
        var value = string.IsNullOrWhiteSpace(backupset) ? "DEFAULT" : backupset.Trim();
        if (value.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase)
            || value.Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            return value.ToUpperInvariant();
        }

        if (value.StartsWith('\'') && value.EndsWith('\''))
            return value;

        return $"'{value}'";
    }

    public static string Qualify(string? database, string? schema, string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        if (!string.IsNullOrWhiteSpace(database) && !string.IsNullOrWhiteSpace(schema))
            return $"{database}.{schema}.{table}";

        if (!string.IsNullOrWhiteSpace(schema))
            return $"{schema}.{table}";

        return table;
    }
}
