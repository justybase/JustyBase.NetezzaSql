using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;
using LspPosition = JustyBase.NetezzaSqlLsp.Protocol.Position;
using LspRange = JustyBase.NetezzaSqlLsp.Protocol.Range;

namespace JustyBase.NetezzaSqlLsp.Services;

internal static class LspTextUtilities
{
    public static int[] ComputeLineStarts(string text)
    {
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                lineStarts.Add(i + 1);
        }
        return lineStarts.ToArray();
    }

    public static int PositionToOffset(string text, int line, int character)
    {
        if (line <= 0)
            return Math.Min(character, text.Length);

        int currentLine = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (currentLine == line)
                return Math.Min(i + character, text.Length);

            if (text[i] == '\n')
                currentLine++;
        }

        return text.Length;
    }

    public static LspPosition OffsetToPosition(int absolute, int[] lineStarts)
    {
        if (absolute <= 0)
            return new LspPosition(0, 0);

        int line = 0;
        for (int i = 1; i < lineStarts.Length; i++)
        {
            if (lineStarts[i] > absolute)
            {
                line = i - 1;
                return new LspPosition(line, absolute - lineStarts[line]);
            }
        }

        line = lineStarts.Length - 1;
        return new LspPosition(line, absolute - lineStarts[line]);
    }

    public static LspRange ToRange(int startAbsolute, int endAbsolute, int[] lineStarts)
    {
        if (endAbsolute < startAbsolute)
            endAbsolute = startAbsolute;

        return new LspRange(
            OffsetToPosition(startAbsolute, lineStarts),
            OffsetToPosition(endAbsolute, lineStarts));
    }

    public static LspRange ToRange(Token<NzToken> token, int[] lineStarts)
    {
        var start = token.Span.Position.Absolute;
        return ToRange(start, start + token.Span.Length, lineStarts);
    }

    public static Location ToLocation(string uri, int startAbsolute, int endAbsolute, int[] lineStarts)
    {
        return new Location(uri, ToRange(startAbsolute, endAbsolute, lineStarts));
    }

    public static string StripQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }

    public static bool IsIdentifierToken(Token<NzToken> token) =>
        token.Kind is NzToken.Identifier or NzToken.QuotedIdentifier;

    public static string NormalizedTokenText(Token<NzToken> token) =>
        StripQuotes(token.ToStringValue());
}
