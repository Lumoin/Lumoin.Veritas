using System;
using System.Runtime.Serialization;

namespace Lumoin.Veritas.Cid;

/// <summary>
/// Thrown when a string or byte sequence cannot be parsed as a valid DASL CID.
/// </summary>
/// <remarks>
/// The exception message identifies which validation rule was violated:
/// missing prefix, invalid base32 character, wrong length, unsupported
/// version, codec, hash type, or hash length. Callers that need to
/// distinguish failure modes programmatically should branch on
/// <see cref="Exception.Message"/> sparingly; the recommended approach is to
/// surface the message and let the operator read it.
/// </remarks>
[Serializable]
public sealed class CidParseException: FormatException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CidParseException"/> class.
    /// </summary>
    public CidParseException()
    {
    }

    /// <summary>
    /// Initializes a new instance with the supplied message.
    /// </summary>
    /// <param name="message">A description of the validation failure.</param>
    public CidParseException(string message): base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance with the supplied message and inner exception.
    /// </summary>
    /// <param name="message">A description of the validation failure.</param>
    /// <param name="innerException">The exception that caused the failure.</param>
    public CidParseException(string message, Exception innerException): base(message, innerException)
    {
    }
}
