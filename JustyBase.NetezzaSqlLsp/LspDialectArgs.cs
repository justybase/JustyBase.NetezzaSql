using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.NetezzaSqlLsp;

/// <summary>
/// Parses LSP process startup arguments that select the default SQL dialect.
/// Supports <c>--dialect=oracle|db2|mssql|mysql|netezza</c> and spaced forms.
/// </summary>
public static class LspDialectArgs
{
    public static SqlDialect Parse(string[] args) =>
        DialectRuntime.ParseName(ExtractDialectValue(args));

    private static string? ExtractDialectValue(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--dialect=", StringComparison.OrdinalIgnoreCase))
                return arg["--dialect=".Length..];

            if (arg.Equals("--dialect", StringComparison.OrdinalIgnoreCase))
                return i + 1 < args.Length ? args[i + 1] : null;
        }

        return null;
    }
}
