using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using JustyBase.Ai.Models;
using JustyBase.Ai.Ports;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ChatMessage = JustyBase.Ai.Models.ChatMessage;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CopilotSdkClient = GitHub.Copilot.CopilotClient;

namespace JustyBase.Ai.Chat;

/// <summary>
/// Wraps the official GitHub Copilot SDK (GitHub.Copilot) for use as a JustyBase chat
/// backend. Authentication is delegated to the Copilot CLI itself ("copilot login", the
/// OAuth device flow); JustyBase never stores tokens. A single SDK session is reused for
/// the lifetime of an application chat session, mirroring how Codex owns one thread.
/// </summary>
public sealed partial class CopilotClient : IAsyncDisposable, IDisposable
{
    private readonly IChatEnvironment _environment;
    private readonly ISimpleLogger _logger;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private CopilotSdkClient? _sdkClient;
    private CopilotSession? _session;
    private string? _requestedSessionId;
    private bool _initialized;
    private bool _disposed;
    private Process? _loginProcess;
    private Func<ChatMode, IReadOnlyList<AIFunction>>? _toolsProvider;
    private bool _loginBrowserOpened;
    private bool _loginCodeNotified;
    private CopilotSessionSettings? _sessionSettings;
    private bool _signedOut;

    private const string SignedOutMessage = "Copilot is signed out in JustyBase.";

    public CopilotClient(IChatEnvironment environment, ISimpleLogger logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public bool IsRunning => _initialized && _sdkClient is not null;

    /// <summary>SDK session id backing the current application chat session, or null.</summary>
    public string? CopilotSessionId
    {
        get
        {
            lock (_stateLock)
            {
                return _requestedSessionId;
            }
        }
    }

    public CopilotAccountInfo? Account { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Device-flow user code shown by the Copilot CLI during sign-in, or null.</summary>
    public string? LoginVerificationCode { get; private set; }

    /// <summary>Device-flow verification URL printed by the Copilot CLI, or null.</summary>
    public string? LoginVerificationUrl { get; private set; }

    /// <summary>
    /// Fired the moment the Copilot CLI prints the device-flow code (from stdout or stderr).
    /// The host surfaces the code in a dialog — the code is not visible anywhere else.
    /// </summary>
    public event Action<string, string>? LoginVerificationCodeAvailable;

    public void SetToolsProvider(Func<ChatMode, IReadOnlyList<AIFunction>>? provider)
        => _toolsProvider = provider;

    /// <summary>Starts the Copilot CLI runtime behind the SDK. Idempotent.</summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            LastError = "Copilot client has been disposed.";
            return false;
        }

        if (IsSignOutSuppressed())
        {
            LastError = SignedOutMessage;
            return false;
        }

        if (_initialized && _sdkClient is not null)
            return true;

        // Startup owns the CLI process. It must not be aborted merely because a UI
        // operation waiting behind it was cancelled.
        await _startLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!IsSignOutSuppressed() && _initialized && _sdkClient is not null)
                return true;

            if (_disposed || IsSignOutSuppressed() || cancellationToken.IsCancellationRequested)
            {
                LastError = _disposed ? "Copilot client has been disposed." : SignedOutMessage;
                return false;
            }

            LastError = null;

            var command = ResolveCopilotCommand();
            var options = new CopilotClientOptions
            {
                // Empty mode is required here because this is a SQL-only integration. It
                // disables ambient CLI capabilities; the session allowlist below opts in
                // only to JustyBase's registered custom tools.
                Mode = CopilotClientMode.Empty,
                BaseDirectory = GetCopilotHome(),
                WorkingDirectory = AppContext.BaseDirectory,
                LogLevel = CopilotLogLevel.Error,
                Logger = new SdkLoggerAdapter(_logger)
            };
            if (command is not null)
                options.Connection = RuntimeConnection.ForStdio(command.FileName, command.Arguments);

            var previousClient = _sdkClient;
            _sdkClient = new CopilotSdkClient(options);

            if (previousClient is not null)
            {
                try { await previousClient.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.TrackError(ex, isCrash: false); }
            }

