namespace Lumoin.Veritas.Json;

/// <summary>
/// Returns the decoded .NET string value of a node whose kind is
/// <see cref="JsonNodeKind.String"/>.
/// </summary>
/// <remarks>
/// JSON-LD processing decodes string values to .NET <see cref="string"/> because
/// term names, language tags, and IRI lexical forms participate in dictionary
/// lookups keyed on .NET strings. The byte-level UTF-8 form is recoverable
/// through other paths when zero-copy semantics are required.
/// </remarks>
/// <param name="handle">
/// The handle of a node whose kind is <see cref="JsonNodeKind.String"/>.
/// </param>
/// <returns>The decoded string value.</returns>
public delegate string GetStringValueDelegate(object handle);
