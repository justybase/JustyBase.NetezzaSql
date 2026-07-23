namespace JustyBase.NetezzaDdl.Models;

/// <summary>Input data required to render a <c>CREATE TABLE</c> statement.</summary>
/// <param name="Database">Owning database name.</param>
/// <param name="Schema">Owning schema name.</param>
/// <param name="TableName">Table name.</param>
/// <param name="Columns">Column definitions.</param>
/// <param name="DistributeColumns">Optional DISTRIBUTE ON columns.</param>
/// <param name="OrganizeColumns">Optional ORGANIZE ON columns.</param>
/// <param name="Keys">Optional primary/foreign key constraints.</param>
/// <param name="TableComment">Optional table comment.</param>
/// <param name="TableOwner">Optional table owner.</param>
/// <param name="OverrideTableName">When set, replaces <paramref name="TableName"/> in the output.</param>
/// <param name="MiddleCode">Arbitrary SQL inserted after the column list.</param>
/// <param name="EndingCode">Arbitrary SQL appended after the closing parenthesis.</param>
public sealed record NetezzaTableDdlInput(
    string Database,
    string Schema,
    string TableName,
    IReadOnlyList<NetezzaColumnDdl> Columns,
    IReadOnlyList<string>? DistributeColumns = null,
    IReadOnlyList<string>? OrganizeColumns = null,
    IReadOnlyList<NetezzaKeyDdl>? Keys = null,
    string? TableComment = null,
    string? TableOwner = null,
    string? OverrideTableName = null,
    string? MiddleCode = null,
    string? EndingCode = null);
