namespace JustyBase.Ai.Ports;

/// <summary>
/// Host-level application environment values needed by the chat pipeline
/// (e.g. the persistent configuration directory used for the Codex subprocess home).
/// </summary>
public interface IChatEnvironment
{
    string ConfigDirectory { get; }
}
