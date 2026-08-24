using System.Collections.Generic;
using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// A JSON Schema 2020-12 output unit. The root unit of a <see cref="OutputFormat.Basic"/> result carries
/// <see cref="Errors"/> (on failure) or <see cref="Annotations"/> (on success); a child unit carries a
/// single <see cref="Error"/> or <see cref="Annotation"/> with its locations.
/// </summary>
/// <remarks>
/// Locations are RFC 6901 JSON Pointer strings; <see cref="AbsoluteKeywordLocation"/> is the keyword's
/// URI within its schema resource. The shape mirrors the standard output format so it can be serialized
/// to the canonical JSON.
/// </remarks>
public sealed record OutputUnit
{
    /// <summary>Gets whether this unit (or the overall result, for a root unit) is valid.</summary>
    public required bool Valid { get; init; }

    /// <summary>Gets the keyword location (a JSON Pointer into the schema), or <see langword="null"/> for a root unit.</summary>
    public string? KeywordLocation { get; init; }

    /// <summary>Gets the absolute keyword location (<c>$id#/pointer</c>), or <see langword="null"/>.</summary>
    public string? AbsoluteKeywordLocation { get; init; }

    /// <summary>Gets the instance location (a JSON Pointer into the instance), or <see langword="null"/> for a root unit.</summary>
    public string? InstanceLocation { get; init; }

    /// <summary>Gets the error message for an error unit, or <see langword="null"/>.</summary>
    public string? Error { get; init; }

    /// <summary>Gets the annotation value for an annotation unit, or <see langword="null"/>.</summary>
    public JsonNode? Annotation { get; init; }

    /// <summary>Gets the flat list of error units (a failed root unit), or <see langword="null"/>.</summary>
    public IReadOnlyList<OutputUnit>? Errors { get; init; }

    /// <summary>Gets the flat list of annotation units (a successful root unit), or <see langword="null"/>.</summary>
    public IReadOnlyList<OutputUnit>? Annotations { get; init; }
}
