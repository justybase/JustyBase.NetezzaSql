using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using JustyBase.Ai.Ports;
using JustyBase.Ai.Services;
using System.Globalization;

namespace JustyBase.Ai.Services;

public static class LocalToolHelpers
{
    public static readonly string[] SensitiveFieldPatterns =
    [
        "\"Password\":", "'Password':", "Password=", "password=",
        "\"password\":", "'password':", "pwd=", "PWD=",
        "\"Pass\":", "'Pass':", "Pass=", "pass="
    ];

    public static readonly ChatObjectType[] DefaultSchemaSearchTypes =
    [
        ChatObjectType.Table,
        ChatObjectType.View,
        ChatObjectType.Procedure,
        ChatObjectType.Function,
        ChatObjectType.ExternalTable,
        ChatObjectType.Synonym,
        ChatObjectType.Fluid,
        ChatObjectType.Index,
        ChatObjectType.Partition
    ];

    public static readonly Dictionary<string, ChatObjectType> SchemaObjectTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["table"] = ChatObjectType.Table,
        ["tables"] = ChatObjectType.Table,
        ["view"] = ChatObjectType.View,
        ["views"] = ChatObjectType.View,
        ["procedure"] = ChatObjectType.Procedure,
        ["procedures"] = ChatObjectType.Procedure,
        ["proc"] = ChatObjectType.Procedure,
        ["function"] = ChatObjectType.Function,
        ["functions"] = ChatObjectType.Function,
        ["synonym"] = ChatObjectType.Synonym,
        ["synonyms"] = ChatObjectType.Synonym,
        ["external"] = ChatObjectType.ExternalTable,
        ["external table"] = ChatObjectType.ExternalTable,
        ["external tables"] = ChatObjectType.ExternalTable,
        ["fluid"] = ChatObjectType.Fluid,
        ["index"] = ChatObjectType.Index,
        ["indexes"] = ChatObjectType.Index,
        ["indices"] = ChatObjectType.Index,
        ["partition"] = ChatObjectType.Partition,
        ["partitions"] = ChatObjectType.Partition
    };

    public static readonly Regex DependencyRegex = new(
        @"\b(?:FROM|JOIN|UPDATE|INTO|MERGE\s+INTO|DELETE\s+FROM|CALL)\s+(?<obj>[A-Za-z0-9_""\.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly HashSet<string> SupportedRepositoryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sql", ".ddl", ".dml", ".txt", ".md", ".csv", ".json", ".yaml", ".yml", ".cs"
    };

    public static readonly Regex MutatingSqlRegex = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|REPLACE|GRANT|REVOKE|GROOM|GENERATE|CALL|EXECUTE\s+PROCEDURE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly Regex ExplainSqlRegex = new(@"\bEXPLAIN\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IEnumerable<ChatObjectType> ResolveSchemaObjectTypes(string? objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType))
        {
            return DefaultSchemaSearchTypes;
        }

        var types = new List<ChatObjectType>();
        foreach (var part in objectType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (SchemaObjectTypeMap.TryGetValue(part, out var type))
            {
                types.Add(type);
            }
        }

        return types.Count == 0 ? DefaultSchemaSearchTypes : types;
    }

    public static string? FindObjectSchema(IChatDatabaseAccess access, string database, string objectName, ChatObjectType typeInDatabase)
    {
        foreach (var schema in access.GetSchemas(database, ""))
        {
            var match = access
                .GetDbObjects(database, schema, "", typeInDatabase)
                .FirstOrDefault(x => x.Name.Equals(objectName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return schema;
            }
        }

        return null;
    }

    public static string BuildUnifiedDiff(string currentText, string proposedText)
    {
        var diffBuilder = new SideBySideDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(currentText, proposedText);

        var sb = new StringBuilder();
        var lineLimit = 1200;
        var emittedLines = 0;
        var hadChanges = false;
        var oldLines = diff.OldText.Lines;
        var newLines = diff.NewText.Lines;
        var max = Math.Max(oldLines.Count, newLines.Count);

        for (var i = 0; i < max; i++)
        {
            var oldLine = i < oldLines.Count ? oldLines[i] : null;
            var newLine = i < newLines.Count ? newLines[i] : null;
            var oldType = oldLine?.Type ?? ChangeType.Imaginary;
            var newType = newLine?.Type ?? ChangeType.Imaginary;

            if (oldType == ChangeType.Unchanged && newType == ChangeType.Unchanged)
            {
                continue;
            }

            hadChanges = true;

            if (emittedLines >= lineLimit)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"... [diff truncated at {lineLimit} lines]");
                break;
            }

            if (oldType == ChangeType.Deleted)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {oldLine!.Text}");
                emittedLines++;
            }
            else if (newType == ChangeType.Inserted)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"+ {newLine!.Text}");
                emittedLines++;
            }
            else if (oldType == ChangeType.Modified && newType == ChangeType.Modified)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"~ {oldLine!.Text}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  -> {newLine!.Text}");
                emittedLines += 2;
            }
        }

        if (!hadChanges)
        {
            return "No differences detected.";
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatReaderPreview(DbDataReader reader, int rowLimit, string databaseName, TimeSpan elapsed)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Query preview from database '{databaseName}' (up to {rowLimit} rows/result-set, elapsed {elapsed.TotalMilliseconds:N0} ms):");

        var resultSetIndex = 0;
        do
        {
            if (reader.FieldCount <= 0)
            {
                continue;
            }

            resultSetIndex++;
            var headers = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Result set #{resultSetIndex}");
            sb.AppendLine(string.Join(" | ", headers));
            sb.AppendLine(new string('-', Math.Min(180, Math.Max(30, headers.Sum(x => x.Length + 3)))));

            var count = 0;
            while (reader.Read() && count < rowLimit)
            {
                var row = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = CopilotSqlAssistantAnalyzer.FormatCellValue(reader.GetValue(i), 160);
                }
                sb.AppendLine(string.Join(" | ", row));
                count++;
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"Rows shown: {count}");
            if (count == rowLimit)
            {
                sb.AppendLine("Preview truncated by rowLimit.");
            }
        }
        while (reader.NextResult());

        if (resultSetIndex == 0)
        {
            return $"Statement executed in {elapsed.TotalMilliseconds:N0} ms (database: {databaseName}).";
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"Result sets returned: {resultSetIndex}");
        return sb.ToString().TrimEnd();
    }

    public static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch
        {
            return [];
        }
    }
}
