using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlLsp.Protocol;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>Provides LSP signature help (parameter hints) for function calls.</summary>
public static class SignatureHelpService
{
    /// <summary>Returns signature help at the given position, or <see langword="null"/>.</summary>
    public static SignatureHelp? GetSignatureHelp(string text, int line, int character, SqlDialect dialect = SqlDialect.Netezza)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        int offset = LspTextUtilities.PositionToOffset(text, line, character);
        var catalog = DialectRuntime.AuthoringCatalogOrNull(dialect);
        var signatureHelp = NzSignatureHelpService.GetSignatureHelp(text, offset, catalog: catalog, dialect: dialect);
        if (signatureHelp is null)
            return null;

        return new SignatureHelp(
            signatureHelp.Signatures
                .Select(signature => new SignatureInformation(
                    signature.Label,
                    signature.Documentation,
                    signature.Parameters.Select(parameter => new ParameterInformation(parameter.Label, parameter.Documentation)).ToArray()))
                .ToArray(),
            signatureHelp.ActiveSignature,
            signatureHelp.ActiveParameter);
    }
}
