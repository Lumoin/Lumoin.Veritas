using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Algebra;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Targets;

/// <summary>
/// Class target — a shape with <c>sh:targetClass C</c> applies to every
/// focus node that is a SHACL instance of class <c>C</c>, i.e., a
/// subject of an <c>rdf:type</c> triple with object in the
/// <c>rdfs:subClassOf</c> closure of <c>C</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §5.1.2. Expansion is a two-step streaming walk:
/// first collect <paramref name="ClassId"/> and its transitive
/// subclasses via <c>rdfs:subClassOf</c>, then yield each distinct
/// <c>rdf:type</c>-subject of any class in that set.
/// </para>
/// </remarks>
/// <param name="ClassId">The encoded class IRI identifier.</param>
/// <param name="RdfTypeId">The encoded <c>rdf:type</c> predicate identifier.</param>
/// <param name="RdfsSubClassOfId">The encoded <c>rdfs:subClassOf</c> predicate identifier.</param>
public sealed record TargetClass(IriId ClassId, IriId RdfTypeId, IriId RdfsSubClassOfId): Target
{
    /// <inheritdoc/>
    public override async IAsyncEnumerable<TermId> ExpandAsync(
        StorageDelegates.MatchTriplesAsync dataMatch,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        HashSet<TermId> yielded = [];

        //Build the class set by walking rdfs:subClassOf *backward* from
        //ClassId: the SHACL instances of C are the nodes typed with C or
        //any subclass X (X rdfs:subClassOf* C), so we descend the
        //subclass hierarchy (object→subject), not ascend to superclasses.
        //The start class is seeded in directly because
        //TransitiveClosureAsync yields strictly the proper descendants.
        HashSet<TermId> classSet = [ClassId];
        RdfAdjacencyAdapter adapter = new(dataMatch);
        await foreach(TermId subClass in TraversalPrimitives.TransitiveClosureAsync(
            ClassId.Value, RdfsSubClassOfId, adapter.BackwardAsync, cancellationToken).ConfigureAwait(false))
        {
            classSet.Add(subClass);
        }

        foreach(TermId cls in classSet)
        {
            await foreach(EncodedTriple triple in dataMatch(TermId.None, RdfTypeId, cls, cancellationToken).ConfigureAwait(false))
            {
                if(yielded.Add(triple.Subject))
                {
                    yield return triple.Subject;
                }
            }
        }
    }
}
