using JustyBase.Ai.Models;
using JustyBase.Ai.Ports;
using JustyBase.Ai.Services;
using System.Text.RegularExpressions;

namespace JustyBase.Ai.Chat;

/// <summary>
/// UI-agnostic chat session orchestration shared by all hosts: message flow,
/// session lifecycle, persistence and tool-confirmation bridging. Host view
/// models compose this controller and map its events onto their UI layer.
/// </summary>
public sealed class ChatSessionController
{
    private readonly ICopilotChatService _chatService;
    private readonly IChatSettingsStore _settingsStore;
    private readonly ISimpleLogger _logger;
    private ChatMessage? _activeAssistantMessage;
    private ChatMessage? _pendingConfirmationMessage;
    private CancellationTokenSource? _currentStreamingCts;

    public ChatSessionController(
        ICopilotChatService chatService,
        IChatSettingsStore settingsStore,
        ISimpleLogger logger)
    {
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        CurrentSession = new ChatSession();
        _chatService.SetToolConfirmationHandler(HandleToolConfirmationAsync);
    }

    /// <summary>Messages of the current session (host renders this list).</summary>
    public List<ChatMessage> Messages { get; } = [];

    /// <summary>Active session object; its <see cref="ChatSession.Messages"/> mirrors <see cref="Messages"/>.</summary>
    public ChatSession CurrentSession { get; private set; }

    public IReadOnlyList<ChatSession> SavedSessions => _settingsStore.Settings.ChatSessions;

    public bool IsStreaming { get; private set; }

    public ChatMessage? ActiveAssistantMessage => _activeAssistantMessage;

    public event EventHandler? SessionChanged;
    public event EventHandler? SessionsChanged;
    public event EventHandler<bool>? StreamingChanged;
    public event EventHandler<ChatMessage>? UserMessageAdded;
    public event EventHandler<ChatMessage>? AssistantMessageStarted;
    public event EventHandler<ChatMessage>? AssistantMessageCompleted;
    public event EventHandler<ChatMessage>? ToolConfirmationRequested;
    public event EventHandler<string>? StatusMessageChanged;

    public void AttachHostProviders(
        Func<string?>? currentSqlProvider,
        Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?>? sqlEditorContextProvider,
        Func<string, bool>? sqlEditorBufferUpdater,
        Func<(string ConnectionName, string DatabaseName)?>? activeSqlContextProvider)
    {
        if (currentSqlProvider is not null)
            _chatService.SetCurrentSqlProvider(currentSqlProvider);
        if (sqlEditorContextProvider is not null)
            _chatService.SetSqlEditorContextProvider(sqlEditorContextProvider);
        if (sqlEditorBufferUpdater is not null)
            _chatService.SetSqlEditorBufferUpdater(sqlEditorBufferUpdater);
        if (activeSqlContextProvider is not null)
            _chatService.SetActiveSqlContextProvider(activeSqlContextProvider);
    }

    /// <summary>Completes a pending tool-approval card (host UI button).</summary>
    public void ConfirmTool(bool allow)
    {
        var pending = _pendingConfirmationMessage;
        if (pending?.ConfirmationTcs is null)
            return;

        pending.ConfirmationPending = false;
        pending.Content = allow
            ? $"✓ Tool '{pending.ToolName}' approved"
            : $"✗ Tool '{pending.ToolName}' denied";
        pending.ConfirmationTcs.TrySetResult(allow);
        _pendingConfirmationMessage = null;
    }

