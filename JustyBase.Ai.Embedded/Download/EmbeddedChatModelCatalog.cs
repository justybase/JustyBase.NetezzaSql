namespace JustyBase.Ai.Embedded.Download;

/// <summary>Well-known embedded chat model ids persisted in AppOptions.</summary>
public static class EmbeddedChatModelIds
{
    public const string Gemma4_12B = "gemma4-12b-it";
    public const string Qwen36_27B = "qwen3.6-27b";
    public const string Devstral2_22B = "devstral-2-22b";
    public const string Qwen36_35BA3B = "qwen3.6-35b-a3b";
    public const string Qwen35_9B = "qwen3.5-9b";
    public const string Qwen35_4B = "qwen3.5-4b";
    public const string Gemma4_31B = "gemma4-31b";
    public const string Gemma4_26BA4B = "gemma4-26b-a4b";

    public const string Default = Qwen35_4B;
}

/// <summary>
/// Catalog of instruct GGUF chat models served by the bundled llama.cpp llama-server for the
/// "Embedded" AI chat backend.
///
/// Source policy: prefer the official model provider's own Q4 GGUF when one exists (Google
/// Gemma QAT q4_0); otherwise use the trusted unsloth Q4_K_M community builds.
/// </summary>
public sealed class EmbeddedChatModelCatalog : IModelCatalog
{
    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new(
            Id: EmbeddedChatModelIds.Qwen35_4B,
            DisplayName: "Qwen 3.5 4B (Q4_K_M) — default",
            FileName: "Qwen3.5-4B-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/main/Qwen3.5-4B-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~2.7 GB",
            SourceModelUrl: new Uri("https://huggingface.co/unsloth/Qwen3.5-4B-GGUF?show_file_info=Qwen3.5-4B-Q4_K_M.gguf"),
            Notes: "Smallest chat model — best starting point on iGPU/CPU. Apache-2.0. Unsloth GGUF.",
            ApproxBytes: 2_700_000_000),
        new(
            Id: EmbeddedChatModelIds.Qwen35_9B,
            DisplayName: "Qwen 3.5 9B (Q4_K_M)",
            FileName: "Qwen3.5-9B-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~5.7 GB",
            SourceModelUrl: new Uri("https://huggingface.co/unsloth/Qwen3.5-9B-GGUF?show_file_info=Qwen3.5-9B-Q4_K_M.gguf"),
            Notes: "Balanced quality/speed — recommended when 8+ GB VRAM is available. Unsloth GGUF.",
            ApproxBytes: 5_700_000_000),
        new(
            Id: EmbeddedChatModelIds.Gemma4_12B,
            DisplayName: "Gemma 4 12B Instruct (QAT q4_0)",
            FileName: "gemma-4-12b-it-qat-q4_0.gguf",
            DownloadUri: new Uri("https://huggingface.co/google/gemma-4-12B-it-qat-q4_0-gguf/resolve/main/gemma-4-12b-it-qat-q4_0.gguf?download=true"),
            ApproxSizeLabel: "~8.5 GB",
            SourceModelUrl: new Uri("https://huggingface.co/google/gemma-4-12B-it-qat-q4_0-gguf?show_file_info=gemma-4-12b-it-qat-q4_0.gguf"),
            Notes: "Google official QAT q4_0 GGUF — subject to Gemma Terms of Use.",
            ApproxBytes: 8_500_000_000,
            Family: "Gemma 4",
            RequiresLicenseAcceptance: true,
            LicenseName: "Gemma Terms of Use",
            LicenseUrl: new Uri("https://ai.google.dev/gemma/terms"),
            LicenseSummary: "Gemma models are subject to Google's Gemma Terms of Use. You must review and accept those terms before downloading."),
        new(
            Id: EmbeddedChatModelIds.Devstral2_22B,
            DisplayName: "Devstral Small 2 24B Instruct (Q4_K_M)",
            FileName: "Devstral-Small-2-24B-Instruct-2512-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/unsloth/Devstral-Small-2-24B-Instruct-2512-GGUF/resolve/main/Devstral-Small-2-24B-Instruct-2512-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~14 GB",
            SourceModelUrl: new Uri("https://huggingface.co/unsloth/Devstral-Small-2-24B-Instruct-2512-GGUF?show_file_info=Devstral-Small-2-24B-Instruct-2512-Q4_K_M.gguf"),
            Notes: "Mistral dev-focused instruct model (Devstral 2, 24B) — license acceptance required. Unsloth GGUF.",
            ApproxBytes: 14_000_000_000,
            Family: "Devstral (Mistral)",
            RequiresLicenseAcceptance: true,
            LicenseName: "Mistral license",
            LicenseUrl: new Uri("https://mistral.ai/legal/"),
            LicenseSummary: "Devstral is released by Mistral AI under its model license. You must review and accept the license before downloading."),
        new(
            Id: EmbeddedChatModelIds.Qwen36_27B,
            DisplayName: "Qwen 3.6 27B (Q4_K_M)",
            FileName: "Qwen3.6-27B-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/unsloth/Qwen3.6-27B-GGUF/resolve/main/Qwen3.6-27B-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~18 GB",
            SourceModelUrl: new Uri("https://huggingface.co/unsloth/Qwen3.6-27B-GGUF?show_file_info=Qwen3.6-27B-Q4_K_M.gguf"),
            Notes: "High-quality chat — needs 24+ GB VRAM or fast CPU + 32 GB RAM. Unsloth GGUF.",
            ApproxBytes: 18_000_000_000),
        new(
            Id: EmbeddedChatModelIds.Gemma4_26BA4B,
            DisplayName: "Gemma 4 26B-A4B (MoE, QAT q4_0)",
            FileName: "gemma-4-26B_q4_0-it.gguf",
            DownloadUri: new Uri("https://huggingface.co/google/gemma-4-26B-A4B-it-qat-q4_0-gguf/resolve/main/gemma-4-26B_q4_0-it.gguf?download=true"),
            ApproxSizeLabel: "~16 GB",
            SourceModelUrl: new Uri("https://huggingface.co/google/gemma-4-26B-A4B-it-qat-q4_0-gguf?show_file_info=gemma-4-26B_q4_0-it.gguf"),
            Notes: "MoE (4B active) — faster inference than dense 26B at similar quality. Google official QAT q4_0. Gemma license.",
            ApproxBytes: 16_000_000_000,
            Family: "Gemma 4 (MoE)",
            RequiresLicenseAcceptance: true,
            LicenseName: "Gemma Terms of Use",
            LicenseUrl: new Uri("https://ai.google.dev/gemma/terms"),
            LicenseSummary: "Gemma models are subject to Google's Gemma Terms of Use. You must review and accept those terms before downloading."),
        new(
            Id: EmbeddedChatModelIds.Qwen36_35BA3B,
            DisplayName: "Qwen 3.6 35B-A3B (MoE, Q4_K_M)",
            FileName: "Qwen3.6-35B-A3B-UD-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/unsloth/Qwen3.6-35B-A3B-GGUF/resolve/main/Qwen3.6-35B-A3B-UD-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~20 GB",
            SourceModelUrl: new Uri("https://huggingface.co/unsloth/Qwen3.6-35B-A3B-GGUF?show_file_info=Qwen3.6-35B-A3B-UD-Q4_K_M.gguf"),
            Notes: "MoE (3B active) — large capability at MoE inference cost. Unsloth Dynamic UD-Q4_K_M.",
            ApproxBytes: 20_000_000_000,
            Family: "Qwen (MoE)"),
        new(
            Id: EmbeddedChatModelIds.Gemma4_31B,
            DisplayName: "Gemma 4 31B (QAT q4_0)",
            FileName: "gemma-4-31B_q4_0-it.gguf",
            DownloadUri: new Uri("https://huggingface.co/google/gemma-4-31B-it-qat-q4_0-gguf/resolve/main/gemma-4-31B_q4_0-it.gguf?download=true"),
            ApproxSizeLabel: "~21 GB",
            SourceModelUrl: new Uri("https://huggingface.co/google/gemma-4-31B-it-qat-q4_0-gguf?show_file_info=gemma-4-31B_q4_0-it.gguf"),
            Notes: "Largest Gemma 4 dense — 32+ GB VRAM / heavy RAM required. Google official QAT q4_0. Gemma license.",
            ApproxBytes: 21_000_000_000,
            Family: "Gemma 4",
            RequiresLicenseAcceptance: true,
            LicenseName: "Gemma Terms of Use",
            LicenseUrl: new Uri("https://ai.google.dev/gemma/terms"),
            LicenseSummary: "Gemma models are subject to Google's Gemma Terms of Use. You must review and accept those terms before downloading."),
    ];

    public ModelDescriptor Resolve(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return Models.First(m => m.Id == EmbeddedChatModelIds.Default);
        }

        foreach (var model in Models)
        {
            if (string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
            {
                return model;
            }
        }

        return Models.First(m => m.Id == EmbeddedChatModelIds.Default);
    }
}
