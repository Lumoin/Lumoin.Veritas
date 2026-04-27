using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Resolves a JSON-LD context URI to its UTF-8 JSON document content.
/// </summary>
/// <remarks>
/// <para>
/// This delegate is the only I/O boundary in the JSON-LD processing pipeline.
/// All context fetching — whether from a local cache, an embedded document set,
/// or an HTTP request — goes through this function. The caller controls caching,
/// access policy, and network behaviour.
/// </para>
/// <para>
/// The resolved bytes should encode a JSON object with a top-level
/// <c>@context</c> property, as defined by the JSON-LD 1.1 specification.
/// JSON is UTF-8 by default per RFC 8259 section 8.1; the bytes flow through
/// the pipeline without transcoding.
/// </para>
/// <para>
/// Return <c>null</c> to indicate that the context could not be resolved.
/// The caller will receive a <see cref="JsonLdProcessingException"/> with a
/// loading failure error code.
/// </para>
/// <para>
/// The returned <see cref="Utf8String"/> is consumed once by the JSON parser
/// delegate and is not used as a dictionary key. Callers should construct it
/// via <see cref="Utf8String.WithoutPrecomputedHash(System.ReadOnlyMemory{byte})"/>
/// to skip the eager hash computation.
/// </para>
/// </remarks>
/// <param name="uri">The absolute URI of the context to resolve.</param>
/// <param name="cancellationToken">A token to cancel the operation.</param>
/// <returns>
/// The UTF-8 JSON content of the resolved context document,
/// or <c>null</c> if the context could not be found.
/// </returns>
public delegate ValueTask<Utf8String?> ContextResolverDelegate(
    Uri uri,
    CancellationToken cancellationToken);
