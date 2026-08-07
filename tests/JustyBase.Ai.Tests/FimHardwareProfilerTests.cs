using JustyBase.Ai.Embedded.Prompting;

namespace JustyBase.Ai.Tests;

public sealed class FimHardwareProfilerTests
{
    [Theory]
    [InlineData(FimGpuClass.None, "Small")]
    [InlineData(FimGpuClass.Integrated, "Medium")]
    [InlineData(FimGpuClass.Discrete, "Large")]
    public void SuggestPresetId_MatchesGpuClass(FimGpuClass gpuClass, string expected)
    {
        Assert.Equal(expected, FimHardwareProfiler.SuggestPresetId(gpuClass));
    }
}
