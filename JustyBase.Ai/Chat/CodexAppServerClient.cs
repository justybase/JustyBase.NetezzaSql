using JustyBase.Ai.Ports;
using JustyBase.Ai.Models;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace JustyBase.Ai.Chat;

/// <summary>
/// Small JSONL client for the official Codex app-server protocol.
/// It deliberately keeps authentication in Codex itself and never persists tokens in JustyBase.
/// </summary>
public sealed class CodexAppServerClient : IAsyncDisposable, IDisposable
{
    private readonly IChatEnvironment _environment;
    private readonly ISimpleLogger _logger;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly object _stateLock = new();
    private Process? _process;
    private StreamWriter? _writer;
    private Task? _readerTask;
    private long _nextRequestId;
    private bool _initialized;
    private bool _disposed;
    private bool _threadReady;
    private string? _activeTurnId;
    private bool _interruptRequested;
    private string? _threadId;
    private ChatMode? _threadMode;
    private string? _codexHome;
    private Func<string, string, Task<string>>? _toolHandler;
    private Func<string, string, Task<bool>>? _toolApprovalHandler;
    private Dictionary<string, string>? _activeTurnToolResults;

    public CodexAppServerClient(IChatEnvironment environment, ISimpleLogger logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public bool IsRunning => _process is { HasExited: false };
    public string? ThreadId => _threadId;
    public CodexAccountInfo? Account { get; private set; }
    public string? LastError { get; private set; }

    public void SetToolHandler(Func<string, string, Task<string>> handler, Func<string, string, Task<bool>> approvalHandler)
    {
        _toolHandler = handler;
        _toolApprovalHandler = approvalHandler;
    }

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized && IsRunning)
            return true;

        // Initialization owns a process and its SQLite state.  It must not be
        // aborted merely because a UI operation waiting behind it was cancelled:
        // doing so used to leave concurrent account probes and Sign in requests
        // reporting a spurious unauthenticated state.
        await _startLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        Process? startupProcess = null;
        try
        {
            if (_initialized && IsRunning)
                return true;

            if (cancellationToken.IsCancellationRequested)
                return false;

            await StopProcessAsync().ConfigureAwait(false);
            LastError = null;

            var command = ResolveCodexCommand();
            var startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = AppendArguments(command.Arguments,
                    "app-server --stdio " +
                    "--disable shell_tool " +
                    "--disable unified_exec " +
                    "--disable apps " +
                    "--disable plugins " +
                    "--disable browser_use " +
                    "--disable browser_use_external " +
                    "--disable computer_use " +
                    "--disable image_generation " +
                    "--disable web_search_request"),
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Codex app-server consumes strict JSONL. Encoding.UTF8 may
                // emit a UTF-8 BOM on the first write; that makes the first
                // JSON-RPC message fail with "expected value at line 1 column
                // 1" and leaves every request waiting until cancellation.
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            };
            var codexHome = GetCodexHome();
            var restrictedWorkspace = Path.Combine(codexHome, "workspace");
            Directory.CreateDirectory(restrictedWorkspace);
            EnsureRestrictedCodexConfig(codexHome);
            startInfo.Environment["CODEX_HOME"] = codexHome;

            startupProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            startupProcess.Exited += (_, _) => FailPending(new InvalidOperationException("Codex app-server stopped."));
            if (!startupProcess.Start())
            {
                LastError = "Could not start the Codex app-server. Install Codex or set JUSTYBASE_CODEX_COMMAND.";
                return false;
            }

            _process = startupProcess;
            startupProcess = null;
            _writer = _process.StandardInput;
            _readerTask = Task.Run(() => ReadLoopAsync(_process.StandardOutput, _process.StandardError), CancellationToken.None);

            // Bound startup independently of a caller's UI cancellation token.
            // A sign-in retry can then wait for the same startup instead of
            // cancelling the app-server while it is acquiring its local state.
            using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var initialize = await RequestAsync(
                "initialize",
                new CodexInitializeParameters
                {
                    ClientInfo = new CodexClientInfo
                    {
                        Name = "JustyBase",
                        Title = "JustyBase SQL Editor",
                        Version = GetVersion()
                    },
                    Capabilities = new CodexClientCapabilities { ExperimentalApi = true }
                },
                CodexJsonContext.Default.CodexInitializeParameters,
                startupTimeout.Token).ConfigureAwait(false);

