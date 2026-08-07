using JustyBase.Ai.Embedded.Prompting;

namespace JustyBase.Ai.Embedded.Prompting;

public sealed class CodeGemmaFimPromptBuilder : IFimPromptBuilder
{
    public string ModelFamilyId => "CodeGemma";
    public IReadOnlyList<string> StopSequences { get; } = ["<|endoftext|>", "<|fim_prefix|>", "<|fim_suffix|>", "<|fim_middle|>"];
    public string Build(string prefix, string suffix) => $"<|fim_prefix|>{prefix}<|fim_suffix|>{suffix}<|fim_middle|>";
}

public sealed class StarCoderFimPromptBuilder : IFimPromptBuilder
{
    public string ModelFamilyId => "StarCoder2";
    public IReadOnlyList<string> StopSequences { get; } = ["<|endoftext|>", "<fim_prefix>", "<fim_suffix>", "<fim_middle>"];
    public string Build(string prefix, string suffix) => $"<fim_prefix>{prefix}<fim_suffix>{suffix}<fim_middle>";
}

public sealed class CodestralFimPromptBuilder : IFimPromptBuilder
{
    public string ModelFamilyId => "Codestral";
    public IReadOnlyList<string> StopSequences { get; } = ["</s>", "[PREFIX]", "[SUFFIX]"];
    public string Build(string prefix, string suffix) => $"[SUFFIX]{suffix}[PREFIX]{prefix}";
}
