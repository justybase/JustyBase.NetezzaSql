using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.NetezzaSqlLsp;

/// <summary>
/// Parses LSP process startup arguments that select the default SQL dialect.
/// Supports <c>--dialect=oracle</c> and <c>--dialect oracle</c>.
/// </summary>
public static class LspDialectArgs
{
    public static SqlDialect Parse(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--dialect=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg["--dialect=".Length..];
                return value.Equals("oracle", StringComparison.OrdinalIgnoreCase)
                    ? SqlDialect.Oracle
                    : SqlDialect.Netezza;
            }

            if (arg.Equals("--dialect", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length &&
                    args[i + 1].Equals("oracle", StringComparison.OrdinalIgnoreCase))
                    return SqlDialect.Oracle;
                return SqlDialect.Netezza;
            }
        }

        return SqlDialect.Netezza;
    }
}
