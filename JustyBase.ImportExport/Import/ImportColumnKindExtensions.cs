using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace JustyBase.ImportExport.Import;

public static class ImportColumnKindExtensions
{
    internal static readonly NumberFormatInfo NumberWithDot = new()
    {
        NumberDecimalSeparator = ".",
        NumberDecimalDigits = 6
    };

    /// <summary>Maps an import column kind to its eager .NET cell type.</summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public static Type GetNativeType(ImportColumnKind kind) => kind switch
    {
        ImportColumnKind.Integer => typeof(long),
        ImportColumnKind.Numeric => typeof(decimal),
        ImportColumnKind.Date or ImportColumnKind.TimeStamp => typeof(DateTime),
        ImportColumnKind.Boolean => typeof(bool),
        _ => typeof(string)
    };
}
