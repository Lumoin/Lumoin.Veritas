using System;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Thrown when CBOR-LD encoding or decoding cannot proceed: an unregistered
/// entry id, a malformed registry entry, an inner CBOR codec failure that
/// surfaces as a format error at the CBOR-LD layer, or a value that does
/// not satisfy the active profile's deterministic requirements.
/// </summary>
[Serializable]
public sealed class CborLdProcessingException: Exception
{
    /// <summary>Initialises a new instance of the <see cref="CborLdProcessingException"/> class.</summary>
    public CborLdProcessingException()
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
}
