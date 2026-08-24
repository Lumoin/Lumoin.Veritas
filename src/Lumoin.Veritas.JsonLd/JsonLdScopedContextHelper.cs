using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Shared helper for applying scoped context entries to an active
/// context within the JSON-LD pipeline. Bridges the JSON-LD
/// pipeline's delegate types
/// (<see cref="ContextResolverDelegate"/>,
/// <see cref="ParseJsonDelegate"/>) into the format-agnostic
/// shapes the <c>LinkedData</c> layer's
/// <c>ContextProcessing.ApplyEmbeddedContextsAsync</c> accepts.
/// </summary>
/// <remarks>
/// <para>
/// Consumed by <see cref="JsonLdExpansionTree"/> for property-scoped
/// context handling: it produces a new
/// <see cref="LinkedDataContext"/> with the scoped entries merged
/// on top of the input active context, respecting W3C JSON-LD §9.6
/// lazy-merge semantics.
/// </para>
/// </remarks>
internal static class JsonLdScopedContextHelper
{
    /// <summary>
    /// Applies a property-scoped context's entries to the supplied
    /// active context and returns the merged result.
    /// </summary>
    /// <param name="activeContext">The active context at the property descent point.</param>
    /// <param name="scopedEntries">The pre-extracted scoped context entries.</param>
    /// <param name="baseUrl">The base URL for relative-IRI resolution.</param>
    /// <param name="resolver">The JSON-LD remote-context resolver.</param>
    /// <param name="parser">The JSON-LD context-body parser.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>A new <see cref="LinkedDataContext"/> with the scoped entries merged.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    public static async ValueTask<LinkedDataContext> ApplyAsync(
        LinkedDataContext activeContext,
        IReadOnlyList<LinkedDataContextEntry> scopedEntries,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        CancellationToken cancellationToken,
        bool propagate = true,
        bool overrideProtected = false)
    {
        FetchRemoteResourceDelegate fetcher = JsonLdRemoteContextParsing.AdaptResolverToFetcher(resolver);
        ParseRemoteContextDelegate remoteParser = JsonLdRemoteContextParsing.Create(parser);

        try
        {
            //A property-scoped context is processed with override protected
            //enabled, so a scoped null context may legitimately clear protected
            //terms (W3C JSON-LD 1.1 §4.1.2); a type-scoped context is not.
            return await ContextProcessing.ApplyEmbeddedContextsAsync(
                activeContext, scopedEntries, baseUrl, fetcher, remoteParser, cache: null, cancellationToken, propagate, overrideProtected)
                .ConfigureAwait(false);
        }
        catch(LinkedDataProcessingException ex) when(ex is not JsonLdProcessingException)
        {
            //Normalise spec-violation exceptions at the JsonLd boundary
            //so the public API consistently throws JsonLdProcessingException.
            throw new JsonLdProcessingException(ex);
        }
    }
}
