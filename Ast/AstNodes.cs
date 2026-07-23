using Superpower.Model;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.NetezzaSqlParser.Ast;

// ====== Position and Error Types ======

public record SourcePosition(int Line, int Column, int Absolute)
{
    public static SourcePosition FromToken(Token<NzToken> token) =>
        new(token.Position.Line, token.Position.Column, token.Position.Absolute);

    public static SourcePosition FromOffset(string text, int offset)
    {
        var line = 1;
        var column = 1;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
        return new SourcePosition(line, column, offset);
    }
}

public record ValidationError(string Message, string Severity, SourcePosition Position, string Code,
    int EndLine = 0, int EndColumn = 0, string? SuggestedFix = null);

// ====== Core SQL Types ======

public record ColumnInfo(string Name, string? Alias = null, SourcePosition? Position = null, string? DataType = null);

public record TableInfo(
    string Name,
    string? Schema = null,
    string? Database = null,
    bool IsCte = false,
    bool IsTempTable = false,
    string? Alias = null,
    IReadOnlyList<ColumnInfo>? Columns = null,
    SourcePosition? Position = null,
    bool IsView = false
);

public record CteInfo(
    string Name,
    bool Recursive,
    IReadOnlyList<ColumnInfo>? Columns = null
);

public record Scope(
    Dictionary<string, TableInfo> Tables,
    Dictionary<string, CteInfo> Ctes,
    Scope? Parent = null,
    int Level = 0
);

public record ValidationResult(
    bool Valid,
    IReadOnlyList<ValidationError> Errors,
    IReadOnlyList<ValidationError> Warnings,
    Scope Scope
);

// ====== AST Node Base ======

public abstract record AstNode(SourcePosition Position);

// ====== Statements ======

public abstract record Statement(SourcePosition Position) : AstNode(Position);

public record SelectStatement(
    SourcePosition Position,
    SelectModifier? Modifier,
    IReadOnlyList<SelectItem> SelectList,
    IReadOnlyList<TableReference>? From,
    Expression? Where,
    IReadOnlyList<Expression>? GroupBy,
    Expression? Having,
    IReadOnlyList<OrderByItem>? OrderBy,
    LimitClause? Limit,
    IReadOnlyList<SetOperation>? SetOperations,
    IReadOnlyList<SelectStatement>? CompoundSelects,
    WithClause? With,
    bool HasInto = false
) : Statement(Position);

public record InsertStatement(
    SourcePosition Position,
    TableName Target,
    IReadOnlyList<string>? Columns,
    IReadOnlyList<IReadOnlyList<Expression>>? Values,
    SelectStatement? SourceQuery
) : Statement(Position);

public record UpdateStatement(
    SourcePosition Position,
    TableName Target,
    string? Alias,
    IReadOnlyList<UpdateSetItem> SetItems,
    IReadOnlyList<TableReference>? From,
    Expression? Where
) : Statement(Position);

public record DeleteStatement(
    SourcePosition Position,
    TableName Target,
    string? Alias,
    Expression? Where
) : Statement(Position);

public record MergeStatement(
    SourcePosition Position,
    TableName Target,
    string? TargetAlias,
    TableSource Source,
    Expression OnCondition,
    IReadOnlyList<MergeClause> Clauses
) : Statement(Position);

public abstract record MergeClause(SourcePosition Position) : AstNode(Position);

public record MergeMatchedUpdateClause(
    SourcePosition Position,
    Expression? Condition,
    IReadOnlyList<UpdateSetItem> SetItems,
    Expression? Where
) : MergeClause(Position);

public record MergeMatchedDeleteClause(
    SourcePosition Position,
    Expression? Condition
) : MergeClause(Position);

public record MergeNotMatchedInsertClause(
    SourcePosition Position,
    Expression? Condition,
    IReadOnlyList<string>? Columns,
    IReadOnlyList<Expression> Values
) : MergeClause(Position);

