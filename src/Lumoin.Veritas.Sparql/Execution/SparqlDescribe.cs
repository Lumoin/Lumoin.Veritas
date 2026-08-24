using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The description strategy a <c>DESCRIBE</c> query applies to its resolved resources. SPARQL 1.2 §16.4 leaves the
/// exact triples returned <b>implementation-defined</b>, so the strategy is a per-call seam: pass <see langword="null"/>
/// to <see cref="SparqlQueryEngine.DescribeAsync"/> for the default (<see cref="SparqlDescribe.ConciseBoundedDescription"/>),
/// or supply your own (symmetric CBD, bounded-depth, outgoing-only, label-enriched, …) without touching the engine.
/// </summary>
/// <param name="resources">The resolved resources to describe (their term ids in the data graph's dictionary).</param>
/// <param name="data">Read access to the active graph.</param>
/// <param name="dictionary">The data graph's term dictionary, for classifying terms (e.g. following blank nodes).</param>
/// <param name="cancellationToken">A token that aborts the description.</param>
/// <returns>The describing triples, streamed.</returns>
public delegate IAsyncEnumerable<EncodedTriple> DescribeStrategy(
    IReadOnlyList<TermId> resources,
    GraphMatchOps data,
    TermDictionary dictionary,
    CancellationToken cancellationToken);

/// <summary>The built-in <c>DESCRIBE</c> description strategies.</summary>
public static class SparqlDescribe
{
    /// <summary>
    /// The default strategy — the <b>Concise Bounded Description</b> (CBD): every triple with a described resource as
    /// subject, transitively following blank-node objects (so a resource's blank-node-rooted structure is included),
    /// stopping at IRIs/literals. Walks the blank-node frontier over an explicit stack (no recursion); each triple is
    /// emitted once.
    /// </summary>
    /// <param name="resources">The resolved resources to describe.</param>
    /// <param name="data">Read access to the active graph.</param>
    /// <param name="dictionary">The term dictionary, used to recognise blank-node objects to follow.</param>
    /// <param name="cancellationToken">A token that aborts the description.</param>
    /// <returns>The CBD triples, streamed.</returns>
    public static IAsyncEnumerable<EncodedTriple> ConciseBoundedDescription(
        IReadOnlyList<TermId> resources,
        GraphMatchOps data,
        TermDictionary dictionary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(dictionary);

        return Enumerate(resources, data, dictionary, cancellationToken);

        static async IAsyncEnumerable<EncodedTriple> Enumerate(
            IReadOnlyList<TermId> resources,
            GraphMatchOps data,
            TermDictionary dictionary,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Stack<TermId> frontier = new();
            for(int i = resources.Count - 1; i >= 0; i--)
            {
                frontier.Push(resources[i]);
            }

            HashSet<TermId> describedSubjects = [];
            HashSet<EncodedTriple> emitted = [];
            while(frontier.Count > 0)
            {
                TermId subject = frontier.Pop();
                if(subject.IsNone || !describedSubjects.Add(subject))
                {
                    continue;
                }

                await foreach(EncodedTriple triple in data.MatchTriples(subject, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
                {
                    if(emitted.Add(triple))
                    {
                        yield return triple;
                    }

                    //CBD is "bounded" by following only blank-node objects — their own triples belong to the
                    //description. An engine-minted node is followed the same way: like a blank node, its
                    //rendered reference cannot be dereferenced or re-queried by a consumer, so its triples
                    //belong to the description too.
                    if(dictionary.Resolve(triple.Object) is BlankNode or EngineNode)
                    {
                        frontier.Push(triple.Object);
                    }
                }
            }
        }
    }
}
