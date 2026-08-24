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
/// Implicit class target — a shape whose subject is also an instance of
/// <c>rdfs:Class</c> (or <c>sh:ShapeClass</c> in SHACL 1.2) applies to all
/// instances of itself-as-a-class.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §5.1.5. Expansion delegates to the same streaming
/// closure as <see cref="TargetClass"/>.
/// </remarks>
/// <param name="ShapeClassId">The encoded shape/class IRI identifier.</param>
/// <param name="RdfTypeId">The encoded <c>rdf:type</c> predicate identifier.</param>
/// <param name="RdfsSubClassOfId">The encoded <c>rdfs:subClassOf</c> predicate identifier.</param>
public sealed record ImplicitClassTarget(IriId ShapeClassId, IriId RdfTypeId, IriId RdfsSubClassOfId): Target
{
    /// <inheritdoc/>
    public override async IAsyncEnumerable<TermId> ExpandAsync(
        StorageDelegates.MatchTriplesAsync dataMatch,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        HashSet<TermId> yielded = [];

        //Walk rdfs:subClassOf *backward* (object→subject) so the class set
        //is C and its transitive subclasses — the SHACL instances of C are
        //the nodes typed with any of those, not with C's superclasses.
        HashSet<TermId> classSet = [ShapeClassId];
        RdfAdjacencyAdapter adapter = new(dataMatch);
        await foreach(TermId subClass in TraversalPrimitives.TransitiveClosureAsync(
            ShapeClassId.Value, RdfsSubClassOfId, adapter.BackwardAsync, cancellationToken).ConfigureAwait(false))
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
