using System.Net;
using System.Text;

namespace JustyBase.Ai.Chat;

/// <summary>
/// Keeps a markdown renderer append-only when the source grows, while allowing a
/// complete reset when markdown sanitization changes an earlier suffix.
/// </summary>
public sealed class ChatMarkdownStreamBuffer
{
    public string RenderedText { get; private set; } = string.Empty;

    public bool Apply(string source, Action clear, Action<string> append)
    {
        var safeText = ChatMarkdownSanitizer.Sanitize(source);
        if (string.Equals(safeText, RenderedText, StringComparison.Ordinal))
        {
            return false;
        }

        if (RenderedText.Length > 0 && safeText.StartsWith(RenderedText, StringComparison.Ordinal))
        {
            var suffix = safeText[RenderedText.Length..];
            if (suffix.Length > 0)
            {
                append(suffix);
            }
        }
        else
        {
            clear();
            if (safeText.Length > 0)
            {
                append(safeText);
            }
        }

        RenderedText = safeText;
        return true;
    }

    public void Reset() => RenderedText = string.Empty;
}

/// <summary>
/// Strips unsafe markdown (non-http(s) links, images, raw HTML angle brackets)
/// before the text reaches a markdown renderer. Host-UI agnostic.
/// </summary>
public static class ChatMarkdownSanitizer
{
    public static string Sanitize(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        var result = new StringBuilder(markdown.Length);
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var unsafeReferences = FindUnsafeReferences(lines);
        var inFence = false;
        char fenceCharacter = '\0';

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (TryGetFence(line, out var currentFenceCharacter))
            {
                if (!inFence)
                {
                    inFence = true;
                    fenceCharacter = currentFenceCharacter;
                }
                else if (fenceCharacter == currentFenceCharacter)
                {
                    inFence = false;
                    fenceCharacter = '\0';
                }

                result.Append(line);
            }
            else if (inFence)
            {
                result.Append(line);
            }
            else if (TryReadReferenceDefinition(line, out var referenceId, out var referenceDestination)
                && unsafeReferences.Contains(NormalizeReference(referenceId))
                && !IsAllowedHttpLink(referenceDestination))
            {
                // Do not leave an unsafe definition for Markdig to interpret as
                // an active reference. Keep a harmless, readable placeholder.
                var start = line.IndexOf('[');
                result.Append(line[..start])
                    .Append('[')
                    .Append(referenceId)
                    .Append("] [blocked link reference]");
            }
            else
            {
                AppendSanitizedInline(line, result, unsafeReferences);
            }

            if (i < lines.Length - 1)
            {
                result.Append('\n');
            }
        }

