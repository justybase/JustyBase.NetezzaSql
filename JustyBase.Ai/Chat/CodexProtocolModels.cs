using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustyBase.Ai.Chat;

internal sealed class CodexRpcRequest
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Id { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    public required JsonElement Parameters { get; init; }
}

internal sealed class CodexInitializeParameters
{
    [JsonPropertyName("clientInfo")]
    public required CodexClientInfo ClientInfo { get; init; }

    [JsonPropertyName("capabilities")]
    public required CodexClientCapabilities Capabilities { get; init; }
}

internal sealed class CodexClientInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }
}

internal sealed class CodexClientCapabilities
{
    [JsonPropertyName("experimentalApi")]
    public bool ExperimentalApi { get; init; }
}

internal sealed class CodexEmptyParameters
{
}

internal sealed class CodexAccountReadParameters
{
    [JsonPropertyName("includeToken")]
    public bool IncludeToken { get; init; }
}

internal sealed class CodexLoginStartParameters
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}

internal sealed class CodexThreadStartParameters
{
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }

    [JsonPropertyName("approvalPolicy")]
    public required string ApprovalPolicy { get; init; }

    [JsonPropertyName("sandbox")]
    public required string Sandbox { get; init; }

    [JsonPropertyName("cwd")]
    public required string CurrentDirectory { get; init; }

    [JsonPropertyName("dynamicTools")]
    public required CodexDynamicToolDefinition[] DynamicTools { get; init; }
}

internal sealed class CodexThreadResumeParameters
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("dynamicTools")]
    public required CodexDynamicToolDefinition[] DynamicTools { get; init; }
}

internal sealed class CodexTurnStartParameters
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("input")]
    public required CodexTurnInput[] Input { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }

    [JsonPropertyName("effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Effort { get; init; }
}

internal sealed class CodexTurnInterruptParameters
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }
}

internal sealed class CodexTurnInput
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

internal sealed class CodexDynamicToolDefinition
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("inputSchema")]
    public required CodexJsonSchema InputSchema { get; init; }
}

internal sealed class CodexJsonSchema
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("additionalProperties")]
    public bool AdditionalProperties { get; init; }

    [JsonPropertyName("properties")]
    public required Dictionary<string, CodexJsonProperty> Properties { get; init; }
}

internal sealed class CodexJsonProperty
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}

internal sealed class CodexToolCallResponse
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("result")]
    public required CodexToolCallResult Result { get; init; }
}

internal sealed class CodexToolCallResult
{
    [JsonPropertyName("contentItems")]
    public required CodexContentItem[] ContentItems { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }
}

internal sealed class CodexContentItem
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CodexRpcRequest))]
[JsonSerializable(typeof(CodexInitializeParameters))]
[JsonSerializable(typeof(CodexClientInfo))]
[JsonSerializable(typeof(CodexClientCapabilities))]
[JsonSerializable(typeof(CodexEmptyParameters))]
[JsonSerializable(typeof(CodexAccountReadParameters))]
[JsonSerializable(typeof(CodexLoginStartParameters))]
[JsonSerializable(typeof(CodexThreadStartParameters))]
[JsonSerializable(typeof(CodexThreadResumeParameters))]
[JsonSerializable(typeof(CodexTurnStartParameters))]
[JsonSerializable(typeof(CodexTurnInterruptParameters))]
[JsonSerializable(typeof(CodexTurnInput))]
[JsonSerializable(typeof(CodexDynamicToolDefinition))]
[JsonSerializable(typeof(CodexDynamicToolDefinition[]))]
[JsonSerializable(typeof(CodexJsonSchema))]
[JsonSerializable(typeof(CodexJsonProperty))]
[JsonSerializable(typeof(Dictionary<string, CodexJsonProperty>))]
[JsonSerializable(typeof(CodexToolCallResponse))]
[JsonSerializable(typeof(CodexToolCallResult))]
[JsonSerializable(typeof(CodexContentItem))]
internal partial class CodexJsonContext : JsonSerializerContext
{
}
