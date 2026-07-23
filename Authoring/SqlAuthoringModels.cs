namespace JustyBase.NetezzaSqlParser.Authoring;

public sealed record SqlHoverInfo(string Content, int StartOffset, int EndOffset);

public sealed record SqlSignatureHelpInfo(
    SqlSignatureInfo[] Signatures,
    int ActiveSignature,
    int ActiveParameter
);

public sealed record SqlSignatureInfo(
    string Label,
    string? Documentation,
    SqlSignatureParameterInfo[] Parameters
);

public sealed record SqlSignatureParameterInfo(
    string Label,
    string? Documentation
);

public enum SqlSymbolKind
{
    Cte,
    Alias,
    Table
}

public sealed record SymbolOccurrence(
    int Id,
    string Name,
    SqlSymbolKind Kind,
    int StartAbsolute,
    int EndAbsolute,
    bool IsDefinition,
    int? DefinitionId
);

public sealed record SqlRenameInfo(
    string OldName,
    SqlSymbolKind Kind,
    IReadOnlyList<SymbolOccurrence> Occurrences
);
