using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// Parses a fetched <see cref="RemoteResource"/> into the format-neutral
/// <see cref="IReadOnlyDictionary{TKey, TValue}"/> shape the
/// <c>ContextProcessing</c> algorithm consumes. Format-specific: the
/// JsonLd shell supplies the canonical JSON-LD context parser; other
/// shells would supply their own equivalent.
/// </summary>
/// <remarks>
/// <para>
/// The returned dictionary mirrors the structure of an inline
/// <c>@context</c> object: keys are JSON-LD-keyword strings
/// (<c>"@base"</c>, <c>"@vocab"</c>, <c>"@language"</c>, etc.) or
/// term names; values are nested dictionaries / lists / primitives /
/// <see langword="null"/> per the JSON value model.
/// </para>
/// <para>
/// Implementations should throw a clear exception when the bytes are
/// not valid input for the expected format; the caller will surface
/// this as a loading-failure error.
/// </para>
/// </remarks>
/// <param name="resource">The fetched resource to parse.</param>
/// <param name="cancellationToken">A token to cancel the parse.</param>
/// <returns>The parsed inline-context dictionary.</returns>
public delegate ValueTask<IReadOnlyDictionary<string, object?>> ParseRemoteContextDelegate(
    RemoteResource resource,
    CancellationToken cancellationToken);
