using Microsoft.Extensions.AI;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustyBase.Ai.Chat;

/// <summary>
/// OpenAI-compatible chat client over raw HTTP with streaming and an in-process agent loop.
///
/// - Sends <c>{"think": false}</c> for servers that support thinking suppression (Ollama, LM Studio).
/// - Serializes <see cref="ChatOptions.Tools"/> as OpenAI function definitions and parses
///   <c>delta.tool_calls</c> from the SSE stream.
/// - When the model requests a tool, the <c>toolExecutor</c> delegate runs it (approval-gated by
///   the caller) and the result is fed back for the next round (max <see cref="MaxToolRounds"/>).
/// </summary>
public sealed class OpenAiCompatibleChatClient : IChatClient
{
    public const int MaxToolRounds = 5;

    private readonly Uri _endpoint;
    private readonly string _modelId;
    private readonly string? _apiKey;
    private readonly Func<string, string, Task<string>>? _toolExecutor;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly bool _sendThinkFalse;
    private readonly bool _sendReasoningEffort;

    public OpenAiCompatibleChatClient(
        Uri endpoint,
        string modelId,
        string? apiKey = null,
        Func<string, string, Task<string>>? toolExecutor = null,
        HttpClient? httpClient = null,
        bool sendReasoningEffort = false)
    {
        _endpoint = endpoint;
        _modelId = modelId;
        _apiKey = apiKey;
        _toolExecutor = toolExecutor;
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // {"think": false} is a non-standard field — only inject it for local servers that
        // support thinking suppression; strict remote OpenAI-compatible gateways reject it.
        _sendThinkFalse = IsLoopback(endpoint);
        // reasoning_effort is a llama.cpp server extension (Qwen3-style thinking budgets) —
        // only send it to backends known to accept it (the embedded llama-server).
        _sendReasoningEffort = sendReasoningEffort;
    }