    public async Task<bool> SendMessageAsync(
        string text,
        IReadOnlyList<ChatAttachment>? attachments = null,
        string? modelId = null,
        string? reasoningEffort = null,
        CancellationToken cancellationToken = default)
    {
        if (IsStreaming)
            return false;

        var normalizedPrompt = string.IsNullOrWhiteSpace(text)
            ? "Analyze attached references."
            : text.Trim();

        var messageAttachments = attachments?
            .Where(a => a is not null)
            .Select(a => a.Clone())
            .ToList() ?? [];

        var userMessage = new ChatMessage
        {
            Content = normalizedPrompt,
            Role = "user",
            Timestamp = DateTime.Now,
            Attachments = messageAttachments
        };

        Messages.Add(userMessage);
        CurrentSession.Messages.Add(userMessage);
        CurrentSession.LastActivityAt = DateTime.Now;
        if (string.IsNullOrWhiteSpace(CurrentSession.Title) || CurrentSession.Title == "New Chat")
        {
            CurrentSession.Title = GenerateSessionTitle(normalizedPrompt);
        }

        _chatService.SetCodexThreadId(CurrentSession.CodexThreadId);
        UserMessageAdded?.Invoke(this, userMessage);

        var assistantMessage = new ChatMessage
        {
            Content = string.Empty,
            Role = "assistant",
            Timestamp = DateTime.Now,
            IsStreaming = true
        };
        Messages.Add(assistantMessage);
        _activeAssistantMessage = assistantMessage;
        AssistantMessageStarted?.Invoke(this, assistantMessage);

        IsStreaming = true;
        StreamingChanged?.Invoke(this, true);
        _currentStreamingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await foreach (var chunk in _chatService.SendMessageAsync(
                Messages.ToList(),
                modelId,
                reasoningEffort,
                _currentStreamingCts.Token))
            {
                assistantMessage.Content += chunk;
            }

            CurrentSession.CodexThreadId = _chatService.GetCodexThreadId();

            assistantMessage.IsStreaming = false;
            stopwatch.Stop();
            assistantMessage.GenerationTimeMs = stopwatch.ElapsedMilliseconds;
            CurrentSession.Messages.Add(assistantMessage);
            CurrentSession.LastActivityAt = DateTime.Now;
            AssistantMessageCompleted?.Invoke(this, assistantMessage);
            return true;
        }
        catch (OperationCanceledException)
        {
            Messages.Remove(assistantMessage);
            return false;
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            assistantMessage.Content = $"Error: {ex.Message}";
            assistantMessage.IsStreaming = false;
            // Persist the error turn too, so reopening the session keeps it visible.
            CurrentSession.Messages.Add(assistantMessage);
            return false;
        }
        finally
        {
            IsStreaming = false;
            _activeAssistantMessage = null;
            _currentStreamingCts?.Dispose();
            _currentStreamingCts = null;
            StreamingChanged?.Invoke(this, false);
            if (CurrentSession.Messages.Count > 0)
            {
                SaveCurrentSession();
            }
        }
    }

    public async Task CancelStreamingAsync()
    {
        if (!IsStreaming)
            return;

        _currentStreamingCts?.Cancel();
        await _chatService.CancelCurrentRequestAsync().ConfigureAwait(false);
    }

    public void NewSession()
    {
        if (IsStreaming)
            return;

        if (Messages.Count > 0)
        {
            SaveCurrentSession();
        }

        CurrentSession = new ChatSession();
        _chatService.SetCodexThreadId(null);
        Messages.Clear();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OpenSession(ChatSession? session)
    {
        if (session is null || IsStreaming)
            return;
        if (session.SessionId == CurrentSession.SessionId && Messages.Count > 0)
            return;

        if (Messages.Count > 0 && session.SessionId != CurrentSession.SessionId)
        {
            SaveCurrentSession();
        }

        Messages.Clear();
        CurrentSession = session;
        Messages.AddRange(session.Messages);
        _chatService.SetCodexThreadId(session.CodexThreadId);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSession(ChatSession? session)
    {
        if (session is null || IsStreaming)
            return;

        var wasActive = session.SessionId == CurrentSession.SessionId;
        _settingsStore.Update(s => s.ChatSessions.RemoveAll(x => x.SessionId == session.SessionId));
        SessionsChanged?.Invoke(this, EventArgs.Empty);

        if (wasActive)
        {
            CurrentSession = new ChatSession();
            _chatService.SetCodexThreadId(null);
            Messages.Clear();
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearSession()
    {
        if (IsStreaming)
            return;

        _settingsStore.Update(s => s.ChatSessions.RemoveAll(x => x.SessionId == CurrentSession.SessionId));
        Messages.Clear();
        CurrentSession = new ChatSession();
        _chatService.SetCodexThreadId(null);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveCurrentSession()
    {
        if (CurrentSession.Messages.Count == 0)
            return;

        _settingsStore.Update(s =>
        {
            var existing = s.ChatSessions.FirstOrDefault(x => x.SessionId == CurrentSession.SessionId);
            if (existing is null)
            {
                s.ChatSessions.Add(CurrentSession);
            }
            else
            {
                existing.Title = CurrentSession.Title;
                existing.LastActivityAt = CurrentSession.LastActivityAt;
                existing.CodexThreadId = CurrentSession.CodexThreadId;
                existing.Messages = CurrentSession.Messages;
            }
        });
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetStatus(string message) => StatusMessageChanged?.Invoke(this, message);

    private async Task<bool> HandleToolConfirmationAsync(string toolName, string toolArgs)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var confirmationMessage = new ChatMessage
        {
            Role = "tool-confirmation",
            Content = "The model wants to use a tool. Allow execution?",
            Timestamp = DateTime.Now,
            IsToolConfirmation = true,
            ToolName = toolName,
            ToolArgs = toolArgs,
            ConfirmationPending = true
        };
        confirmationMessage.ConfirmationTcs = tcs;
        _pendingConfirmationMessage = confirmationMessage;

        Messages.Add(confirmationMessage);
        ToolConfirmationRequested?.Invoke(this, confirmationMessage);

        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(5))).ConfigureAwait(false);
        var result = completedTask == tcs.Task && tcs.Task.Result;

        if (ReferenceEquals(_pendingConfirmationMessage, confirmationMessage))
        {
            _pendingConfirmationMessage = null;
        }

        confirmationMessage.ConfirmationPending = false;
        confirmationMessage.Content = completedTask == tcs.Task
            ? (result ? $"✓ Tool '{toolName}' approved" : $"✗ Tool '{toolName}' denied")
            : $"✗ Tool '{toolName}' denied (approval timeout)";

        return result;
    }

    private static string GenerateSessionTitle(string? firstUserMessage)
    {
        if (string.IsNullOrWhiteSpace(firstUserMessage))
            return "New Chat";

        var text = firstUserMessage.Replace('\r', ' ').Replace('\n', ' ');
        text = Regex.Replace(text, @"[`*_#>|\[\]()]|^[-=]{2,}", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length == 0)
            return "New Chat";

        var sentenceEnd = text.IndexOfAny(['.', '?', '!']);
        if (sentenceEnd > 0)
            text = text[..sentenceEnd].Trim();
        if (text.Length == 0)
            return "New Chat";

        const int maxTitleLength = 50;
        return text.Length <= maxTitleLength ? text : text[..maxTitleLength].TrimEnd() + "…";
    }
}
