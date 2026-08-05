using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace JustyBase.ImportExport.Import;

internal static class ImportColumnKindExtensions
{
    internal static readonly NumberFormatInfo NumberWithDot = new()
    {
        NumberDecimalSeparator = ".",
        NumberDecimalDigits = 6
    };

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    internal static Type GetNativeType(ImportColumnKind kind) => kind switch
    {
        ImportColumnKind.Integer => typeof(long),
        ImportColumnKind.Numeric => typeof(decimal),
        ImportColumnKind.Date or ImportColumnKind.TimeStamp => typeof(DateTime),
        ImportColumnKind.Boolean => typeof(bool),
        _ => typeof(string)
    };
}
