using System;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Signals that a dataset-clause graph IRI names no graph the resolving <see cref="GraphSourceResolver"/>
/// can serve — for the engine's store-local default source (<see cref="DatasetGraphSource"/>), an IRI that
/// is not one of the dataset's own loaded named graphs. It is raised from the resolver's enumeration per
/// the seam's documented failure contract, and a protocol boundary translates it into its value-based
/// dataset-refusal answer; it deliberately derives from <see cref="Exception"/> directly, so no catch of
/// <see cref="ArgumentException"/> or <see cref="NotSupportedException"/> can swallow it by base type.
/// </summary>
public sealed class UnknownGraphSourceException : Exception
{
    /// <summary>Constructs the refusal with the default message.</summary>
    public UnknownGraphSourceException()
    {
    }

    /// <summary>Constructs the refusal.</summary>
    /// <param name="message">The refusal description, naming the IRI that resolved to no graph.</param>
    public UnknownGraphSourceException(string message)
        : base(message)
    {
    }

    /// <summary>Constructs the refusal over an underlying cause.</summary>
    /// <param name="message">The refusal description, naming the IRI that resolved to no graph.</param>
    /// <param name="innerException">The underlying failure.</param>
    public UnknownGraphSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
