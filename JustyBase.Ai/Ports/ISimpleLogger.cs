namespace JustyBase.Ai.Ports;

/// <summary>
/// Minimal logger surface consumed by the AI chat pipeline. Hosts adapt their own
/// logger implementation onto this port.
/// </summary>
public interface ISimpleLogger
{
    void TrackError(Exception ex, bool isCrash);
}

public sealed class EmptySimpleLogger : ISimpleLogger
{
    public static readonly EmptySimpleLogger Instance = new();

    public void TrackError(Exception ex, bool isCrash)
    {
    }
}
