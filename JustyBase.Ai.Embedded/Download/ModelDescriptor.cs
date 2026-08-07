namespace JustyBase.Ai.Embedded.Download;

/// <summary>Describes a downloadable GGUF model (FIM or chat) with its HuggingFace source.</summary>
public sealed record ModelDescriptor(
    string Id,
    string DisplayName,
    string FileName,
    Uri DownloadUri,
    string ApproxSizeLabel,
    Uri SourceModelUrl,
    string Notes,
    long ApproxBytes,
    string Family = "Qwen (recommended)",
    bool RequiresLicenseAcceptance = false,
    string? LicenseName = null,
    Uri? LicenseUrl = null,
    string? LicenseSummary = null);

public interface IModelCatalog
{
    IReadOnlyList<ModelDescriptor> Models { get; }
    ModelDescriptor Resolve(string? modelId);
}
