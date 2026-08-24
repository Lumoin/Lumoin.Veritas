using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The well-known graph-analytics <c>SERVICE</c> endpoints — the single home for the analytics algorithm names and
/// their in-process SERVICE endpoint IRIs, so no surface re-types the raw strings (the catalog keys its descriptors
/// by these names; the engine recognises these endpoints). An endpoint is <see cref="Base"/> plus an algorithm name,
/// optionally with <c>name=value</c> parameters in the IRI query string — for example
/// <c><see cref="CliquesEndpoint"/> + "?size=3"</c>. <see cref="IsAnalyticsEndpoint"/> tests membership and
/// <see cref="TryParseEndpoint"/> decodes an endpoint into its algorithm and parameters. The base is provisional and
/// is recorded in the surfacing design note.
/// </summary>
public static class GraphAnalyticsServices
{
    /// <summary>The base IRI shared by every in-process analytics SERVICE endpoint.</summary>
    public const string Base = "https://lumoin.com/veritas/analytics/";

    /// <summary>The undirected-triangle-count algorithm name (the catalog key).</summary>
    public const string TriangleCount = "triangle-count";

    /// <summary>The global-clustering-coefficient algorithm name.</summary>
    public const string GlobalClustering = "global-clustering";

    /// <summary>The degree algorithm name.</summary>
    public const string Degree = "degree";

    /// <summary>The PageRank algorithm name.</summary>
    public const string PageRank = "pagerank";

    /// <summary>The weakly-connected-components algorithm name.</summary>
    public const string ConnectedComponents = "connected-components";

    /// <summary>The strongly-connected-components algorithm name.</summary>
    public const string StronglyConnectedComponents = "strongly-connected-components";

    /// <summary>The k-core-decomposition algorithm name.</summary>
    public const string KCore = "k-core";

    /// <summary>The single-source-shortest-paths algorithm name.</summary>
    public const string ShortestPaths = "shortest-paths";

    /// <summary>The fixed-size-clique algorithm name.</summary>
    public const string Cliques = "cliques";

    /// <summary>The closeness-centrality algorithm name.</summary>
    public const string Closeness = "closeness";

    /// <summary>The betweenness-centrality algorithm name.</summary>
    public const string Betweenness = "betweenness";

    /// <summary>The eigenvector-centrality algorithm name.</summary>
    public const string Eigenvector = "eigenvector";

    /// <summary>The triangle-count SERVICE endpoint IRI.</summary>
    public const string TriangleCountEndpoint = Base + TriangleCount;

    /// <summary>The global-clustering SERVICE endpoint IRI.</summary>
    public const string GlobalClusteringEndpoint = Base + GlobalClustering;

    /// <summary>The degree SERVICE endpoint IRI.</summary>
    public const string DegreeEndpoint = Base + Degree;

    /// <summary>The PageRank SERVICE endpoint IRI.</summary>
    public const string PageRankEndpoint = Base + PageRank;

    /// <summary>The connected-components SERVICE endpoint IRI.</summary>
    public const string ConnectedComponentsEndpoint = Base + ConnectedComponents;

    /// <summary>The strongly-connected-components SERVICE endpoint IRI.</summary>
    public const string StronglyConnectedComponentsEndpoint = Base + StronglyConnectedComponents;

    /// <summary>The k-core-decomposition SERVICE endpoint IRI.</summary>
    public const string KCoreEndpoint = Base + KCore;

    /// <summary>The single-source-shortest-paths SERVICE endpoint IRI.</summary>
    public const string ShortestPathsEndpoint = Base + ShortestPaths;

    /// <summary>The cliques SERVICE endpoint IRI.</summary>
    public const string CliquesEndpoint = Base + Cliques;

    /// <summary>The closeness-centrality SERVICE endpoint IRI.</summary>
    public const string ClosenessEndpoint = Base + Closeness;

    /// <summary>The betweenness-centrality SERVICE endpoint IRI.</summary>
    public const string BetweennessEndpoint = Base + Betweenness;

    /// <summary>The eigenvector-centrality SERVICE endpoint IRI.</summary>
    public const string EigenvectorEndpoint = Base + Eigenvector;

    /// <summary>The base IRI as UTF-8 bytes, for a zero-allocation endpoint-prefix test that never materialises a non-analytics endpoint IRI.</summary>
    private static Utf8String BaseUtf8 { get; } = Utf8Strings.From(Base);

    /// <summary>Whether an IRI is an in-process analytics SERVICE endpoint — it begins with <see cref="Base"/>. Tests the raw UTF-8 bytes, so a non-analytics endpoint is rejected without materialising it.</summary>
    /// <param name="iri">The endpoint IRI.</param>
    /// <returns><see langword="true"/> when the IRI is an analytics endpoint.</returns>
    public static bool IsAnalyticsEndpoint(Utf8String iri)
    {
        return iri.Memory.Span.StartsWith(BaseUtf8.Memory.Span);
    }

    /// <summary>
    /// Parses an analytics endpoint IRI into its algorithm name (the path segment after <see cref="Base"/>) and the
    /// decoded <c>name=value</c> parameters from the IRI query string. Returns <see langword="false"/> — without
    /// materialising the IRI — when it is not an analytics endpoint.
    /// </summary>
    /// <param name="iri">The endpoint IRI.</param>
    /// <param name="algorithm">On success, the algorithm name.</param>
    /// <param name="parameters">On success, the decoded <c>name=value</c> parameters.</param>
    /// <returns><see langword="true"/> when the endpoint is an analytics endpoint.</returns>
    public static bool TryParseEndpoint(Utf8String iri, out string algorithm, out IReadOnlyList<string> parameters)
    {
        algorithm = string.Empty;
        parameters = [];

        if(!IsAnalyticsEndpoint(iri))
        {
            return false;
        }

        string remainder = iri.ToString()[Base.Length..];
        int separator = remainder.IndexOf('?', StringComparison.Ordinal);
        if(separator < 0)
        {
            algorithm = remainder;

            return true;
        }

        algorithm = remainder[..separator];
        List<string> assignments = [];
        foreach(string pair in remainder[(separator + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            assignments.Add(equals < 0
                ? Uri.UnescapeDataString(pair)
                : Uri.UnescapeDataString(pair[..equals]) + "=" + Uri.UnescapeDataString(pair[(equals + 1)..]));
        }

        parameters = assignments;

        return true;
    }
}