public record CreateTableStatement(
    SourcePosition Position,
    TableName Table,
    bool IfNotExists,
    bool Temporary,
    bool Global,
    IReadOnlyList<ColumnDefinition>? Columns,
    IReadOnlyList<TableConstraint>? Constraints,
    SelectStatement? AsSelect,
    DistributeClause? Distribute,
    OrganizeClause? Organize
) : Statement(Position);

public record CreateViewStatement(
    SourcePosition Position,
    TableName View,
    bool OrReplace,
    IReadOnlyList<string>? ColumnAliases,
    SelectStatement Query
) : Statement(Position);

public record CreateProcedureStatement(
    SourcePosition Position,
    TableName Procedure,
    bool OrReplace,
    IReadOnlyList<ProcedureParameter>? Parameters,
    string? Returns,
    ExecuteAs? ExecuteAs,
    string Language,
    ProcedureBody Body
) : Statement(Position);

public enum ProcedureParameterMode { In, Out, InOut }

public record ProcedureParameter(
    SourcePosition Position,
    string Name,
    ProcedureParameterMode Mode
);

public record CreateExternalTableStatement(
    SourcePosition Position,
    TableName Table,
    TableName? SameAs,
    IReadOnlyList<ColumnDefinition>? Columns,
    IReadOnlyList<ExternalTableOption>? Options
) : Statement(Position);

public record CreateSequenceStatement(
    SourcePosition Position,
    TableName Sequence
) : Statement(Position);

public record DropStatement(
    SourcePosition Position,
    string ObjectType,
    IReadOnlyList<TableName> Targets,
    bool IfExists
) : Statement(Position);

public record AlterTableStatement(
    SourcePosition Position,
    TableName Table,
    IReadOnlyList<AlterTableAction>? Actions = null
) : Statement(Position);

public abstract record AlterTableAction(SourcePosition Position, string RawSql) : AstNode(Position);
public record AddColumnAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record AddConstraintAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record AlterColumnAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record DropColumnAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record DropConstraintAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record ModifyColumnAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record RenameColumnAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record RenameToAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record OwnerToAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record SetPrivilegesAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record OrganizeOnAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record CascadeAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);
public record UnknownAlterAction(SourcePosition Position, string RawSql) : AlterTableAction(Position, RawSql);

public record TruncateStatement(
    SourcePosition Position,
    TableName Table
) : Statement(Position);

public record GroomStatement(
    SourcePosition Position,
    TableName Table,
    GroomMode? Mode,
    GroomReclaim? Reclaim
) : Statement(Position);

public record GenerateStatisticsStatement(
    SourcePosition Position,
    TableName Table,
    bool Express,
    IReadOnlyList<string>? Columns
) : Statement(Position);

public record CommentStatement(
    SourcePosition Position,
    string ObjectType,
    TableName Object,
    string? Column,
    string Comment
) : Statement(Position);

public record GrantStatement(
    SourcePosition Position
) : Statement(Position);

public record RevokeStatement(
    SourcePosition Position
) : Statement(Position);

public record WithClause(
    SourcePosition Position,
    bool Recursive,
    IReadOnlyList<CteDefinition> Ctes
) : AstNode(Position);

public record CteDefinition(
    SourcePosition Position,
    string Name,
    IReadOnlyList<string>? Columns,
    SelectStatement Query
) : AstNode(Position);

public record SetStatement(
    SourcePosition Position
) : Statement(Position);

public record BeginStatement(
    SourcePosition Position
) : Statement(Position);

public record CommitStatement(
    SourcePosition Position
) : Statement(Position);

public record RollbackStatement(
    SourcePosition Position
) : Statement(Position);

public record CallStatement(
    SourcePosition Position,
    TableName Procedure
) : Statement(Position);

public record VariableSetStatement(
    SourcePosition Position,
    Expression Value
) : Statement(Position);

// ====== Clauses ======

public record SelectModifier(bool Distinct, bool All);

public record SelectItem(
    SourcePosition Position,
    Expression Expression,
    string? Alias
) : AstNode(Position);

public record TableReference(
    SourcePosition Position,
    TableSource Source,
    IReadOnlyList<JoinClause>? Joins
) : AstNode(Position);

