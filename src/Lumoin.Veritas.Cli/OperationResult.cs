namespace Lumoin.Veritas.Cli;

/// <summary>
/// The value-based result of a <see cref="VeritasOperations"/> call: a success carrying rendered
/// <see cref="Output"/>, or a failure carrying an <see cref="ErrorMessage"/>. Expected conditions
/// (unreadable input, a malformed query, an unsupported query form) are reported this way rather
/// than thrown, so every surface decides how to present them.
/// </summary>
/// <param name="Succeeded">Whether the operation produced output.</param>
/// <param name="Output">The rendered output on success; empty on failure.</param>
/// <param name="ErrorMessage">The error description on failure; <see langword="null"/> on success.</param>
internal readonly record struct OperationResult(bool Succeeded, string Output, string? ErrorMessage)
{
    /// <summary>Creates a success result carrying <paramref name="output"/>.</summary>
    /// <param name="output">The rendered output.</param>
    /// <returns>A succeeded result.</returns>
    public static OperationResult Ok(string output)
    {
        return new OperationResult(true, output, null);
    }

    /// <summary>Creates a failure result carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error description.</param>
    /// <returns>A failed result.</returns>
    public static OperationResult Failed(string error)
    {
        return new OperationResult(false, string.Empty, error);
    }
}
