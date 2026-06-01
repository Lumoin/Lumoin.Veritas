namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Two-way mapping between the typed <see cref="JsonLdErrorCode"/>
/// enum and the spec-defined error name strings used by the
/// format-agnostic <see cref="Lumoin.Veritas.LinkedData.LinkedDataProcessingException"/>.
/// </summary>
/// <remarks>
/// The strings on the right-hand side are taken verbatim from the
/// W3C JSON-LD 1.1 specification's error registry (and corresponding
/// JSON-LD 1.1 API specification). They are the canonical wire form
/// for context-processing errors and are shared by every format
/// that layers on JSON-LD's active-context algorithm.
/// </remarks>
/// <seealso href="https://www.w3.org/TR/json-ld11-api/#jsonlderrorcode"/>
internal static class JsonLdErrorCodeMapping
{
    /// <summary>Maps an enum value to its spec-defined error name string.</summary>
    public static string ToErrorString(JsonLdErrorCode code) => code switch
    {
        JsonLdErrorCode.InvalidBaseIri => "invalid base IRI",
        JsonLdErrorCode.InvalidContextEntry => "invalid context entry",
        JsonLdErrorCode.InvalidContextNullification => "invalid context nullification",
        JsonLdErrorCode.InvalidBaseDirection => "invalid base direction",
        JsonLdErrorCode.InvalidContainerMapping => "invalid container mapping",
        JsonLdErrorCode.InvalidIriMapping => "invalid IRI mapping",
        JsonLdErrorCode.InvalidLanguageMapping => "invalid language mapping",
        JsonLdErrorCode.InvalidNestValue => "invalid @nest value",
        JsonLdErrorCode.InvalidIncludedValue => "invalid @included value",
        JsonLdErrorCode.InvalidPrefixValue => "invalid @prefix value",
        JsonLdErrorCode.InvalidPropagateValue => "invalid @propagate value",
        JsonLdErrorCode.InvalidProtectedValue => "invalid @protected value",
        JsonLdErrorCode.InvalidReverseProperty => "invalid reverse property",
        JsonLdErrorCode.InvalidTypeMappingTermDefinition => "invalid type mapping",
        JsonLdErrorCode.InvalidValueObject => "invalid value object",
        JsonLdErrorCode.InvalidLocalContext => "invalid local context",
        JsonLdErrorCode.LoadingRemoteContextFailed => "loading remote context failed",
        JsonLdErrorCode.ContextOverflow => "context overflow",
        JsonLdErrorCode.KeywordRedefinition => "keyword redefinition",
        JsonLdErrorCode.ProtectedTermRedefinition => "protected term redefinition",
        JsonLdErrorCode.InvalidTermDefinition => "invalid term definition",
        JsonLdErrorCode.CyclicIriMapping => "cyclic IRI mapping",
        _ => "invalid term definition"
    };

    /// <summary>Maps a spec-defined error name string back to its enum value.</summary>
    public static JsonLdErrorCode FromErrorString(string errorCode) => errorCode switch
    {
        "invalid base IRI" => JsonLdErrorCode.InvalidBaseIri,
        "invalid context entry" => JsonLdErrorCode.InvalidContextEntry,
        "invalid context nullification" => JsonLdErrorCode.InvalidContextNullification,
        "invalid base direction" => JsonLdErrorCode.InvalidBaseDirection,
        "invalid container mapping" => JsonLdErrorCode.InvalidContainerMapping,
        "invalid IRI mapping" => JsonLdErrorCode.InvalidIriMapping,
        "invalid language mapping" => JsonLdErrorCode.InvalidLanguageMapping,
        "invalid @nest value" => JsonLdErrorCode.InvalidNestValue,
        "invalid @included value" => JsonLdErrorCode.InvalidIncludedValue,
        "invalid @prefix value" => JsonLdErrorCode.InvalidPrefixValue,
        "invalid @propagate value" => JsonLdErrorCode.InvalidPropagateValue,
        "invalid @protected value" => JsonLdErrorCode.InvalidProtectedValue,
        "invalid reverse property" => JsonLdErrorCode.InvalidReverseProperty,
        "invalid type mapping" => JsonLdErrorCode.InvalidTypeMappingTermDefinition,
        "invalid value object" => JsonLdErrorCode.InvalidValueObject,
        "invalid local context" => JsonLdErrorCode.InvalidLocalContext,
        "loading remote context failed" => JsonLdErrorCode.LoadingRemoteContextFailed,
        "context overflow" => JsonLdErrorCode.ContextOverflow,
        "keyword redefinition" => JsonLdErrorCode.KeywordRedefinition,
        "protected term redefinition" => JsonLdErrorCode.ProtectedTermRedefinition,
        "invalid term definition" => JsonLdErrorCode.InvalidTermDefinition,
        "cyclic IRI mapping" => JsonLdErrorCode.CyclicIriMapping,
        _ => JsonLdErrorCode.InvalidTermDefinition
    };
}
