using System;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata;

/// <summary>
/// Represents a JSONata runtime error raised during evaluation — for example a type error, or an
/// error signalled by the <c>$error</c> or <c>$assert</c> functions.
/// </summary>
/// <remarks>
/// The <see cref="Code"/> property carries the JSONata error code (a UTF-8 <see cref="Utf8String"/> from
/// <see cref="WellKnownJsonataErrors"/>) when one is defined, empty otherwise, and <see cref="Token"/> the
/// associated token when the error names one (<see langword="null"/> otherwise).
/// </remarks>
public sealed class JsonataErrorException : Exception
{
    /// <summary>Initializes a new <see cref="JsonataErrorException"/> with a default message.</summary>
    public JsonataErrorException()
        : base("A JSONata evaluation error occurred.")
    {
    }

    /// <summary>Initializes a new <see cref="JsonataErrorException"/> with the given message.</summary>
    /// <param name="message">A description of the error.</param>
    public JsonataErrorException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new <see cref="JsonataErrorException"/> with the given message and inner exception.</summary>
    /// <param name="message">A description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public JsonataErrorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new <see cref="JsonataErrorException"/> with the given error code, token, and message.</summary>
    /// <param name="code">The JSONata error code (a <see cref="WellKnownJsonataErrors"/> member), or the empty <see cref="Utf8String"/> when none is defined.</param>
    /// <param name="token">The token associated with the error, or <see langword="null"/> when none is named.</param>
    /// <param name="message">A description of the error.</param>
    public JsonataErrorException(Utf8String code, string? token, string message)
        : base(message)
    {
        Code = code;
        Token = token;
    }

    /// <summary>Gets the JSONata error code as a UTF-8 string, or the empty <see cref="Utf8String"/> when none is defined.</summary>
    public Utf8String Code { get; }

    /// <summary>Gets the token associated with the error, or <see langword="null"/> when none is named.</summary>
    public string? Token { get; }
}
