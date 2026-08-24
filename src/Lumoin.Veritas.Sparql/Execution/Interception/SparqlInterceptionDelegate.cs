using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Algebra;

namespace Lumoin.Veritas.Sparql.Execution.Interception;

/// <summary>
/// One evaluation interception, consulted per expand-phase operator by the driver: the entry
/// pattern-matches <paramref name="node"/>, applies its guards against the evaluation state in
/// <paramref name="site"/>, and returns a value-based verdict — an answer for the whole subtree, a leaf
/// annotation, or a decline. Named rather than a bare functional so the binding is a discoverable type;
/// entries are static methods bound as method groups, never capturing lambdas. The site frame passes by
/// value (an <c>in</c> parameter is unavailable to the async window entry).
/// </summary>
/// <param name="node">The expand-phase operator under consideration.</param>
/// <param name="site">The evaluation state the entry consults.</param>
/// <param name="cancellationToken">A token that aborts an answering evaluation.</param>
/// <returns>The entry's outcome at this position.</returns>
internal delegate ValueTask<SparqlInterceptionOutcome> SparqlInterceptionDelegate(AlgebraOperator node, SparqlInterceptionSite site, CancellationToken cancellationToken);
