namespace JustyBase.Netezza;

/// <summary>
/// Represents the outcome of an operation that can fail.
/// Alternative to throwing exceptions for expected failure modes.
/// </summary>
/// <typeparam name="T">The type of the successful result value.</typeparam>
public sealed record NetezzaResult<T>
{
    /// <summary>Whether the operation completed successfully.</summary>
    public bool Success { get; }

    /// <summary>The result value when <see cref="Success"/> is <see langword="true"/>.</summary>
    public T? Value { get; }

    /// <summary>An error message when <see cref="Success"/> is <see langword="false"/>.</summary>
    public string? Error { get; }

    private NetezzaResult(T value)
    {
        Success = true;
        Value = value;
    }

    private NetezzaResult(string error)
    {
        Success = false;
        Error = error;
    }

    /// <summary>Creates a successful result.</summary>
    public static NetezzaResult<T> Ok(T value) => new(value);

    /// <summary>Creates a failed result with the given error message.</summary>
    public static NetezzaResult<T> Fail(string error) => new(error);

    /// <summary>Returns the value on success; throws on failure.</summary>
    /// <exception cref="InvalidOperationException">The operation failed.</exception>
    public T GetValueOrThrow()
    {
        if (!Success)
            throw new InvalidOperationException($"Operation failed: {Error}");
        return Value!;
    }

    /// <summary>Returns the value on success; returns <paramref name="defaultValue"/> on failure.</summary>
    public T? GetValueOrDefault(T? defaultValue = default) => Success ? Value : defaultValue;
}
