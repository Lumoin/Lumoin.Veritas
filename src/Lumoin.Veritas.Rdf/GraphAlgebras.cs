using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Delegate contracts used by the graph recursion schemes in this namespace.
/// </summary>
/// <remarks>
/// <para>
/// A <c>GraphAlgebra&lt;TResult&gt;</c> consumes graph structure into a result.
/// It is the "what to do at each node" half of a plain fold: given a node,
/// the set of triples outgoing from that node, and the already-folded
/// results of the triples' object nodes, produce the result for this node.
/// All children are forced before the algebra runs.
/// </para>
/// <para>
/// A <c>GraphKAlgebra&lt;TResult&gt;</c> is the iterator-based variant used
/// by <see cref="GraphKFold"/>. It receives a <see cref="ChildHandles{TResult}"/>
/// and controls which children are forced, in what order, and whether any
/// remain unforced. Written as an iterator method returning
/// <see cref="System.Collections.Generic.IEnumerator{T}"/> of
/// <see cref="ForceRequest"/>, with the algebra's result written through
/// <see cref="ChildHandles{TResult}.SetResult"/> before completion.
/// </para>
/// <para>
/// A <c>GraphCoalgebra&lt;TSeed&gt;</c> produces graph structure from a seed.
/// It is the "how to expand each seed" half of an unfold: given a seed value,
/// produce a triple and zero or more further seeds to expand.
/// </para>
/// <para>
/// A <c>GraphParaAlgebra&lt;TResult&gt;</c> is a node-level fold that also sees
/// each outgoing triple alongside its object's folded value. This is required
/// whenever the fold result must reference the original graph structure — for
/// example, when emitting OWL inferences that quote the triples they derive
/// from, or when a SHACL validation result must record which original triple
/// caused a violation.
/// </para>
/// <para>
/// The algebras are node-centric rather than edge-centric. A node with multiple
/// outgoing edges is reduced once: the algebra receives all its outgoing
/// triples in a single call, together with the folded results of the objects
/// of those triples (in matching order). This matches the shape required by
/// SHACL <c>sh:property</c> evaluation, OWL forward chaining, and any other
/// "visit each node, combine its children's results" operation.
/// </para>
/// <para>
/// Objects that never appear as the subject of any outgoing triple (leaves of
/// the traversal) contribute <c>default(TResult)</c> to the child-result list
/// in the plain fold. Consumer algebras should treat <c>default(TResult)</c>
/// as the "no result" case: for value types this is the zero value, for
/// reference types it is <c>null</c>.
/// </para>
/// <para>
/// These contracts are deliberately small. The recursion schemes
/// (<see cref="GraphFold"/>, <see cref="GraphKFold"/>,
/// <see cref="GraphUnfold"/>, <see cref="GraphHylo"/>,
/// <see cref="GraphPara"/>) combine them with a
/// <see cref="StorageDelegates.MatchTriplesAsync"/> to traverse a graph.
/// </para>
/// </remarks>
public static class GraphAlgebras
{
    /// <summary>
    /// A fold step over a graph node: consume the node together with its
    /// outgoing triples and the folded results of those triples' object nodes,
    /// produce a result for this node. Used by <see cref="GraphFold"/>.
    /// </summary>
    /// <typeparam name="TResult">The type produced by the fold.</typeparam>
    /// <param name="nodeId">The encoded identifier of the node being reduced.</param>
    /// <param name="outgoingTriples">The triples whose subject is <paramref name="nodeId"/>, in discovery order.</param>
    /// <param name="childResults">
    /// The folded result for each outgoing triple's object, in the same order as
    /// <paramref name="outgoingTriples"/>. Objects whose node has no outgoing
    /// edges (leaves in the traversal) are represented with <c>default(TResult)</c>.
    /// </param>
    /// <returns>The fold result for this node.</returns>
    public delegate TResult GraphAlgebra<TResult>(
        TermId nodeId,
        IReadOnlyList<EncodedTriple> outgoingTriples,
        IReadOnlyList<TResult> childResults);

    /// <summary>
    /// A fold step using iterator-based selective child evaluation. Used by
    /// <see cref="GraphKFold"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned iterator yields <see cref="ForceRequest"/> values to the
    /// driver. Each yielded <see cref="ForceRequest.Force(int)"/> asks the
    /// driver to compute the folded value of a specific child, after which
    /// the algebra observes it via <see cref="ChildHandles{TResult}.Get"/>.
    /// The algebra writes its own result via
    /// <see cref="ChildHandles{TResult}.SetResult"/> exactly once before
    /// the iterator completes.
    /// </para>
    /// <para>
    /// Algebras are typically written as C# iterator methods
    /// (<c>yield return</c>). The compiler generates one state-machine class
    /// per algebra invocation; nothing captures from outer scope, so there
    /// are no closures.
    /// </para>
    /// </remarks>
    /// <typeparam name="TResult">The type produced by the fold.</typeparam>
    /// <param name="nodeId">The encoded identifier of the node being reduced.</param>
    /// <param name="outgoingTriples">
    /// The triples whose subject is <paramref name="nodeId"/>, in discovery order.
    /// Child indices in <see cref="ForceRequest.Force(int)"/> refer to this array.
    /// </param>
    /// <param name="children">
    /// Handles through which the algebra observes child results and writes
    /// its own. Must not be stored beyond the iterator's lifetime.
    /// </param>
    /// <returns>An iterator of force requests terminated by algebra completion.</returns>
    public delegate IEnumerator<ForceRequest> GraphKAlgebra<TResult>(
        TermId nodeId,
        ReadOnlyMemory<EncodedTriple> outgoingTriples,
        ChildHandles<TResult> children);

    /// <summary>
    /// An unfold step over a graph: from a seed value, produce a triple and the
    /// further seeds from which outward neighbours should be expanded.
    /// </summary>
    /// <remarks>
    /// Returning a <see cref="GraphExpansion{TSeed}"/> with a null triple signals
    /// that the seed does not expand and contributes no triples. Returning an
    /// empty seeds list with a non-null triple signals that the triple is a leaf.
    /// </remarks>
    /// <typeparam name="TSeed">The seed value type.</typeparam>
    /// <param name="seed">The seed to expand.</param>
    /// <returns>The triple produced by the seed and the seeds for its neighbours.</returns>
    public delegate GraphExpansion<TSeed> GraphCoalgebra<TSeed>(TSeed seed);

    /// <summary>
    /// A paramorphism step: a node-level fold whose algebra receives, for each
    /// outgoing triple, both the triple itself and the folded result of its
    /// object's subtree.
    /// </summary>
    /// <typeparam name="TResult">The type produced by the fold.</typeparam>
    /// <param name="nodeId">The encoded identifier of the node being reduced.</param>
    /// <param name="outgoingEdges">
    /// For each outgoing triple, a pair consisting of the triple itself and the
    /// folded result of the triple's object. Objects that have no outgoing
    /// edges are represented with <c>default(TResult)</c> for the folded result.
    /// </param>
    /// <returns>The fold result for this node.</returns>
    public delegate TResult GraphParaAlgebra<TResult>(
        TermId nodeId,
        IReadOnlyList<(EncodedTriple Triple, TResult ChildResult)> outgoingEdges);
}
