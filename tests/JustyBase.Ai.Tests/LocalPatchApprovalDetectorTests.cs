using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class LocalPatchApprovalDetectorTests
{
    [Theory]
    [InlineData("approve")]
    [InlineData("approve.")]
    [InlineData("accept")]
    [InlineData("apply")]
    [InlineData("zatwierdzam")]
    [InlineData("potwierdzam")]
    [InlineData("potwierdź")]
    [InlineData("tak")]
    [InlineData("TAK!")]
    [InlineData("  approve  ")]
    [InlineData("ok")]
    [InlineData("yes?")]
    public void IsApprovalMessage_ReturnsTrue_ForApprovalOnlyMessages(string message)
    {
        Assert.True(LocalPatchApprovalDetector.IsApprovalMessage(message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ok teraz opisz plan")]
    [InlineData("approve and continue")]
    [InlineData("nie zatwierdzam")]
    [InlineData("odrzuć")]
    [InlineData("cancel")]
    [InlineData("tak i kontynuuj")]
    [InlineData("potwierdzam i lecimy")]
    [InlineData("deny")]
    [InlineData("anuluj")]
    public void IsApprovalMessage_ReturnsFalse_ForNonApprovalMessages(string? message)
    {
        Assert.False(LocalPatchApprovalDetector.IsApprovalMessage(message));
    }
}
