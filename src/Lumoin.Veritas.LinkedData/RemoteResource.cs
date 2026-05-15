using System;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// A fetched remote-context document carrying its raw bytes plus
/// transport-level metadata. Produced by <see cref="FetchRemoteResourceDelegate"/>
/// implementations and consumed by <see cref="ParseRemoteContextDelegate"/>
/// implementations. The split lets the library treat fetching as
/// format-agnostic (raw bytes + content type) and parsing as
/// format-specific (bytes → POCO shape).
/// </summary>
/// <seealso href="https://www.w3.org/TR/json-ld11-api/#context-processing-algorithm"/>
public sealed class RemoteResource
{
    /// <summary>
    /// Gets the raw bytes of the fetched resource. Not guaranteed to be
    /// UTF-8 — the parser consults <see cref="ContentType"/> to decide
    /// how to decode them.
    /// </summary>
    public required ReadOnlyMemory<byte> Bytes { get; init; }

    /// <summary>
    /// Gets the IANA media type of the fetched bytes (e.g.
    /// <c>"application/ld+json"</c>). Fetcher implementations
    /// populate this from the transport layer (HTTP <c>Content-Type</c>
    /// header, file extension, etc.) so parsers can dispatch.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets the resource's final URL after any transport-level redirects.
    /// Used by the context-processing algorithm as the base for resolving
    /// relative IRIs inside the resolved context.
    /// </summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public string? FinalUrl { get; init; }

    /// <summary>Gets the timestamp at which the resource was retrieved. Useful for cache freshness checks.</summary>
    public DateTimeOffset? RetrievedAt { get; init; }
}
