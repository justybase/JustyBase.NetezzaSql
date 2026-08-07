using System.Text;
using System.Globalization;

namespace JustyBase.Ai.Services;

public static class LocalDatabaseContextFormatter
{
    public static string BuildNoActiveConnectionContext()
        => "[DATABASE_CONTEXT]\nNo active database connection.\n[/DATABASE_CONTEXT]";

    public static string BuildDatabaseContext(string connectionName, string databaseName, IReadOnlyList<string> schemas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[DATABASE_CONTEXT]");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Active Connection: {connectionName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Active Database: {databaseName}");
        sb.AppendLine();
        sb.AppendLine("CRITICAL: Always use qualified object names:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Preferred: {databaseName}.SCHEMA.OBJECT");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  If schema unknown: {databaseName}..OBJECT (double dot - valid but less preferred)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Example: SELECT * FROM {databaseName}.ADMIN.USERS");
        sb.AppendLine();
        sb.AppendLine("Available schemas:");
        foreach (var schema in schemas)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  - {databaseName}.{schema}");
        }
        sb.AppendLine("[/DATABASE_CONTEXT]");
        return sb.ToString().TrimEnd();
    }

    public static string BuildFallbackContext(string connectionName, string databaseName)
        => string.Format(CultureInfo.InvariantCulture, "[DATABASE_CONTEXT]\nActive Database: {0}\nConnection: {1}\nFormat: {0}.SCHEMA.OBJECT or {0}..OBJECT\n[/DATABASE_CONTEXT]", databaseName, connectionName);
}
