using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Targets;

/// <summary>
/// <c>sh:targetNode</c> — the shape applies to the given single node.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §5.1.2. The node is yielded regardless of whether
/// it appears as a subject, predicate, or object in the data graph.
/// The node may be of any kind (IRI, blank, or literal), so the
/// identifier is typed as <see cref="TermId"/> rather than a narrower wrapper.
/// </remarks>
/// <param name="NodeId">The encoded node identifier.</param>
public sealed record TargetNode(TermId NodeId): Target
{
    /// <inheritdoc/>
    public override async IAsyncEnumerable<TermId> ExpandAsync(
        StorageDelegates.MatchTriplesAsync dataMatch,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = dataMatch;
        cancellationToken.ThrowIfCancellationRequested();
        yield return NodeId;
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
