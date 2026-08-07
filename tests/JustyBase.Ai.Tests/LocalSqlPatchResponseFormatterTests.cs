using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class LocalSqlPatchResponseFormatterTests
{
    [Fact]
    public void Constants_ShouldMatchExpectedMessages()
    {
        Assert.Equal("Proposed SQL content cannot be empty.", LocalSqlPatchResponseFormatter.ProposedSqlCannotBeEmptyMessage);
        Assert.Equal("No changes detected between current buffer and proposed SQL.", LocalSqlPatchResponseFormatter.NoChangesDetectedMessage);
        Assert.Equal("No pending patch preview exists. Run PreviewSqlEditorPatch first.", LocalSqlPatchResponseFormatter.NoPendingPatchPreviewMessage);
        Assert.Equal("Editor buffer update is unavailable because no updater provider is registered.", LocalSqlPatchResponseFormatter.BufferUpdateUnavailableMessage);
        Assert.Equal("Editor buffer changed since diff preview. Re-run PreviewSqlEditorPatch before applying.", LocalSqlPatchResponseFormatter.BufferChangedSincePreviewMessage);
        Assert.Equal("Patch application failed.", LocalSqlPatchResponseFormatter.PatchApplicationFailedMessage);
    }

    [Fact]
    public void FormatPatchPreviewReady_ShouldContainDiffAndGuidance()
    {
        const string diff = "@@ -1,1 +1,1 @@";
        var result = LocalSqlPatchResponseFormatter.FormatPatchPreviewReady(diff);

        Assert.Contains("Patch preview ready. Review before applying.", result, StringComparison.Ordinal);
        Assert.Contains(diff, result, StringComparison.Ordinal);
        Assert.Contains("ApplyPreviewedSqlEditorPatch", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPatchApplied_ShouldIncludeLineCounts()
    {
        var result = LocalSqlPatchResponseFormatter.FormatPatchApplied(3, 5);

        Assert.Equal("Patch applied to active editor buffer. Line count: 3 -> 5.", result);
    }

    [Fact]
    public void FormatPatchApplicationFailed_ShouldIncludeReason()
    {
        var result = LocalSqlPatchResponseFormatter.FormatPatchApplicationFailed("boom");

        Assert.Equal("Patch application failed: boom", result);
    }
}
