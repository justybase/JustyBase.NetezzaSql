using System.Text.Json.Serialization;

namespace JustyBase.Ai.Services;

internal sealed class ApplySqlFixConfirmation
{
    [JsonPropertyName("proposedSql")]
    public required string ProposedSql { get; init; }
}

internal sealed class ExecuteSqlConfirmation
{
    [JsonPropertyName("sql")]
    public required string Sql { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ApplySqlFixConfirmation))]
[JsonSerializable(typeof(ExecuteSqlConfirmation))]
internal partial class ChatToolConfirmationJsonContext : JsonSerializerContext
{
}
