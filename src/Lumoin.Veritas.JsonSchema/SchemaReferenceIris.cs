using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Iris;

namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// The JSON Schema layer's string face over the byte-native <see cref="IriResolver"/>:
/// schema documents carry their <c>$id</c>/<c>$ref</c>/<c>$schema</c> URIs as strings
/// over the JSON value model, so this seam encodes once per resolution and hands the
/// text back.
/// </summary>
internal static class SchemaReferenceIris
{
    /// <summary>Resolves a schema reference against a base URI per RFC 3986 §5.</summary>
    /// <param name="baseUri">The base URI in effect.</param>
    /// <param name="reference">The reference to resolve.</param>
    /// <returns>The resolved URI's text, or the reference unchanged when the base cannot resolve it.</returns>
    public static string Resolve(string baseUri, string reference)
    {
        IriBase parsedBase = IriResolver.ParseBase(Utf8Strings.From(baseUri));

        return IriResolver.ResolveIri(in parsedBase, Utf8Strings.From(reference)).ToString();
    }
}
