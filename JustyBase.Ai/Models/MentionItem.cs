using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JustyBase.Ai.Models;

public enum MentionType
{
    Schema,
    Table,
    View,
    Procedure,
    Function,
    Column,
    Connection,
    Database,
    SqlEditor,
    Results,
    File
}

public sealed class MentionItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _fullName = string.Empty;
    private MentionType _type;
    private string? _schema;
    private string? _database;
    private string? _description;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string FullName
    {
        get => _fullName;
        set
        {
            if (_fullName != value)
            {
                _fullName = value;
                OnPropertyChanged();
            }
        }
    }

    public MentionType Type
    {
        get => _type;
        set
        {
            if (_type != value)
            {
                _type = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypeIcon));
                OnPropertyChanged(nameof(TypeColor));
            }
        }
    }

    public string? Schema
    {
        get => _schema;
        set
        {
            if (_schema != value)
            {
                _schema = value;
                OnPropertyChanged();
            }
        }
    }

    public string? Database
    {
        get => _database;
        set
        {
            if (_database != value)
            {
                _database = value;
                OnPropertyChanged();
            }
        }
    }

    public string? Description
    {
        get => _description;
        set
        {
            if (_description != value)
            {
                _description = value;
                OnPropertyChanged();
            }
        }
    }

    public string TypeIcon => Type switch
    {
        MentionType.Schema => "📁",
        MentionType.Table => "📋",
        MentionType.View => "👁",
        MentionType.Procedure => "⚙",
        MentionType.Function => "λ",
        MentionType.Column => "▮",
        MentionType.Connection => "🔌",
        MentionType.Database => "🗄",
        MentionType.SqlEditor => "📝",
        MentionType.Results => "📊",
        MentionType.File => "📄",
        _ => "•"
    };

    public string TypeColor => Type switch
    {
        MentionType.Schema => "#4a9",
        MentionType.Table => "#48f",
        MentionType.View => "#84f",
        MentionType.Procedure => "#f84",
        MentionType.Function => "#f4a",
        MentionType.Column => "#888",
        MentionType.Connection => "#4a4",
        MentionType.Database => "#44f",
        MentionType.SqlEditor => "#a4a",
        MentionType.Results => "#4aa",
        MentionType.File => "#aa4",
        _ => "#666"
    };

    public string TypeLabel => Type switch
    {
        MentionType.Schema => "schema",
        MentionType.Table => "table",
        MentionType.View => "view",
        MentionType.Procedure => "proc",
        MentionType.Function => "func",
        MentionType.Column => "col",
        MentionType.Connection => "conn",
        MentionType.Database => "db",
        MentionType.SqlEditor => "editor",
        MentionType.Results => "results",
        MentionType.File => "file",
        _ => "item"
    };

    public string DisplayText => string.IsNullOrWhiteSpace(FullName) ? Name : FullName;

    public string InsertText => Type switch
    {
        MentionType.Table or MentionType.View or MentionType.Procedure or MentionType.Function
            when !string.IsNullOrWhiteSpace(Schema) && !string.IsNullOrWhiteSpace(Database)
            => $"{Database}.{Schema}.{Name}",
        MentionType.Table or MentionType.View or MentionType.Procedure or MentionType.Function
            when !string.IsNullOrWhiteSpace(Schema)
            => $"{Schema}.{Name}",
        MentionType.Column when !string.IsNullOrWhiteSpace(Schema)
            => $"{Schema}.{Name}",
        _ => Name
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public MentionItem Clone()
    {
        return new MentionItem
        {
            Name = Name,
            FullName = FullName,
            Type = Type,
            Schema = Schema,
            Database = Database,
            Description = Description
        };
    }
}
