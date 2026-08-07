using System.Text.RegularExpressions;

namespace JustyBase.Ai.Services;

public static partial class LocalPatchApprovalDetector
{
    public static bool IsApprovalMessage(string? userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var trimmed = userMessage.Trim();
        if (NegativeApprovalRegex().IsMatch(trimmed))
        {
            return false;
        }

        return PositiveApprovalRegex().IsMatch(trimmed);
    }

    [GeneratedRegex(@"\b(nie\s+zatwierdz(\w*)?|deny|odrzu[cć](\w*)?|cancel|anuluj)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NegativeApprovalRegex();

    [GeneratedRegex(@"^(zatwierd[źz](am)?|potwierd[źz](am)?|approve(d)?|accept(ed)?|apply|ok|yes|tak)[\.\!\?]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PositiveApprovalRegex();
}
