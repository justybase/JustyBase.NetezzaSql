using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustyBase.ImportExport.Export;

[JsonSerializable(typeof(string?[]))]
internal sealed partial class ImportExportJsonContext : JsonSerializerContext;

/// <summary>
/// Shared JSON array export from an <see cref="IDataReader"/> (row = JSON string array).
/// Originally extracted from the Legacy host's <c>TabularTextExporter.WriteJson</c>.
/// </summary>
public static class JsonExportWriter
{
    public static long WriteFromDataReader(
        TextWriter writer,
        IDataReader reader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(reader);

        long rowCount = 0;
        writer.Write('[');
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rowCount > 0)
            {
                writer.Write(',');
            }

            string?[] values = new string?[reader.FieldCount];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = reader.IsDBNull(index)
                    ? null
                    : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);
            }

            writer.Write(JsonSerializer.Serialize(values, ImportExportJsonContext.Default.StringArray));
            rowCount++;
        }

        writer.Write(']');
        return rowCount;
    }
}