public record TableSource(
    SourcePosition Position,
    TableName? Table,
    SelectStatement? Subquery,
    string? Alias,
    bool FunctionSource = false,
    SourcePosition? AliasPosition = null
) : AstNode(Position);

public record TableName(
    string Name,
    string? Schema = null,
    string? Database = null
);

public record JoinClause(
    SourcePosition Position,
    JoinType Type,
    bool Natural,
    TableSource Source,
    Expression? OnCondition,
    IReadOnlyList<string>? UsingColumns
) : AstNode(Position);

public enum JoinType
{
    Inner, Left, Right, Full, Cross, Implicit
}

public record OrderByItem(
    SourcePosition Position,
    Expression Expression,
    bool Descending,
    bool NullsFirst
) : AstNode(Position);

public record LimitClause(
    SourcePosition Position,
    int Limit,
    int? Offset
) : AstNode(Position);

public record SetOperation(
    SourcePosition Position,
    SetOperationType Type,
    bool All
) : AstNode(Position);

public enum SetOperationType
{
    Union, Intersect, Except
}

public record IntoClause(
    SourcePosition Position,
    IReadOnlyList<string> Variables
) : AstNode(Position);

// ====== DDL Elements ======

public record ColumnDefinition(
    SourcePosition Position,
    string Name,
    DataTypeInfo Type,
    bool NotNull,
    Expression? DefaultValue,
    IReadOnlyList<ColumnConstraint>? Constraints
) : AstNode(Position);

public record DataTypeInfo(
    SourcePosition Position,
    string Name,
    IReadOnlyList<string>? Parameters
) : AstNode(Position);

public abstract record ColumnConstraint(SourcePosition Position) : AstNode(Position);
public record NotNullConstraint(SourcePosition Position) : ColumnConstraint(Position);
public record NullConstraint(SourcePosition Position) : ColumnConstraint(Position);
public record DefaultConstraint(SourcePosition Position, Expression Value) : ColumnConstraint(Position);
public record PrimaryKeyColumnConstraint(SourcePosition Position) : ColumnConstraint(Position);
public record UniqueColumnConstraint(SourcePosition Position) : ColumnConstraint(Position);
public record ReferencesConstraint(SourcePosition Position, TableName ReferencedTable, IReadOnlyList<string>? Columns)
    : ColumnConstraint(Position);
public record NamedColumnConstraint(SourcePosition Position, string Name, ColumnConstraint Constraint)
    : ColumnConstraint(Position);

public abstract record TableConstraint(SourcePosition Position) : AstNode(Position);
public record PrimaryKeyConstraint(SourcePosition Position, IReadOnlyList<string>? Columns, string? Name)
    : TableConstraint(Position);
public record UniqueConstraint(SourcePosition Position, IReadOnlyList<string>? Columns, string? Name)
    : TableConstraint(Position);
public record ForeignKeyConstraint(
    SourcePosition Position,
    IReadOnlyList<string>? Columns,
    TableName ReferencedTable,
    IReadOnlyList<string>? ReferencedColumns,
    string? Name
) : TableConstraint(Position);
public record CheckConstraint(SourcePosition Position, Expression Condition)
    : TableConstraint(Position);

public record DistributeClause(
    SourcePosition Position,
    bool Random,
    IReadOnlyList<string>? Columns
) : AstNode(Position);

public record OrganizeClause(
    SourcePosition Position,
    IReadOnlyList<string> Columns
) : AstNode(Position);

public record GroomMode(SourcePosition Position, GroomModeKind Kind) : AstNode(Position);
public enum GroomModeKind { Records, Pages, Versions, All }
public record GroomReclaim(SourcePosition Position, bool Backupset, bool None) : AstNode(Position);

public enum ExecuteAs { Owner, Caller }

// ====== External Table ======

public record ExternalTableOption(
    SourcePosition Position,
    string Name,
    ExternalOptionValue? Value
) : AstNode(Position);

public abstract record ExternalOptionValue(SourcePosition Position) : AstNode(Position);
public record ExternalStringValue(SourcePosition Position, string Value) : ExternalOptionValue(Position);
public record ExternalNumberValue(SourcePosition Position, long Value) : ExternalOptionValue(Position);
public record ExternalIdentifierValue(SourcePosition Position, string Value) : ExternalOptionValue(Position);

