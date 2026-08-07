using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class ChatRetryStreamTests
{
    [Fact]
    public async Task Timeout_NothingYielded_RetriesThenSucceeds()
    {
        var call = 0;
        var result = await CollectAsync(
            toolsEnabled: false,
            maxRetries: 1,
            produce: _ =>
            {
                call++;
                return call == 1
                    ? new ScriptedStream(ct => Task.FromException<string>(new OperationCanceledException()))
                    : new ScriptedStream(ct => Task.FromResult("hello"));
            });

        Assert.Equal(["hello"], result);
        Assert.Equal(2, call);
    }

    [Fact]
    public async Task Timeout_AfterContentYielded_DoesNotRetry()
    {
        var call = 0;
        var result = await CollectAsync(
            toolsEnabled: false,
            maxRetries: 1,
            produce: _ =>
            {
                call++;
                return new ScriptedStream(
                    ct => Task.FromResult("partial"),
                    ct => Task.FromException<string>(new OperationCanceledException()));
            });

        Assert.Equal(["partial"], result);
        Assert.Equal(1, call);
    }

    [Fact]
    public async Task ToolRequest_Timeout_DoesNotRetryAndReportsError()
    {
        var call = 0;
        var result = await CollectAsync(
            toolsEnabled: true,
            maxRetries: 3,
            produce: _ =>
            {
                call++;
                return new ScriptedStream(ct => Task.FromException<string>(new OperationCanceledException()));
            });

        Assert.Equal(1, call);
        Assert.Equal(["\n[Response timeout - no response within 300 seconds]"], result);
    }

    [Fact]
    public async Task UserCancel_StopsImmediately()
    {
        using var cts = new CancellationTokenSource();
        await using var enumerator = LocalChatService.StreamWithRetryCoreAsync(
                toolsEnabled: false,
                timeoutSeconds: 120,
                maxRetries: 1,
                produce: _ => new ScriptedStream(
                    ct => Task.FromResult("a"),
                    async _ =>
                    {
                        await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
                        return "x";
                    }))
            .GetAsyncEnumerator(cts.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("a", enumerator.Current);

        cts.Cancel();
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task Error_AfterPartialYield_NoRetryNoErrorText()
    {
        var tracked = 0;
        var result = await CollectAsync(
            toolsEnabled: false,
            maxRetries: 1,
            produce: _ => new ScriptedStream(
                ct => Task.FromResult("a"),
                ct => Task.FromException<string>(new InvalidOperationException("boom"))),
            trackError: _ => tracked++);

        Assert.Equal(["a"], result);
        Assert.Equal(1, tracked);
    }

    [Fact]
    public async Task Success_YieldsAllChunks()
    {
        var result = await CollectAsync(
            toolsEnabled: false,
            maxRetries: 1,
            produce: _ => new ScriptedStream(
                ct => Task.FromResult("a"),
                ct => Task.FromResult("b")));

        Assert.Equal(["a", "b"], result);
    }

    private static async Task<List<string>> CollectAsync(
        bool toolsEnabled,
        int maxRetries,
        Func<CancellationToken, IAsyncEnumerable<string>> produce,
        Action<Exception>? trackError = null)
    {
        var result = new List<string>();
        await foreach (var chunk in LocalChatService.StreamWithRetryCoreAsync(
            toolsEnabled: toolsEnabled,
            timeoutSeconds: 120,
            maxRetries: maxRetries,
            produce: produce,
            trackError: trackError))
        {
            result.Add(chunk);
        }

        return result;
    }

    /// <summary>Runs each step in turn; a faulted/cancelled step propagates on MoveNextAsync.</summary>
    private sealed class ScriptedStream : IAsyncEnumerable<string>
    {
        private readonly IReadOnlyList<Func<CancellationToken, Task<string>>> _steps;

        public ScriptedStream(params Func<CancellationToken, Task<string>>[] steps) => _steps = steps;

        public async IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            foreach (var step in _steps)
            {
                var value = await step(cancellationToken).ConfigureAwait(false);
                yield return value;
            }
        }
    }
}
