namespace Lumoin.Veritas.Json;

/// <summary>
/// Returns the raw lexical form of a node whose kind is <see cref="JsonNodeKind.Number"/>.
/// </summary>
/// <remarks>
/// <para>
/// The string returned is the JSON source text of the number, exactly as it
/// appears in the document — for example <c>"1"</c>, <c>"1.1"</c>, <c>"1e3"</c>,
/// or <c>"-0.5"</c>. The caller chooses how to interpret it (as <see cref="int"/>,
/// <see cref="double"/>, <see cref="decimal"/>, or in raw form for
/// type-preserving serialisation).
/// </para>
/// <para>
/// Returning the raw lexical form rather than a typed numeric value preserves
/// the original document precision. The JSON-LD specification distinguishes
/// integer and floating-point lexical forms when producing typed RDF literals,
/// and that distinction is only visible at the lexical level.
/// </para>
/// </remarks>
/// <param name="handle">
/// The handle of a node whose kind is <see cref="JsonNodeKind.Number"/>.
/// </param>
/// <returns>The raw JSON lexical form of the number.</returns>
public delegate string GetRawNumberDelegate(object handle);