            _initialized = initialize.ValueKind != JsonValueKind.Undefined;
            await NotifyAsync("initialized", new CodexEmptyParameters(), CodexJsonContext.Default.CodexEmptyParameters, startupTimeout.Token).ConfigureAwait(false);
            return _initialized;
        }
        catch (Exception ex)
        {
            LastError = BuildUserError(ex);
            _logger.TrackError(ex, isCrash: false);
            await StopProcessAsync().ConfigureAwait(false);
            return false;
        }
        finally
        {
            startupProcess?.Dispose();
            _startLock.Release();
        }
    }

    public async Task<CodexAccountInfo?> ReadAccountAsync(CancellationToken cancellationToken = default)
    {
        if (!await InitializeAsync(cancellationToken).ConfigureAwait(false))
            return null;

        try
        {
            var result = await RequestAsync(
                "account/read",
                new CodexAccountReadParameters { IncludeToken = false },
                CodexJsonContext.Default.CodexAccountReadParameters,
                cancellationToken).ConfigureAwait(false);
            Account = CodexAccountInfo.FromJson(result);
            return Account;
        }
        catch (Exception ex)
        {
            LastError = BuildUserError(ex);
            return null;
        }
    }

    public async Task<bool> StartChatGptLoginAsync(CancellationToken cancellationToken = default)
    {
        if (!await InitializeAsync(cancellationToken).ConfigureAwait(false))
            return false;

        try
        {
            var result = await RequestAsync(
                "account/login/start",
                new CodexLoginStartParameters { Type = "chatgpt" },
                CodexJsonContext.Default.CodexLoginStartParameters,
                cancellationToken).ConfigureAwait(false);
            var authUrl = result.TryGetProperty("authUrl", out var url) ? url.GetString() : null;
            if (string.IsNullOrWhiteSpace(authUrl))
                throw new InvalidOperationException("Codex app-server did not return a ChatGPT sign-in URL.");

            Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });

            Account = CodexAccountInfo.FromJson(result);
            return true;
        }
        catch (Exception ex)
        {
            LastError = BuildUserError(ex);
            _logger.TrackError(ex, isCrash: false);
            return false;
        }
    }

    public async Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (!await InitializeAsync(cancellationToken).ConfigureAwait(false))
            return false;

        try
        {
            await RequestAsync(
                "account/logout",
                new CodexEmptyParameters(),
                CodexJsonContext.Default.CodexEmptyParameters,
                cancellationToken).ConfigureAwait(false);
            Account = null;
            _threadId = null;
            _threadReady = false;
            _threadMode = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = BuildUserError(ex);
            return false;
        }
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        if (!await InitializeAsync(cancellationToken).ConfigureAwait(false))
            return [];

        try
        {
            var result = await RequestAsync(
                "model/list",
                new CodexEmptyParameters(),
                CodexJsonContext.Default.CodexEmptyParameters,
                cancellationToken).ConfigureAwait(false);
            var models = new List<string> { "Auto" };
            if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in data.EnumerateArray())
                {
                    var id = model.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(id) && !models.Contains(id, StringComparer.OrdinalIgnoreCase))
                        models.Add(id);
                }
            }
            return models;
        }
        catch
        {
            return ["Auto"];
        }
    }

    public async Task<List<string>> ListReasoningEffortsAsync(string? modelId, CancellationToken cancellationToken = default)
    {
        if (!await InitializeAsync(cancellationToken).ConfigureAwait(false))
            return [];

        try
        {
            var result = await RequestAsync(
                "model/list",
                new CodexEmptyParameters(),
                CodexJsonContext.Default.CodexEmptyParameters,
                cancellationToken).ConfigureAwait(false);

            var selectedModel = string.IsNullOrWhiteSpace(modelId) || string.Equals(modelId, "Auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : modelId;
            var efforts = new List<string>();

            if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in data.EnumerateArray())
                {
                    var id = model.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    if (selectedModel is not null && !string.Equals(id, selectedModel, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!model.TryGetProperty("supportedReasoningEfforts", out var supported) || supported.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var effort in supported.EnumerateArray())
                    {
                        var value = ReadReasoningEffortValue(effort);
                        if (!string.IsNullOrWhiteSpace(value) && !efforts.Contains(value, StringComparer.OrdinalIgnoreCase))
                            efforts.Add(value);
                    }

                    if (selectedModel is not null && efforts.Count > 0)
                        break;
                }
            }

            return efforts.Count > 0
                ? efforts
                : ["low", "medium", "high"];
        }
        catch
        {
            return ["low", "medium", "high"];
        }
    }

    internal static string? ReadReasoningEffortValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString();

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        // Current Codex app-server versions return entries such as:
        // { "reasoningEffort": "low", "description": "..." }.
        // Older versions used one of the fallback names below.
        foreach (var propertyName in new[] { "reasoningEffort", "effort", "value", "id", "name" })
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                return property.GetString();
        }

        return null;
    }

    public async IAsyncEnumerable<string> SendAsync(
        IReadOnlyList<ChatMessage> messages,
        string? modelId,
        string? reasoningEffort,
        ChatMode mode,
        string systemPrompt,
        string context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!await InitializeAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return $"[Codex unavailable: {LastError}]";
            yield break;
        }

        var lastUserMessage = messages.LastOrDefault(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
        if (lastUserMessage is null)
            yield break;

        if (_threadMode != mode)
        {
            _threadReady = false;
            _threadMode = mode;
        }

        if (string.IsNullOrWhiteSpace(_threadId))
        {
            var thread = await RequestAsync(
                "thread/start",
                BuildThreadStartParameters(modelId, mode),
                CodexJsonContext.Default.CodexThreadStartParameters,
                cancellationToken).ConfigureAwait(false);
            _threadId = ReadString(thread, "thread", "id") ?? ReadString(thread, "id");
            if (string.IsNullOrWhiteSpace(_threadId))
                throw new InvalidOperationException("Codex did not return a thread id.");
            _threadReady = true;
        }
        else if (!_threadReady)
        {
            try
            {
                await RequestAsync(
                    "thread/resume",
                    new CodexThreadResumeParameters
                    {
                        ThreadId = _threadId!,
                        DynamicTools = CodexToolSchemas.Create(mode)
                    },
                    CodexJsonContext.Default.CodexThreadResumeParameters,
                    cancellationToken).ConfigureAwait(false);
                _threadReady = true;
            }
            catch (InvalidOperationException)
            {
                // A deleted/expired persisted thread should not make the chat unusable.
                _threadId = null;
                var thread = await RequestAsync(
                    "thread/start",
                    BuildThreadStartParameters(modelId, mode),
                    CodexJsonContext.Default.CodexThreadStartParameters,
                    cancellationToken).ConfigureAwait(false);
                _threadId = ReadString(thread, "thread", "id") ?? ReadString(thread, "id");
                if (string.IsNullOrWhiteSpace(_threadId))
                    throw new InvalidOperationException("Codex did not return a thread id.");
                _threadReady = true;
            }
        }

        var prompt = BuildPrompt(messages, lastUserMessage, mode, systemPrompt, context);
        var turnToolResults = new Dictionary<string, string>(StringComparer.Ordinal);
        lock (_stateLock)
        {
            _activeTurnToolResults = turnToolResults;
            _activeTurnId = null;
            _interruptRequested = false;
        }

        try
        {
            await foreach (var item in StreamUntilTurnCompletesAsync(
                turnCancellationToken => RequestAsync(
                    "turn/start",
                    new CodexTurnStartParameters
                    {
                        ThreadId = _threadId!,
                        Input =
                        [
                            new CodexTurnInput { Type = "text", Text = prompt }
                        ],
                        Model = string.Equals(modelId, "Auto", StringComparison.OrdinalIgnoreCase) ? null : modelId,
                        Effort = string.IsNullOrWhiteSpace(reasoningEffort) ? null : reasoningEffort
                    },
                    CodexJsonContext.Default.CodexTurnStartParameters,
                    turnCancellationToken),
                cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_activeTurnToolResults, turnToolResults))
                    _activeTurnToolResults = null;
            }
        }
    }

    public async Task InterruptCurrentTurnAsync()
    {
        string? threadId;
        string? turnId;
        lock (_stateLock)
        {
            threadId = _threadId;
            turnId = _activeTurnId;
            if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId))
            {
                _interruptRequested = true;
                return;
            }
        }

        try
        {
            await RequestAsync(
                "turn/interrupt",
                new CodexTurnInterruptParameters { ThreadId = threadId!, TurnId = turnId! },
                CodexJsonContext.Default.CodexTurnInterruptParameters,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The turn may have completed between reading its id and sending
            // the interrupt. The UI cancellation has already happened, so an
            // error here must not keep Stop waiting or replace the user's state.
            Debug.WriteLine($"[Codex] turn/interrupt ignored: {ex.Message}");
        }
    }

    public void SetThreadId(string? threadId)
    {
        _threadId = threadId;
        _threadReady = false;
        _threadMode = null;
    }

    private CodexThreadStartParameters BuildThreadStartParameters(string? modelId, ChatMode mode)
        => new()
        {
            Model = string.Equals(modelId, "Auto", StringComparison.OrdinalIgnoreCase) ? null : modelId,
            ApprovalPolicy = "never",
            Sandbox = "read-only",
            CurrentDirectory = Path.Combine(GetCodexHome(), "workspace"),
            DynamicTools = CodexToolSchemas.Create(mode)
        };

    private static string BuildPrompt(
        IReadOnlyList<ChatMessage> messages,
        ChatMessage lastUserMessage,
        ChatMode mode,
        string systemPrompt,
        string context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the AI assistant inside JustyBase SQL Editor.");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Current mode: {mode.ToDisplayName()}.");
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            sb.AppendLine(systemPrompt.Trim());
        sb.AppendLine("Use only the supplied active SQL context and the explicitly available tools.");
        sb.AppendLine("Never request or expose SQL result rows. SQL execution and editor changes require user approval.");
        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.AppendLine();
            sb.AppendLine(context.Trim());
        }
        sb.AppendLine();
        foreach (var message in messages.Where(m => m != lastUserMessage).TakeLast(10))
        {
            sb.Append(message.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "User: " : "Assistant: ");
            sb.AppendLine(message.Content);
        }
        sb.AppendLine("User: ");
        sb.AppendLine(lastUserMessage.Content);
        return sb.ToString();
    }

    private async IAsyncEnumerable<string> StreamUntilTurnCompletesAsync(
        Func<CancellationToken, Task<JsonElement>> startTurn,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var queue = new ConcurrentQueue<string>();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalError = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousHandler = _streamHandler;
        using var turnTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        turnTimeoutCts.CancelAfter(TimeSpan.FromMinutes(2));
        Task<JsonElement>? turnTask = null;
        using var turnStartTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _streamHandler = async (method, parameters) =>
        {
            if (method == "item/agentMessage/delta")
            {
                var delta = ReadString(parameters, "delta") ?? ReadString(parameters, "text");
                if (!string.IsNullOrEmpty(delta)) queue.Enqueue(delta);
            }

            if (method == "turn/completed")
            {
                completion.TrySetResult(true);
            }
            else if (method is "turn/failed" or "error")
            {
                // The app-server may emit a reconnect/transport error and then
                // retry the upstream request several times. The UI must not wait
                // for those retries: surface the first actionable error and stop
                // the turn request immediately.
                terminalError.TrySetResult(CreateTurnError(method, parameters));
                completion.TrySetResult(true);
                turnTimeoutCts.Cancel();
            }

            await Task.CompletedTask;
        };

        try
        {
            // Keep the acknowledgement request alive long enough to obtain the
            // turn id even if the UI cancels immediately. Once the id arrives,
            // InterruptCurrentTurnAsync can send the protocol-level interrupt.
            turnTask = startTurn(turnStartTimeout.Token);
            // turn/start only acknowledges that the turn was accepted. The
            // actual response arrives through notifications. Awaiting this task
            // here also prevents it from being included in the loop below after
            // it has already completed, which would create a synchronous,
            // zero-delay loop on the caller's context.
            JsonElement turnStartResult;
            try
            {
                turnStartResult = await turnTask.ConfigureAwait(false);
                var turnId = ReadString(turnStartResult, "turn", "id") ?? ReadString(turnStartResult, "id");
                var interruptImmediately = false;
                lock (_stateLock)
                {
                    _activeTurnId = turnId;
                    interruptImmediately = _interruptRequested;
                }

                if (interruptImmediately && !string.IsNullOrWhiteSpace(turnId))
                    await InterruptCurrentTurnAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (terminalError.Task.IsCompleted)
            {
                // The notification handler below has the more useful protocol
                // error. It is re-thrown immediately after this acknowledgement.
            }

            if (terminalError.Task.IsCompleted)
                throw await terminalError.Task.ConfigureAwait(false);

            var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, turnTimeoutCts.Token);
            while (!completion.Task.IsCompleted || !queue.IsEmpty)
            {
                while (queue.TryDequeue(out var chunk))
                    yield return chunk;

                if (terminalError.Task.IsCompleted)
                    throw await terminalError.Task.ConfigureAwait(false);

                await Task.WhenAny(
                    completion.Task,
                    terminalError.Task,
                    timeoutTask,
                    Task.Delay(30, cancellationToken)).ConfigureAwait(false);

                if (terminalError.Task.IsCompleted)
                    throw await terminalError.Task.ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (turnTimeoutCts.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested
                    && !completion.Task.IsCompleted)
                {
                    throw new TimeoutException("Codex did not complete the turn within two minutes.");
                }
            }

            while (queue.TryDequeue(out var remainingChunk))
                yield return remainingChunk;

            if (terminalError.Task.IsCompleted)
                throw await terminalError.Task.ConfigureAwait(false);

            await turnTask.ConfigureAwait(false);
        }
        finally
        {
            try { turnTimeoutCts.Cancel(); }
            catch (ObjectDisposedException) { }
            if (turnTask is not null && !turnTask.IsCompleted)
            {
                try { await turnTask.ConfigureAwait(false); }
                catch { }
            }
            _streamHandler = previousHandler;
            lock (_stateLock)
            {
                _activeTurnId = null;
                _interruptRequested = false;
            }
        }
    }

    private static Exception CreateTurnError(string method, JsonElement parameters)
    {
        var message = ReadString(parameters, "error", "message")
                      ?? ReadString(parameters, "error")
                      ?? ReadString(parameters, "message")
                      ?? "Codex app-server reported an error.";
        var details = ReadString(parameters, "error", "additionalDetails")
                      ?? ReadString(parameters, "additionalDetails");
        var description = string.IsNullOrWhiteSpace(details)
            ? message
            : $"{message} {details}";

        return new InvalidOperationException($"Codex {method}: {description}");
    }

    private Func<string, JsonElement, Task>? _streamHandler;

    private async Task<JsonElement> RequestAsync<T>(
        string method,
        T parameters,
        JsonTypeInfo<T> parametersTypeInfo,
        CancellationToken cancellationToken)
    {
        if (_writer is null || !IsRunning)
            throw new InvalidOperationException("Codex app-server is not running.");

        var id = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            var request = new CodexRpcRequest
            {
                Id = id,
                Method = method,
                Parameters = JsonSerializer.SerializeToElement(parameters, parametersTypeInfo)
            };
            await WriteAsync(request, CodexJsonContext.Default.CodexRpcRequest, cancellationToken).ConfigureAwait(false);
            return await tcs.Task.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task NotifyAsync<T>(
        string method,
        T parameters,
        JsonTypeInfo<T> parametersTypeInfo,
        CancellationToken cancellationToken)
    {
        var request = new CodexRpcRequest
        {
            Method = method,
            Parameters = JsonSerializer.SerializeToElement(parameters, parametersTypeInfo)
        };
        return WriteAsync(request, CodexJsonContext.Default.CodexRpcRequest, cancellationToken);
    }

    private async Task WriteAsync<T>(T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        var writer = _writer ?? throw new InvalidOperationException("Codex app-server input is unavailable.");
        var line = JsonSerializer.Serialize(value, typeInfo);
        lock (_stateLock)
        {
            writer.WriteLine(line);
            writer.Flush();
        }
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task ReadLoopAsync(StreamReader stdout, StreamReader stderr)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var line = await stderr.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;
                if (!string.IsNullOrWhiteSpace(line))
                    Debug.WriteLine($"[Codex] {line}");
            }
        });

        while (!_disposed)
        {
            var line = await stdout.ReadLineAsync().ConfigureAwait(false);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement.Clone();
                var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
                var parameters = root.TryGetProperty("params", out var paramsElement) ? paramsElement.Clone() : default;
                if (method == "item/tool/call")
                {
                    _ = HandleToolCallAsync(root, parameters);
                    continue;
                }

                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id) && _pending.TryGetValue(id, out var pending))
                {
                    if (root.TryGetProperty("error", out var error))
                        pending.TrySetException(new InvalidOperationException(error.ToString()));
                    else
                        pending.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : root);
                    continue;
                }

                if (method is not null && _streamHandler is { } streamHandler)
                {
                    _ = streamHandler(method, parameters);
                }
            }
            catch (Exception ex)
            {
                _logger.TrackError(ex, isCrash: false);
            }
        }
    }

    private async Task HandleToolCallAsync(JsonElement request, JsonElement parameters)
    {
        var name = ReadString(parameters, "tool")
                   ?? ReadString(parameters, "name")
                   ?? ReadString(parameters, "toolName")
                   ?? string.Empty;
        var arguments = parameters.TryGetProperty("arguments", out var args) ? args.ToString() : "{}";
        var result = "Tool is unavailable.";
        var success = false;

        try
        {
            var writeTool = name is "apply_sql_document_change" or "execute_sql";
            var operationKey = writeTool ? BuildToolOperationKey(name, arguments) : null;
            if (writeTool && operationKey is not null && TryGetActiveTurnToolResult(operationKey, out var previousResult))
            {
                // A model can retry the same dynamic tool call after a slow
                // approval. The document was already changed, so asking again
                // would be both confusing and unsafe.
                result = $"The same operation was already approved and completed earlier in this turn. Do not call it again. Result: {previousResult}";
                success = true;
            }
            else if (writeTool && _toolApprovalHandler is not null && !await _toolApprovalHandler(name, arguments).ConfigureAwait(false))
            {
                result = "The user denied this operation.";
            }
            else if (_toolHandler is not null)
            {
                result = await _toolHandler(name, arguments).ConfigureAwait(false);
                success = IsSuccessfulToolResult(result);

                if (success && operationKey is not null)
                    CacheActiveTurnToolResult(operationKey, result);
            }
        }
        catch (Exception ex)
        {
            result = $"Tool failed: {ex.Message}";
        }

        if (request.TryGetProperty("id", out var requestId) && requestId.TryGetInt64(out var requestIdValue))
        {
            await WriteAsync(
                new CodexToolCallResponse
                {
                    Id = requestIdValue,
                    Result = new CodexToolCallResult
                    {
                        ContentItems =
                        [
                            new CodexContentItem { Type = "inputText", Text = result }
                        ],
                        Success = success
                    }
                },
                CodexJsonContext.Default.CodexToolCallResponse,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private bool TryGetActiveTurnToolResult(string operationKey, out string result)
    {
        lock (_stateLock)
        {
            if (_activeTurnToolResults is not null
                && _activeTurnToolResults.TryGetValue(operationKey, out var cached))
            {
                result = cached;
                return true;
            }
        }

        result = string.Empty;
        return false;
    }

    private void CacheActiveTurnToolResult(string operationKey, string result)
    {
        lock (_stateLock)
        {
            _activeTurnToolResults?[operationKey] = result;
        }
    }

    internal static string BuildToolOperationKey(string toolName, string arguments)
    {
        if (toolName.Equals("apply_sql_document_change", StringComparison.Ordinal)
            && TryReadJsonString(arguments, "proposedSql", out var proposedSql))
        {
            return $"{toolName}\n{proposedSql.Replace("\r\n", "\n", StringComparison.Ordinal).Trim()}";
        }

        return $"{toolName}\n{arguments.Trim()}";
    }

    private static bool TryReadJsonString(string json, string propertyName, out string value)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString() ?? string.Empty;
                return true;
            }
        }
        catch (JsonException)
        {
            // Fall back to the raw argument text for malformed tool input.
        }

        value = string.Empty;
        return false;
    }

    private static bool IsSuccessfulToolResult(string result)
        => !string.IsNullOrWhiteSpace(result)
           && !result.StartsWith("[Error", StringComparison.OrdinalIgnoreCase)
           && !result.StartsWith("[Blocked", StringComparison.OrdinalIgnoreCase)
           && !result.StartsWith("[SQL execution denied", StringComparison.OrdinalIgnoreCase)
           && !result.StartsWith("[SQL change denied", StringComparison.OrdinalIgnoreCase)
           && !result.StartsWith("Tool failed", StringComparison.OrdinalIgnoreCase)
           && !result.StartsWith("Failed ", StringComparison.OrdinalIgnoreCase)
           && !result.Contains(" lookup failed:", StringComparison.OrdinalIgnoreCase)
           && !result.Contains(" search failed:", StringComparison.OrdinalIgnoreCase)
           && !result.Contains(" execution failed:", StringComparison.OrdinalIgnoreCase);

    private void FailPending(Exception exception)
    {
        foreach (var pair in _pending)
            pair.Value.TrySetException(exception);
    }

    private async Task StopProcessAsync()
    {
        _initialized = false;
        _threadReady = false;
        _threadMode = null;
        FailPending(new OperationCanceledException("Codex app-server stopped."));
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch { }
        _writer = null;
        _process?.Dispose();
        _process = null;
        await Task.CompletedTask;
    }

    private static string? ReadString(JsonElement element, params string[] path)
    {
        foreach (var property in path)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var child))
                element = child;
            else
                return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static CodexCommand ResolveCodexCommand()
    {
        var configured = Environment.GetEnvironmentVariable("JUSTYBASE_CODEX_COMMAND");
        var requested = string.IsNullOrWhiteSpace(configured) ? "codex" : configured.Trim();
        var executable = FindExecutable(requested);

        if (string.IsNullOrWhiteSpace(executable))
            executable = requested;

        if (OperatingSystem.IsWindows() && executable.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var shell = FindExecutable("pwsh.exe") ?? FindExecutable("powershell.exe") ?? "powershell.exe";
            return new CodexCommand(shell, $"-NoProfile -ExecutionPolicy Bypass -File {QuoteArgument(executable)}");
        }

        return new CodexCommand(executable, string.Empty);
    }

    private static string? FindExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var candidate = command.Trim().Trim('"');
        if (Path.IsPathRooted(candidate) && File.Exists(candidate))
            return candidate;

        if (candidate.Contains(Path.DirectorySeparatorChar) || candidate.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;

        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".cmd", ".exe", ".bat", ".ps1", string.Empty }
            : new[] { string.Empty };
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var path = Path.Combine(directory.Trim().Trim('"'), candidate + extension);
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    private static string AppendArguments(string prefix, string suffix)
        => string.IsNullOrWhiteSpace(prefix) ? suffix : $"{prefix} {suffix}";

    private static string QuoteArgument(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value.Replace("\"", "\\\"")}\"" : value;

    private string GetCodexHome()
    {
        if (!string.IsNullOrWhiteSpace(_codexHome))
            return _codexHome;

        var persistentHome = Path.Combine(_environment.ConfigDirectory, "codex");
        if (CanWriteToDirectory(persistentHome))
            return _codexHome = persistentHome;

        // Codex creates helper binaries in CODEX_HOME/tmp. Some restricted
        // launchers can read the application-data directory but cannot create
        // those files. Use a user-writable fallback so Sign in can still start.
        var fallbackHome = Path.Combine(Path.GetTempPath(), "JustyBase", "codex");
        Directory.CreateDirectory(fallbackHome);
        CopyAuthenticationIfNeeded(persistentHome, fallbackHome);
        return _codexHome = fallbackHome;
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void CopyAuthenticationIfNeeded(string sourceHome, string destinationHome)
    {
        try
        {
            var source = Path.Combine(sourceHome, "auth.json");
            var destination = Path.Combine(destinationHome, "auth.json");
            if (File.Exists(source) && !File.Exists(destination))
                File.Copy(source, destination);
        }
        catch (UnauthorizedAccessException)
        {
            // A new browser sign-in is still possible when the old session
            // cannot be read from a restricted launcher.
        }
        catch (IOException)
        {
            // Do not make app-server startup depend on migrating old state.
        }
    }

    private static void EnsureRestrictedCodexConfig(string codexHome)
    {
        var configPath = Path.Combine(codexHome, "config.toml");
        const string config = """
            # JustyBase owns this CODEX_HOME. The app-server is used as a model gateway
            # with JustyBase-managed SQL tools; unrelated Codex tools stay disabled.
            approval_policy = "never"
            sandbox_mode = "read-only"

            [features]
            shell_tool = false
            unified_exec = false
            web_search = false
            web_search_request = false
            image_generation = false
            apps = false
            plugins = false
            browser_use = false
            browser_use_external = false
            computer_use = false
            """;

        if (!File.Exists(configPath) || !string.Equals(File.ReadAllText(configPath), config, StringComparison.Ordinal))
            File.WriteAllText(configPath, config, Encoding.UTF8);
    }

    private sealed record CodexCommand(string FileName, string Arguments);

    private static string BuildUserError(Exception ex)
        => ex is System.ComponentModel.Win32Exception
            ? "Codex CLI was not found. Install Codex or set JUSTYBASE_CODEX_COMMAND."
            : ex.Message;

    private static string GetVersion()
        => typeof(CodexAppServerClient).Assembly.GetName().Version?.ToString() ?? "dev";

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopProcessAsync().ConfigureAwait(false);
        _startLock.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}

