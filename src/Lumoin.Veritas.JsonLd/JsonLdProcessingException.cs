using System;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Indicates an error during JSON-LD document processing.
/// </summary>
/// <remarks>
/// Error codes correspond to those defined in the JSON-LD 1.1 Processing Algorithms
/// and API specification. The <see cref="ErrorCode"/> property carries the machine-readable
/// identifier; the exception message provides a human-readable description.
/// </remarks>
public sealed class JsonLdProcessingException: Exception
{
    /// <summary>
    /// Initializes a new <see cref="JsonLdProcessingException"/> with a default message.
    /// </summary>
    public JsonLdProcessingException()
        : base("A JSON-LD processing error occurred.")
    {
    }

    /// <summary>
    /// Initializes a new <see cref="JsonLdProcessingException"/> with the given message.
    /// </summary>
    /// <param name="message">A human-readable description of the error.</param>
    public JsonLdProcessingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="JsonLdProcessingException"/> with the given message
    /// and inner exception.
    /// </summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public JsonLdProcessingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="JsonLdProcessingException"/> with an error code and message.
    /// </summary>
    /// <param name="errorCode">The JSON-LD error code from the W3C specification.</param>
    /// <param name="message">A human-readable description of the error.</param>
    public JsonLdProcessingException(JsonLdErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new <see cref="JsonLdProcessingException"/> with an error code, message,
    /// and inner exception.
    /// </summary>
    /// <param name="errorCode">The JSON-LD error code from the W3C specification.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public JsonLdProcessingException(JsonLdErrorCode errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the W3C JSON-LD error code identifying the type of processing failure.
    /// </summary>
    public JsonLdErrorCode ErrorCode { get; }
}

/// <summary>
/// JSON-LD error codes from the W3C JSON-LD 1.1 Processing Algorithms specification.
/// </summary>
public enum JsonLdErrorCode
{
    /// <summary>A value of <c>@base</c> in a context was not a valid IRI or null.</summary>
    InvalidBaseIri,

    /// <summary>A context entry value was not a valid IRI, compact IRI, term, keyword, null, or an array of these.</summary>
    InvalidContextEntry,

    /// <summary>A context was invalid or could not be loaded.</summary>
    InvalidContextNullification,

    /// <summary>The <c>@direction</c> value was not a valid direction string.</summary>
    InvalidBaseDirection,

    /// <summary>A <c>@container</c> value was not a recognized container type.</summary>
    InvalidContainerMapping,

    /// <summary>The <c>@id</c> value in a term definition was not a valid IRI.</summary>
    InvalidIriMapping,

    /// <summary>A <c>@language</c> value was not a valid BCP47 language tag.</summary>
    InvalidLanguageMapping,

    /// <summary>A <c>@nest</c> value was not a valid term or keyword.</summary>
    InvalidNestValue,

    /// <summary>A <c>@prefix</c> value was not a boolean.</summary>
    InvalidPrefixValue,

    /// <summary>A <c>@propagate</c> value was not a boolean.</summary>
    InvalidPropagateValue,

    /// <summary>A <c>@protected</c> value was not a boolean.</summary>
    InvalidProtectedValue,

    /// <summary>A <c>@reverse</c> property was not a valid IRI.</summary>
    InvalidReverseProperty,

    /// <summary>A <c>@type</c> value in a term definition was not a valid IRI or keyword.</summary>
    InvalidTypeMappingTermDefinition,

    /// <summary>A <c>@value</c> object had an invalid combination of properties.</summary>
    InvalidValueObject,

    /// <summary>A context value was not a valid string, object, array, or null.</summary>
    InvalidLocalContext,

    /// <summary>A remote context could not be loaded.</summary>
    LoadingRemoteContextFailed,

    /// <summary>A context contained a recursive reference that could not be resolved.</summary>
    ContextOverflow,

    /// <summary>A keyword was used where a term was expected.</summary>
    KeywordRedefinition,

    /// <summary>A protected term definition was overridden in an incompatible way.</summary>
    ProtectedTermRedefinition,

    /// <summary>A value in the document could not be processed under the active context.</summary>
    InvalidTermDefinition,

    /// <summary>Processing encountered a cyclic IRI mapping.</summary>
    CyclicIriMapping,
}