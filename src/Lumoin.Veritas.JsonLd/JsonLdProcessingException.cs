using System;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Indicates an error during JSON-LD document processing. Derives from
/// <see cref="LinkedDataProcessingException"/> so format-agnostic
/// active-context algorithms in <c>Lumoin.Veritas.LinkedData</c> can
/// surface errors through a common base type; the JsonLd-typed enum
/// view is preserved via <see cref="ErrorCode"/>.
/// </summary>
/// <remarks>
/// Error codes correspond to those defined in the W3C JSON-LD 1.1
/// Processing Algorithms and API specification. The <see cref="ErrorCode"/>
/// property carries the machine-readable enum identifier; the inherited
/// <see cref="LinkedDataProcessingException.ErrorCode"/> property
/// (string-typed) carries the spec-defined error name.
/// </remarks>
public sealed class JsonLdProcessingException: LinkedDataProcessingException
{
    /// <summary>Initialises a new instance with a default message.</summary>
    public JsonLdProcessingException()
        : base("A JSON-LD processing error occurred.")
    {
        ErrorCode = JsonLdErrorCode.InvalidTermDefinition;
    }

    /// <summary>Initialises a new instance with the given message.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    public JsonLdProcessingException(string message)
        : base(message)
    {
        ErrorCode = JsonLdErrorCode.InvalidTermDefinition;
    }

    /// <summary>Initialises a new instance with the given message and inner exception.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public JsonLdProcessingException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = JsonLdErrorCode.InvalidTermDefinition;
    }

    /// <summary>Initialises a new instance with an error code and message.</summary>
    /// <param name="errorCode">The JSON-LD error code from the W3C specification.</param>
    /// <param name="message">A human-readable description of the error.</param>
    public JsonLdProcessingException(JsonLdErrorCode errorCode, string message)
        : base(JsonLdErrorCodeMapping.ToErrorString(errorCode), message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Initialises a new instance with an error code, message, and inner exception.</summary>
    /// <param name="errorCode">The JSON-LD error code from the W3C specification.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public JsonLdProcessingException(JsonLdErrorCode errorCode, string message, Exception innerException)
        : base(JsonLdErrorCodeMapping.ToErrorString(errorCode), message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Wraps a base <see cref="LinkedDataProcessingException"/> as a
    /// JSON-LD-specific exception. Used by the JsonLd shell to translate
    /// the format-agnostic exception type into the typed-enum surface the
    /// JsonLd public API uses.
    /// </summary>
    /// <param name="inner">The underlying exception to wrap.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public JsonLdProcessingException(LinkedDataProcessingException inner)
        : base(inner is null ? string.Empty : inner.ErrorCode, inner is null ? string.Empty : inner.Message, inner!)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ErrorCode = JsonLdErrorCodeMapping.FromErrorString(inner.ErrorCode);
    }

    /// <summary>
    /// Gets the W3C JSON-LD error code identifying the type of
    /// processing failure. The inherited
    /// <see cref="LinkedDataProcessingException.ErrorCode"/> property
    /// gives the same code as a spec-defined error name string.
    /// </summary>
    public new JsonLdErrorCode ErrorCode { get; }
}

/// <summary>
/// JSON-LD error codes from the W3C JSON-LD 1.1 Processing Algorithms specification.
/// </summary>
public enum JsonLdErrorCode
{
    /// <summary>A value of <c>@base</c> in a context was not a valid IRI or null.</summary>
    InvalidBaseIri,

    /// <summary>A value of <c>@vocab</c> in a context was not a string, valid IRI, or null.</summary>
    InvalidVocabMapping,

    /// <summary>A context-level <c>@language</c> value was not a string or null.</summary>
    InvalidDefaultLanguage,

    /// <summary>A context-level <c>@import</c> value was not a string.</summary>
    InvalidImportValue,

    /// <summary>A context <c>@version</c> value was not the number <c>1.1</c>.</summary>
    InvalidVersionValue,

    /// <summary>A term was defined as an alias of a keyword that may not be aliased (e.g. <c>@context</c>).</summary>
    InvalidKeywordAlias,

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

    /// <summary>An <c>@included</c> value was not a node object (or array of node objects).</summary>
    InvalidIncludedValue,

    /// <summary>A <c>@prefix</c> value was not a boolean.</summary>
    InvalidPrefixValue,

    /// <summary>A <c>@propagate</c> value was not a boolean.</summary>
    InvalidPropagateValue,

    /// <summary>A <c>@protected</c> value was not a boolean.</summary>
    InvalidProtectedValue,

    /// <summary>A <c>@reverse</c> property was not a valid IRI.</summary>
    InvalidReverseProperty,

    /// <summary>A node object's <c>@id</c> value was not a string.</summary>
    InvalidIdValue,

    /// <summary>A node object's <c>@type</c> value was not a string or array of strings.</summary>
    InvalidTypeValue,

    /// <summary>A node object's <c>@reverse</c> value was not a map.</summary>
    InvalidReverseValue,

    /// <summary>An <c>@reverse</c> map contained a key that expands to a keyword.</summary>
    InvalidReversePropertyMap,

    /// <summary>A reverse property's value was a value object or list object, which a reverse property may not take.</summary>
    InvalidReversePropertyValue,

    /// <summary>A language-map value was not a string (or array of strings / null).</summary>
    InvalidLanguageMapValue,

    /// <summary>A <c>@list</c> or <c>@set</c> object carried a disallowed sibling key.</summary>
    InvalidSetOrListObject,

    /// <summary>Two distinct keys in a node object expanded to the same keyword.</summary>
    CollidingKeywords,

    /// <summary>A <c>@type</c> value in a term definition was not a valid IRI or keyword.</summary>
    InvalidTypeMappingTermDefinition,

    /// <summary>A <c>@value</c> object had an invalid combination of properties.</summary>
    InvalidValueObject,

    /// <summary>A context value was not a valid string, object, array, or null.</summary>
    InvalidLocalContext,

    /// <summary>A remote context could not be loaded.</summary>
    LoadingRemoteContextFailed,

    /// <summary>A remote context document was not a map with a single <c>@context</c>, or <c>@import</c> referenced more than one context.</summary>
    InvalidRemoteContext,

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
