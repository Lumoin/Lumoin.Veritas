using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// Fetches a remote Linked Data context resource. Format-agnostic: this
/// delegate's responsibility is purely retrieval — returning the raw
/// bytes plus transport-level metadata. Parsing the bytes into a
/// processable shape is the separate <see cref="ParseRemoteContextDelegate"/>
/// step.
/// </summary>
/// <remarks>
/// <para>
/// The split decouples fetch policy (HTTP, file system, packaged
/// resource access, retries, timeouts, authentication) from parse
/// policy (JSON, CBOR, or future formats). Implementations are
/// expected to resolve <paramref name="contextUrl"/> against
/// <paramref name="baseUrl"/> before fetching if the URL is relative.
/// </para>
/// <para>
/// Implementations should throw a clear exception (a derivative of
/// <c>InvalidOperationException</c>, <c>System.Net.Http.HttpRequestException</c>,
/// <c>System.IO.IOException</c>, or similar transport-layer type) when
/// the resource cannot be retrieved; the caller will surface this as a
/// format-specific loading-failure error.
/// </para>
/// </remarks>
/// <param name="contextUrl">The URL of the context to retrieve. May be relative; the implementation resolves against <paramref name="baseUrl"/>.</param>
/// <param name="baseUrl">The base URL in effect at the caller's point, or <see langword="null"/> if no base is in scope.</param>
/// <param name="cancellationToken">A token to cancel the fetch.</param>
/// <returns>The fetched resource. The returned bytes are owned by the caller.</returns>
[SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
    Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
public delegate ValueTask<RemoteResource> FetchRemoteResourceDelegate(
    string contextUrl,
    string? baseUrl,
    CancellationToken cancellationToken);
