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
/// Catalog of instruct chat models for the "Embedded" AI chat backend. Windows/Linux hosts
/// the bundled llama.cpp llama-server with Q4 GGUF sources (official provider QAT q4_0 when
/// available, otherwise unsloth Q4_K_M). Apple Silicon hosts the verified mlx-community 4-bit
/// MLX snapshots via <c>mlx_lm.server</c>.
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
            ApproxBytes: 2_700_000_000,
            MlxRepoId: "mlx-community/Qwen3.5-4B-MLX-4bit",
            MlxSizeLabel: "~3.1 GB",
            MlxApproxBytes: 3_061_132_920),
        new(
            Id: EmbeddedChatModelIds.Qwen35_9B,
            DisplayName: "Qwen 3.5 9B (Q4_K_M)",
            FileName: "Qwen3.5-9B-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~5.7 GB",
            SourceModelUrl: new Uri("https://huggingface.co/unsloth/Qwen3.5-9B-GGUF?show_file_info=Qwen3.5-9B-Q4_K_M.gguf"),
            Notes: "Balanced quality/speed — recommended when 8+ GB VRAM is available. Unsloth GGUF.",
            ApproxBytes: 5_700_000_000,
            MlxRepoId: "mlx-community/Qwen3.5-9B-MLX-4bit",
            MlxSizeLabel: "~6.0 GB",
            MlxApproxBytes: 5_977_074_591),
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
            LicenseSummary: "Gemma models are subject to Google's Gemma Terms of Use. You must review and accept those terms before downloading.",
            MlxRepoId: "mlx-community/gemma-4-12B-it-4bit",
            MlxSizeLabel: "~6.8 GB",
            MlxApproxBytes: 6_773_372_848),
        new(
            Id: EmbeddedChatModelIds.Devstral2_22B,
            DisplayName: "Devstral Small 2 24B Instruct (Q4_K_M)",
            FileName: "Devstral-Small-2-24B-Instruct-2512-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/unsloth/Devstral-Small-2-24B-Instruct-2512-GGUF/resolve/main/Devstral-Small-2-24B-Instruct-2512-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~14 GB",
            SourceModelUrl: new Uri("https://huggingface.co/unsloth/Devstral-Small-2-24B-Instruct-2512-GGUF?show_file_info=Devstral-Small-2-24B-Instruct-2512-Q4_K_M.gguf"),
            Notes: "Mistral dev-focused instruct model (Devstral 2, 24B) — license acceptance required. Unsloth GGUF.",
            ApproxBytes: 14_000_000_000,
            MlxRepoId: "mlx-community/Devstral-Small-2-24B-Instruct-2512-4bit",
            MlxSizeLabel: "~15.1 GB",
            MlxApproxBytes: 15_136_819_784,
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
            ApproxBytes: 18_000_000_000,
            MlxRepoId: "mlx-community/Qwen3.6-27B-4bit",
            MlxSizeLabel: "~16.1 GB",
            MlxApproxBytes: 16_081_490_064),
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
            MlxRepoId: "mlx-community/gemma-4-26b-a4b-it-4bit",
            MlxSizeLabel: "~15.4 GB",
            MlxApproxBytes: 15_373_588_575,
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
            Family: "Qwen (MoE)",
            MlxRepoId: "mlx-community/Qwen3.6-35B-A3B-4bit",
            MlxSizeLabel: "~20.4 GB",
            MlxApproxBytes: 20_429_169_263),
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
            LicenseSummary: "Gemma models are subject to Google's Gemma Terms of Use. You must review and accept those terms before downloading.",
            MlxRepoId: "mlx-community/gemma-4-31b-it-4bit",
            MlxSizeLabel: "~18.4 GB",
            MlxApproxBytes: 18_444_421_751),
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
