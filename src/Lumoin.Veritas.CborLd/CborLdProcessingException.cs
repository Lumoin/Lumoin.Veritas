using System;
using System.Diagnostics;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Thrown when CBOR-LD encoding or decoding cannot proceed: an unregistered
/// entry id, a malformed registry entry, an inner CBOR codec failure that
/// surfaces as a format error at the CBOR-LD layer, a value that does not
/// satisfy the active profile's deterministic requirements, or an
/// active-context-processing error surfaced from the format-agnostic
/// <see cref="LinkedDataProcessingException"/> core.
/// </summary>
/// <remarks>
/// Derives from <see cref="LinkedDataProcessingException"/> so the
/// shared active-context algorithms in
/// <c>Lumoin.Veritas.LinkedData.ContextProcessing</c> can surface errors
/// through a common base type; the spec-defined error code is preserved
/// via the inherited <see cref="LinkedDataProcessingException.ErrorCode"/>
/// property. No CBOR-LD-specific enum is introduced — W3C CBOR-LD 1.0
/// defers error semantics to JSON-LD 1.1, so the spec error strings are
/// sufficient.
/// </remarks>
[DebuggerDisplay("{ErrorCode,nq}: {Message,nq}")]
public sealed class CborLdProcessingException: LinkedDataProcessingException
{
    /// <summary>Initialises a new instance with a default message.</summary>
    public CborLdProcessingException()
        : base("A CBOR-LD processing error occurred.")
    {
    }

    /// <summary>Initialises a new instance with the supplied message.</summary>
    /// <param name="message">A description of the failure.</param>
    public CborLdProcessingException(string message): base(message)
    {
    }

    /// <summary>Initialises a new instance with the supplied message and inner exception.</summary>
    /// <param name="message">A description of the failure.</param>
    /// <param name="innerException">The exception that caused the failure.</param>
    public CborLdProcessingException(string message, Exception innerException): base(message, innerException)
    {
    }

    /// <summary>Initialises a new instance with the supplied spec error code and message.</summary>
    /// <param name="errorCode">The spec-defined error name.</param>
    /// <param name="message">A description of the failure.</param>
    public CborLdProcessingException(string errorCode, string message)
        : base(errorCode, message)
    {
    }

    /// <summary>Initialises a new instance with the supplied error code, message, and inner exception.</summary>
    /// <param name="errorCode">The spec-defined error name.</param>
    /// <param name="message">A description of the failure.</param>
    /// <param name="innerException">The exception that caused the failure.</param>
    public CborLdProcessingException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException)
    {
    }

    /// <summary>
    /// Wraps an underlying <see cref="LinkedDataProcessingException"/> raised
    /// from the shared active-context core, preserving the spec error code.
    /// </summary>
    /// <param name="inner">The underlying exception to wrap.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public CborLdProcessingException(LinkedDataProcessingException inner)
        : base(inner is null ? string.Empty : inner.ErrorCode, inner is null ? string.Empty : inner.Message, inner!)
    {
        ArgumentNullException.ThrowIfNull(inner);
    }
}