public sealed record CodexAccountInfo(string? Email, string? Plan, bool IsAuthenticated)
{
    public static CodexAccountInfo? FromJson(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
            return null;

        // account/login/start returns a ChatGPT login descriptor containing an
        // auth URL and login id. That is not an authenticated account yet.
        if (json.TryGetProperty("authUrl", out _)
            || json.TryGetProperty("loginId", out _))
        {
            var loginPlan = json.TryGetProperty("planType", out var loginPlanElement)
                ? loginPlanElement.GetString()
                : null;
            return new CodexAccountInfo(null, loginPlan, false);
        }

        var account = json.TryGetProperty("account", out var accountElement) ? accountElement : json;
        var email = account.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null;
        var plan = account.TryGetProperty("planType", out var planElement)
            ? planElement.GetString()
            : json.TryGetProperty("planType", out var rootPlanElement)
                ? rootPlanElement.GetString()
                : null;
        var authMode = account.TryGetProperty("authMode", out var authModeElement)
            ? authModeElement.GetString()
            : json.TryGetProperty("authMode", out var rootAuthModeElement)
                ? rootAuthModeElement.GetString()
                : null;
        var authenticated = account.TryGetProperty("type", out var typeElement)
            ? !string.Equals(typeElement.GetString(), "none", StringComparison.OrdinalIgnoreCase)
            : !string.Equals(authMode, "none", StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(authMode) || !string.IsNullOrWhiteSpace(email));
        return new CodexAccountInfo(email, plan, authenticated);
    }
}

