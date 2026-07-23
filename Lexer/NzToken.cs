namespace JustyBase.NetezzaSqlParser.Lexer;

public enum NzToken
{
    Unknown = 0,

    // Comments
    LineComment,
    BlockComment,

    // Multi-word keywords (must be defined before single-word in tokenizer)
    GroupBy,
    OrderBy,
    PartitionBy,
    MinusSet,

    // DML Keywords
    Select,
    From,
    Where,
    Insert,
    Into,
    Values,
    Value,
    Update,
    Set,
    Delete,
    Materialized,
    Perform,
    Reverse,
    Out,
    Inout,
    Sqlstate,
    Others,

    // JOIN Keywords
    Join,
    Inner,
    Left,
    Right,
    Full,
    Outer,
    Cross,
    Natural,
    Only,
    On,

    // Logical Operators
    And,
    Or,
    Not,

    // SELECT Modifiers
    As,
    Distinct,
    All,

    // Set Operations
    Union,
    Intersect,
    Except,

    // Clauses
    Having,
    Limit,
    Offset,

    // NULL handling
    Nulls,
    Null,
    Is,

    // Pattern matching
    Ilike,
    Like,
    Escape,
    In,
    Between,
    Exists,

    // CASE expression
    Case,
    When,
    Then,
    Elsif,
    If,
    Else,
    End,

    // NZPLSQL Keywords
    Nzplsql,
    BeginProc,
    EndProc,
    Begin,
    Declare,
    Exception,
    Return,
    Alias,
    Constant,
    Loop,
    While,
    Exit,
    Raise,
    Notice,
    Debug,
    Error1,
    Rollback,
    Commit,
    Call,
    Immediate,
    Using,

    // DCL Keywords
    Grant,
    Revoke,
    To,
    Public,
    Type,
    Cascade,
    Restrict,
    SameAs,
    Hash,
    Deferrable,
    Initially,

    // DDL Keywords
    Create,
    Replace,
    Database,
    Schema,
    Table,
    Sequence,
    Session,
    Synonym,
    User,
    Procedure,
    Temporary,
    Temp,
    Drop,
    Truncate,
    Explain,
    Verbose,
    Distribution,
    Plantext,
    Plangraph,
    Alter,
    Show,
    Copy,
    Lock,
    Merge,
    Matched,
    Reindex,
    Reset,
    External,
    Views,
    View,
    Comment,
    Rename,
    Modify,
    Privileges,
    Deferred,
    Match,
    Action,
    Within,
    LabelStart,
    LabelEnd,
    History,
    Configuration,
    Scheduler,
    Rule,
    NotNull,
    Warning,
    Column,
    Add,
    Constraint,
    Primary,
    Key,
    Foreign,
    References,
    Unique,
    Check,
    Global,
    Returns,
    Language,
    Execute,
    Exec,
    Owner,
    Caller,
    RefTable,
    Varargs,
    Varray,
    Autocommit,

    // CTE Keywords
    With,
    Final,
    Recursive,

    // Netezza-specific
    Distribute,
    Random,
    Organize,
    Groom,
    Versions,
    Records,
    Pages,
    Ready,
    Start,
    Reclaim,
    Backupset,
    Default,
    None,
    Generate,
    Next,
    Express,
    Statistics,
    For,
    Of,

    // ORDER BY / FETCH
    Asc,
    Desc,
    Fetch,
    First,

    // Quantified comparisons
    Any,
    Some,

    // Window functions
    Over,
    Rows,
    Range,
    Groups,
    Current,
    Row,
    Unbounded,
    Preceding,
    Following,
    Filter,
    Exclude,
    Ties,

    // Expressions / built-ins
    Extract,
    Cast,

    // AtSet
    AtSet,

    // Operators (sorted by length descending for tokenizer)
    NotEquals,        // <> or !=
    LessThanEquals,   // <=
    GreaterThanEquals,// >=
    Concat,           // ||
    DoubleColon,      // ::
    Assign,           // :=

    EqualsOp,         // =
    LessThan,         // <
    GreaterThan,      // >
    Plus,             // +
    Minus,            // -
    Multiply,         // *
    Divide,           // /
    Modulo,           // %
    Caret,            // ^

    Dot,              // .
    Comma,            // ,
    Semicolon,        // ;
    LParen,           // (
    RParen,           // )
    LBracket,         // [
    RBracket,         // ]

    // Parameter
    Parameter,        // ?

    // Literals
    NumberLiteral,
    StringLiteral,

    // Variables
    BracedVariable,     // ${variable}
    BracesOnlyVariable, // {variable}
    DollarNumber,       // $1
    DollarIdentifier,   // $variable

    // Identifiers
    QuotedIdentifier,   // "identifier"
    Identifier,         // regular identifier / keyword-as-identifier

    // Whitespace
    WhiteSpace,
}
