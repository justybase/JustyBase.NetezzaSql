using JustyBase.Ai.Embedded.Prompting;

namespace JustyBase.Ai.Embedded.Prompting;

/// <summary>DeepSeek-Coder FIM special tokens (alternative model family).</summary>
public sealed class DeepSeekFimPromptBuilder : IFimPromptBuilder
{
    public string ModelFamilyId => "deepseek-coder";

    public IReadOnlyList<string> StopSequences { get; } =
    [
        "<｜fim▁begin｜>",
        "<｜fim▁hole｜>",
        "<｜fim▁end｜>",
        "<|EOT|>",
        "<｜end▁of▁sentence｜>",
    ];

    public string Build(string prefix, string suffix) =>
        $"<｜fim▁begin｜>{prefix}<｜fim▁hole｜>{suffix}<｜fim▁end｜>";
}
