using System;
using System.Diagnostics;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// Represents a recoverable error encountered while processing a Linked
/// Data context, term definition, or active-context-related algorithm.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="ErrorCode"/> property holds the spec-defined error name
/// (e.g. <c>"invalid context entry"</c>, <c>"invalid term definition"</c>)
/// taken verbatim from the W3C JSON-LD 1.1 specification's error registry.
/// Format-specific consumers (such as JSON-LD or CBOR-LD) may wrap
/// instances of this exception with derived classes that expose typed
/// code views over the same underlying string code.
/// </para>
/// </remarks>
/// <seealso href="https://www.w3.org/TR/json-ld11-api/#jsonlderrorcode"/>
[DebuggerDisplay("{ErrorCode,nq}: {Message,nq}")]
public class LinkedDataProcessingException: Exception
{
    /// <summary>Initialises a new instance with default error code and message.</summary>
    public LinkedDataProcessingException()
        : base("A Linked Data processing error occurred.")
    {
        ErrorCode = string.Empty;
    }

    /// <summary>Initialises a new instance with the supplied message.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    public LinkedDataProcessingException(string message)
        : base(message)
    {
        ErrorCode = string.Empty;
    }

    /// <summary>Initialises a new instance with the supplied message and inner exception.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public LinkedDataProcessingException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = string.Empty;
    }

    /// <summary>Initialises a new instance with the supplied error code and message.</summary>
    /// <param name="errorCode">The spec-defined error name.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public LinkedDataProcessingException(string errorCode, string message)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(errorCode);
        ArgumentNullException.ThrowIfNull(message);
        ErrorCode = errorCode;
    }

    /// <summary>Initialises a new instance with the supplied error code, message, and inner exception.</summary>
    /// <param name="errorCode">The spec-defined error name.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public LinkedDataProcessingException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(errorCode);
        ArgumentNullException.ThrowIfNull(message);
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the spec-defined error code. Standard W3C JSON-LD 1.1 error
    /// names are used verbatim.
    /// </summary>
    public string ErrorCode { get; }
}
