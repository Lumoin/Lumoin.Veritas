using System;
using System.Globalization;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Thrown when a CBOR wire form declares a size that exceeds the
/// corresponding cap on <see cref="CborSerializerOptions"/>. The exception
/// names the cap that fired and the value that exceeded it so callers can
/// diagnose precisely what to lift if the input is legitimate.
/// </summary>
public sealed class CborSizeLimitExceededException: InvalidOperationException
{
    /// <summary>Initialises a new <see cref="CborSizeLimitExceededException"/> with default values.</summary>
    public CborSizeLimitExceededException()
        : this(string.Empty, 0, 0)
    {
    }

    /// <summary>Initialises a new <see cref="CborSizeLimitExceededException"/> with a message.</summary>
    /// <param name="message">The exception message.</param>
    public CborSizeLimitExceededException(string message)
        : base(message)
    {
        CapName = string.Empty;
    }

    /// <summary>Initialises a new <see cref="CborSizeLimitExceededException"/> with a message and inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public CborSizeLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
        CapName = string.Empty;
    }

    /// <summary>Initialises a new <see cref="CborSizeLimitExceededException"/>.</summary>
    /// <param name="capName">The name of the limit that fired (e.g. <c>MaxArrayLength</c>).</param>
    /// <param name="declaredValue">The wire-declared value that exceeded the cap.</param>
    /// <param name="cap">The configured cap.</param>
    public CborSizeLimitExceededException(string capName, long declaredValue, long cap)
        : base(string.Create(CultureInfo.InvariantCulture, $"CBOR wire form exceeds {capName}: declared {declaredValue}, cap {cap}."))
    {
        CapName = capName;
        DeclaredValue = declaredValue;
        Cap = cap;
    }

    /// <summary>Gets the name of the cap that fired.</summary>
    public string CapName { get; }

    /// <summary>Gets the wire-declared value.</summary>
    public long DeclaredValue { get; }

    /// <summary>Gets the configured cap.</summary>
    public long Cap { get; }
}
