using System.Text;
using JustyBase.NetezzaDdl;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Builds the convenience SELECT snippet shown after an import (mirror of the Legacy
/// <c>ImportProgressForm</c>): columns are listed explicitly (never <c>*</c>) with an alias
/// and a LIMIT. Hosts use it for the progress document's result pane and copy buttons.
/// </summary>
public static class ImportSelectSqlBuilder
{
    /// <summary>
    /// Builds <c>SELECT T.col1, T.col2 ... FROM &lt;table&gt; T LIMIT 100</c> from
    /// <c>"name TYPE"</c> header definitions; falls back to <c>SELECT *</c> when no column
    /// name can be extracted.
    /// </summary>
    public static string BuildAliasedColumnSelect(string tableReference, IReadOnlyList<string> headerDefinitions, string alias = "T")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableReference);
        ArgumentNullException.ThrowIfNull(headerDefinitions);

        string[] columns = headerDefinitions
            .Select(ExtractColumnNameFromHeaderDefinition)
            .Where(static column => !string.IsNullOrWhiteSpace(column))
            .ToArray();

        if (columns.Length == 0)
        {
            return $"SELECT * FROM {tableReference} {alias} LIMIT 100";
        }

        var sb = new StringBuilder();
        sb.AppendLine("SELECT");
        sb.AppendLine($"{alias}.{QuoteSelectIdentifier(columns[0])}");
        for (int i = 1; i < columns.Length; i++)
        {
            sb.AppendLine($", {alias}.{QuoteSelectIdentifier(columns[i])}");
        }

        sb.AppendLine("FROM");
        sb.AppendLine($"{tableReference} {alias}");
        sb.AppendLine("LIMIT 100");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Extracts the bare column name from a <c>"name TYPE"</c> definition, honoring a
    /// leading double-quoted identifier.
    /// </summary>
    public static string ExtractColumnNameFromHeaderDefinition(string headerDefinition)
    {
        if (string.IsNullOrWhiteSpace(headerDefinition))
        {
            return string.Empty;
        }

        headerDefinition = headerDefinition.Trim();
        if (headerDefinition.StartsWith('"'))
        {
            int endQuote = headerDefinition.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return headerDefinition.Substring(1, endQuote - 1);
            }
        }

        int space = headerDefinition.IndexOf(' ');
        return space <= 0 ? headerDefinition : headerDefinition[..space];
    }

    private static string QuoteSelectIdentifier(string identifier)
        => NetezzaNameHelper.QuoteNameIfNeeded(identifier);
}