internal static class CodexToolSchemas
{
    public static CodexDynamicToolDefinition[] Create(ChatMode mode)
    {
        var all = new[]
        {
        Tool("get_current_sql", "Read the active SQL document. Never returns SQL result rows."),
        Tool("get_sql_editor_context", "Read active SQL text and current selection."),
        Tool("get_active_database_context", "Read active connection and database metadata."),
        Tool("list_schemas", "List database schemas only.", Property("databaseName", "string"), Property("limit", "integer")),
        Tool("browse_schema_objects", "Browse schema object names only.", Property("schemaName", "string"), Property("objectType", "string"), Property("limit", "integer")),
        Tool("search_schema_objects", "Search schema object names only.", Property("pattern", "string"), Property("objectType", "string"), Property("schemaName", "string"), Property("limit", "integer")),
        Tool("get_object_definition", "Read object DDL without table data.", Property("objectName", "string"), Property("objectType", "string"), Property("schemaName", "string"), Property("databaseName", "string"), Property("maxChars", "integer")),
        Tool("get_object_columns", "Read column metadata without table data.", Property("objectName", "string"), Property("schemaName", "string"), Property("databaseName", "string"), Property("limit", "integer")),
        Tool("get_table_metadata", "Read table metadata without statistics or result rows.", Property("tableName", "string"), Property("schemaName", "string"), Property("databaseName", "string"), Property("includeStatsPreview", "boolean"), Property("rowLimit", "integer")),
        Tool("get_diagnostics", "Read heuristic SQL diagnostics and lint errors. Results may be incomplete, stale, or incorrect; treat them as advisory and verify against the SQL and schema.", Property("severity", "string"), Property("limit", "integer")),
        Tool("get_last_execution_error", "Read the latest SQL execution error without result data."),
        Tool("get_netezza_reference", "Read Netezza SQL reference information.", Property("topic", "string")),
        Tool("export_schema", "Export bounded schema metadata and DDL without table rows.", Property("schemaName", "string"), Property("objectType", "string"), Property("maxChars", "integer")),
        Tool("apply_sql_document_change", "Propose a full replacement of the active SQL document. Requires user approval.", Property("proposedSql", "string")),
        Tool("execute_sql", "Execute exact SQL after user approval. Never returns result rows.", Property("sql", "string"))
        };

        return mode switch
        {
            ChatMode.Simple => [],
            ChatMode.SqlFix => all.Where(static tool => tool.Name is
                "get_current_sql" or
                "get_sql_editor_context" or
                "get_diagnostics" or
                "browse_schema_objects" or
                "get_object_columns" or
                "get_table_metadata" or
                "apply_sql_document_change").ToArray(),
            _ => all
        };
    }

    private static CodexDynamicToolDefinition Tool(string name, string description, params CodexToolProperty[] properties)
    {
        return new CodexDynamicToolDefinition
        {
            Type = "function",
            Name = name,
            Description = description,
            InputSchema = new CodexJsonSchema
            {
                Type = "object",
                AdditionalProperties = false,
                Properties = properties.ToDictionary(p => p.Name, p => new CodexJsonProperty { Type = p.Type }, StringComparer.Ordinal)
            }
        };
    }

    private static CodexToolProperty Property(string name, string type) => new(name, type);

    private sealed record CodexToolProperty(string Name, string Type);
}
