using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace JustyBase.Ai.Models;

public sealed class ChatMessage : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private string _role = "user";
    private DateTime _timestamp = DateTime.Now;
    private bool _isStreaming;
    private bool _isToolConfirmation;
    private string _toolName = string.Empty;
    private string _toolArgs = string.Empty;
    private bool _confirmationPending = true;
    private string _thinkingContent = string.Empty;
    private TaskCompletionSource<bool>? _confirmationTcs;
    private List<ChatAttachment> _attachments = [];
    private List<ToolCallInfo> _toolCalls = [];
    private bool _isToolExpanded = true;
    private bool _isUserExpanded = true;
    private ChatMode _mode = ChatMode.Expert;
    private bool _isLoopStep;
    private int _loopStepNumber;
    private string _loopStatus = string.Empty;
    private long _generationTimeMs;

    public string Content
    {
        get => _content;
        set
        {
            if (_content != value)
            {
                _content = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowThinkingInline));
            }
        }
    }

    public string Role
    {
        get => _role;
        set
        {
            if (_role != value)
            {
                _role = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime Timestamp
    {
        get => _timestamp;
        set
        {
            if (_timestamp != value)
            {
                _timestamp = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (_isStreaming != value)
            {
                _isStreaming = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowThinkingInline));
            }
        }
    }

    public long GenerationTimeMs
    {
        get => _generationTimeMs;
        set
        {
            if (_generationTimeMs != value)
            {
                _generationTimeMs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GenerationTimeDisplay));
            }
        }
    }

    public string GenerationTimeDisplay => GenerationTimeMs > 0
        ? GenerationTimeMs >= 1000
            ? $"{GenerationTimeMs / 1000.0:F1}s"
            : $"{GenerationTimeMs}ms"
        : string.Empty;

    public bool IsToolConfirmation
    {
        get => _isToolConfirmation;
        set
        {
            if (_isToolConfirmation != value)
            {
                _isToolConfirmation = value;
                OnPropertyChanged();
            }
        }
    }

    public string ToolName
    {
        get => _toolName;
        set
        {
            if (_toolName != value)
            {
                _toolName = value;
                OnPropertyChanged();
            }
        }
    }

    public string ToolArgs
    {
        get => _toolArgs;
        set
        {
            if (_toolArgs != value)
            {
                _toolArgs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ToolArgsFormatted));
            }
        }
    }

    public string ToolArgsFormatted => FormatToolArgs(_toolArgs);

    private static string FormatToolArgs(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(args);
            var json = document.RootElement.Clone();
            var sb = new StringBuilder();
            FormatJsonElement(json, sb, 0);
            return sb.ToString().TrimEnd();
        }
        catch
        {
            return args;
        }
    }

    private static void FormatJsonElement(JsonElement element, StringBuilder sb, int indent)
    {
        var indentStr = new string(' ', indent * 2);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    sb.Append(indentStr);
                    sb.Append(prop.Name);
                    sb.Append(": ");

                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        sb.AppendLine(prop.Value.GetString());
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        sb.AppendLine(prop.Value.GetDouble().ToString());
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                    {
                        sb.AppendLine(prop.Value.GetBoolean().ToString());
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        sb.AppendLine();
                        FormatJsonElement(prop.Value, sb, indent + 1);
                    }
                    else
                    {
                        sb.AppendLine(prop.Value.ToString());
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    sb.Append(indentStr);
                    sb.Append("- ");
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        sb.AppendLine(item.GetString());
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        sb.AppendLine();
                        FormatJsonElement(item, sb, indent + 1);
                    }
                    else
                    {
                        sb.AppendLine(item.ToString());
                    }
                }
                break;

            default:
                sb.Append(element.ToString());
                break;
        }
    }

    public bool ConfirmationPending
    {
        get => _confirmationPending;
        set
        {
            if (_confirmationPending != value)
            {
                _confirmationPending = value;
                OnPropertyChanged();
            }
        }
    }

    public string ThinkingContent
    {
        get => _thinkingContent;
        set
        {
            if (_thinkingContent != value)
            {
                _thinkingContent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThinkingContent));
                OnPropertyChanged(nameof(ShowThinkingInline));
            }
        }
    }

    public bool HasThinkingContent => !string.IsNullOrWhiteSpace(_thinkingContent);

    public bool ShowThinkingInline => IsStreaming && HasThinkingContent && string.IsNullOrWhiteSpace(Content);

    public List<ChatAttachment> Attachments
    {
        get => _attachments;
        set
        {
            if (!ReferenceEquals(_attachments, value))
            {
                _attachments = value ?? [];
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAttachments));
            }
        }
    }

    public bool HasAttachments => _attachments.Count > 0;

    public List<ToolCallInfo> ToolCalls
    {
        get => _toolCalls;
        set
        {
            if (!ReferenceEquals(_toolCalls, value))
            {
                _toolCalls = value ?? [];
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasToolCalls));
                OnPropertyChanged(nameof(ToolCallsCount));
                OnPropertyChanged(nameof(LastToolCall));
            }
        }
    }

    public bool HasToolCalls => _toolCalls.Count > 0;
    public int ToolCallsCount => _toolCalls.Count;
    public ToolCallInfo? LastToolCall => _toolCalls.Count > 0 ? _toolCalls[^1] : null;

    public bool IsToolExpanded
    {
        get => _isToolExpanded;
        set
        {
            if (_isToolExpanded != value)
            {
                _isToolExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsUserExpanded
    {
        get => _isUserExpanded;
        set
        {
            if (_isUserExpanded != value)
            {
                _isUserExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public ChatMode Mode
    {
        get => _mode;
        set
        {
            if (_mode != value)
            {
                _mode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ModeDisplayName));
            }
        }
    }

    public string ModeDisplayName => _mode.ToDisplayName();

    public bool IsLoopStep
    {
        get => _isLoopStep;
        set
        {
            if (_isLoopStep != value)
            {
                _isLoopStep = value;
                OnPropertyChanged();
            }
        }
    }

    public int LoopStepNumber
    {
        get => _loopStepNumber;
        set
        {
            if (_loopStepNumber != value)
            {
                _loopStepNumber = value;
                OnPropertyChanged();
            }
        }
    }

    public string LoopStatus
    {
        get => _loopStatus;
        set
        {
            if (_loopStatus != value)
            {
                _loopStatus = value;
                OnPropertyChanged();
            }
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public TaskCompletionSource<bool>? ConfirmationTcs
    {
        get => _confirmationTcs;
        set => _confirmationTcs = value;
    }

    public ChatMessage()
    {
    }

    public ChatMessage(string content, string role)
    {
        Content = content;
        Role = role;
        Timestamp = DateTime.Now;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ChatAttachment
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }

    public string EffectiveDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
            {
                return DisplayName;
            }

            var fileOrDirectoryName = GetLastPathSegment(Path);
            return string.IsNullOrWhiteSpace(fileOrDirectoryName) ? Path : fileOrDirectoryName;
        }
    }

    private static string GetLastPathSegment(string path)
    {
        int lastSeparator = path.LastIndexOfAny(['/', '\\']);
        return lastSeparator >= 0 ? path[(lastSeparator + 1)..] : path;
    }

    public string DisplayLabel => IsDirectory
        ? $"[DIR] {EffectiveDisplayName}"
        : $"[FILE] {EffectiveDisplayName}";

    public ChatAttachment Clone()
    {
        return new ChatAttachment
        {
            Path = Path,
            DisplayName = DisplayName,
            IsDirectory = IsDirectory,
            StartLine = StartLine,
            EndLine = EndLine
        };
    }
}

public sealed class ChatSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Chat";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastActivityAt { get; set; } = DateTime.Now;
    /// <summary>Optional thread id owned by the official Codex app-server.</summary>
    public string? CodexThreadId { get; set; }
    public List<ChatMessage> Messages { get; set; } = [];

    public override string ToString() => string.IsNullOrWhiteSpace(Title) ? "New Chat" : Title;
}
