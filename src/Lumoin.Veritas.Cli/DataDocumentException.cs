using System;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// Indicates that a data document could not be ingested — a missing file, an unsupported format, or a parse error.
/// The streaming ingest pipeline raises this while the engine is draining the quad stream, and
/// <see cref="VeritasOperations.OpenDatabaseAsync"/> turns it back into an operation error naming the failing
/// document rather than letting it throw out of the open.
/// </summary>
internal sealed class DataDocumentException: Exception
{
    /// <summary>Initializes a new <see cref="DataDocumentException"/> with a default message.</summary>
    public DataDocumentException()
        : base("A data document could not be ingested.")
    {
    }

    /// <summary>Initializes a new <see cref="DataDocumentException"/> with the given message.</summary>
    /// <param name="message">The operation-error message naming the failing document.</param>
    public DataDocumentException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new <see cref="DataDocumentException"/> with the given message and inner exception.</summary>
    /// <param name="message">The operation-error message naming the failing document.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public DataDocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
