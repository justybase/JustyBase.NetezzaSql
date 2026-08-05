namespace JustyBase.ImportExport.Import.TypeChooser;

/// <summary>
/// Contract mirror of the vscode <c>DatabaseColumnTypeChooser</c>: a streaming, monotonic
/// per-column type estimator fed one raw string value at a time via <see cref="RefreshCurrentType"/>.
/// </summary>
public abstract class DatabaseColumnTypeChooser
{
    public abstract DatabaseImportDataType CurrentType { get; }

    public abstract int GetMaxScale();

    public abstract int GetMaxPrecision();

    public abstract DatabaseImportDataType RefreshCurrentType(string strVal);
}
