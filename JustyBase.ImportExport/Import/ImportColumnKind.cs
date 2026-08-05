namespace JustyBase.ImportExport.Import;

public enum ImportColumnKind
{
    NoInfo,
    Integer,
    Numeric,
    Nvarchar,
    Date,
    TimeStamp,
    Boolean
}

public sealed record DetectedImportColumnType(
    ImportColumnKind Kind,
    int LengthOrPrecision = 0,
    int Scale = 0,
    bool IsNullable = true);