// ====== Expressions ======

public abstract record Expression(SourcePosition Position) : AstNode(Position);

public record ColumnReference(
    SourcePosition Position,
    string? Qualifier,
    string Name
) : Expression(Position);

public record StarExpression(
    SourcePosition Position,
    string? Qualifier
) : Expression(Position);

public record Literal(SourcePosition Position, LiteralKind Kind, string Value) : Expression(Position);

public enum LiteralKind
{
    Number, String, Null, BooleanTrue, BooleanFalse
}

public record TypeLiteral(
    SourcePosition Position,
    string TypeName,
    string Value
) : Expression(Position);

public record FunctionCall(
    SourcePosition Position,
    string Name,
    string? Schema,
    bool Distinct,
    bool StarArgument,
    IReadOnlyList<Expression>? Arguments,
    FilterClause? Filter,
    OverClause? Over,
    WithinGroupClause? WithinGroup = null
) : Expression(Position);

public record WithinGroupClause(
    SourcePosition Position,
    IReadOnlyList<OrderByItem>? OrderBy
) : AstNode(Position);

public record FilterClause(
    SourcePosition Position,
    Expression Condition
) : Expression(Position);

public record OverClause(
    SourcePosition Position,
    IReadOnlyList<Expression>? PartitionBy,
    IReadOnlyList<OrderByItem>? OrderBy,
    WindowFrame? Frame
) : AstNode(Position);

public record WindowFrame(
    SourcePosition Position,
    WindowFrameUnit Unit,
    FrameBound? Start,
    FrameBound? End,
    ExcludeClause? Exclude
) : AstNode(Position);

public enum WindowFrameUnit { Rows, Range, Groups }

public record FrameBound(
    SourcePosition Position,
    FrameBoundKind Kind,
    long? Value
) : AstNode(Position);

public enum FrameBoundKind
{
    CurrentRow, UnboundedPreceding, UnboundedFollowing,
    Preceding, Following
}

public record ExcludeClause(
    SourcePosition Position,
    ExcludeKind Kind
) : AstNode(Position);

public enum ExcludeKind { CurrentRow, Group, Ties, NoOthers }

public record CaseExpression(
    SourcePosition Position,
    Expression? Value,
    IReadOnlyList<WhenThenClause> WhenClauses,
    Expression? ElseClause
) : Expression(Position);

public record WhenThenClause(
    SourcePosition Position,
    Expression When,
    Expression Then
) : AstNode(Position);

public record CastExpression(
    SourcePosition Position,
    Expression Expression,
    DataTypeInfo TargetType
) : Expression(Position);

public record BinaryExpression(
    SourcePosition Position,
    BinaryOperator Operator,
    Expression Left,
    Expression Right
) : Expression(Position);

public enum BinaryOperator
{
    And, Or,
    Equals, NotEquals, LessThan, GreaterThan, LessThanEquals, GreaterThanEquals,
    Like, Ilike, NotLike, NotIlike, In, NotIn,
    Between, NotBetween,
    Is, IsNot,
    Plus, Minus, Multiply, Divide, Modulo, Caret,
    Concat
}

public record UnaryExpression(
    SourcePosition Position,
    UnaryOperator Operator,
    Expression Operand
) : Expression(Position);

public enum UnaryOperator
{
    Not, Plus, Minus, Exists
}

public record InExpression(
    SourcePosition Position,
    Expression Left,
    bool Not,
    IReadOnlyList<Expression>? Values,
    SelectStatement? Subquery
) : Expression(Position);

public record BetweenExpression(
    SourcePosition Position,
    Expression Value,
    bool Not,
    Expression Low,
    Expression High
) : Expression(Position);

public record IsExpression(
    SourcePosition Position,
    Expression Left,
    bool Not,
    bool Null,
    bool Boolean,
    bool Unknown
) : Expression(Position);

public record ExistsExpression(
    SourcePosition Position,
    SelectStatement Subquery
) : Expression(Position);

