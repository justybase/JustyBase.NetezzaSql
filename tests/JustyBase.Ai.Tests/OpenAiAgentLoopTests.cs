using JustyBase.Ai.Chat;
using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace JustyBase.Ai.Tests;

/// <summary>
/// End-to-end agent loop over the OpenAI-compatible SSE client: streams deltas, executes
/// tool calls through the injected executor and feeds the result back for the next round,
/// bounded by MaxToolRounds. No real HTTP — a scripted HttpMessageHandler supplies the SSE.
/// </summary>
public sealed class OpenAiAgentLoopTests
{
    [Fact]
    public async Task Streaming_TextThenToolCallThenResult_RoundTripsToolResult()
    {
        var handler = new ScriptedSseHandler(
            """
            data: {"choices":[{"delta":{"role":"assistant","content":"hello"}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"GetCurrentSql","arguments":"{}"}}]}}]}

            data: [DONE]
            """,
            """
            data: {"choices":[{"delta":{"content":" done"}}]}

            data: [DONE]
            """);

        string? executedName = null;
        string? executedArgs = null;
        var client = new OpenAiCompatibleChatClient(
            new Uri("http://localhost:1234/v1"),
            "test-model",
            toolExecutor: (name, args) =>
            {
                executedName = name;
                executedArgs = args;
                return Task.FromResult("SELECT 1");
            },
            httpClient: new HttpClient(handler));

        var collected = await CollectAsync(client, messages: [new ChatMessage(ChatRole.User, "what is 1+1?")]);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("hello" + "\n\n[Tool 'GetCurrentSql' executed: SELECT 1]" + " done", string.Concat(collected));
        Assert.Equal("GetCurrentSql", executedName);
        Assert.Equal("{}", executedArgs);

        // The second request must contain the tool result as a "tool" message.
        Assert.Contains("SELECT 1", handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.Contains("\"role\":\"tool\"", handler.RequestBodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Streaming_MultipleToolCalls_UsesOneAssistantMessageAndPreservesArguments()
    {
        var handler = new ScriptedSseHandler(
            """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_a","function":{"name":"First","arguments":"{\"value\":1}"}},{"index":1,"id":"call_b","function":{"name":"Second","arguments":"{\"value\":2}"}}]}}]}

            data: [DONE]
            """,
            """
            data: [DONE]
            """);

        var executed = new List<string>();
        var client = new OpenAiCompatibleChatClient(
            new Uri("http://localhost:1234/v1"),
            "test-model",
            toolExecutor: (name, arguments) =>
            {
                executed.Add($"{name}:{arguments}");
                return Task.FromResult($"result-{name}");
            },
            httpClient: new HttpClient(handler));

        await CollectAsync(client, messages: [new ChatMessage(ChatRole.User, "run both")]);

        Assert.Equal(["First:{\"value\":1}", "Second:{\"value\":2}"], executed);
        Assert.Equal(2, handler.RequestCount);
        var followUp = handler.RequestBodies[1];
        Assert.Contains("\"role\":\"assistant\"", followUp, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"First\"", followUp, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Second\"", followUp, StringComparison.Ordinal);
        Assert.True(followUp.IndexOf("\"role\":\"assistant\"", StringComparison.Ordinal)
            < followUp.IndexOf("\"role\":\"tool\"", StringComparison.Ordinal));

        using var document = JsonDocument.Parse(followUp);
        var assistant = document.RootElement
            .GetProperty("messages")
            .EnumerateArray()
            .First(message => message.GetProperty("role").GetString() == "assistant");
        var calls = assistant.GetProperty("tool_calls");
        Assert.Equal(2, calls.GetArrayLength());
        Assert.Equal("{\"value\":1}", calls[0].GetProperty("function").GetProperty("arguments").GetString());
        Assert.Equal("{\"value\":2}", calls[1].GetProperty("function").GetProperty("arguments").GetString());
    }

    [Fact]
    public async Task Streaming_RepeatedToolCalls_StopsAfterMaxToolRounds()
    {
        var handler = new ScriptedSseHandler(
            """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c","function":{"name":"ListSchemas","arguments":"{}"}}]}}]}

            data: [DONE]
            """);
        var client = new OpenAiCompatibleChatClient(
            new Uri("http://localhost:1234/v1"),
            "test-model",
            toolExecutor: (_, _) => Task.FromResult("ok"),
            httpClient: new HttpClient(handler));

        await CollectAsync(client, messages: [new ChatMessage(ChatRole.User, "loop")]);

        // Round 0..MaxToolRounds-1 = exactly MaxToolRounds HTTP requests.
        Assert.Equal(OpenAiCompatibleChatClient.MaxToolRounds, handler.RequestCount);
    }

    [Fact]
    public async Task Streaming_WithoutToolExecutor_IgnoresToolCalls()
    {
        var handler = new ScriptedSseHandler(
            """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c","function":{"name":"ExecuteSql","arguments":"{}"}}]}}]}

            data: [DONE]
            """);
        var client = new OpenAiCompatibleChatClient(
            new Uri("http://localhost:1234/v1"),
            "test-model",
            toolExecutor: null,
            httpClient: new HttpClient(handler));

        var collected = await CollectAsync(client, messages: [new ChatMessage(ChatRole.User, "run")]);

        // Without an executor the tool call is never executed and no follow-up request is made.
        Assert.Equal(1, handler.RequestCount);
        Assert.Empty(collected);
    }

    [Fact]
    public async Task Streaming_ReasoningOnly_FlushesReasoningAsAnswer()
    {
        var handler = new ScriptedSseHandler(
            """
            data: {"choices":[{"delta":{"reasoning_content":"The user just said hi. I should answer "}}]}

            data: {"choices":[{"delta":{"reasoning_content":"in a friendly way."}}]}

            data: [DONE]
            """);
        var client = new OpenAiCompatibleChatClient(
            new Uri("http://localhost:1234/v1"),
            "test-model",
            httpClient: new HttpClient(handler));

        var collected = await CollectAsync(client, messages: [new ChatMessage(ChatRole.User, "HI")]);

        // A thinking model that answers entirely inside its reasoning phase (no delta.content)
        // must still produce a visible answer.
        Assert.Equal("The user just said hi. I should answer in a friendly way.", string.Concat(collected));
    }

    [Fact]
    public async Task Streaming_ReasoningThenContent_ExposesReasoningViaProperties()
    {
        var handler = new ScriptedSseHandler(
            """
            data: {"choices":[{"delta":{"reasoning_content":"thinking part"}}]}

            data: {"choices":[{"delta":{"content":"final answer"}}]}

            data: [DONE]
            """);
        var client = new OpenAiCompatibleChatClient(
            new Uri("http://localhost:1234/v1"),
            "test-model",
            httpClient: new HttpClient(handler));

        var reasoning = new List<string>();
        var collected = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "q")]))
        {
            if (update.AdditionalProperties?.TryGetValue("reasoning_content", out var r) == true
                && r is string reasoningChunk)
            {
                reasoning.Add(reasoningChunk);
            }

            if (update.Text is not null)
            {
                collected.Add(update.Text);
            }
        }

        Assert.Equal("final answer", string.Concat(collected));
        Assert.Equal("thinking part", string.Concat(reasoning));
    }

    private static async Task<List<string>> CollectAsync(
        OpenAiCompatibleChatClient client,
        IList<ChatMessage> messages)
    {
        var collected = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync(messages))
        {
            if (update.Text is not null)
            {
                collected.Add(update.Text);
            }
        }

        return collected;
    }

    private sealed class ScriptedSseHandler : HttpMessageHandler
    {
        private readonly List<string> _bodies;
        private int _index;
        public int RequestCount { get; private set; }
        public List<string> RequestBodies { get; } = [];

        public ScriptedSseHandler(params string[] bodies)
        {
            _bodies = bodies.ToList();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            // Each request consumes the next body; the last body repeats for later rounds.
            var index = Math.Min(_index, Math.Max(0, _bodies.Count - 1));
            _index++;
            var body = _bodies.Count == 0 ? "data: [DONE]\n" : _bodies[index];

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
