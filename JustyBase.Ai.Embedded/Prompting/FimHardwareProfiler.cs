using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace JustyBase.Ai.Embedded.Prompting;

public enum FimGpuClass
{
    None,
    Integrated,
    Discrete,
}

/// <summary>Suggests a FIM preset from local Vulkan device class (vulkaninfo / heuristics).</summary>
public static partial class FimHardwareProfiler
{
    public static FimGpuClass DetectGpuClass(TimeSpan? timeout = null)
    {
        try
        {
            var summary = RunVulkanInfoSummary(timeout ?? TimeSpan.FromSeconds(8));
            if (string.IsNullOrWhiteSpace(summary))
            {
                return FimGpuClass.None;
            }

            if (summary.Contains("PHYSICAL_DEVICE_TYPE_DISCRETE_GPU", StringComparison.OrdinalIgnoreCase)
                || DiscreteNameRegex().IsMatch(summary))
            {
                // Prefer discrete when both appear (e.g. laptop dGPU + iGPU).
                if (summary.Contains("PHYSICAL_DEVICE_TYPE_DISCRETE_GPU", StringComparison.OrdinalIgnoreCase))
                {
                    return FimGpuClass.Discrete;
                }
            }

            if (summary.Contains("PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU", StringComparison.OrdinalIgnoreCase)
                || summary.Contains("Radeon 780M", StringComparison.OrdinalIgnoreCase)
                || summary.Contains("Iris", StringComparison.OrdinalIgnoreCase)
                || summary.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase))
            {
                // llvmpipe alone is software — treat as no GPU.
                if (summary.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase)
                    && !summary.Contains("PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU", StringComparison.OrdinalIgnoreCase)
                    && !summary.Contains("PHYSICAL_DEVICE_TYPE_DISCRETE_GPU", StringComparison.OrdinalIgnoreCase))
                {
                    return FimGpuClass.None;
                }

                return FimGpuClass.Integrated;
            }

            if (summary.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase))
            {
                return FimGpuClass.None;
            }
        }
#pragma warning disable CA1031
        catch
#pragma warning restore CA1031
        {
            // Fall through — no GPU assumed.
        }

        return FimGpuClass.None;
    }

    public static string SuggestPresetId(FimGpuClass gpuClass) =>
        gpuClass switch
        {
            FimGpuClass.Discrete => FimPresets.Large,
            FimGpuClass.Integrated => FimPresets.Medium,
            _ => FimPresets.Small,
        };

    public static string DescribeSuggestion(FimGpuClass gpuClass, string presetId)
    {
        var gpu = gpuClass switch
        {
            FimGpuClass.Discrete => "discrete GPU",
            FimGpuClass.Integrated => "integrated GPU (Vulkan iGPU)",
            _ => "no usable GPU (CPU)",
        };
        return $"Suggested preset for {gpu}: {presetId}";
    }

    private static string? RunVulkanInfoSummary(TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "vulkaninfo",
            Arguments = "--summary",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        var stdout = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.BeginOutputReadLine();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
#pragma warning disable CA1031
            catch
#pragma warning restore CA1031
            {
                // ignore
            }

            return null;
        }

        return stdout.ToString();
    }

    [GeneratedRegex(@"RTX|GeForce|Radeon RX|Arc A\d{3}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiscreteNameRegex();
}
