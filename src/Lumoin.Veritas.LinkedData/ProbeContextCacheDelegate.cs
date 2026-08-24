using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// Optional cache-probe step for the remote-context retrieval pipeline.
/// Implementations consult whatever caching layer they wire — memory,
/// distributed, per-request, etc. — and return the cached
/// <see cref="RemoteResource"/> when present. Returning <see langword="null"/>
/// signals a cache miss; the caller falls through to
/// <see cref="FetchRemoteResourceDelegate"/>.
/// </summary>
/// <remarks>
/// <para>
/// The cache shape is intentionally minimal: the library doesn't dictate
/// freshness semantics, storage backend, or invalidation policy. The
/// application owns those decisions and exposes only the probe operation.
/// </para>
/// <para>
/// Implementations may throw transport-layer exceptions; the caller's
/// pipeline surfaces these as loading failures, identical to the
/// behaviour when a fetch fails.
/// </para>
/// </remarks>
/// <param name="contextUrl">The URL whose cached resource (if any) to probe.</param>
/// <param name="cancellationToken">A token to cancel the probe.</param>
/// <returns>The cached resource, or <see langword="null"/> on cache miss.</returns>
[SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
    Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
public delegate ValueTask<RemoteResource?> ProbeContextCacheDelegate(
    string contextUrl,
    CancellationToken cancellationToken);
