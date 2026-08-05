namespace JustyBase.ImportExport.Import.TypeChooser;

/// <summary>Contract mirror of the vscode <c>DatabaseImportTypeMapper</c>.</summary>
public interface DatabaseImportTypeMapper
{
    DatabaseImportDataType CreateDataType(string dbType, int? precision = null, int? scale = null, int? length = null);

    DatabaseColumnTypeChooser CreateColumnTypeChooser(string? decimalDelimiter = null, ColumnTypeChooserOptions? options = null);
}

/// <summary>Netezza implementation of <see cref="DatabaseImportTypeMapper"/>.</summary>
public sealed class NetezzaImportTypeMapper : DatabaseImportTypeMapper
{
    public static NetezzaImportTypeMapper Instance { get; } = new();

    public DatabaseImportDataType CreateDataType(string dbType, int? precision = null, int? scale = null, int? length = null)
        => new NetezzaImportDataType(dbType, precision, scale, length);

    public DatabaseColumnTypeChooser CreateColumnTypeChooser(string? decimalDelimiter = null, ColumnTypeChooserOptions? options = null)
        => new NetezzaColumnTypeChooser(decimalDelimiter ?? ".", options);
}
