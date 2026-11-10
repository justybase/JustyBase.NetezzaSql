namespace JustyBase.NetezzaSqlParser.Parser;

/// <summary>
/// Raised when SQL nesting is deeper than the parser accepts. A recursive-descent parser that keeps
/// going would eventually overflow the stack, and a stack overflow cannot be caught or reported; this
/// exception turns the same input into an ordinary, readable parse failure.
/// </summary>
public sealed class NzParserDepthException : Exception
{
    public NzParserDepthException(string what, int depth, int limit)
        : base($"{what} is nested deeper than {limit} levels. Simplify the statement or split it into smaller parts.")
    {
        What = what;
        Depth = depth;
        Limit = limit;
    }

    /// <summary>Which grammar area hit the limit (an expression, a SELECT, a WITH clause...).</summary>
    public string What { get; }

    /// <summary>The depth that was requested.</summary>
    public int Depth { get; }

    /// <summary>The configured maximum.</summary>
    public int Limit { get; }
}
