namespace JustyBase.Ai.Embedded.Prompting;

/// <summary>
/// Builds a Fill-in-the-Middle prompt for a specific model family.
/// </summary>
public interface IFimPromptBuilder
{
    string ModelFamilyId { get; }
    string Build(string prefix, string suffix);
    IReadOnlyList<string> StopSequences { get; }
}
