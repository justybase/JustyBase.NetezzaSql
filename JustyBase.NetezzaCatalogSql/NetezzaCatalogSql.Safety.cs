namespace JustyBase.NetezzaCatalogSql;

public static partial class NetezzaCatalogSql
{
    private static string NormalizeDatabaseIdentifier(string value, string parameterName = "database")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.StartsWith("\"", StringComparison.Ordinal)
            ? value
            : value.ToUpperInvariant();

        if (normalized.StartsWith("\"", StringComparison.Ordinal))
        {
            if (normalized.Length < 2 || !normalized.EndsWith("\"", StringComparison.Ordinal))
                throw new ArgumentException("A quoted database identifier must have matching quotes.", parameterName);

            for (int i = 1; i < normalized.Length - 1; i++)
            {
                if (normalized[i] != '"')
                    continue;

                if (i + 1 < normalized.Length - 1 && normalized[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                throw new ArgumentException("A quoted database identifier contains an unescaped quote.", parameterName);
            }

            return normalized;
        }

        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (i == 0 ? !IsIdentifierStart(c) : !IsIdentifierPart(c))
                throw new ArgumentException(
                    $"'{value}' is not a valid unquoted database identifier.", parameterName);
        }

        return normalized;
    }

    private static string EscapeSqlLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static bool IsIdentifierStart(char c)
        => c == '_' || char.IsLetter(c);

    private static bool IsIdentifierPart(char c)
        => c == '_' || c == '$' || c == '#' || char.IsLetterOrDigit(c);
}
