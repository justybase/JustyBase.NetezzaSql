using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.NetezzaSqlParser.Authoring;

public static class NzSignatureHelpService
{
    public static SqlSignatureHelpInfo? GetSignatureHelp(
        string text,
        int offset,
        DocumentParsingCoordinator? parsingCoordinator = null,
        string? documentUri = null,
        ISqlAuthoringCatalog? catalog = null,
        SqlDialect dialect = SqlDialect.Netezza)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        parsingCoordinator?.GetOrCreate(documentUri ?? "default", dialect).Parse(text);
        offset = Math.Clamp(offset, 0, text.Length);
        catalog ??= NetezzaSqlAuthoringCatalog.Instance;

        try
        {
            var tokens = DialectRuntime.Tokenize(text, dialect).ToArray();
            if (tokens.Length == 0)
                return null;

            // Keep each open parenthesis frame: commas belong only to their
            // immediate frame, and closing a nested call restores its parent.
            var frames = new List<(string? Function, int Parameter)>();
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (token.Span.Position.Absolute >= offset) break;
                if (token.Kind == NzToken.LParen)
                {
                    var name = i > 0 && IsFunctionNameToken(tokens[i - 1].Kind)
                        ? ShortFunctionName(tokens[i - 1].ToStringValue()) : null;
                    frames.Add((name, 0));
                }
                else if (token.Kind == NzToken.RParen && frames.Count > 0)
                    frames.RemoveAt(frames.Count - 1);
                else if (token.Kind == NzToken.Comma && frames.Count > 0)
                {
                    var frame = frames[^1];
                    frames[^1] = (frame.Function, frame.Parameter + 1);
                }
            }
            var current = frames.LastOrDefault(frame => frame.Function is not null);
            if (current.Function is null || !TryGetSignatures(current.Function, catalog, out var signatures))
                return null;
            var activeParameter = current.Parameter;

            int activeSignature = SelectSignature(signatures, activeParameter);
            var active = signatures[activeSignature];
            return new SqlSignatureHelpInfo(
                signatures,
                ActiveSignature: activeSignature,
                ActiveParameter: Math.Min(activeParameter, Math.Max(0, active.Parameters.Length - 1)));
        }
        catch
        {
            return null;
        }
    }

    public static bool TryGetSignature(string functionName, out SqlSignatureInfo signature)
    {
        return TryGetSignature(functionName, NetezzaSqlAuthoringCatalog.Instance, out signature);
    }

    public static bool TryGetSignature(string functionName, ISqlAuthoringCatalog catalog, out SqlSignatureInfo signature)
    {
        if (TryGetSignatures(functionName, catalog, out var signatures))
        {
            signature = signatures[0];
            return true;
        }

        signature = null!;
        return false;
    }

    public static bool TryGetSignatures(string functionName, out SqlSignatureInfo[] signatures)
    {
        return TryGetSignatures(functionName, NetezzaSqlAuthoringCatalog.Instance, out signatures);
    }

    public static bool TryGetSignatures(string functionName, ISqlAuthoringCatalog catalog, out SqlSignatureInfo[] signatures)
    {
        if (!catalog.TryGetFunction(functionName, out var function))
        {
            signatures = Array.Empty<SqlSignatureInfo>();
            return false;
        }

        signatures = function.Signatures
            .Select(s => new SqlSignatureInfo(s.Label, s.Documentation, s.Parameters.ToArray()))
            .ToArray();
        return signatures.Length > 0;
    }

    private static int SelectSignature(SqlSignatureInfo[] signatures, int activeParameter)
    {
        for (int i = 0; i < signatures.Length; i++)
        {
            var parameterCount = signatures[i].Parameters.Length;
            if (activeParameter < parameterCount)
                return i;
        }

        return signatures.Length - 1;
    }

    private static bool IsFunctionNameToken(NzToken kind) =>
        kind.IsIdentifierLike() || kind is NzToken.If
            or NzToken.OracleQualifiedFunction;

    private static string ShortFunctionName(string name)
    {
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }
}
