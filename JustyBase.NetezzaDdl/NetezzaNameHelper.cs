namespace JustyBase.NetezzaDdl;

public static class NetezzaNameHelper
{
    public static bool PreferUpperCase { get; set; } = true;

    public static (string Database, string Schema, string Table) GetCleanedNames(
        string database,
        string schema,
        string tableName)
    {
        return (
            QuoteNameIfNeeded(database),
            QuoteNameIfNeeded(schema),
            QuoteNameIfNeeded(tableName));
    }

    public static string QuoteNameIfNeeded(string word)
    {
        if (string.IsNullOrEmpty(word))
            return word;

        if (!IsGoodName(word))
            return $"\"{word.Replace("\"", "\"\"")}\"";

        return word;
    }

    public static string UnquoteName(string word)
    {
        if (string.IsNullOrEmpty(word))
            return word;

        if (word.StartsWith('"') && word.EndsWith('"') && word.Length >= 2)
            return word[1..^1].Replace("\"\"", "\"");

        return word;
    }

    public static string RandomTempName(string prefix = "TMP_")
    {
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return prefix + new string(Enumerable.Repeat(letters, 10)
            .Select(s => s[Random.Shared.Next(s.Length)])
            .ToArray());
    }

    public static string? EscapeComment(string? comment)
    {
        if (comment is null)
            return null;

        return comment.Contains('\'') ? comment.Replace("'", "''") : comment;
    }

    /// <summary>Escapes text embedded in a single-quoted SQL literal.</summary>
    public static string EscapeLiteral(string value) => value.Replace("'", "''");

    /// <summary>Reads the connection fields used by the reference DDL helpers.</summary>
    public static NetezzaConnectionSettings ParseConnectionString(string? connectionString)
    {
        string? host = null, database = null, user = null, password = null;
        int? port = null;
        if (string.IsNullOrWhiteSpace(connectionString))
            return new(null, null, null, null, null);

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            var key = part[..separator].Trim().ToUpperInvariant();
            var value = part[(separator + 1)..].Trim();
            switch (key)
            {
                case "SERVER":
                case "HOST": host = value; break;
                case "PORT": if (int.TryParse(value, out var parsedPort)) port = parsedPort; break;
                case "DATABASE": database = value; break;
                case "UID":
                case "USER": user = value; break;
                case "PWD":
                case "PASSWORD": password = value; break;
            }
        }

        return new(host, port, database, user, password);
    }

    /// <summary>Adds Netezza's ANY length to character procedure return types.</summary>
    public static string FixProcedureReturnType(string? value)
    {
        var type = value?.Trim() ?? string.Empty;
        if (type.Length == 0 || type.Contains('(')) return type;
        return type.ToUpperInvariant() switch
        {
            "CHARACTER VARYING" => "CHARACTER VARYING(ANY)",
            "NATIONAL CHARACTER VARYING" => "NATIONAL CHARACTER VARYING(ANY)",
            "NATIONAL CHARACTER" => "NATIONAL CHARACTER(ANY)",
            "CHARACTER" => "CHARACTER(ANY)",
            _ => type
        };
    }

    private static bool IsGoodName(string word)
    {
        if (word.Length == 0 || char.IsDigit(word[0]))
            return false;

        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            if (char.IsLower(c) || !char.IsLetter(c) && !char.IsDigit(c) && c != '_')
                return false;
        }

        return true;
    }
}

public sealed record NetezzaConnectionSettings(
    string? Host,
    int? Port,
    string? Database,
    string? User,
    string? Password);
