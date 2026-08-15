using System.Text;
using JustyBase.Sqlite.Models;

namespace JustyBase.Sqlite.Ddl;

/// <summary>
/// Generates SQLite CREATE TABLE statements from a neutral metadata snapshot.
/// Emits the canonical SQLite style: inline PRIMARY KEY on the single key
/// column (so AUTOINCREMENT remains expressible), table-level PRIMARY KEY for
/// composite keys, NOT NULL and DEFAULT clauses when known.
/// </summary>
public static class SqliteDdlGenerator
{
    /// <summary>Builds a CREATE TABLE statement for the given table.</summary>
    public static string CreateTable(SqliteSchemaTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var sb = new StringBuilder();
        sb.Append("CREATE TABLE ");
        if (table.Database is not null &&
            !table.Database.Equals("main", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(QuoteIdentifier(table.Database));
            sb.Append('.');
        }
        sb.Append(QuoteIdentifier(table.Name));

        var columns = table.Columns ?? [];
        var primaryKeys = columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToArray();
        bool inlinePrimaryKey = primaryKeys.Length == 1;

        sb.Append(" (\n  ");
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
                sb.Append(",\n  ");
            var column = columns[i];
            sb.Append(QuoteIdentifier(column.Name));
            if (!string.IsNullOrWhiteSpace(column.DataType))
            {
                sb.Append(' ');
                sb.Append(column.DataType.Trim());
            }
            if (inlinePrimaryKey && column.IsPrimaryKey)
                sb.Append(" PRIMARY KEY");
            if (column.NotNull)
                sb.Append(" NOT NULL");
            if (column.DefaultValue is not null)
            {
                sb.Append(" DEFAULT ");
                sb.Append(column.DefaultValue);
            }
        }

        if (!inlinePrimaryKey && primaryKeys.Length > 1)
        {
            sb.Append(",\n  PRIMARY KEY (");
            sb.Append(string.Join(", ", primaryKeys.Select(QuoteIdentifier)));
            sb.Append(')');
        }

        sb.Append("\n)");
        return sb.ToString();
    }

    /// <summary>Quotes an SQLite identifier with double quotes.</summary>
    public static string QuoteIdentifier(string value)
        => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
