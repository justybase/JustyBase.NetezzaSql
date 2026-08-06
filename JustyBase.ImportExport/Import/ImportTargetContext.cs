namespace JustyBase.ImportExport.Import;

/// <summary>
/// Ambient context for the current import execution: when <see cref="TargetColumnNames"/> is
/// set, the engine writes into the existing table using those destination column names
/// (1:1 with the source columns, in source order) instead of creating a new table.
/// </summary>
public static class ImportTargetContext
{
    public static IReadOnlyList<string>? TargetColumnNames { get; set; }
}
