namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Shared semantic token kinds for editor and LSP consumers.
/// Order must match <see cref="NzSemanticTokenClassifier.TokenTypesLegend"/>.
/// </summary>
public enum SemanticTokenKind
{
    Comment = 0,
    String = 1,
    Number = 2,
    Keyword = 3,
    Type = 4,
    Function = 5,
    Operator = 6,
    Parameter = 7,
    Variable = 8,
    Table = 9,
    Column = 10,
    Cte = 11,
    Alias = 12,
    Identifier = 13,
}

[Flags]
public enum SemanticTokenModifiers
{
    None = 0,
    Deprecated = 1 << 0,
    Definition = 1 << 1,
    DefaultLibrary = 1 << 2,
}

public readonly record struct SemanticTokenSpan(
    int Start,
    int Length,
    SemanticTokenKind Kind,
    SemanticTokenModifiers Modifiers = SemanticTokenModifiers.None);
