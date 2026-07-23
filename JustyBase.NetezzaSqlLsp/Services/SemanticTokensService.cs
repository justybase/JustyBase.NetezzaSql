using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>Provides LSP semantic tokens for Netezza SQL syntax highlighting.</summary>
public static class SemanticTokensService
{
    public static readonly string[] TokenTypesLegend = NzSemanticTokenClassifier.TokenTypesLegend;
    public static readonly string[] TokenModifiersLegend = NzSemanticTokenClassifier.TokenModifiersLegend;

    /// <summary>Returns semantic tokens for the given SQL text, encoded in the LSP delta format.</summary>
    public static Protocol.SemanticTokensResult GetSemanticTokens(
        string sql,
        NzSemanticTokenClassifier classifier,
        string? documentUri = null)
    {
        if (string.IsNullOrEmpty(sql))
            return new Protocol.SemanticTokensResult(null);

        var spans = classifier.Classify(sql, documentUri);
        if (spans.Count == 0)
            return new Protocol.SemanticTokensResult(Array.Empty<uint>());

        var items = spans.Select(s => (
            s.Start,
            (int)s.Kind,
            MapModifiers(s.Modifiers),
            s.Length)).ToList();

        items.Sort((a, b) => a.Start.CompareTo(b.Start));

        var data = new List<uint>(items.Count * 5);
        var lineOffsets = new List<int> { 0 };
        for (int i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '\n')
                lineOffsets.Add(i + 1);
        }

        int prevLine = 0;
        int prevChar = 0;
        foreach (var (absolute, type, modifier, length) in items)
        {
            int line = FindLine(absolute, lineOffsets);
            int col = absolute - lineOffsets[line];

            int deltaLine = line - prevLine;
            int deltaChar = deltaLine == 0 ? col - prevChar : col;

            data.Add((uint)deltaLine);
            data.Add((uint)deltaChar);
            data.Add((uint)length);
            data.Add((uint)type);
            data.Add((uint)modifier);

            prevLine = line;
            prevChar = col;
        }

        return new Protocol.SemanticTokensResult(data.ToArray());
    }

    private static int MapModifiers(SemanticTokenModifiers modifiers)
    {
        int result = 0;
        if (modifiers.HasFlag(SemanticTokenModifiers.Deprecated))
            result |= 1 << 0;
        if (modifiers.HasFlag(SemanticTokenModifiers.Definition))
            result |= 1 << 1;
        if (modifiers.HasFlag(SemanticTokenModifiers.DefaultLibrary))
            result |= 1 << 2;
        return result;
    }

    private static int FindLine(int absolute, List<int> lineOffsets)
    {
        for (int i = lineOffsets.Count - 1; i >= 0; i--)
        {
            if (lineOffsets[i] <= absolute)
                return i;
        }
        return 0;
    }
}