    private static bool IsLoopback(Uri endpoint)
    {
        if (endpoint.IsLoopback)
        {
            return true;
        }

        return string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(endpoint.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(endpoint.Host, "::1", StringComparison.OrdinalIgnoreCase);
    }

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            if (update.Text is not null)
            {
                sb.Append(update.Text);
            }
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, sb.ToString()));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var history = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var tools = BuildTools(options);

        for (var round = 0; round < MaxToolRounds; round++)
        {
            // Stream each SSE delta live so long local generations render incrementally;
            // tool calls are accumulated in parallel for the agent loop below.
            var toolCalls = new List<ToolCallAccumulator>();

            var body = BuildRequest(history, options, tools);
            var json = JsonSerializer.Serialize(body, OpenAiCompatJsonContext.Default.OpenAiChatRequest);

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoint.AbsoluteUri.TrimEnd('/') + "/chat/completions"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Authorization = new("Bearer", _apiKey);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line == "data: [DONE]")
                {
                    break;
                }

                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(line.AsMemory(6));
                var root = doc.RootElement;
                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    continue;
                }

                var choice = choices[0];
                if (!choice.TryGetProperty("delta", out var delta))
                {
                    continue;
                }

                if (delta.TryGetProperty("content", out var content))
                {
                    var chunk = content.GetString();
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
                    }
                }

                if (delta.TryGetProperty("tool_calls", out var calls))
                {
                    foreach (var call in calls.EnumerateArray())
                    {
                        var index = call.TryGetProperty("index", out var indexProp) && indexProp.TryGetInt32(out var idx)
                            ? idx
                            : Math.Max(0, toolCalls.Count - 1); // some servers omit "index" — merge into the last call
                        var id = call.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                        var name = string.Empty;
                        var args = string.Empty;
                        if (call.TryGetProperty("function", out var fn))
                        {
                            if (fn.TryGetProperty("name", out var nameProp))
                            {
                                name = nameProp.GetString() ?? string.Empty;
                            }

                            if (fn.TryGetProperty("arguments", out var argsProp))
                            {
                                args = argsProp.GetString() ?? string.Empty;
                            }
                        }

                        while (toolCalls.Count <= index)
                        {
                            toolCalls.Add(new ToolCallAccumulator());
                        }

                        toolCalls[index] = toolCalls[index] with
                        {
                            Id = toolCalls[index].Id ?? id,
                            Name = toolCalls[index].Name + name,
                            Arguments = toolCalls[index].Arguments + args,
                        };
                    }
                }
            }

            if (toolCalls.Count == 0 || _toolExecutor is null)
            {
                yield break;
            }

            // Execute tools and continue the conversation for the next round.
            foreach (var call in toolCalls)
            {
                string result;
                try
                {
                    result = await _toolExecutor(call.Name, call.Arguments).ConfigureAwait(false);
                }
#pragma warning disable CA1031
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    result = $"[Tool error: {ex.Message}]";
                }

                var callId = string.IsNullOrWhiteSpace(call.Id)
                    ? $"call_{round}_{toolCalls.IndexOf(call)}"
                    : call.Id;
                history = AppendToolTurn(history, callId, call.Name, call.Arguments, result);

                var trimmed = result.Length > 400 ? result[..400] + "…" : result;
                yield return new ChatResponseUpdate(ChatRole.Assistant, $"\n\n[Tool '{call.Name}' executed: {trimmed}]");
            }
        }
    }

    private OpenAiChatRequest BuildRequest(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        List<OpenAiToolDefinition>? tools)
    {
        var openAiMessages = new List<OpenAiChatMessage>(messages.Count + 4);
        foreach (var message in messages)
        {
            var functionCalls = message.Contents?.OfType<FunctionCallContent>().ToList();
            if (functionCalls is { Count: > 0 })
            {
                openAiMessages.Add(new OpenAiChatMessage
                {
                    Role = "assistant",
                    Content = null,
                    ToolCalls = functionCalls
                        .Select(fc => new OpenAiToolCall
                        {
                            Id = fc.CallId,
                            Type = "function",
                            Function = new OpenAiToolCallFunction
                            {
                                Name = fc.Name,
                                Arguments = SerializeArguments(fc.Arguments),
                            },
                        })
                        .ToList(),
                });
                continue;
            }

            var toolResults = message.Contents?.OfType<FunctionResultContent>().ToList();
            if (toolResults is { Count: > 0 })
            {
                foreach (var result in toolResults)
                {
                    openAiMessages.Add(new OpenAiChatMessage
                    {
                        Role = "tool",
                        ToolCallId = result.CallId,
                        Content = StringifyResult(result.Result),
                    });
                }

                continue;
            }

            var text = message.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                openAiMessages.Add(new OpenAiChatMessage
                {
                    Role = message.Role == ChatRole.System ? "system" : message.Role == ChatRole.User ? "user" : "assistant",
                    Content = text,
                });
            }
        }

        string? reasoningEffort = _sendReasoningEffort && options?.AdditionalProperties is { } props
            && props.TryGetValue("reasoning_effort", out var effort)
            && effort is string { Length: > 0 } effortText
                ? effortText
                : null;

        return new OpenAiChatRequest
        {
            Model = _modelId,
            Messages = openAiMessages,
            Stream = true,
            // {"think": false} disables model thinking. When a reasoning_effort budget is
            // requested (embedded Qwen3-style models), the model must be allowed to think —
            // sending both would cancel the requested effort.
            Think = _sendThinkFalse && string.IsNullOrEmpty(reasoningEffort) ? false : null,
            MaxTokens = options?.MaxOutputTokens,
            Temperature = options?.Temperature,
            Tools = tools,
            ReasoningEffort = reasoningEffort,
        };
    }

    private static List<OpenAiToolDefinition>? BuildTools(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 })
        {
            return null;
        }

        var tools = new List<OpenAiToolDefinition>();
        foreach (var tool in options.Tools)
        {
            if (tool is not AIFunction function)
            {
                continue;
            }

            var parameters = function.JsonSchema;
            tools.Add(new OpenAiToolDefinition
            {
                Type = "function",
                Function = new OpenAiToolFunction
                {
                    Name = function.Name,
                    Description = function.Description,
                    Parameters = parameters.ValueKind == JsonValueKind.Undefined
                        ? null
                        : parameters,
                },
            });
        }

        return tools.Count == 0 ? null : tools;
    }

    private static IReadOnlyList<ChatMessage> AppendToolTurn(
        IReadOnlyList<ChatMessage> history,
        string callId,
        string toolName,
        string argumentsJson,
        string result)
    {
        var updated = new List<ChatMessage>(history)
        {
            new(ChatRole.Assistant, [new FunctionCallContent(callId, toolName, ParseArguments(argumentsJson))]),
            new(ChatRole.Tool, [new FunctionResultContent(callId, result)]),
        };
        return updated;
    }

    private static IDictionary<string, object?> ParseArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return new Dictionary<string, object?>();
        }

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, object?>();
            }

            var result = new Dictionary<string, object?>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value;
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private static string SerializeArguments(IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        if (arguments is null)
        {
            return "{}";
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var argument in arguments)
            {
                writer.WritePropertyName(argument.Key);
                WriteArgument(writer, argument.Value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteArgument(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                writer.WriteNumberValue(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
                break;
            case float number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static string StringifyResult(object? result)
    {
        switch (result)
        {
            case null:
                return string.Empty;
            case string text:
                return text;
            case JsonElement element:
                return element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? string.Empty
                    : element.GetRawText();
            default:
                return Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    private sealed record ToolCallAccumulator(string? Id = null, string Name = "", string Arguments = "");
}

#region DTOs

internal sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAiChatMessage> Messages { get; init; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("think")]
    public bool? Think { get; init; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }

    [JsonPropertyName("reasoning_effort")]
    public string? ReasoningEffort { get; init; }

    [JsonPropertyName("tools")]
    public List<OpenAiToolDefinition>? Tools { get; init; }
}

internal sealed class OpenAiChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; init; }
}

internal sealed class OpenAiToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public OpenAiToolFunction Function { get; init; } = new();
}

internal sealed class OpenAiToolFunction
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; init; }
}

internal sealed class OpenAiToolCall
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("function")]
    public OpenAiToolCallFunction Function { get; init; } = new();
}

internal sealed class OpenAiToolCallFunction
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OpenAiChatRequest))]
internal sealed partial class OpenAiCompatJsonContext : JsonSerializerContext
{
}

#endregion
