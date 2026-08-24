using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// A single entry in an <c>@context</c> array (or the sole entry when the
/// value is not an array). One of three shapes: a remote-context URL, an
/// inline context (a dictionary of <see cref="LinkedDataTermSource"/>),
/// or a reset (both <see cref="Url"/> and <see cref="Terms"/> are
/// <see langword="null"/>). This format-neutral shape is the input to
/// the active-context processing core; both JSON-LD and CBOR-LD shells
/// extract their format-specific tree into a list of these entries
/// before calling the core.
/// </summary>
/// <seealso href="https://www.w3.org/TR/json-ld11-api/#context-processing-algorithm"/>
public sealed class LinkedDataContextEntry
{
    /// <summary>Initialises a URL-bearing context entry.</summary>
    /// <param name="url">The context URL. <see langword="null"/> resets the context.</param>
    /// <param name="baseUrl">The base URL in effect at the point this entry appears, used for resolving relative URLs inside the entry.</param>
    /// <param name="syntheticKey">A stable deduplication key. Must be non-null and unique among entries within a single extraction run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="syntheticKey"/> is <see langword="null"/>.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public LinkedDataContextEntry(string? url, string? baseUrl, string syntheticKey)
    {
        ArgumentNullException.ThrowIfNull(syntheticKey);
        Url = url;
        BaseUrl = baseUrl;
        SyntheticKey = syntheticKey;
    }

    /// <summary>Initialises an inline-context entry.</summary>
    /// <param name="terms">The pre-extracted term sources, keyed by term name.</param>
    /// <param name="baseUrl">The base URL in effect at the point this entry appears.</param>
    /// <param name="syntheticKey">A stable deduplication key.</param>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public LinkedDataContextEntry(
        IReadOnlyDictionary<string, LinkedDataTermSource> terms,
        string? baseUrl,
        string syntheticKey)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(syntheticKey);
        Terms = terms;
        BaseUrl = baseUrl;
        SyntheticKey = syntheticKey;
    }

    /// <summary>
    /// Initialises a reset-context entry (both <see cref="Url"/> and
    /// <see cref="Terms"/> are <see langword="null"/>).
    /// </summary>
    /// <param name="syntheticKey">A stable deduplication key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="syntheticKey"/> is <see langword="null"/>.</exception>
    public LinkedDataContextEntry(string syntheticKey)
    {
        ArgumentNullException.ThrowIfNull(syntheticKey);
        SyntheticKey = syntheticKey;
    }

    /// <summary>Gets the context URL if this entry resolves remotely; otherwise <see langword="null"/>.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public string? Url { get; }

    /// <summary>Gets the pre-extracted term sources for an inline context; otherwise <see langword="null"/>.</summary>
    public IReadOnlyDictionary<string, LinkedDataTermSource>? Terms { get; }

    /// <summary>Gets the base URL in effect at the point this entry appears, for resolving relative URLs inside it.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public string? BaseUrl { get; }

    /// <summary>
    /// Gets the stable deduplication key. Used by
    /// <c>IterativeTraversal</c>-driven scoped-context walks to avoid
    /// re-processing the same context entry through two paths.
    /// </summary>
    public string SyntheticKey { get; }

    /// <summary>Indicates whether this entry resets the context.</summary>
    public bool IsReset => Url is null && Terms is null;

    /// <summary>Gets or initialises the <c>@base</c> directive (absent: <see langword="null"/>; explicit null: <see cref="HasBase"/> is <see langword="true"/> with this <see langword="null"/>).</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public string? Base { get; init; }

    /// <summary>Indicates whether the entry carries a <c>@base</c> directive at all.</summary>
    public bool HasBase { get; init; }

    /// <summary>Gets or initialises the <c>@vocab</c> directive.</summary>
    public string? Vocab { get; init; }

    /// <summary>Indicates whether the entry carries a <c>@vocab</c> directive at all.</summary>
    public bool HasVocab { get; init; }

    /// <summary>Gets or initialises the <c>@language</c> directive.</summary>
    public string? Language { get; init; }

    /// <summary>Indicates whether the entry carries a <c>@language</c> directive at all.</summary>
    public bool HasLanguage { get; init; }

    /// <summary>Gets or initialises the <c>@direction</c> directive.</summary>
    public string? Direction { get; init; }

    /// <summary>Indicates whether the entry carries a <c>@direction</c> directive at all.</summary>
    public bool HasDirection { get; init; }

    /// <summary>Gets or initialises the <c>@propagate</c> directive.</summary>
    public bool? Propagate { get; init; }

    /// <summary>Gets or initialises the entry-level <c>@protected</c> directive that applies to every term in <see cref="Terms"/>.</summary>
    public bool? Protected { get; init; }

    /// <summary>Gets or initialises the <c>@import</c> URL referencing another inline context to merge.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public string? Import { get; init; }
}
