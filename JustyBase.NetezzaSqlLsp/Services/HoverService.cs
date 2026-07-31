using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.NetezzaSqlLsp.Protocol;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>Provides LSP hover information for Netezza SQL identifiers.</summary>
public static class HoverService
{
    /// <summary>Returns hover content at the given position, or <see langword="null"/>.</summary>
    public static Hover? GetHover(string text, int line, int character, ISchemaProvider? schema, SqlDialect dialect = SqlDialect.Netezza)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        int offset = LspTextUtilities.PositionToOffset(text, line, character);
        var catalog = DialectRuntime.AuthoringCatalogOrNull(dialect);
        var hover = NzHoverService.GetHover(text, offset, schema, catalog: catalog, dialect: dialect);
        return hover is null
            ? null
            : new Hover(new MarkupContent("markdown", hover.Content), null);
    }
}
