namespace JustyBase.Ai.Services;

public static class LocalStreamResponseFormatter
{
    public const string ClientNotInitializedMessage = "[Error: Copilot client not initialized. Please restart the application.]";
    public const string NoResponseMessage = "[No response received. Model may be unavailable or there may be an authentication issue.]";

    public static string FormatError(string error)
    {
        return $"\n[Error: {error}]";
    }

    public static string FormatTimeout(int timeoutSeconds)
    {
        return $"\n[Response timeout - no response within {timeoutSeconds} seconds]";
    }
}
