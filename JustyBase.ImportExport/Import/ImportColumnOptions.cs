namespace JustyBase.ImportExport.Import;

/// <summary>
/// The only handoff between host-UI review and the execution engines (mirror of the
/// vscode <c>ImportColumnOptions</c>): a column subset/order, per-column forced SQL
/// types and target-name overrides.
/// </summary>
public sealed record ImportColumnOptions(
    IReadOnlyList<int>? SelectedColumnIndexes = null,
    IReadOnlyDictionary<int, string>? ForcedColumnTypes = null,
    IReadOnlyDictionary<int, string>? ColumnNameOverrides = null);
