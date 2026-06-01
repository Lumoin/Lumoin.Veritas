using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.JsonLd.Document;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Canonical JSON-LD remote-context parser. Composes a
/// <see cref="JsonLdParserDelegate"/> (supplied by the application) with
/// a walker that flattens the parsed <see cref="JsonLdNode"/> tree into
/// the format-neutral <see cref="IReadOnlyDictionary{TKey, TValue}"/>
/// shape that
/// <c>Lumoin.Veritas.LinkedData.ContextProcessing.ProcessRemoteContextAsync</c>
/// consumes. Both JSON-LD and CBOR-LD pipelines wire the same parser
/// because remote contexts on the web are JSON-LD documents regardless
/// of the local document's format.
/// </summary>
public static class JsonLdRemoteContextParsing
{
    /// <summary>
    /// Returns a <see cref="ParseRemoteContextDelegate"/> backed by the
    /// supplied <paramref name="jsonParser"/>. The returned delegate
    /// parses the fetched UTF-8 JSON bytes via
    /// <paramref name="jsonParser"/>, locates the <c>@context</c>
    /// property of the resulting root object (per W3C JSON-LD 1.1 §4.1
    /// remote-context loading rule), and walks the inline-context
    /// tree into a <see cref="Dictionary{TKey, TValue}"/> whose values
    /// are nested dictionaries, lists, primitives, or
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="jsonParser">The application's JSON parser implementation.</param>
    /// <returns>A <see cref="ParseRemoteContextDelegate"/> ready for wiring.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jsonParser"/> is <see langword="null"/>.</exception>
    public static ParseRemoteContextDelegate Create(JsonLdParserDelegate jsonParser)
    {
        ArgumentNullException.ThrowIfNull(jsonParser);
        return (resource, cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(resource);
            cancellationToken.ThrowIfCancellationRequested();

            //The resource's bytes carry a JSON document; convert to
            //Utf8String for the parser. Use the no-precomputed-hash form
            //since these bytes are consumed once and not used as a key.
            Utf8String utf8 = Utf8String.WithoutPrecomputedHash(resource.Bytes);
            JsonLdNode root = jsonParser(utf8);

            //A remote context document must be a single map carrying @context.
            if(root.Kind != JsonLdNodeKind.Object)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidRemoteContext,
                    string.Create(CultureInfo.InvariantCulture, $"Remote context document at '{resource.FinalUrl}' must be a JSON object."));
            }

            if(!root.TryGetProperty("@context", out JsonLdNode contextNode))
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidRemoteContext,
                    string.Create(CultureInfo.InvariantCulture, $"Remote context document at '{resource.FinalUrl}' has no '@context' property."));
            }

            //Flatten the context node into the dict-of-object shape. The
            //top-level result is expected to be an inline context object;
            //array-shaped @context values are wrapped as a single
            //"@context" array under one synthetic key for consistency,
            //though typical W3C contexts are objects at the top level.
            object? flattened = FlattenNode(contextNode);
            if(flattened is IReadOnlyDictionary<string, object?> dict)
            {
                return ValueTask.FromResult(dict);
            }
            //Array-shaped @context at the top level is presented as a
            //single-key dict whose value is the array; the calling
            //algorithm handles that path.
            Dictionary<string, object?> wrapped = new()
            {
                ["@context"] = flattened
            };
            return ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(wrapped);
        };
    }

    /// <summary>
    /// Adapts a legacy <see cref="ContextResolverDelegate"/> into a
    /// <see cref="FetchRemoteResourceDelegate"/>. The bytes from the
    /// resolver become the <see cref="RemoteResource.Bytes"/>; the
    /// resource's content type defaults to <c>application/ld+json</c>.
    /// </summary>
    /// <param name="resolver">The legacy resolver delegate.</param>
    /// <returns>A fetcher delegate that wraps <paramref name="resolver"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/> is <see langword="null"/>.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public static FetchRemoteResourceDelegate AdaptResolverToFetcher(ContextResolverDelegate resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return async (contextUrl, baseUrl, cancellationToken) =>
        {
            _ = baseUrl;
            Utf8String? resolved = await resolver(new Uri(contextUrl), cancellationToken).ConfigureAwait(false);
            if(resolved is null)
            {
                throw new LinkedDataProcessingException(
                    "loading remote context failed",
                    string.Create(CultureInfo.InvariantCulture, $"Failed to load remote context from '{contextUrl}'."));
            }
            return new RemoteResource
            {
                Bytes = resolved.Value.Memory,
                ContentType = "application/ld+json",
                FinalUrl = contextUrl
            };
        };
    }

    private static object? FlattenNode(JsonLdNode node)
    {
        switch(node.Kind)
        {
            case JsonLdNodeKind.Null:
            {
                return null;
            }
            case JsonLdNodeKind.True:
            {
                return true;
            }
            case JsonLdNodeKind.False:
            {
                return false;
            }
            case JsonLdNodeKind.String:
            {
                return node.GetString();
            }
            case JsonLdNodeKind.Number:
            {
                string raw = node.GetRawNumber();
                if(long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long asInt))
                {
                    return asInt;
                }
                if(double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double asDouble))
                {
                    return asDouble;
                }
                return raw;
            }
            case JsonLdNodeKind.Array:
            {
                List<object?> result = [];
                foreach(JsonLdNode element in node.EnumerateArray())
                {
                    result.Add(FlattenNode(element));
                }
                return result;
            }
            case JsonLdNodeKind.Object:
            {
                Dictionary<string, object?> result = [];
                foreach(KeyValuePair<string, JsonLdNode> entry in node.EnumerateObject())
                {
                    result[entry.Key] = FlattenNode(entry.Value);
                }
                return result;
            }
            default:
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"Unhandled JsonLdNodeKind {node.Kind}."));
            }
        }
    }
}
