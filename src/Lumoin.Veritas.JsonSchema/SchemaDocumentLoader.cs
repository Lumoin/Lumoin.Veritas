using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// Resolves an absolute schema URI (with no fragment) to its parsed document root, or returns
/// <see langword="false"/> when the URI cannot be retrieved. This is the seam through which
/// <c>$ref</c> reaches schemas outside the one being validated.
/// </summary>
/// <remarks>
/// The validator is synchronous on its hot path, so this delegate is synchronous; asynchronous
/// retrieval (HTTP, cache) belongs at the composition boundary, where documents are fetched and handed
/// to a loader that serves them from memory.
/// </remarks>
/// <param name="absoluteUri">The absolute document URI, without a fragment.</param>
/// <param name="document">On success, the parsed document root.</param>
/// <returns><see langword="true"/> when the document was resolved.</returns>
public delegate bool SchemaDocumentLoader(string absoluteUri, out JsonNode document);
