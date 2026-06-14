using System;

namespace Lumoin.Veritas.Jsonata;

/// <summary>
/// Indicates that a recognized JSONata construct is not implemented by this engine build.
/// </summary>
/// <remarks>
/// Distinct from <see cref="JsonataParseException"/>: the input is well-formed JSONata, but the
/// requested construct is outside the set this build evaluates. Callers that must tell an
/// unimplemented feature from malformed input can catch this type directly.
/// </remarks>
public sealed class JsonataUnsupportedFeatureException : Exception
{
    /// <summary>Initializes a new <see cref="JsonataUnsupportedFeatureException"/> with a default message.</summary>
    public JsonataUnsupportedFeatureException()
        : base("The JSONata construct is not supported by this engine build.")
    {
    }

    /// <summary>Initializes a new <see cref="JsonataUnsupportedFeatureException"/> with the given message.</summary>
    /// <param name="message">A description of the unsupported construct.</param>
    public JsonataUnsupportedFeatureException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new <see cref="JsonataUnsupportedFeatureException"/> with the given message and inner exception.</summary>
    /// <param name="message">A description of the unsupported construct.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public JsonataUnsupportedFeatureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
