namespace JustyBase.NetezzaDdl.Models;

/// <summary>Input data required to render a <c>CREATE VIEW</c> statement.</summary>
/// <param name="Database">Owning database name.</param>
/// <param name="Schema">Owning schema name.</param>
/// <param name="ViewName">View name.</param>
/// <param name="ViewDefinition">The SELECT query defining the view.</param>
/// <param name="ViewComment">Optional view comment.</param>
public sealed record NetezzaViewDdlInput(
    string Database,
    string Schema,
    string ViewName,
    string ViewDefinition,
    string? ViewComment = null);
