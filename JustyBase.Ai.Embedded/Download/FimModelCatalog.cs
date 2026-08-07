namespace JustyBase.Ai.Embedded.Download;

/// <summary>Well-known embedded FIM model ids persisted in AppOptions.</summary>
public static class FimModelIds
{
    public const string Qwen25Coder15B = "qwen2.5-coder-1.5b";
    public const string Qwen25Coder3B = "qwen2.5-coder-3b";
    public const string Qwen25Coder7B = "qwen2.5-coder-7b";
    public const string Qwen25Coder14B = "qwen2.5-coder-14b";

    public const string CodeGemma2B = "codegemma-2b";
    public const string CodeGemma7B = "codegemma-7b";

    public const string StarCoder2_3B = "starcoder2-3b";
    public const string StarCoder2_7B = "starcoder2-7b";
    public const string StarCoder2_15B = "starcoder2-15b";

    public const string Codestral22B = "codestral-22b";

    public const string Default = Qwen25Coder3B;
}

/// <summary>
/// Catalog of GGUF models for Fill-in-the-Middle served by the bundled llama.cpp llama-server.
/// llama.cpp has built-in FIM templates for Qwen2.5-Coder, CodeGemma, StarCoder2 and Codestral.
/// </summary>
public sealed class FimModelCatalog : IModelCatalog
{
    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new(
            Id: FimModelIds.Qwen25Coder15B,
            DisplayName: "Qwen2.5-Coder 1.5B (Q4_K_M) — Small preset",
            FileName: "Qwen2.5-Coder-1.5B.Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/QuantFactory/Qwen2.5-Coder-1.5B-GGUF/resolve/main/Qwen2.5-Coder-1.5B.Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~1.0 GB",
            SourceModelUrl: new Uri("https://huggingface.co/Qwen/Qwen2.5-Coder-1.5B"),
            Notes: "Base (non-Instruct) Q4_K_M GGUF of Qwen2.5-Coder-1.5B. Default for Small preset.",
            ApproxBytes: 986_048_352),
        new(
            Id: FimModelIds.Qwen25Coder3B,
            DisplayName: "Qwen2.5-Coder 3B (Q4_K_M) — default",
            FileName: "Qwen2.5-Coder-3B-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/Qwen2.5-Coder-3B-GGUF/resolve/main/Qwen2.5-Coder-3B-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~1.9 GB",
            SourceModelUrl: new Uri("https://huggingface.co/Qwen/Qwen2.5-Coder-3B"),
            Notes: "Base FIM model. Default for Medium preset / new installs.",
            ApproxBytes: 1_929_903_200),
        new(
            Id: FimModelIds.Qwen25Coder7B,
            DisplayName: "Qwen2.5-Coder 7B (Q4_K_M) — Medium/Large default",
            FileName: "Qwen2.5-Coder-7B.Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/QuantFactory/Qwen2.5-Coder-7B-GGUF/resolve/main/Qwen2.5-Coder-7B.Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~4.7 GB",
            SourceModelUrl: new Uri("https://huggingface.co/QuantFactory/Qwen2.5-Coder-7B-GGUF"),
            Notes: "Base FIM model from QuantFactory. Default for Medium/Large.",
            ApproxBytes: 4_680_000_000),
        new(
            Id: FimModelIds.Qwen25Coder14B,
            DisplayName: "Qwen2.5-Coder 14B (Q4_K_M) — heavy",
            FileName: "Qwen2.5-Coder-14B-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/Qwen2.5-Coder-14B-GGUF/resolve/main/Qwen2.5-Coder-14B-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~9.0 GB",
            SourceModelUrl: new Uri("https://huggingface.co/Qwen/Qwen2.5-Coder-14B"),
            Notes: "Optional Large upgrade when VRAM allows.",
            ApproxBytes: 9_000_000_000),

