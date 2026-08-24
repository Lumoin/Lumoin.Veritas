using Ptr = Lumoin.Veritas.JsonPointer.JsonPointer;

namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// A single assertion failure produced while validating an instance against a schema.
/// </summary>
/// <param name="InstanceLocation">The location of the failing value within the instance document, as an RFC 6901 JSON Pointer.</param>
/// <param name="KeywordLocation">The location of the failing keyword within the schema document, as an RFC 6901 JSON Pointer.</param>
/// <param name="Message">A short human-readable description of what was expected and what was seen.</param>
public sealed record ValidationError(
    Ptr InstanceLocation,
    Ptr KeywordLocation,
    string Message)
{
    /// <summary>Gets the absolute keyword location — the failing keyword's URI within its schema resource (<c>$id#/pointer</c>), or <see langword="null"/> when unavailable.</summary>
    public string? AbsoluteKeywordLocation { get; init; }
}
