using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Implements the W3C JSON-LD 1.1 Context Processing Algorithm by
/// extracting the JSON-LD-shaped input into format-agnostic POCOs and
/// delegating to <see cref="ContextProcessing"/> in
/// <c>Lumoin.Veritas.LinkedData</c>. The shell is responsible for
/// adapting the legacy <see cref="ContextResolverDelegate"/> /
/// <see cref="ParseJsonDelegate"/> pair into the
/// <see cref="FetchRemoteResourceDelegate"/> /
/// <see cref="ParseRemoteContextDelegate"/> shape the format-agnostic
/// core consumes, and for wrapping any
/// <see cref="LinkedDataProcessingException"/> the core throws as a
/// JsonLd-specific <see cref="JsonLdProcessingException"/>.
/// </summary>
/// <seealso href="https://www.w3.org/TR/json-ld11-api/#context-processing-algorithm"/>
public static class ContextProcessor
{
    /// <summary>
    /// Processes a JSON-LD <c>@context</c> value and returns the resulting
    /// active context.
    /// </summary>
    /// <param name="activeContext">The context active before processing begins.</param>
    /// <param name="localContext">The value of the <c>@context</c> entry.</param>
    /// <param name="baseUrl">The base URL of the document being processed.</param>
    /// <param name="resolver">The delegate used to fetch remote context documents.</param>
    /// <param name="parser">The delegate used to parse fetched UTF-8 JSON bytes.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The active context after processing the local context.</returns>
    /// <exception cref="JsonLdProcessingException">The local context contained an invalid value or could not be processed.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows JSON-LD spec which uses string URIs throughout the processing algorithm.")]
    public static async ValueTask<LinkedDataContext> ProcessAsync(
        LinkedDataContext activeContext,
        JsonNode localContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(parser);

        int keyCounter = 0;
        IReadOnlyList<LinkedDataContextEntry> entries =
            ContextExtraction.ExtractEntries(localContext, baseUrl, ref keyCounter);

        FetchRemoteResourceDelegate fetcher = JsonLdRemoteContextParsing.AdaptResolverToFetcher(resolver);
        ParseRemoteContextDelegate parseDelegate = JsonLdRemoteContextParsing.Create(parser);

        try
        {
            return await ContextProcessing.ApplyEmbeddedContextsAsync(
                activeContext, entries, baseUrl,
                fetcher, parseDelegate, cache: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch(LinkedDataProcessingException ex) when(ex is not JsonLdProcessingException)
        {
            throw new JsonLdProcessingException(ex);
        }
    }
}
