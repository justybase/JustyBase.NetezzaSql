namespace JustyBase.Ai.Services;

public static class LocalToolStatusFormatter
{
    private const string UnknownToolName = "unknown";

    public static string FormatAutoApproved(string? toolName)
    {
        return $"Tool: {NormalizeToolName(toolName)}";
    }

    public static string FormatToolStart(string? toolName)
    {
        return $"Tool start: {NormalizeToolName(toolName)}";
    }

    public static string FormatToolProgress(string? progressMessage)
    {
        return string.IsNullOrWhiteSpace(progressMessage) ? "Tool progress update" : progressMessage;
    }

    public static string FormatToolCompletion(bool success)
    {
        return success ? "Tool completed" : "Tool failed";
    }

    private static string NormalizeToolName(string? toolName)
    {
        return string.IsNullOrWhiteSpace(toolName) ? UnknownToolName : toolName;
    }
}
