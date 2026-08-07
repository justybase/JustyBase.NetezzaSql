namespace JustyBase.Ai.Services;

public static class LocalSqlPatchResponseFormatter
{
    public const string ProposedSqlCannotBeEmptyMessage = "Proposed SQL content cannot be empty.";
    public const string NoChangesDetectedMessage = "No changes detected between current buffer and proposed SQL.";
    public const string NoPendingPatchPreviewMessage = "No pending patch preview exists. Run PreviewSqlEditorPatch first.";
    public const string BufferUpdateUnavailableMessage = "Editor buffer update is unavailable because no updater provider is registered.";
    public const string BufferChangedSincePreviewMessage = "Editor buffer changed since diff preview. Re-run PreviewSqlEditorPatch before applying.";
    public const string PatchApplicationFailedMessage = "Patch application failed.";

    public static string FormatPatchPreviewReady(string diff)
    {
        return $"""
Patch preview ready. Review before applying.
--- active-sql-buffer
+++ active-sql-buffer (proposed)
{diff}

After review and user approval, call ApplyPreviewedSqlEditorPatch to update the editor buffer.
""";
    }

    public static string FormatPatchApplied(int oldLineCount, int newLineCount)
    {
        return $"Patch applied to active editor buffer. Line count: {oldLineCount} -> {newLineCount}.";
    }

    public static string FormatPatchApplicationFailed(string errorMessage)
    {
        return $"Patch application failed: {errorMessage}";
    }
}