        new(
            Id: FimModelIds.CodeGemma2B,
            DisplayName: "CodeGemma 2B (Q4_K_M)",
            FileName: "codegemma-2b-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/codegemma-2b-GGUF/resolve/main/codegemma-2b-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~1.6 GB",
            SourceModelUrl: new Uri("https://huggingface.co/google/codegemma-2b"),
            Notes: "Google CodeGemma — alternative small coder.",
            ApproxBytes: 1_600_000_000,
            Family: "CodeGemma",
            RequiresLicenseAcceptance: true,
            LicenseName: "Gemma Terms of Use",
            LicenseUrl: new Uri("https://ai.google.dev/gemma/terms"),
            LicenseSummary: "CodeGemma is subject to Google's Gemma Terms of Use. You must review and accept those terms before downloading."),
        new(
            Id: FimModelIds.CodeGemma7B,
            DisplayName: "CodeGemma 7B (Q4_K_M)",
            FileName: "codegemma-7b-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/codegemma-7b-GGUF/resolve/main/codegemma-7b-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~5.0 GB",
            SourceModelUrl: new Uri("https://huggingface.co/google/codegemma-7b"),
            Notes: "Google CodeGemma 7B.",
            ApproxBytes: 5_000_000_000,
            Family: "CodeGemma",
            RequiresLicenseAcceptance: true,
            LicenseName: "Gemma Terms of Use",
            LicenseUrl: new Uri("https://ai.google.dev/gemma/terms"),
            LicenseSummary: "CodeGemma is subject to Google's Gemma Terms of Use. You must review and accept those terms before downloading."),

        new(
            Id: FimModelIds.StarCoder2_3B,
            DisplayName: "StarCoder2 3B (Q4_K_M)",
            FileName: "starcoder2-3b-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/starcoder2-3b-GGUF/resolve/main/starcoder2-3b-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~1.8 GB",
            SourceModelUrl: new Uri("https://huggingface.co/bigcode/starcoder2-3b"),
            Notes: "BigCode StarCoder2 3B (FIM-capable).",
            ApproxBytes: 1_800_000_000,
            Family: "StarCoder2"),
        new(
            Id: FimModelIds.StarCoder2_7B,
            DisplayName: "StarCoder2 7B (Q4_K_M)",
            FileName: "starcoder2-7b-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/starcoder2-7b-GGUF/resolve/main/starcoder2-7b-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~4.4 GB",
            SourceModelUrl: new Uri("https://huggingface.co/bigcode/starcoder2-7b"),
            Notes: "BigCode StarCoder2 7B.",
            ApproxBytes: 4_400_000_000,
            Family: "StarCoder2"),
        new(
            Id: FimModelIds.StarCoder2_15B,
            DisplayName: "StarCoder2 15B (Q4_K_M)",
            FileName: "starcoder2-15b-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/starcoder2-15b-GGUF/resolve/main/starcoder2-15b-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~9.1 GB",
            SourceModelUrl: new Uri("https://huggingface.co/bigcode/starcoder2-15b"),
            Notes: "BigCode StarCoder2 15B — needs substantial VRAM/RAM.",
            ApproxBytes: 9_100_000_000,
            Family: "StarCoder2"),

        new(
            Id: FimModelIds.Codestral22B,
            DisplayName: "Codestral 22B (Q4_K_M) — license required",
            FileName: "Codestral-22B-v0.1-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/Codestral-22B-v0.1-GGUF/resolve/main/Codestral-22B-v0.1-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~13 GB",
            SourceModelUrl: new Uri("https://huggingface.co/mistralai/Codestral-22B-v0.1"),
            Notes: "Mistral Codestral 22B. MNPL license — explicit acceptance required before download.",
            ApproxBytes: 13_000_000_000,
            Family: "Codestral",
            RequiresLicenseAcceptance: true,
            LicenseName: "Mistral Non-Production License (MNPL)",
            LicenseUrl: new Uri("https://mistral.ai/licenses/MNPL-0.1.md"),
            LicenseSummary:
                "Codestral is released under the Mistral Non-Production License (MNPL). " +
                "It is NOT a permissive open-source license: commercial / production use is restricted. " +
                "You must read and accept the MNPL before downloading or using this model."),
    ];

    public ModelDescriptor Resolve(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return Models.First(m => m.Id == FimModelIds.Default);
        }

        foreach (var model in Models)
        {
            if (string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
            {
                return model;
            }
        }

        return Models.First(m => m.Id == FimModelIds.Default);
    }
}