        return result.ToString();
    }

    public static bool IsAllowedHttpLink(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        var decodedHref = WebUtility.HtmlDecode(href).Trim();
        return Uri.TryCreate(decodedHref, UriKind.Absolute, out var uri)
            && uri.Host.Length > 0
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static void AppendSanitizedInline(
        string line,
        StringBuilder result,
        ISet<string>? unsafeReferences = null)
    {
        var inCodeSpan = false;
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];

            if (current == '`')
            {
                inCodeSpan = !inCodeSpan;
                result.Append(current);
                continue;
            }

            if (inCodeSpan)
            {
                result.Append(current);
                continue;
            }

            if (current == '!' && index + 1 < line.Length && line[index + 1] == '['
                && TryReadImage(line, index + 1, out var imageLabel, out var imageEnd))
            {
                AppendSanitizedInline(imageLabel, result, unsafeReferences);
                index = imageEnd;
                continue;
            }

            if (current == '['
                && TryReadLabelAndDestination(line, index, out var linkLabel, out var linkEnd, out var destination))
            {
                if (IsAllowedHttpLink(destination))
                {
                    result.Append('[');
                    AppendSanitizedInline(linkLabel, result, unsafeReferences);
                    result.Append("](").Append(destination).Append(')');
                }
                else
                {
                    AppendSanitizedInline(linkLabel, result, unsafeReferences);
                }

                index = linkEnd;
                continue;
            }

            if (current == '['
                && TryReadReference(line, index, out var referenceLabel, out var referenceEnd, out var referenceId))
            {
                if (unsafeReferences is not null
                    && unsafeReferences.Contains(NormalizeReference(referenceId)))
                {
                    AppendSanitizedInline(referenceLabel, result, unsafeReferences);
                }
                else
                {
                    result.Append('[');
                    AppendSanitizedInline(referenceLabel, result, unsafeReferences);
                    result.Append("][").Append(referenceId).Append(']');
                }

                index = referenceEnd;
                continue;
            }

            if (current == '<')
            {
                var closingBracket = line.IndexOf('>', index + 1);
                if (closingBracket > index)
                {
                    var candidate = line[(index + 1)..closingBracket].Trim();
                    if (IsAllowedHttpLink(candidate))
                    {
                        result.Append('[').Append(candidate).Append("](").Append(candidate).Append(')');
                    }
                    else
                    {
                        result.Append("&lt;");
                        result.Append(line, index + 1, closingBracket - index - 1);
                        result.Append("&gt;");
                    }

                    index = closingBracket;
                    continue;
                }

                result.Append("&lt;");
                continue;
            }

            if (current == '>' && IsBlockQuoteMarker(line, index))
            {
                result.Append(current);
                continue;
            }

            if (current == '>')
            {
                result.Append("&gt;");
                continue;
            }

            result.Append(current);
        }
    }

    private static bool IsBlockQuoteMarker(string line, int index)
    {
        if (index > 3)
        {
            return false;
        }

        for (var i = 0; i < index; i++)
        {
            if (line[i] != ' ')
            {
                return false;
            }
        }

        return index + 1 == line.Length || char.IsWhiteSpace(line[index + 1]);
    }

    private static bool TryReadLabelAndDestination(
        string line,
        int labelStart,
        out string label,
        out int end,
        out string destination)
    {
        label = string.Empty;
        destination = string.Empty;
        end = labelStart;

        var labelEnd = FindClosingBracket(line, labelStart);
        if (labelEnd < 0 || labelEnd + 1 >= line.Length || line[labelEnd + 1] != '(')
        {
            return false;
        }

        var destinationEnd = FindClosingParenthesis(line, labelEnd + 1);
        if (destinationEnd < 0)
        {
            return false;
        }

        label = line[(labelStart + 1)..labelEnd];
        destination = ExtractDestination(line[(labelEnd + 2)..destinationEnd]);
        end = destinationEnd;
        return true;
    }

    private static bool TryReadImage(string line, int labelStart, out string label, out int end)
    {
        label = string.Empty;
        end = labelStart;

        var labelEnd = FindClosingBracket(line, labelStart);
        if (labelEnd < 0)
        {
            return false;
        }

        // Inline, full and shortcut reference images are all rendered as
        // selectable alt text. No image destination is passed to the renderer.
        if (labelEnd + 1 < line.Length && line[labelEnd + 1] == '(')
        {
            var destinationEnd = FindClosingParenthesis(line, labelEnd + 1);
            if (destinationEnd < 0)
            {
                return false;
            }

            end = destinationEnd;
        }
        else if (labelEnd + 1 < line.Length && line[labelEnd + 1] == '[')
        {
            var referenceEnd = FindClosingBracket(line, labelEnd + 1);
            if (referenceEnd < 0)
            {
                return false;
            }

            end = referenceEnd;
        }
        else
        {
            end = labelEnd;
        }

        label = line[(labelStart + 1)..labelEnd];
        return true;
    }

    private static bool TryReadReference(
        string line,
        int labelStart,
        out string label,
        out int end,
        out string referenceId)
    {
        label = string.Empty;
        referenceId = string.Empty;
        end = labelStart;

        var labelEnd = FindClosingBracket(line, labelStart);
        if (labelEnd < 0 || labelEnd + 1 >= line.Length || line[labelEnd + 1] != '[')
        {
            return false;
        }

        var referenceEnd = FindClosingBracket(line, labelEnd + 1);
        if (referenceEnd < 0)
        {
            return false;
        }

        label = line[(labelStart + 1)..labelEnd];
        referenceId = line[(labelEnd + 2)..referenceEnd];
        if (referenceId.Length == 0)
        {
            referenceId = label;
        }

        end = referenceEnd;
        return true;
    }

    private static HashSet<string> FindUnsafeReferences(IReadOnlyList<string> lines)
    {
        var unsafeReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (TryReadReferenceDefinition(line, out var referenceId, out var destination)
                && !IsAllowedHttpLink(destination))
            {
                unsafeReferences.Add(NormalizeReference(referenceId));
            }
        }

        return unsafeReferences;
    }

    private static bool TryReadReferenceDefinition(string line, out string referenceId, out string destination)
    {
        referenceId = string.Empty;
        destination = string.Empty;
        var start = 0;
        while (start < line.Length && start < 3 && line[start] == ' ')
        {
            start++;
        }

        if (start >= line.Length || line[start] != '[')
        {
            return false;
        }

        var labelEnd = FindClosingBracket(line, start);
        if (labelEnd < 0 || labelEnd + 1 >= line.Length || line[labelEnd + 1] != ':')
        {
            return false;
        }

        referenceId = line[(start + 1)..labelEnd];
        destination = ExtractDestination(line[(labelEnd + 2)..]);
        return referenceId.Length > 0 && destination.Length > 0;
    }

    private static string NormalizeReference(string referenceId) => referenceId.Trim();

    private static int FindClosingParenthesis(string line, int openingParenthesis)
    {
        var nesting = 0;
        for (var i = openingParenthesis; i < line.Length; i++)
        {
            if (line[i] == '(')
            {
                nesting++;
            }
            else if (line[i] == ')' && --nesting == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindClosingBracket(string line, int openingBracket)
    {
        var nesting = 0;
        for (var i = openingBracket; i < line.Length; i++)
        {
            if (line[i] == '[')
            {
                nesting++;
            }
            else if (line[i] == ']' && --nesting == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static string ExtractDestination(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith('<') && trimmed.IndexOf('>') is var closing && closing > 0)
        {
            return trimmed[1..closing];
        }

        var whitespace = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return whitespace >= 0 ? trimmed[..whitespace] : trimmed;
    }

    private static bool TryGetFence(string line, out char fenceCharacter)
    {
        fenceCharacter = '\0';
        var index = 0;
        while (index < line.Length && index < 3 && line[index] == ' ')
        {
            index++;
        }

        if (index + 2 >= line.Length || (line[index] != '`' && line[index] != '~'))
        {
            return false;
        }

        var character = line[index];
        if (line[index + 1] != character || line[index + 2] != character)
        {
            return false;
        }

        fenceCharacter = character;
        return true;
    }
}
