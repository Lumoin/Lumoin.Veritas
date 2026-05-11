using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// Resolves a Linked Data context URI to its UTF-8-encoded document content.
/// </summary>
/// <remarks>
/// <para>
/// This delegate is the I/O boundary in any Linked Data processing pipeline:
/// JSON-LD, CBOR-LD, or another format that uses W3C JSON-LD-style active
/// contexts. All context fetching — whether from a local cache, an
/// embedded document set, or an HTTP request — goes through this function.
/// The caller controls caching, access policy, and network behaviour.
/// </para>
/// <para>
/// The resolved bytes encode whatever document format the consumer's
/// pipeline expects (a JSON document for JSON-LD, a CBOR-LD context bytes
/// for CBOR-LD, etc.). The bytes flow through to the consuming pipeline
/// without transcoding by this delegate.
/// </para>
/// <para>
/// Return <c>null</c> to indicate that the context could not be resolved.
/// The caller's consuming pipeline will surface this as a format-specific
/// loading-failure error.
/// </para>
/// <para>
/// The returned <see cref="Utf8String"/> is consumed once by the
/// pipeline's parser and is not used as a dictionary key. Callers should
/// construct it via
/// <see cref="Utf8String.WithoutPrecomputedHash(System.ReadOnlyMemory{byte})"/>
/// to skip the eager hash computation.
/// </para>
/// </remarks>
/// <param name="uri">The absolute URI of the context to resolve.</param>
/// <param name="cancellationToken">A token to cancel the operation.</param>
/// <returns>
/// The UTF-8 content of the resolved context document, or <c>null</c> if
/// the context could not be found.
/// </returns>
public delegate ValueTask<Utf8String?> ContextResolverDelegate(
    Uri uri,
    CancellationToken cancellationToken);
