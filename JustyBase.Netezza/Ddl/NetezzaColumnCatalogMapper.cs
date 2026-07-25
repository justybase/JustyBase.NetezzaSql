using JustyBase.Netezza.Models;
using JustyBase.NetezzaDdl;
using JustyBase.NetezzaDdl.Models;

namespace JustyBase.Netezza.Ddl;

/// <summary>
/// Maps Netezza catalog column rows into schema / DDL models.
/// Mirrors JustyBaseLite <c>mapTableColumnsRows</c> / <c>normalizeBooleanFlag</c>.
/// </summary>
public static class NetezzaColumnCatalogMapper
{
    /// <summary>
    /// Normalizes catalog boolean flags (bool, 0/1, t/f, true/false, yes/no, on/off).
    /// </summary>
    public static bool NormalizeBooleanFlag(object? value)
    {
        if (value is null || value is DBNull)
            return false;

        if (value is bool b)
            return b;

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
            return Convert.ToInt64(value) != 0;

        if (value is float or double or decimal)
            return Convert.ToDouble(value) != 0d;

        if (value is string s)
        {
            var normalized = s.Trim().ToLowerInvariant();
            return normalized is "1" or "t" or "true" or "yes" or "on";
        }

        return false;
    }

    /// <summary>Converts ATTNOTNULL to the nullable semantics used by <see cref="NetezzaSchemaColumn"/>.</summary>
    public static bool AttNotNullToNullable(bool attNotNull) => !attNotNull;

    /// <summary>Converts a catalog ATTNOTNULL-style flag to nullable.</summary>
    public static bool AttNotNullToNullable(object? attNotNull) => !NormalizeBooleanFlag(attNotNull);

    /// <summary>Builds a schema column from catalog fields (FORMAT_TYPE + ATTNOTNULL).</summary>
    public static NetezzaSchemaColumn ToSchemaColumn(
        string name,
        string? formatType,
        object? attNotNull,
        string? description = null,
        string? defaultValue = null)
    {
        var typeName = string.IsNullOrWhiteSpace(formatType)
            ? "VARCHAR(ANY)"
            : NetezzaNameHelper.StripEmbeddedNotNull(formatType);
        return new(
            name,
            typeName,
            AttNotNullToNullable(attNotNull),
            string.IsNullOrEmpty(description) ? null : description,
            string.IsNullOrEmpty(defaultValue) ? null : defaultValue);
    }

    /// <summary>Builds a DDL column from catalog fields (FORMAT_TYPE + ATTNOTNULL).</summary>
    public static NetezzaColumnDdl ToColumnDdl(
        string name,
        string? formatType,
        object? attNotNull,
        string? description = null,
        string? defaultValue = null)
    {
        var typeName = string.IsNullOrWhiteSpace(formatType)
            ? "VARCHAR(ANY)"
            : NetezzaNameHelper.StripEmbeddedNotNull(formatType);
        return new(
            name,
            typeName,
            string.IsNullOrEmpty(description) ? null : description,
            string.IsNullOrEmpty(defaultValue) ? null : defaultValue,
            NormalizeBooleanFlag(attNotNull));
    }

    /// <summary>Builds a DDL column from an existing schema column.</summary>
    public static NetezzaColumnDdl ToColumnDdl(NetezzaSchemaColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        var typeName = string.IsNullOrWhiteSpace(column.DataType)
            ? "VARCHAR(ANY)"
            : NetezzaNameHelper.StripEmbeddedNotNull(column.DataType);
        return new NetezzaColumnDdl(
            column.Name,
            typeName,
            column.Description,
            column.DefaultValue,
            !column.Nullable);
    }
}
