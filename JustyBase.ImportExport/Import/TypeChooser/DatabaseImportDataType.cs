namespace JustyBase.ImportExport.Import.TypeChooser;

/// <summary>
/// Contract mirror of the vscode <c>DatabaseImportDataType</c>: a dialect-agnostic
/// representation of a column type with optional precision/scale/length and a
/// DDL-rendering <see cref="ToString"/>.
/// </summary>
public abstract class DatabaseImportDataType
{
    public abstract string DbType { get; }

    public virtual int? Precision => null;

    public virtual int? Scale => null;

    public virtual int? Length => null;

    public abstract override string ToString();
}

/// <summary>
/// Netezza flavor of <see cref="DatabaseImportDataType"/>. Renders the same strings as the
/// vscode <c>NetezzaDataType</c> (e.g. <c>NUMERIC(16,2)</c>, <c>NVARCHAR(20)</c>).
/// </summary>
public sealed class NetezzaImportDataType : DatabaseImportDataType
{
    public NetezzaImportDataType(string dbType, int? precision = null, int? scale = null, int? length = null)
    {
        DbType = dbType;
        Precision = precision;
        Scale = scale;
        Length = length;
    }

    public override string DbType { get; }

    public override int? Precision { get; }

    public override int? Scale { get; }

    public override int? Length { get; }

    public override string ToString()
    {
        if (DbType is "BIGINT" or "DATE" or "DATETIME" or "BOOLEAN")
            return DbType;
        if (DbType == "NUMERIC")
            return $"{DbType}({Precision},{Scale})";
        if (DbType == "NVARCHAR")
            return $"{DbType}({Length})";
        return "NVARCHAR(255)";
    }
}
