namespace JustyBase.NetezzaSqlParser.Authoring;

public enum SqlQuickFixSafety { Safe, ReviewRequired }

public sealed record SqlTextEdit(int StartOffset, int EndOffset, string NewText);

/// <summary>Production action metadata; offsets are UTF-16 code units.</summary>
public sealed record SqlQuickFixInfo(string Title, SqlQuickFixSafety Safety,
    IReadOnlyList<SqlTextEdit> Edits);
