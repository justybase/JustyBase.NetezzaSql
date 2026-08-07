using JustyBase.Ai.Chat;
using Microsoft.Extensions.AI;
using System.Net;
using System.Text;

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
