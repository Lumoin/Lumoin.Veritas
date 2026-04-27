using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Shacl.Targets;

/// <summary>
/// Abstract base for SHACL target declarations.
/// </summary>
/// <remarks>
/// <para>
/// A target identifies focus nodes in the data graph to which a shape
/// applies. Per SHACL 1.2 Core §5.1, targets are declared on shapes via
/// <c>sh:targetClass</c>, <c>sh:targetNode</c>, <c>sh:targetSubjectsOf</c>,
/// <c>sh:targetObjectsOf</c>, <c>sh:targetWhere</c> (1.2), and the implicit
/// class target for <c>sh:ShapeClass</c>.
/// </para>
/// <para>
/// The <see cref="ExpandAsync"/> method yields focus-node identifiers as
/// an async stream. Implementations must not materialize the full
/// focus-node set internally — this is the core of the streaming
/// commitment that lets validators operate on million-node graphs without
/// collapsing under memory pressure.
/// </para>
/// </remarks>
public abstract record Target
{
    /// <summary>Optional source-range annotation for diagnostics. <c>null</c> when loaded from RDF.</summary>
    public SourceSpan? Span { get; init; }

    /// <summary>
    /// Yields the focus-node identifiers this target expands to against
    /// the given data graph.
    /// </summary>
    /// <remarks>
    /// Implementations stream via <paramref name="dataMatch"/>. Callers
    /// iterate with <c>await foreach</c>.
    /// </remarks>
    /// <param name="dataMatch">Match delegate against the data graph.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async sequence of encoded focus-node identifiers.</returns>
    public abstract IAsyncEnumerable<TermId> ExpandAsync(
        StorageDelegates.MatchTriplesAsync dataMatch,
        CancellationToken cancellationToken = default);
}
