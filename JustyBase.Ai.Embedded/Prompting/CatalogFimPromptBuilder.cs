using JustyBase.Ai.Embedded.Prompting;
using JustyBase.Ai.Embedded.Download;

namespace JustyBase.Ai.Embedded.Prompting;

/// <summary>Routes FIM formatting to the syntax required by the selected model family.</summary>
public sealed class CatalogFimPromptBuilder : IFimPromptBuilder
{
    private readonly IModelCatalog _catalog;
    private readonly Func<string?> _selectedModelId;

    public CatalogFimPromptBuilder(IModelCatalog catalog, Func<string?> selectedModelId)
    {
        _catalog = catalog;
        _selectedModelId = selectedModelId;
    }

    private IFimPromptBuilder Current => _catalog.Resolve(_selectedModelId()).Family switch
    {
        "CodeGemma" => new CodeGemmaFimPromptBuilder(),
        "StarCoder2" => new StarCoderFimPromptBuilder(),
        "Codestral" => new CodestralFimPromptBuilder(),
        _ => new QwenFimPromptBuilder(),
    };

    public string ModelFamilyId => Current.ModelFamilyId;
    public IReadOnlyList<string> StopSequences => Current.StopSequences;
    public string Build(string prefix, string suffix) => Current.Build(prefix, suffix);
}
