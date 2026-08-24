using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The engine's store-local graph source: resolves a dataset-clause IRI (a query's <c>FROM</c> /
/// <c>FROM NAMED</c>, or a protocol-supplied dataset) to the triples of the SAME dataset's named graph
/// of that name, decoded through the dataset's term dictionary. An IRI naming no loaded graph refuses
/// with <see cref="UnknownGraphSourceException"/> — never an empty guess and never a network fetch; a
/// host wanting remote dereference supplies its own <see cref="GraphSourceResolver"/> through the engine
/// options, which overrides this default entirely. Carries the dataset and dictionary as explicit state
/// so the resolver handed downward is a bound method group rather than a lambda closing over locals.
/// </summary>
/// <param name="dataset">The dataset whose named graphs are served.</param>
/// <param name="dictionary">The term dictionary the graph names and triples decode through.</param>
public sealed class DatasetGraphSource(SparqlDataset dataset, TermDictionary dictionary)
{
    /// <summary>The dataset whose named graphs are served.</summary>
    private SparqlDataset Dataset { get; } = dataset;

    /// <summary>The term dictionary the graph names and triples decode through.</summary>
    private TermDictionary Dictionary { get; } = dictionary;

    /// <summary>
    /// Resolves <paramref name="source"/> to the named graph of that IRI in the carried dataset and
    /// streams its triples decoded. The access context is not consulted here: the resolved triples only
    /// ever build the query's effective dataset, and reads over that dataset pass through the engine's
    /// access-control policy exactly as reads over the original one do.
    /// </summary>
    /// <param name="source">The dataset-clause graph IRI.</param>
    /// <param name="accessContext">The caller's opaque access context; forwarded by the seam, unused by the store-local source.</param>
    /// <param name="cancellationToken">A token that aborts the enumeration.</param>
    /// <returns>The named graph's triples, streamed decoded.</returns>
    /// <exception cref="UnknownGraphSourceException"><paramref name="source"/> names no loaded named graph.</exception>
    public async IAsyncEnumerable<DataTriple> ResolveAsync(IriRef source, AccessContext? accessContext, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        TermId graphName = Dictionary.GetIdOrDefault(new NamedNode(source.Value));
        if(graphName.IsNone || !Dataset.TryGetNamedGraph(graphName, out HypertrieGraphStore store))
        {
            throw new UnknownGraphSourceException($"The dataset graph IRI <{source.Value}> names no loaded named graph; the store-local graph source serves only the dataset's own named graphs.");
        }

        foreach(EncodedTriple triple in store.Match(default, default, default))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new DataTriple(Dictionary.Resolve(triple.Subject), Dictionary.Resolve(triple.Predicate), Dictionary.Resolve(triple.Object));
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
