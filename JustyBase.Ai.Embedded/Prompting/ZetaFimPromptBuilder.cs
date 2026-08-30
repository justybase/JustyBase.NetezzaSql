namespace JustyBase.Ai.Embedded.Prompting;

/// <summary>Zed Zeta 2.1 FIM tokens (SPM order: suffix-prefix-middle).</summary>
public sealed class ZetaFimPromptBuilder : IFimPromptBuilder
{
    public string ModelFamilyId => "zeta";

    public IReadOnlyList<string> StopSequences { get; } =
    [
        "<[end_of_sentence]>",
        "<[fim-prefix]>",
        "<[fim-suffix]>",
        "<[fim-middle]>",
        "<|marker_1|>",
        "<|marker_2|>",
        "<|user_cursor|>",
        "<|endoftext|>",
    ];

    // Zeta expects SPM: <[fim-suffix]>suffix<[fim-prefix]>prefix<[fim-middle]>
    public string Build(string prefix, string suffix) =>
        $"<[fim-suffix]>{suffix}<[fim-prefix]>{prefix}<[fim-middle]>";
}
