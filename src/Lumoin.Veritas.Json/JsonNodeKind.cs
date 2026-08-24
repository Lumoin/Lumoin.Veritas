namespace Lumoin.Veritas.Json;

/// <summary>
/// Categorises a <see cref="JsonNode"/> by the kind of JSON value it represents.
/// </summary>
/// <remarks>
/// <para>
/// The seven kinds correspond to the JSON value types defined in RFC 8259: object, array,
/// string, number, the two boolean literals, and the null literal. JSON-LD processing
/// dispatches on this discriminator throughout the expansion and context-processing
/// algorithms.
/// </para>
/// <para>
/// The two boolean literals are distinguished from each other so that callers can
/// pattern-match without a separate boolean-value retrieval. This mirrors the structure
/// of the JSON token grammar, where <c>true</c> and <c>false</c> are distinct tokens.
/// </para>
/// </remarks>
public enum JsonNodeKind
{
    /// <summary>The JSON null literal.</summary>
    Null,

    /// <summary>A JSON string value.</summary>
    String,

    /// <summary>A JSON number value, in any of the lexical forms permitted by RFC 8259.</summary>
    Number,

    /// <summary>The JSON boolean literal <c>true</c>.</summary>
    True,

    /// <summary>The JSON boolean literal <c>false</c>.</summary>
    False,

    /// <summary>A JSON object value.</summary>
    Object,

    /// <summary>A JSON array value.</summary>
    Array
}
