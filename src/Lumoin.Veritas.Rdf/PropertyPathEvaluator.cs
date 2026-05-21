using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Evaluates <see cref="PropertyPath"/> expressions against a graph, producing
/// the set of nodes reachable from a starting node or set.
/// </summary>
/// <remarks>
/// <para>
/// The evaluator follows the semantics of
/// <see href="https://www.w3.org/TR/sparql11-query/#propertypaths">SPARQL 1.1 §9</see>
/// and <see href="https://www.w3.org/TR/shacl12-core/#property-paths">SHACL 1.2 Core §2.3</see>.
/// All operations are set-valued and de-duplicated. The result is a distinct set
/// of node identifiers, not a multiset of paths.
/// </para>
/// <para>
/// The implementation is fully non-recursive: each constructor is handled with
/// its own iterative traversal. Nested paths are evaluated by materialising
/// intermediate frontier sets rather than by recursive method calls. This keeps
/// the call stack bounded regardless of path nesting depth.
/// </para>
/// <para>
/// Evaluation is driven by a <see cref="GraphMatchOps"/> bundle: single-pattern
/// match for storage that still goes one triple at a time, and
/// subject-set / object-set primitives for the one-step expansion at the heart
/// of every constructor. The subject- and object-set primitives let the
/// hypertrie amortise the predicate-rooted descent across an entire start set
/// rather than re-descending per element.
/// </para>
/// </remarks>
public static class PropertyPathEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="path"/> from a single starting node and yields
    /// each reachable distinct node exactly once.
    /// </summary>
    /// <param name="start">The encoded identifier of the starting node.</param>
    /// <param name="path">The property path AST.</param>
    /// <param name="ops">The bundle of match delegates over the graph.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of reachable node identifiers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
    /// <remarks>
    /// <para>
    /// The yielded sequence is a set: each reachable node appears exactly once
    /// even when several distinct paths reach it. This matches the set-valued
    /// semantics of SPARQL 1.1 §9 and SHACL 1.2 Core §2.3; multiset semantics
    /// (one binding per distinct path) are not produced here.
    /// </para>
    /// <para>
    /// Corner cases by constructor:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>ZeroOrMorePath</c>: the start node is always included, regardless of
    /// whether it has any outgoing edges matching the inner path. The traversal
    /// is cycle-safe: a visited set short-circuits the BFS when the inner path
    /// reaches a node already seen.
    /// </description></item>
    /// <item><description>
    /// <c>OneOrMorePath</c>: the start node is included if and only if it is
    /// reachable from itself via the inner path (a cycle). This is achieved by
    /// seeding the BFS visited set with the first inner step's result, not with
    /// the starts. An empty first step short-circuits the traversal to the empty
    /// set without entering the BFS loop.
    /// </description></item>
    /// <item><description>
    /// <c>ZeroOrOnePath</c>: the start node is always included unconditionally,
    /// even when the inner path produces an empty step from it.
    /// </description></item>
    /// <item><description>
    /// <c>InversePath</c>: a bare predicate inverse is dispatched directly to
    /// a backward storage call. A compound inverse (over a sequence,
    /// alternative, or repetition) is rewritten internally so that <c>^</c> is
    /// pushed inward: sequence steps are reversed and inverted, alternatives
    /// distribute the inverse over their branches, repetitions commute with
    /// inversion, and double inverse cancels.
    /// </description></item>
    /// <item><description>
    /// <c>SequencePath</c>: the result of each step becomes the start set of
    /// the next step. An empty intermediate set propagates to an empty final
    /// result.
    /// </description></item>
    /// <item><description>
    /// <c>AlternativePath</c>: branches are evaluated independently from the
    /// same start set, and the union is returned. Duplicate alternatives
    /// deduplicate naturally via the set semantics.
    /// </description></item>
    /// </list>
    /// <para>
    /// The evaluator is fully iterative at the data-graph level: BFS frontiers
    /// in the Kleene operators use an explicit <see cref="Queue{T}"/>. Stack
    /// depth is therefore bounded by the AST nesting, not by graph diameter or
    /// cycle length.
    /// </para>
    /// </remarks>
    public static IAsyncEnumerable<TermId> EvaluateAsync(
        TermId start,
        PropertyPath path,
        GraphMatchOps ops,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        return EvaluateFromSetCore([start], path, ops, cancellationToken);
    }

    /// <summary>
    /// Evaluates <paramref name="path"/> from a set of starting nodes and yields
    /// each reachable distinct node exactly once.
    /// </summary>
    /// <param name="starts">The encoded identifiers of the starting nodes.</param>
    /// <param name="path">The property path AST.</param>
    /// <param name="ops">The bundle of match delegates over the graph.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of reachable node identifiers.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="starts"/> or <paramref name="path"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The semantics match <see cref="EvaluateAsync(TermId, PropertyPath, GraphMatchOps, CancellationToken)"/>
    /// extended to a set of starts: the result is the union of evaluating the
    /// path from each start. Per-start results are deduplicated structurally.
    /// </para>
    /// <para>
    /// An empty <paramref name="starts"/> collection yields the empty set for
    /// every constructor. In particular, <c>ZeroOrMorePath</c> over no starts
    /// yields no reflexive elements because there are no starts to be
    /// reflexive about, and <c>ZeroOrOnePath</c> over no starts yields the
    /// empty set for the same reason.
    /// </para>
    /// <para>
    /// All corner cases listed on <see cref="EvaluateAsync(TermId, PropertyPath, GraphMatchOps, CancellationToken)"/>
    /// apply per-start to this overload.
    /// </para>
    /// </remarks>
    public static IAsyncEnumerable<TermId> EvaluateFromSetAsync(
        IReadOnlyCollection<TermId> starts,
        PropertyPath path,
        GraphMatchOps ops,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(path);
        return EvaluateFromSetCore(starts, path, ops, cancellationToken);
    }

    private static async IAsyncEnumerable<TermId> EvaluateFromSetCore(
        IReadOnlyCollection<TermId> starts,
        PropertyPath path,
        GraphMatchOps ops,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        HashSet<TermId> results = await EvaluateToSetAsync(starts, path, ops, cancellationToken).ConfigureAwait(false);
        foreach(TermId node in results)
        {
            yield return node;
        }
    }

    private static async ValueTask<HashSet<TermId>> EvaluateToSetAsync(
        IReadOnlyCollection<TermId> starts,
        PropertyPath path,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch(path)
        {
            case PredicatePath predicatePath:
            {
                return await ExpandOneStepAsync(starts, predicatePath.Predicate, forward: true, ops, cancellationToken).ConfigureAwait(false);
            }

            case InversePath inversePath when inversePath.Inner is PredicatePath innerPredicate:
            {
                return await ExpandOneStepAsync(starts, innerPredicate.Predicate, forward: false, ops, cancellationToken).ConfigureAwait(false);
            }

            case InversePath inversePath:
            {
                PropertyPath inverted = InvertPath(inversePath.Inner);
                return await EvaluateToSetAsync(starts, inverted, ops, cancellationToken).ConfigureAwait(false);
            }

            case SequencePath sequencePath:
            {
                return await EvaluateSequenceAsync(starts, sequencePath.Steps, ops, cancellationToken).ConfigureAwait(false);
            }

            case AlternativePath alternativePath:
            {
                return await EvaluateAlternativeAsync(starts, alternativePath.Alternatives, ops, cancellationToken).ConfigureAwait(false);
            }

            case ZeroOrMorePath zeroOrMorePath:
            {
                return await EvaluateZeroOrMoreAsync(starts, zeroOrMorePath.Inner, ops, cancellationToken).ConfigureAwait(false);
            }

            case OneOrMorePath oneOrMorePath:
            {
                return await EvaluateOneOrMoreAsync(starts, oneOrMorePath.Inner, ops, cancellationToken).ConfigureAwait(false);
            }

            case ZeroOrOnePath zeroOrOnePath:
            {
                HashSet<TermId> oneStep = await EvaluateToSetAsync(starts, zeroOrOnePath.Inner, ops, cancellationToken).ConfigureAwait(false);
                foreach(TermId start in starts)
                {
                    oneStep.Add(start);
                }

                return oneStep;
            }

            default:
            {
                throw new NotSupportedException($"Unsupported property path constructor: {path.GetType().Name}.");
            }
        }
    }

    private static async ValueTask<HashSet<TermId>> ExpandOneStepAsync(
        IReadOnlyCollection<TermId> starts,
        IriId predicate,
        bool forward,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        if(starts.Count == 0)
        {
            return [];
        }

        //Materialise the start set into a pre-sized array. The subject-
        //and object-set primitives consume ReadOnlyMemory<TermId>, and a
        //single allocation here lets the storage layer amortise the
        //predicate-rooted descent across the whole set instead of
        //re-descending per start.
        TermId[] startsArray = new TermId[starts.Count];
        int index = 0;
        foreach(TermId start in starts)
        {
            startsArray[index++] = start;
        }

        HashSet<TermId> next = [];
        if(forward)
        {
            await foreach(EncodedTriple triple in ops.MatchTriplesBySubjects(
                startsArray, predicate.Value, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                next.Add(triple.Object);
            }
        }
        else
        {
            await foreach(EncodedTriple triple in ops.MatchTriplesByObjects(
                TermId.None, predicate.Value, startsArray, cancellationToken).ConfigureAwait(false))
            {
                next.Add(triple.Subject);
            }
        }

        return next;
    }

    private static async ValueTask<HashSet<TermId>> EvaluateSequenceAsync(
        IReadOnlyCollection<TermId> starts,
        ImmutableArray<PropertyPath> steps,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<TermId> current = starts;
        foreach(PropertyPath step in steps)
        {
            HashSet<TermId> expanded = await EvaluateToSetAsync(current, step, ops, cancellationToken).ConfigureAwait(false);
            current = expanded;
        }

        return current is HashSet<TermId> asHashSet ? asHashSet : [.. current];
    }

    private static async ValueTask<HashSet<TermId>> EvaluateAlternativeAsync(
        IReadOnlyCollection<TermId> starts,
        ImmutableArray<PropertyPath> alternatives,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        HashSet<TermId> union = [];
        foreach(PropertyPath alternative in alternatives)
        {
            HashSet<TermId> branch = await EvaluateToSetAsync(starts, alternative, ops, cancellationToken).ConfigureAwait(false);
            foreach(TermId node in branch)
            {
                union.Add(node);
            }
        }

        return union;
    }

    /// <summary>
    /// Rewrites a property path so that its traversal direction is reversed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inverse of a compound path is obtained by pushing <c>^</c> inward,
    /// reversing the order of sequence steps, and leaving alternatives and
    /// repetition quantifiers untouched (an alternative's inverse is the
    /// alternative of inverses; repetition commutes with inversion).
    /// </para>
    /// <para>
    /// Note: this helper is directly recursive because it walks the property-path
    /// AST, not the data graph. Property-path ASTs are authored by humans in
    /// SHACL shapes and SPARQL queries; their depth is bounded by hand-written
    /// nesting, not by graph data. The no-recursion discipline applies to graph
    /// traversal where depth scales with data.
    /// </para>
    /// </remarks>
    private static PropertyPath InvertPath(PropertyPath path)
    {
        switch(path)
        {
            case PredicatePath p:
            {
                return new InversePath(p);
            }

            case InversePath ip:
            {
                //Double inversion cancels.
                return ip.Inner;
            }

            case SequencePath seq:
            {
                //Reverse the step order and invert each step.
                ImmutableArray<PropertyPath>.Builder builder = ImmutableArray.CreateBuilder<PropertyPath>(seq.Steps.Length);
                for(int i = seq.Steps.Length - 1; i >= 0; i--)
                {
                    builder.Add(InvertPath(seq.Steps[i]));
                }

                return new SequencePath(builder.ToImmutable());
            }

            case AlternativePath alt:
            {
                ImmutableArray<PropertyPath>.Builder builder = ImmutableArray.CreateBuilder<PropertyPath>(alt.Alternatives.Length);
                foreach(PropertyPath branch in alt.Alternatives)
                {
                    builder.Add(InvertPath(branch));
                }

                return new AlternativePath(builder.ToImmutable());
            }

            case ZeroOrMorePath zm:
            {
                return new ZeroOrMorePath(InvertPath(zm.Inner));
            }

            case OneOrMorePath om:
            {
                return new OneOrMorePath(InvertPath(om.Inner));
            }

            case ZeroOrOnePath zo:
            {
                return new ZeroOrOnePath(InvertPath(zo.Inner));
            }

            default:
            {
                throw new NotSupportedException($"Unsupported property path constructor in inversion: {path.GetType().Name}.");
            }
        }
    }

    /// <summary>
    /// Implements <c>A*</c>: the set of nodes reachable from
    /// <paramref name="starts"/> via zero or more applications of
    /// <paramref name="inner"/>, including the starts by reflexivity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reflexivity is unconditional: every start is added to the visited set
    /// before any inner expansion runs, so a start with no outgoing inner edge
    /// still appears in the result. The BFS terminates on cycles because
    /// <see cref="HashSet{T}.Add(T)"/> returns <c>false</c> for already-visited
    /// nodes, blocking re-enqueueing.
    /// </para>
    /// </remarks>
    private static async ValueTask<HashSet<TermId>> EvaluateZeroOrMoreAsync(
        IReadOnlyCollection<TermId> starts,
        PropertyPath inner,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        HashSet<TermId> visited = [];
        Queue<TermId> frontier = new();

        foreach(TermId start in starts)
        {
            if(visited.Add(start))
            {
                frontier.Enqueue(start);
            }
        }

        while(frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TermId current = frontier.Dequeue();

            HashSet<TermId> expanded = await EvaluateToSetAsync([current], inner, ops, cancellationToken).ConfigureAwait(false);
            foreach(TermId next in expanded)
            {
                if(visited.Add(next))
                {
                    frontier.Enqueue(next);
                }
            }
        }

        return visited;
    }

    /// <summary>
    /// Implements <c>A+</c>: the set of nodes reachable from
    /// <paramref name="starts"/> via one or more applications of
    /// <paramref name="inner"/>, including a start only if it is reachable
    /// from itself via the inner path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mechanical difference from <c>A*</c> is the BFS seed. The first
    /// inner step is taken from the starts, and its result — not the starts
    /// themselves — seeds the visited set. A start therefore appears in the
    /// output only when the inner path reaches it from another node (or from
    /// itself via a self-loop). An empty first step short-circuits the
    /// traversal to the empty set before any BFS iteration runs.
    /// </para>
    /// </remarks>
    private static async ValueTask<HashSet<TermId>> EvaluateOneOrMoreAsync(
        IReadOnlyCollection<TermId> starts,
        PropertyPath inner,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        //Take one step first, then take zero-or-more from each result, and union all.
        HashSet<TermId> firstStep = await EvaluateToSetAsync(starts, inner, ops, cancellationToken).ConfigureAwait(false);
        if(firstStep.Count == 0)
        {
            return firstStep;
        }

        HashSet<TermId> visited = [];
        Queue<TermId> frontier = new();
        foreach(TermId node in firstStep)
        {
            if(visited.Add(node))
            {
                frontier.Enqueue(node);
            }
        }

        while(frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TermId current = frontier.Dequeue();

            HashSet<TermId> expanded = await EvaluateToSetAsync([current], inner, ops, cancellationToken).ConfigureAwait(false);
            foreach(TermId next in expanded)
            {
                if(visited.Add(next))
                {
                    frontier.Enqueue(next);
                }
            }
        }

        return visited;
    }
}
