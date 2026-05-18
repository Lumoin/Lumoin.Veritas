using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Targets;

/// <summary>
/// <c>sh:targetObjectsOf</c> — the shape applies to every object that
/// appears in a triple whose predicate is <see cref="PredicateId"/>.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §5.1.4. Duplicates are filtered.</remarks>
/// <param name="PredicateId">The encoded predicate IRI identifier.</param>
public sealed record TargetObjectsOf(IriId PredicateId): Target
{
    /// <inheritdoc/>
    public override async IAsyncEnumerable<TermId> ExpandAsync(
        StorageDelegates.MatchTriplesAsync dataMatch,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        HashSet<TermId> yielded = [];
        await foreach(EncodedTriple triple in dataMatch(TermId.None, PredicateId, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            if(yielded.Add(triple.Object))
            {
                yield return triple.Object;
            }
        }
    }
}