            await _sdkClient.StartAsync().ConfigureAwait(false);
            _initialized = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = BuildUserError(ex);
            _logger.TrackError(ex, isCrash: false);
            if (_sdkClient is not null)
            {
                try { await _sdkClient.DisposeAsync().ConfigureAwait(false); }
                catch { }
            }

            _sdkClient = null;
            _initialized = false;
            return false;
        }
        finally
        {
            _startLock.Release();
        }
    }

    /// <summary>
    /// Reads the authentication status from the CLI runtime. Never exposes token material;
    /// only account identity (login, auth type) is surfaced to the UI.
    /// </summary>
    public async Task<CopilotAccountInfo?> ReadAccountAsync(CancellationToken cancellationToken = default)
    {
        if (IsSignOutSuppressed())
        {
            Account = new CopilotAccountInfo(false, null, null, "Signed out in JustyBase.");
            return Account;
        }

        if (!await InitializeAsync(cancellationToken).ConfigureAwait(false))
            return null;

        try
        {
            var status = await _sdkClient!.GetAuthStatusAsync().ConfigureAwait(false);

            // GetAuthStatusAsync reports gh-cli credentials as authenticated even when the
            // stored session is not usable for Copilot (stale token, revoked seat). Verify
            // with a real probe so the UI does not offer "sign out" to a user who is not
            // actually signed in.
            var usable = status.IsAuthenticated && await CanReachCopilotAsync().ConfigureAwait(false);

            Account = new CopilotAccountInfo(
                usable,
                usable ? status.Login : null,
                usable ? status.AuthType : null,
                usable ? status.StatusMessage : "Copilot session is not usable — sign in again.");
            return Account;
        }
        catch (Exception ex)
        {
            LastError = BuildUserError(ex);
            return null;
        }
    }

    private async Task<bool> CanReachCopilotAsync()
    {
        try
        {
            _ = await _sdkClient!.ListModelsAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Starts the Copilot CLI OAuth device flow in the background and opens the browser to
    /// the verification URL. The CLI itself stores the resulting token (never JustyBase).
    /// </summary>
    public async Task<bool> StartLoginAsync(CancellationToken cancellationToken = default)
    {
        // A previous logout intentionally suppresses all implicit credential use. Explicit
        // login is the one operation that is allowed to clear that suppression.
        ClearSignOutMarker();
        Volatile.Write(ref _signedOut, false);
        if (!await InitializeAsync(cancellationToken).ConfigureAwait(false))
            return false;

        try
        {
            StopLoginProcess();
            var command = ResolveCopilotLoginCommand();
            var startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            foreach (var arg in command.Arguments)
                startInfo.ArgumentList.Add(arg);
            startInfo.ArgumentList.Add("login");
            startInfo.ArgumentList.Add("--device-code");
            startInfo.Environment["NO_BROWSER"] = "1";

            LoginVerificationCode = null;
            LoginVerificationUrl = null;
            _loginBrowserOpened = false;
            _loginCodeNotified = false;

            var process = Process.Start(startInfo);
            if (process is null)
                throw new InvalidOperationException("Could not start the Copilot CLI for sign-in.");

            _loginProcess = process;
            _ = Task.Run(() => ObserveLoginProcessAsync(process), CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            LastError = BuildUserError(ex);
            _logger.TrackError(ex, isCrash: false);
            return false;
        }
    }

    /// <summary>
    /// Best-effort sign out: clears the Copilot CLI's recorded logged-in users and any
    /// plain-text token file under COPILOT_HOME. GitHub CLI (gh) credentials themselves are
    /// left untouched; a JustyBase marker prevents their silent reuse until explicit login.
    /// Resets the in-memory account and session state regardless.
    /// </summary>
    public async Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Sign out must invalidate the running runtime as well as the persisted
            // credentials. Otherwise a later SendAsync could reuse credentials that the
            // user has just revoked from this application.
            Volatile.Write(ref _signedOut, true);
            StopLoginProcess();
            var home = GetCopilotHome();
            var configCleared = RemoveLoggedInUsersFromConfig(Path.Combine(home, "config.json"));
            var authCleared = TryDeleteFile(Path.Combine(home, "auth.json"));
            var markerWritten = WriteSignOutMarker(home);
            var clientStopped = await StopSdkClientAsync(clearSessionId: true).ConfigureAwait(false);
            Account = null;
            if (!configCleared || !authCleared || !markerWritten || !clientStopped)
            {
                LastError = "Copilot local credentials could not be cleared completely.";
                return false;
            }

            // GitHub CLI credentials are deliberately not revoked here. The marker
            // prevents JustyBase from silently reusing them until the user starts a
            // new device-flow login explicitly.
            LastError = null;
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
            return ["Auto"];

        try
        {
            var models = await _sdkClient!.ListModelsAsync().ConfigureAwait(false);
            var result = new List<string> { "Auto" };
            foreach (var model in models)
            {
                if (!string.IsNullOrWhiteSpace(model.Id) && !result.Contains(model.Id, StringComparer.OrdinalIgnoreCase))
                    result.Add(model.Id);
            }

            return result;
        }
        catch
        {
            return ["Auto"];
        }
    }

    public async Task<List<string>> ListReasoningEffortsAsync(string? modelId, CancellationToken cancellationToken = default)
    {
        if (!await InitializeAsync(cancellationToken).ConfigureAwait(false))
            return ["low", "medium", "high"];

        try
        {
            var models = await _sdkClient!.ListModelsAsync().ConfigureAwait(false);
            var selectedModel = string.IsNullOrWhiteSpace(modelId) || string.Equals(modelId, "Auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : modelId;
            var efforts = new List<string>();
            foreach (var model in models)
            {
                if (selectedModel is not null && !string.Equals(model.Id, selectedModel, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (model.SupportedReasoningEfforts is null)
                    continue;

                foreach (var effort in model.SupportedReasoningEfforts)
                {
                    if (!string.IsNullOrWhiteSpace(effort) && !efforts.Contains(effort, StringComparer.OrdinalIgnoreCase))
                        efforts.Add(effort);
                }

                if (selectedModel is not null && efforts.Count > 0)
                    break;
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

    /// <summary>
    /// Streams a turn from the Copilot SDK session. Reasoning deltas are delivered through
    /// <paramref name="onReasoningChunk"/> so the host can surface them like other backends.
    /// </summary>
    public async IAsyncEnumerable<string> SendAsync(
        IReadOnlyList<ChatMessage> messages,
        string? modelId,
        string? reasoningEffort,
        ChatMode mode,
        string systemPrompt,
        string context,
        Action<string>? onReasoningChunk = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (IsSignOutSuppressed())
        {
            LastError = SignedOutMessage;
            yield return $"[GitHub Copilot unavailable: {LastError}]";
            yield break;
        }

        if (!await InitializeAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return $"[GitHub Copilot unavailable: {LastError}]";
            yield break;
        }

        var lastUserMessage = messages.LastOrDefault(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
        if (lastUserMessage is null)
            yield break;

        var session = await EnsureSessionAsync(modelId, reasoningEffort, mode, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            yield return $"[GitHub Copilot unavailable: {LastError}]";
            yield break;
        }

        var prompt = BuildPrompt(lastUserMessage, mode, systemPrompt, context);
        await foreach (var chunk in StreamUntilIdleAsync(session, prompt, onReasoningChunk, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    /// <summary>Aborts the in-flight turn in the SDK session.</summary>
    public async Task InterruptCurrentTurnAsync()
    {
        CopilotSession? session;
        lock (_stateLock)
        {
            session = _session;
        }

        if (session is null)
            return;

        try
        {
            await session.AbortAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The turn may have completed between reading the session and aborting.
            Debug.WriteLine($"[Copilot] abort ignored: {ex.Message}");
        }
    }

    /// <summary>
    /// Switches the Copilot backend to the SDK session backing the given application chat
    /// session. The current SDK session is dropped when the id differs; the replacement is
    /// created or resumed lazily on the next <see cref="SendAsync"/>.
    /// </summary>
    public void SetCopilotSessionId(string? sessionId)
    {
        CopilotSession? previousSession;
        lock (_stateLock)
        {
            if (string.Equals(sessionId, _requestedSessionId, StringComparison.Ordinal))
                return;

            previousSession = _session;
            _session = null;
            _sessionSettings = null;
            _requestedSessionId = sessionId;
        }

        if (previousSession is not null)
            _ = DisposeSessionQuietlyAsync(previousSession);
    }

    private async Task<CopilotSession?> EnsureSessionAsync(
        string? modelId,
        string? reasoningEffort,
        ChatMode mode,
        CancellationToken cancellationToken)
    {
        var desiredSettings = new CopilotSessionSettings(
            NormalizeModelId(modelId),
            NormalizeReasoningEffort(reasoningEffort),
            mode);

        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CopilotSession? session;
            CopilotSession? previousSession;
            string? requestedSessionId;
            lock (_stateLock)
            {
                session = _session;
                if (session is not null && desiredSettings.Equals(_sessionSettings))
                    return session;

                // Tool registrations and the permission callback are session-scoped. A
                // mode/settings change therefore needs a fresh SDK session wrapper so the
                // current configuration is applied instead of silently reusing the first
                // turn's configuration.
                previousSession = _session;
                _session = null;
                _sessionSettings = null;
                requestedSessionId = _requestedSessionId;
            }

            if (previousSession is not null)
                await DisposeSessionQuietlyAsync(previousSession).ConfigureAwait(false);

            var sdkClient = _sdkClient;
            if (sdkClient is null)
                return null;

            var resumed = false;
            if (!string.IsNullOrWhiteSpace(requestedSessionId))
            {
                try
                {
                    session = await sdkClient.ResumeSessionAsync(
                        requestedSessionId,
                        BuildResumeSessionConfig(desiredSettings),
                        cancellationToken).ConfigureAwait(false);
                    resumed = true;
                }
                catch (Exception ex)
                {
                    // A deleted/expired persisted session should not make the chat unusable.
                    Debug.WriteLine($"[Copilot] resume of {requestedSessionId} failed: {ex.Message}");
                    lock (_stateLock)
                    {
                        if (string.Equals(_requestedSessionId, requestedSessionId, StringComparison.Ordinal))
                            _requestedSessionId = null;
                    }
                }
            }

            if (session is null)
            {
                session = await sdkClient.CreateSessionAsync(
                    BuildSessionConfig(desiredSettings),
                    cancellationToken).ConfigureAwait(false);
            }

            // Resume applies the tool/permission configuration above. SetModelAsync is
            // still used for resumed sessions because model selection is a mutable session
            // property and some runtimes retain the previous selection on resume.
            if (resumed && desiredSettings.ModelId is not null)
            {
                try
                {
                    var options = new SetModelOptions
                    {
                        ReasoningEffort = desiredSettings.ReasoningEffort
                    };
                    await session.SetModelAsync(desiredSettings.ModelId, options, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Copilot] SetModelAsync ignored: {ex.Message}");
                }
            }

            lock (_stateLock)
            {
                _session = session;
                _sessionSettings = desiredSettings;
                _requestedSessionId = session.SessionId;
            }

            return session;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private SessionConfig BuildSessionConfig(CopilotSessionSettings settings)
    {
        var config = new SessionConfig();
        PopulateSessionConfig(config, settings);
        return config;
    }

    private ResumeSessionConfig BuildResumeSessionConfig(CopilotSessionSettings settings)
    {
        var config = new ResumeSessionConfig();
        PopulateSessionConfig(config, settings);
        return config;
    }

    private void PopulateSessionConfig(SessionConfigBase config, CopilotSessionSettings settings)
    {
        config.ClientName = "JustyBase";
        config.Model = settings.ModelId;
        config.ReasoningEffort = settings.ReasoningEffort;
        config.Streaming = true;
        config.WorkingDirectory = GetWorkspaceDirectory();
        config.OnPermissionRequest = RejectUnknownPermissionsAsync;

        var tools = _toolsProvider?.Invoke(settings.Mode) ?? [];
        config.Tools = tools.Select(t => (AIFunctionDeclaration)t).ToList();

        // Empty mode does not expose ambient CLI tools. Keep the allowlist explicit as
        // well, so a future SDK/runtime cannot widen the boundary by adding new built-ins.
        // Custom tool names are source-qualified because that is the SDK's canonical filter
        // syntax for declarations supplied through SessionConfigBase.Tools.
        config.AvailableTools = tools
            .Select(t => $"custom:{t.Name}")
            .ToList();
    }

    private static string? NormalizeModelId(string? modelId)
        => string.IsNullOrWhiteSpace(modelId) || string.Equals(modelId, "Auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : modelId.Trim();

    private static string? NormalizeReasoningEffort(string? reasoningEffort)
        => string.IsNullOrWhiteSpace(reasoningEffort) ? null : reasoningEffort.Trim();

    private sealed record CopilotSessionSettings(string? ModelId, string? ReasoningEffort, ChatMode Mode);

    /// <summary>
    /// All JustyBase tools skip the protocol permission prompt (SkipPermission) because write
    /// tools (ExecuteSql / ApplySqlFix) already pass through the host approval UI. Any other
    /// permission request — e.g. a built-in CLI tool that slipped through the exclusion list —
    /// is rejected with feedback instead of being left pending forever.
    /// </summary>
    private static Task<PermissionDecision> RejectUnknownPermissionsAsync(PermissionRequest request, PermissionInvocation invocation)
        => Task.FromResult(PermissionDecision.Reject("This operation is not allowed in JustyBase SQL Editor."));

    private async IAsyncEnumerable<string> StreamUntilIdleAsync(
        CopilotSession session,
        string prompt,
        Action<string>? onReasoningChunk,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var queue = new ConcurrentQueue<string>();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalError = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var turnTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        turnTimeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

        using var subscription = session.On<SessionEvent>(evt =>
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    if (!string.IsNullOrEmpty(delta.Data.DeltaContent))
                        queue.Enqueue(delta.Data.DeltaContent);
                    break;
                case AssistantReasoningDeltaEvent reasoning:
                    if (!string.IsNullOrEmpty(reasoning.Data.DeltaContent))
                        onReasoningChunk?.Invoke(reasoning.Data.DeltaContent);
                    break;
                case SessionErrorEvent error:
                    terminalError.TrySetResult(new InvalidOperationException($"GitHub Copilot: {error.Data.Message}"));
                    completion.TrySetResult(true);
                    turnTimeoutCts.Cancel();
                    break;
                case SessionIdleEvent:
                    completion.TrySetResult(true);
                    break;
            }
        });

        try
        {
            await session.SendAsync(new MessageOptions { Prompt = prompt }, cancellationToken).ConfigureAwait(false);

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
                    throw new TimeoutException("GitHub Copilot did not complete the turn within two minutes.");
                }
            }

            while (queue.TryDequeue(out var remainingChunk))
                yield return remainingChunk;

            if (terminalError.Task.IsCompleted)
                throw await terminalError.Task.ConfigureAwait(false);
        }
        finally
        {
            try { turnTimeoutCts.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    private static string BuildPrompt(
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

        // CopilotSession is stateful and already owns previous turns. Sending only this
        // turn prevents the retained UI history from being appended a second time.
        sb.AppendLine();
        sb.AppendLine("User: ");
        sb.AppendLine(lastUserMessage.Content);
        return sb.ToString();
    }

    #region Auth helpers

    private void StopLoginProcess()
    {
        var process = _loginProcess;
        _loginProcess = null;
        if (process is { HasExited: false })
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
        }

        process?.Dispose();
    }

    private async Task ObserveLoginProcessAsync(Process process)
    {
        try
        {
            // The CLI prints the verification URL/code to stdout, but when it cannot copy
            // the code to the clipboard it prints them to stderr instead. Both streams must
            // be consumed in real time so the code reaches the dialog immediately.
            var stderrTask = Task.Run(async () =>
            {
                while (process.StandardError.ReadLine() is { } line)
                    HandleLoginLine(line);
            }, CancellationToken.None);

            while (process.StandardOutput.ReadLine() is { } line)
                HandleLoginLine(line);

            await process.WaitForExitAsync().ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0 && LastError is null)
            {
                LastError = "Copilot CLI sign-in did not complete. Check the CLI output or run 'copilot login' in a terminal.";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Copilot login] {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_loginProcess, process))
                _loginProcess = null;
        }
    }

    private void HandleLoginLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        Debug.WriteLine($"[Copilot login] {line}");
        var url = TryBuildVerificationUrl(line);
        if (url is not null && !_loginBrowserOpened)
        {
            _loginBrowserOpened = true;
            OpenBrowser(url);
        }

        if (!_loginCodeNotified && !string.IsNullOrWhiteSpace(LoginVerificationCode))
        {
            _loginCodeNotified = true;
            LoginVerificationCodeAvailable?.Invoke(
                LoginVerificationCode,
                LoginVerificationUrl ?? "https://github.com/login/device");
        }
    }

    internal string? TryBuildVerificationUrl(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var code = DeviceCodeRegex().Match(line);
        var url = VerificationUrlRegex().Match(line);

        if (code.Success)
            LoginVerificationCode = code.Value;
        if (url.Success)
            LoginVerificationUrl = url.Value;

        if (!url.Success)
            return null;

        // The CLI may print the code and the URL on the same or on separate lines;
        // remember the last seen code so the browser is opened with it pre-filled either way.
        var codeToUse = code.Success ? code.Value : LoginVerificationCode;
        return codeToUse is not null
            ? $"{url.Value}?user_code={Uri.EscapeDataString(codeToUse)}"
            : url.Value;
    }

    [GeneratedRegex(@"https://github\.com/login/device", RegexOptions.IgnoreCase)]
    private static partial Regex VerificationUrlRegex();

    // GitHub device-flow codes are 8 alphanumeric chars grouped as XXXX-XXXX (e.g. 529D-2183).
    [GeneratedRegex(@"\b[A-Z0-9]{4}-[A-Z0-9]{4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceCodeRegex();

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Copilot] failed to open browser: {ex.Message}");
        }
    }

    private static bool RemoveLoggedInUsersFromConfig(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
                return true;

            var raw = File.ReadAllText(configPath);
            var commentPrefix = string.Empty;
            var jsonStart = raw;
            var match = JsonObjectStartRegex().Match(raw);
            if (match.Success)
            {
                commentPrefix = raw[..match.Index];
                jsonStart = raw[match.Index..];
            }

            var node = JsonNode.Parse(jsonStart) as JsonObject;
            if (node is null)
                return false;

            node.Remove("loggedInUsers");
            node.Remove("lastLoggedInUser");
            var updated = commentPrefix + node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, updated, Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Copilot] failed to clear logged-in users: {ex.Message}");
            return false;
        }
    }

    [GeneratedRegex(@"(?m)^\s*\{")]
    private static partial Regex JsonObjectStartRegex();

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Copilot] failed to delete {path}: {ex.Message}");
            return false;
        }
    }

    private static bool WriteSignOutMarker(string home)
    {
        try
        {
            Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, ".justybase-signed-out"), DateTimeOffset.UtcNow.ToString("O"));
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Copilot] failed to write sign-out marker: {ex.Message}");
            return false;
        }
    }

    private bool IsSignOutSuppressed()
        => Volatile.Read(ref _signedOut)
            || File.Exists(Path.Combine(GetCopilotHome(), ".justybase-signed-out"));

    private static void ClearSignOutMarker()
        => TryDeleteFile(Path.Combine(GetCopilotHome(), ".justybase-signed-out"));

    private static string GetCopilotHome()
    {
        var copilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME");
        if (!string.IsNullOrWhiteSpace(copilotHome))
            return copilotHome;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot");
    }

    #endregion

    #region CLI resolution

    private static CopilotCommand? ResolveCopilotCommand()
    {
        var configured = Environment.GetEnvironmentVariable("JUSTYBASE_COPILOT_COMMAND");
        if (string.IsNullOrWhiteSpace(configured))
        {
            // A null SDK Connection selects the runtime bundled by GitHub.Copilot.SDK.
            // Only override that default when a separately installed CLI is actually
            // available on PATH.
            var installed = FindExecutable("copilot");
            return installed is null ? null : CreateCopilotCommand(installed);
        }

        var requested = configured.Trim().Trim('"');
        var executable = FindExecutable(requested);

        if (string.IsNullOrWhiteSpace(executable))
            executable = requested;

        return CreateCopilotCommand(executable);
    }

    private static CopilotCommand ResolveCopilotLoginCommand()
    {
        var command = ResolveCopilotCommand();
        if (command is not null)
            return command;

        // Login is launched as a separate CLI process because the SDK exposes the runtime
        // protocol, not the device-flow command. Use the package's RID-specific binary for
        // clean installations where no global `copilot` command exists.
        var bundled = FindBundledCopilotExecutable();
        return bundled is not null
            ? CreateCopilotCommand(bundled)
            : new CopilotCommand("copilot", []);
    }

    private static CopilotCommand CreateCopilotCommand(string executable)
    {
        if (OperatingSystem.IsWindows() && executable.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var shell = FindExecutable("pwsh.exe") ?? FindExecutable("powershell.exe") ?? "powershell.exe";
            // ArgumentList performs the required quoting. Supplying a pre-quoted value
            // makes PowerShell receive the quote characters as part of the -File path.
            return new CopilotCommand(shell, new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", executable });
        }

        return new CopilotCommand(executable, []);
    }

    private static string? FindBundledCopilotExecutable()
    {
        var binary = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
        var rids = new List<string>();
        var runtimeRid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        if (!string.IsNullOrWhiteSpace(runtimeRid))
            rids.Add(runtimeRid);

        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            _ => null
        };
        if (architecture is not null)
        {
            var portableRid = OperatingSystem.IsWindows()
                ? $"win-{architecture}"
                : OperatingSystem.IsMacOS()
                    ? $"osx-{architecture}"
                    : $"linux-{architecture}";
            if (!rids.Contains(portableRid, StringComparer.OrdinalIgnoreCase))
                rids.Add(portableRid);
        }

        foreach (var rid in rids)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", binary);
            if (File.Exists(path))
                return path;
        }

        return null;
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

    private sealed record CopilotCommand(string FileName, IList<string> Arguments);

    #endregion

    private string GetWorkspaceDirectory()
    {
        var workspace = Path.Combine(_environment.ConfigDirectory, "copilot", "workspace");
        try
        {
            Directory.CreateDirectory(workspace);
            return workspace;
        }
        catch (UnauthorizedAccessException)
        {
            return AppContext.BaseDirectory;
        }
        catch (IOException)
        {
            return AppContext.BaseDirectory;
        }
    }

    private static string BuildUserError(Exception ex)
        => ex is System.ComponentModel.Win32Exception
            ? "Copilot CLI was not found. Install GitHub Copilot CLI or set JUSTYBASE_COPILOT_COMMAND."
            : ex.Message;

    private async Task<bool> StopSdkClientAsync(bool clearSessionId)
    {
        await _sessionLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await _startLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                CopilotSession? session;
                CopilotSdkClient? client;
                lock (_stateLock)
                {
                    session = _session;
                    _session = null;
                    _sessionSettings = null;
                    if (clearSessionId)
                        _requestedSessionId = null;

                    client = _sdkClient;
                    _sdkClient = null;
                    _initialized = false;
                }

                var stopped = true;
                if (session is not null)
                {
                    try { await session.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        stopped = false;
                        Debug.WriteLine($"[Copilot] session dispose ignored: {ex.Message}");
                    }
                }

                if (client is not null)
                {
                    try { await client.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        stopped = false;
                        Debug.WriteLine($"[Copilot] client dispose ignored: {ex.Message}");
                    }
                }

                return stopped;
            }
            finally
            {
                _startLock.Release();
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private static async Task DisposeSessionQuietlyAsync(CopilotSession session)
    {
        try { await session.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { Debug.WriteLine($"[Copilot] session dispose ignored: {ex.Message}"); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopLoginProcess();

        await StopSdkClientAsync(clearSessionId: true).ConfigureAwait(false);

        _startLock.Dispose();
        _sessionLock.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>Adapts the host logger onto the Microsoft.Extensions.Logging surface the SDK expects.</summary>
    private sealed class SdkLoggerAdapter : ILogger
    {
        private readonly ISimpleLogger _logger;

        public SdkLoggerAdapter(ISimpleLogger logger)
        {
            _logger = logger;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel)
            => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (logLevel >= LogLevel.Warning)
                _logger.TrackError(new InvalidOperationException(message), isCrash: false);
            else
                Debug.WriteLine($"[Copilot SDK] {message}");
        }
    }
}

public sealed record CopilotAccountInfo(bool IsAuthenticated, string? Login, string? AuthType, string? StatusMessage);
