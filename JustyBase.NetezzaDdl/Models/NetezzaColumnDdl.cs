namespace JustyBase.NetezzaDdl.Models;

/// <summary>Metadata for a single column in a DDL statement.</summary>
/// <param name="Name">Column name.</param>
/// <param name="FullTypeName">Complete data type string (e.g. <c>VARCHAR(100)</c>).</param>
/// <param name="Description">Optional column description.</param>
/// <param name="ColDefault">Optional default value expression.</param>
/// <param name="NotNull"><see langword="true"/> when the column is NOT NULL.</param>
public sealed record NetezzaColumnDdl(
    string Name,
    string FullTypeName,
    string? Description = null,
    string? ColDefault = null,
    bool NotNull = false);
