using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace JustyBase.Ai.Models;

public enum ToolCallStatus
{
    Pending,
    Running,
    Success,
    Error,
    Cancelled,
    WaitingForApproval
}

public sealed class ToolCallInfo : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N")[..8];
    private string _name = string.Empty;
    private string _arguments = string.Empty;
    private string _result = string.Empty;
    private ToolCallStatus _status = ToolCallStatus.Pending;
    private DateTime _startedAt = DateTime.Now;
    private DateTime? _completedAt;
    private bool _isExpanded = true;
    private bool _requiresApproval;

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

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => GetToolDisplayName(_name);

    public string Arguments
    {
        get => _arguments;
        set
        {
            if (_arguments != value)
            {
                _arguments = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ArgumentsPreview));
            }
        }
    }

    public string ArgumentsPreview => _arguments.Length > 200 
        ? _arguments[..200] + "..." 
        : _arguments;

    public string ArgumentsFormatted => FormatArguments(_arguments);

    public string Result
    {
        get => _result;
        set
        {
            if (_result != value)
            {
                _result = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ResultPreview));
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public string ResultPreview => _result.Length > 500 
        ? _result[..500] + "..." 
        : _result;

    public bool HasResult => !string.IsNullOrWhiteSpace(_result);

    public ToolCallStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsSuccess));
                OnPropertyChanged(nameof(IsError));
                OnPropertyChanged(nameof(IsWaitingForApproval));
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(StatusText));

                if ((value == ToolCallStatus.Success || value == ToolCallStatus.Error || value == ToolCallStatus.Cancelled) 
                    && _completedAt is null)
                {
                    _completedAt = DateTime.Now;
                    OnPropertyChanged(nameof(CompletedAt));
                    OnPropertyChanged(nameof(Duration));
                }
            }
        }
    }

    public DateTime StartedAt
    {
        get => _startedAt;
        set
        {
            if (_startedAt != value)
            {
                _startedAt = value;
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
                OnPropertyChanged(nameof(Duration));
            }
        }
    }

    public TimeSpan? Duration => _completedAt.HasValue 
        ? _completedAt.Value - _startedAt 
        : null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public bool RequiresApproval
    {
        get => _requiresApproval;
        set
        {
            if (_requiresApproval != value)
            {
                _requiresApproval = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsRunning => Status == ToolCallStatus.Running;
    public bool IsSuccess => Status == ToolCallStatus.Success;
    public bool IsError => Status == ToolCallStatus.Error;
    public bool IsWaitingForApproval => Status == ToolCallStatus.WaitingForApproval;

    public string StatusIcon => Status switch
    {
        ToolCallStatus.Pending => "⏳",
        ToolCallStatus.Running => "⚙",
        ToolCallStatus.Success => "✓",
        ToolCallStatus.Error => "✗",
        ToolCallStatus.Cancelled => "⊘",
        ToolCallStatus.WaitingForApproval => "?",
        _ => "•"
    };

    public string StatusText => Status switch
    {
        ToolCallStatus.Pending => "Pending",
        ToolCallStatus.Running => "Running...",
        ToolCallStatus.Success => "Completed",
        ToolCallStatus.Error => "Failed",
        ToolCallStatus.Cancelled => "Cancelled",
        ToolCallStatus.WaitingForApproval => "Waiting for approval",
        _ => "Unknown"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string GetToolDisplayName(string toolName) => toolName?.ToLowerInvariant() switch
    {
        "getcurrentsql" => "Get Current SQL",
        "getcurrentsqlselection" => "Get Selection",
        "getcurrentsqleditorcontext" => "Get Editor Context",
        "searchincurrentsql" => "Search in SQL",
        "searchschemaobjects" => "Search Schema",
        "getobjectcolumns" => "Get Columns",
        "getobjectdefinition" => "Get Definition",
        "getobjectdependencies" => "Get Dependencies",
        "gettablemetadata" => "Get Table Metadata",
        "executesql" => "Execute SQL",
        "compileprocedure" => "Compile Procedure",
        "compileview" => "Compile View",
        "previewsqlEditorpatch" => "Preview Patch",
        "applypreviewedsqleditorpatch" => "Apply Patch",
        "searchsqlhistory" => "Search History",
        "searchexecutionlogs" => "Search Logs",
        "searchsqlrepository" => "Search Repository",
        "getresultgridpreview" => "Get Results",
        "getnetezzareference" => "Get Reference",
        "analyzenetezzasql" => "Analyze SQL",
        "updatetodolist" => "Update Tasks",
        _ => toolName ?? "Unknown Tool"
    };

    private static string FormatArguments(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(args);
            var json = document.RootElement.Clone();
            var formatted = new System.Text.StringBuilder();
            FormatJsonElement(json, formatted, 0);
            return formatted.ToString().TrimEnd();
        }
        catch
        {
            return args;
        }
    }

    private static void FormatJsonElement(JsonElement element, System.Text.StringBuilder sb, int indent)
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
                        sb.Append(prop.Value.GetString());
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        sb.Append(prop.Value.GetDouble());
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                    {
                        sb.Append(prop.Value.GetBoolean());
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        sb.AppendLine();
                        FormatJsonElement(prop.Value, sb, indent + 1);
                        continue;
                    }
                    else
                    {
                        sb.Append(prop.Value.ToString());
                    }
                    sb.AppendLine();
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

    public ToolCallInfo Clone()
    {
        return new ToolCallInfo
        {
            Id = Id,
            Name = Name,
            Arguments = Arguments,
            Result = Result,
            Status = Status,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            IsExpanded = IsExpanded,
            RequiresApproval = RequiresApproval
        };
    }
}
