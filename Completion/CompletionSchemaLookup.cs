using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSqlParser.Completion;

/// <summary>
/// Resolves table metadata using the active dialect's unqualified-name rules.
/// </summary>
internal static class CompletionSchemaLookup
{
    public static TableInfo? GetTable(
        ISchemaProvider schema,
        SqlDialect dialect,
        string? database,
        string? schemaName,
        string tableName)
    {
        if (dialect == SqlDialect.Sqlite && database is null && schemaName is null)
        {
            // SQLite searches TEMP before MAIN for unqualified table names. If
            // neither contains the object, the provider can fall back to an
            // attached catalog or an unqualified metadata entry.
            var temp = schema.GetTable(null, "temp", tableName);
            if (temp is not null)
                return temp;

            var main = schema.GetTable(null, "main", tableName);
            if (main is not null)
                return main;
        }

        return schema.GetTable(database, schemaName, tableName);
    }
}
