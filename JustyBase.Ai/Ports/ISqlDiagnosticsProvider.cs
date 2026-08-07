namespace JustyBase.Ai.Ports;

/// <summary>Severity-typed diagnostic entry consumed by the "get_diagnostics" chat tool.</summary>
public sealed record ChatDiagnosticItem(
    string RuleId,
    string Message,
    string Severity,
    int StartLine,
    int StartColumn);

/// <summary>
/// Host adapter over the current SQL diagnostics (lint results). The chat tool
/// executor reads <see cref="Items"/> to give the model advisory diagnostic context.
/// </summary>
public interface ISqlDiagnosticsProvider
{
    IReadOnlyList<ChatDiagnosticItem> Items { get; }
}

public sealed class EmptySqlDiagnosticsProvider : ISqlDiagnosticsProvider
{
    public static readonly EmptySqlDiagnosticsProvider Instance = new();

    public IReadOnlyList<ChatDiagnosticItem> Items => [];
}