public record QuantifiedComparisonExpression(
    SourcePosition Position,
    BinaryOperator Operator,
    QuantifierKind Quantifier,
    Expression Left,
    Expression Right
) : Expression(Position);

public enum QuantifierKind { Any, Some, All }

public record SubqueryExpression(
    SourcePosition Position,
    SelectStatement Query
) : Expression(Position);

public record ExtractExpression(
    SourcePosition Position,
    string Field,
    Expression Source
) : Expression(Position);

public record CastFunctionExpression(
    SourcePosition Position,
    Expression Expression,
    DataTypeInfo TargetType
) : Expression(Position);

public record SequenceValueExpression(
    SourcePosition Position,
    TableName Sequence,
    bool NextVal
) : Expression(Position);

public record ParameterExpression(
    SourcePosition Position
) : Expression(Position);

// ====== Procedure Body ======

public record ProcedureBody(
    SourcePosition Position,
    IReadOnlyList<VariableDeclaration>? Declarations,
    IReadOnlyList<ProcedureStatement> Statements,
    IReadOnlyList<ExceptionHandler>? ExceptionHandlers
) : AstNode(Position);

public record VariableDeclaration(
    SourcePosition Position,
    string Name,
    DataTypeInfo Type,
    bool Alias,
    bool Constant,
    string? AliasFor
) : AstNode(Position);

public abstract record ProcedureStatement(SourcePosition Position) : AstNode(Position);
public record AssignmentStatement(SourcePosition Position, string Variable, Expression Value) : ProcedureStatement(Position);
public record ProcedureReturnStatement(SourcePosition Position, Expression? Value) : ProcedureStatement(Position);
public record ProcedureIfStatement(SourcePosition Position, Expression Condition,
    IReadOnlyList<ProcedureStatement> ThenStatements,
    IReadOnlyList<ProcedureElsif>? ElsifClauses,
    IReadOnlyList<ProcedureStatement>? ElseStatements) : ProcedureStatement(Position);
public record ProcedureElsif(SourcePosition Position, Expression Condition,
    IReadOnlyList<ProcedureStatement> Statements) : AstNode(Position);
public record ProcedureLoopStatement(SourcePosition Position,
    IReadOnlyList<ProcedureStatement> Statements) : ProcedureStatement(Position);
public record ProcedureWhileStatement(SourcePosition Position, Expression Condition,
    IReadOnlyList<ProcedureStatement> Statements) : ProcedureStatement(Position);
public record ProcedureForStatement(SourcePosition Position, string Variable,
    Expression? From, Expression? To, Expression? By,
    SelectStatement? ForQuery,
    Expression? ExecuteSql,
    IReadOnlyList<ProcedureStatement> Statements) : ProcedureStatement(Position);
public record ProcedureExitStatement(SourcePosition Position, Expression? When) : ProcedureStatement(Position);
public record ProcedureRaiseStatement(SourcePosition Position, RaiseLevel Level, Expression Message)
    : ProcedureStatement(Position);
public enum RaiseLevel { Exception, Notice, Debug, Error }
public record ProcedureRollbackStatement(SourcePosition Position) : ProcedureStatement(Position);
public record ProcedureCommitStatement(SourcePosition Position) : ProcedureStatement(Position);
public record ProcedureCallStatement(SourcePosition Position, TableName Procedure,
    IReadOnlyList<Expression>? Arguments) : ProcedureStatement(Position);
public record ProcedureExecuteImmediateStatement(SourcePosition Position, Expression Sql,
    IReadOnlyList<string>? Using) : ProcedureStatement(Position);

public record ProcedureSqlStatement(SourcePosition Position, Statement Sql) : ProcedureStatement(Position);
public record ProcedureBlockStatement(SourcePosition Position, ProcedureBody Body) : ProcedureStatement(Position);

public record ExceptionHandler(
    SourcePosition Position,
    string? Condition,
    IReadOnlyList<ProcedureStatement> Statements
) : AstNode(Position);

// ====== Update Set Items ======

public record UpdateSetItem(
    SourcePosition Position,
    ColumnReference Column,
    Expression Value
) : AstNode(Position);
