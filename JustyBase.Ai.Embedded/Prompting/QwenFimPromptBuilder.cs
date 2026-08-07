using JustyBase.Ai.Embedded.Prompting;

namespace JustyBase.Ai.Embedded.Prompting;

/// <summary>Qwen2.5-Coder FIM special tokens.</summary>
public sealed class QwenFimPromptBuilder : IFimPromptBuilder
{
    public string ModelFamilyId => "qwen2.5-coder";

    public IReadOnlyList<string> StopSequences { get; } =
    [
        "<|endoftext|>",
        "<|fim_prefix|>",
        "<|fim_suffix|>",
        "<|fim_middle|>",
        "<|repo_name|>",
        "<|file_sep|>",
    ];

    public string Build(string prefix, string suffix) =>
        $"<|fim_prefix|>{prefix}<|fim_suffix|>{suffix}<|fim_middle|>";
}
