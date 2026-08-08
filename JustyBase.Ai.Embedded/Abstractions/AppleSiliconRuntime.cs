using System.Runtime.InteropServices;

namespace JustyBase.Ai.Embedded.Abstractions;

/// <summary>
/// Platform detection for the embedded MLX backend. MLX is supported only on macOS running
/// native ARM64 binaries (Apple Silicon / unified memory). X64 macOS (Intel, Rosetta) and
/// non-macOS hosts keep the llama.cpp/GGUF backend.
/// </summary>
public static class AppleSiliconRuntime
{
    /// <summary>True when the current process runs natively on Apple Silicon.</summary>
    public static bool IsSupported =>
        OperatingSystem.IsMacOS()
        && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
}