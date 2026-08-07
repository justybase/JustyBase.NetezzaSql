using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustyBase.Ai.Models;

public enum TodoStatus
{
    Pending,
    InProgress,
    Completed,
    Cancelled
}

public sealed class TodoItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N")[..8];
    private string _content = string.Empty;
    private TodoStatus _status = TodoStatus.Pending;
    private int _priority;
    private DateTime _createdAt = DateTime.Now;
    private DateTime? _completedAt;

    public string Id
    {
        get => _id;
        set
        {
            if (_id != value)
            {
                _id = value;
                OnPropertyChanged();
            }
        }
    }

    public string Content
    {
        get => _content;
        set
        {
            if (_content != value)
            {
                _content = value;
                OnPropertyChanged();
            }
        }
    }

    public TodoStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsInProgress));
                OnPropertyChanged(nameof(IsPending));
                OnPropertyChanged(nameof(StatusIcon));
                
                if (value == TodoStatus.Completed && _completedAt is null)
                {
                    _completedAt = DateTime.Now;
                    OnPropertyChanged(nameof(CompletedAt));
                }
            }
        }
    }

    public int Priority
    {
        get => _priority;
        set
        {
            if (_priority != value)
            {
                _priority = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set
        {
            if (_createdAt != value)
            {
                _createdAt = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set
        {
            if (_completedAt != value)
            {
                _completedAt = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsCompleted => Status == TodoStatus.Completed;
    public bool IsInProgress => Status == TodoStatus.InProgress;
    public bool IsPending => Status == TodoStatus.Pending;

    public string StatusIcon => Status switch
    {
        TodoStatus.Completed => "✓",
        TodoStatus.InProgress => "▶",
        TodoStatus.Cancelled => "⊘",
        _ => "○"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public TodoItem Clone()
    {
        return new TodoItem
        {
            Id = Id,
            Content = Content,
            Status = Status,
            Priority = Priority,
            CreatedAt = CreatedAt,
            CompletedAt = CompletedAt
        };
    }
}

public sealed class TodoList : INotifyPropertyChanged
{
    private int _version;
    private DateTime _lastUpdatedAt = DateTime.Now;

    public string Id { get; init; } = Guid.NewGuid().ToString();
    public List<TodoItem> Items { get; init; } = [];

    public int Version
    {
        get => _version;
        private set
        {
            if (_version != value)
            {
                _version = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime LastUpdatedAt
    {
        get => _lastUpdatedAt;
        private set
        {
            if (_lastUpdatedAt != value)
            {
                _lastUpdatedAt = value;
                OnPropertyChanged();
            }
        }
    }

    public int TotalCount => Items.Count;
    public int CompletedCount => Items.Count(x => x.Status == TodoStatus.Completed);
    public int PendingCount => Items.Count(x => x.Status == TodoStatus.Pending);
    public int InProgressCount => Items.Count(x => x.Status == TodoStatus.InProgress);
    
    public double ProgressPercentage => TotalCount > 0 
        ? Math.Round((double)CompletedCount / TotalCount * 100, 1) 
        : 0;

    public void UpdateFromJson(string todosJson)
    {
        if (string.IsNullOrWhiteSpace(todosJson))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(todosJson, TodoJsonContext.Default.ListTodoItemDto);
            if (parsed is null || parsed.Count == 0)
            {
                return;
            }

            Items.Clear();
            foreach (var dto in parsed)
            {
                Items.Add(new TodoItem
                {
                    Id = dto.Id ?? Guid.NewGuid().ToString("N")[..8],
                    Content = dto.Content ?? string.Empty,
                    Status = ParseStatus(dto.Status),
                    Priority = dto.Priority ?? 0
                });
            }

            Version++;
            LastUpdatedAt = DateTime.Now;
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(InProgressCount));
            OnPropertyChanged(nameof(ProgressPercentage));
        }
        catch
        {
        }
    }

    private static TodoStatus ParseStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "completed" or "done" => TodoStatus.Completed,
        "in_progress" or "inprogress" or "in-progress" or "started" => TodoStatus.InProgress,
        "cancelled" or "canceled" => TodoStatus.Cancelled,
        _ => TodoStatus.Pending
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

// This DTO is kept separate from the UI model because it represents the external
// JSON payload accepted by TodoList.UpdateFromJson.
internal sealed class TodoItemDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Content")]
    public string? Content { get; set; }

    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    [JsonPropertyName("Priority")]
    public int? Priority { get; set; }
}

[JsonSerializable(typeof(List<TodoItemDto>))]
internal sealed partial class TodoJsonContext : JsonSerializerContext
{
}
