using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;

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

        return path switch
        {
            PredicatePath predicatePath => await ExpandOneStepAsync(starts, predicatePath.Predicate, forward: true, ops, cancellationToken).ConfigureAwait(false),
            InversePath { Inner: PredicatePath innerPredicate } => await ExpandOneStepAsync(starts, innerPredicate.Predicate, forward: false, ops, cancellationToken).ConfigureAwait(false),
            InversePath inversePath => await EvaluateToSetAsync(starts, InvertPath(inversePath.Inner), ops, cancellationToken).ConfigureAwait(false),
            SequencePath sequencePath => await EvaluateSequenceAsync(starts, sequencePath.Steps, ops, cancellationToken).ConfigureAwait(false),
            AlternativePath alternativePath => await EvaluateAlternativeAsync(starts, alternativePath.Alternatives, ops, cancellationToken).ConfigureAwait(false),
            ZeroOrMorePath zeroOrMorePath => await EvaluateZeroOrMoreAsync(starts, zeroOrMorePath.Inner, ops, cancellationToken).ConfigureAwait(false),
            OneOrMorePath oneOrMorePath => await EvaluateOneOrMoreAsync(starts, oneOrMorePath.Inner, ops, cancellationToken).ConfigureAwait(false),
            ZeroOrOnePath zeroOrOnePath => await EvaluateZeroOrOneAsync(starts, zeroOrOnePath.Inner, ops, cancellationToken).ConfigureAwait(false),
            NegatedPropertySet negatedPropertySet => await EvaluateNegatedPropertySetAsync(starts, negatedPropertySet, ops, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Unsupported property path constructor: {path.GetType().Name}.")
        };
    }

    /// <summary>
    /// Implements <c>A?</c>: the start nodes plus every node one application of
    /// <paramref name="inner"/> away. The starts are always included by reflexivity,
    /// even when the inner step from them is empty.
    /// </summary>
    private static async ValueTask<HashSet<TermId>> EvaluateZeroOrOneAsync(
        IReadOnlyCollection<TermId> starts,
        PropertyPath inner,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        HashSet<TermId> oneStep = await EvaluateToSetAsync(starts, inner, ops, cancellationToken).ConfigureAwait(false);
        foreach(TermId start in starts)
        {
            oneStep.Add(start);
        }

        return oneStep;
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
    /// Implements a negated property set: from each start, the union of forward objects
    /// over predicates not in <see cref="NegatedPropertySet.Forward"/> and inverse subjects
    /// over predicates not in <see cref="NegatedPropertySet.Inverse"/>. There is no predicate
    /// to descend on, so each start's incident triples are scanned with a wildcard predicate
    /// and the excluded predicates are filtered out.
    /// </summary>
    private static async ValueTask<HashSet<TermId>> EvaluateNegatedPropertySetAsync(
        IReadOnlyCollection<TermId> starts,
        NegatedPropertySet negatedPropertySet,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        HashSet<TermId> excludedForward = [];
        foreach(IriId forward in negatedPropertySet.Forward)
        {
            excludedForward.Add(forward.Value);
        }

        HashSet<TermId> excludedInverse = [];
        foreach(IriId inverse in negatedPropertySet.Inverse)
        {
            excludedInverse.Add(inverse.Value);
        }

        //A negated set splits into a forward term NPS(fwd) and an inverse term inv(NPS(inv)); a side
        //with no listed predicates contributes no term at all (it does NOT mean "exclude nothing, match
        //all"), so each scan runs only when that direction appears in the set.
        bool scanForward = negatedPropertySet.Forward.Length > 0;
        bool scanInverse = negatedPropertySet.Inverse.Length > 0;

        HashSet<TermId> results = [];
        foreach(TermId start in starts)
        {
            if(start.IsNone)
            {
                continue;
            }

            if(scanForward)
            {
                await foreach(EncodedTriple triple in ops.MatchTriples(start, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
                {
                    if(!excludedForward.Contains(triple.Predicate))
                    {
                        results.Add(triple.Object);
                    }
                }
            }

            if(scanInverse)
            {
                await foreach(EncodedTriple triple in ops.MatchTriples(TermId.None, TermId.None, start, cancellationToken).ConfigureAwait(false))
                {
                    if(!excludedInverse.Contains(triple.Predicate))
                    {
                        results.Add(triple.Subject);
                    }
                }
            }
        }

        return results;
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
        return path switch
        {
            PredicatePath p => new InversePath(p),

            //Double inversion cancels.
            InversePath ip => ip.Inner,

            //Reverse the step order and invert each step.
            SequencePath seq => new SequencePath(InvertSteps(seq.Steps, reverse: true)),

            //An alternative's inverse is the alternative of the inverted branches (order is irrelevant).
            AlternativePath alt => new AlternativePath(InvertSteps(alt.Alternatives, reverse: false)),
            ZeroOrMorePath zm => new ZeroOrMorePath(InvertPath(zm.Inner)),
            OneOrMorePath om => new OneOrMorePath(InvertPath(om.Inner)),
            ZeroOrOnePath zo => new ZeroOrOnePath(InvertPath(zo.Inner)),

            //Inverting a negated set swaps its forward and inverse exclusions.
            NegatedPropertySet nps => new NegatedPropertySet(nps.Inverse, nps.Forward),
            _ => throw new NotSupportedException($"Unsupported property path constructor in inversion: {path.GetType().Name}.")
        };
    }

    /// <summary>Inverts each path in <paramref name="steps"/>, optionally reversing their order (for sequence inversion).</summary>
    /// <param name="steps">The paths to invert.</param>
    /// <param name="reverse">Whether to emit the inverted paths in reverse order.</param>
    /// <returns>The inverted paths.</returns>
    private static ImmutableArray<PropertyPath> InvertSteps(ImmutableArray<PropertyPath> steps, bool reverse)
    {
        ImmutableArray<PropertyPath>.Builder builder = ImmutableArray.CreateBuilder<PropertyPath>(steps.Length);
        for(int i = 0; i < steps.Length; i++)
        {
            builder.Add(InvertPath(steps[reverse ? steps.Length - 1 - i : i]));
        }

        return builder.ToImmutable();
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
    /// <para>
    /// The frontier is a pool-rented <see cref="FrontierBuffer"/> rather than
    /// a <see cref="HashSet{T}"/>; uniqueness on the frontier is guaranteed
    /// structurally because nodes are only appended when <c>visited.Add</c>
    /// returned true. Two buffers are held and swapped each iteration, so
    /// per-iteration allocation drops to zero past the seed and any growth
    /// events.
    /// </para>
    /// </remarks>
    private static async ValueTask<HashSet<TermId>> EvaluateZeroOrMoreAsync(
        IReadOnlyCollection<TermId> starts,
        PropertyPath inner,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        HashSet<TermId> visited = [];
        using FrontierBuffer currentFrontier = new(VeritasMemoryPool<TermId>.Shared, initialCapacity: 16);
        using FrontierBuffer nextFrontier = new(VeritasMemoryPool<TermId>.Shared, initialCapacity: 16);

        //Seed: every start enters visited and the frontier. Reflexivity
        //is unconditional for ZeroOrMore so a start with no outgoing
        //inner edge still appears in the result via visited.
        foreach(TermId start in starts)
        {
            if(visited.Add(start))
            {
                currentFrontier.Add(start);
            }
        }

        FrontierBuffer current = currentFrontier;
        FrontierBuffer next = nextFrontier;

        while(current.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            next.Reset();
            await ExpandFrontierAsync(
                current.AsMemory(), inner, visited, next, ops, cancellationToken).ConfigureAwait(false);

            (current, next) = (next, current);
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
        //First step from the starts seeds the visited set so the start
        //node is included only if reachable via a cycle. Empty first
        //step short-circuits the BFS.
        HashSet<TermId> firstStep = await EvaluateToSetAsync(starts, inner, ops, cancellationToken).ConfigureAwait(false);
        if(firstStep.Count == 0)
        {
            return firstStep;
        }

        HashSet<TermId> visited = new(firstStep);
        using FrontierBuffer currentFrontier = new(VeritasMemoryPool<TermId>.Shared, initialCapacity: Math.Max(firstStep.Count, 16));
        using FrontierBuffer nextFrontier = new(VeritasMemoryPool<TermId>.Shared, initialCapacity: 16);

        foreach(TermId node in firstStep)
        {
            currentFrontier.Add(node);
        }

        FrontierBuffer current = currentFrontier;
        FrontierBuffer next = nextFrontier;

        while(current.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            next.Reset();
            await ExpandFrontierAsync(
                current.AsMemory(), inner, visited, next, ops, cancellationToken).ConfigureAwait(false);

            (current, next) = (next, current);
        }

        return visited;
    }

    /// <summary>
    /// Advances the whole BFS frontier by one inner-path step, appending
    /// newly-discovered nodes to <paramref name="next"/>. Dispatches to the
    /// batched storage primitives for predicate-leaf and leaf-alternation
    /// inners; falls back to a per-element loop calling
    /// <see cref="EvaluateToSetAsync"/> for arbitrary nested inners.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The visited-set update happens inline as the storage stream yields
    /// triples, so a node already in <paramref name="visited"/> is never
    /// appended to <paramref name="next"/>. This is the correctness invariant
    /// for cycle-safe BFS: stale rediscoveries do not re-enqueue.
    /// </para>
    /// </remarks>
    private static async ValueTask ExpandFrontierAsync(
        ReadOnlyMemory<TermId> frontier,
        PropertyPath inner,
        HashSet<TermId> visited,
        FrontierBuffer next,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        switch(inner)
        {
            case PredicatePath predicateLeaf:
            {
                await ExpandFrontierByPredicateAsync(
                    frontier, predicateLeaf.Predicate, forward: true, visited, next, ops, cancellationToken).ConfigureAwait(false);
                break;
            }

            case InversePath { Inner: PredicatePath inverseLeaf }:
            {
                await ExpandFrontierByPredicateAsync(
                    frontier, inverseLeaf.Predicate, forward: false, visited, next, ops, cancellationToken).ConfigureAwait(false);
                break;
            }

            case AlternativePath alternative when AllBranchesAreLeaves(alternative):
            {
                await ExpandFrontierByLeafAlternationAsync(
                    frontier, alternative, visited, next, ops, cancellationToken).ConfigureAwait(false);
                break;
            }

            default:
            {
                await ExpandFrontierByFallbackAsync(
                    frontier, inner, visited, next, ops, cancellationToken).ConfigureAwait(false);
                break;
            }
        }
    }

    private static bool AllBranchesAreLeaves(AlternativePath alternative)
    {
        foreach(PropertyPath branch in alternative.Alternatives)
        {
            if(branch is PredicatePath)
            {
                continue;
            }

            if(branch is InversePath { Inner: PredicatePath })
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Predicate-leaf fast path: one batched storage call advances the
    /// whole frontier in a single predicate-rooted descent.
    /// <paramref name="forward"/> distinguishes <c>:p</c> from <c>^:p</c>.
    /// The frontier memory is passed straight through to the storage
    /// primitive, with no per-call buffer materialisation.
    /// </summary>
    private static async ValueTask ExpandFrontierByPredicateAsync(
        ReadOnlyMemory<TermId> frontier,
        IriId predicate,
        bool forward,
        HashSet<TermId> visited,
        FrontierBuffer next,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        if(frontier.IsEmpty)
        {
            return;
        }

        if(forward)
        {
            await foreach(EncodedTriple triple in ops.MatchTriplesBySubjects(
                frontier, predicate.Value, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                if(visited.Add(triple.Object))
                {
                    next.Add(triple.Object);
                }
            }
        }
        else
        {
            await foreach(EncodedTriple triple in ops.MatchTriplesByObjects(
                TermId.None, predicate.Value, frontier, cancellationToken).ConfigureAwait(false))
            {
                if(visited.Add(triple.Subject))
                {
                    next.Add(triple.Subject);
                }
            }
        }
    }

    /// <summary>
    /// Leaf-alternation fast path: each branch issues one batched call
    /// against the same shared frontier memory. The visited-set update
    /// is shared across branches so later branches see updates from
    /// earlier ones; the per-branch unions append to the same
    /// <paramref name="next"/> buffer.
    /// </summary>
    private static async ValueTask ExpandFrontierByLeafAlternationAsync(
        ReadOnlyMemory<TermId> frontier,
        AlternativePath alternation,
        HashSet<TermId> visited,
        FrontierBuffer next,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        if(frontier.IsEmpty)
        {
            return;
        }

        foreach(PropertyPath branch in alternation.Alternatives)
        {
            switch(branch)
            {
                case PredicatePath predicateLeaf:
                {
                    await foreach(EncodedTriple triple in ops.MatchTriplesBySubjects(
                        frontier, predicateLeaf.Predicate.Value, TermId.None, cancellationToken).ConfigureAwait(false))
                    {
                        if(visited.Add(triple.Object))
                        {
                            next.Add(triple.Object);
                        }
                    }

                    break;
                }

                case InversePath { Inner: PredicatePath inverseLeaf }:
                {
                    await foreach(EncodedTriple triple in ops.MatchTriplesByObjects(
                        TermId.None, inverseLeaf.Predicate.Value, frontier, cancellationToken).ConfigureAwait(false))
                    {
                        if(visited.Add(triple.Subject))
                        {
                            next.Add(triple.Subject);
                        }
                    }

                    break;
                }

                default:
                {
                    //Dispatcher only routes here when AllBranchesAreLeaves
                    //returned true, so reaching this arm means an invariant
                    //has slipped.
                    throw new InvalidOperationException(
                        "Alternation branch in fast path is not a predicate or inverse-predicate leaf.");
                }
            }
        }
    }

    /// <summary>
    /// Fallback expansion for arbitrary inner paths (sequence, nested
    /// Kleene, mixed-shape alternations). Iterates the frontier per
    /// element and dispatches each to <see cref="EvaluateToSetAsync"/>;
    /// the per-element shape preserves correctness on the long tail at
    /// the cost of not capturing the batched-frontier perf win.
    /// </summary>
    private static async ValueTask ExpandFrontierByFallbackAsync(
        ReadOnlyMemory<TermId> frontier,
        PropertyPath inner,
        HashSet<TermId> visited,
        FrontierBuffer next,
        GraphMatchOps ops,
        CancellationToken cancellationToken)
    {
        for(int i = 0; i < frontier.Length; i++)
        {
            TermId current = frontier.Span[i];

            HashSet<TermId> step = await EvaluateToSetAsync(
                [current], inner, ops, cancellationToken).ConfigureAwait(false);

            foreach(TermId discovered in step)
            {
                if(visited.Add(discovered))
                {
                    next.Add(discovered);
                }
            }
        }
    }

    /// <summary>
    /// Pool-rented append-only buffer used as the BFS frontier in the
    /// Kleene helpers. Replaces the prior <see cref="HashSet{T}"/> frontier:
    /// uniqueness on the frontier is guaranteed structurally because nodes
    /// are only appended when the caller's separate visited-set
    /// <c>Add</c> returned true.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Holds an <see cref="IMemoryOwner{T}"/> rental from the supplied
    /// <see cref="VeritasMemoryPool{T}"/>. On overflow the buffer
    /// doubles via a fresh rental; the prior owner is disposed so the
    /// pool can reuse its slab.
    /// </para>
    /// <para>
    /// Not thread-safe. One instance is owned by one BFS evaluation and
    /// disposed when the evaluation exits.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("FrontierBuffer Count={Count} Capacity={Owner.Memory.Length}")]
    private sealed class FrontierBuffer: IDisposable
    {
        /// <summary>The pool the buffer rents its backing memory from.</summary>
        private VeritasMemoryPool<TermId> Pool { get; }

        /// <summary>The current rental backing the buffer; replaced on grow events.</summary>
        private IMemoryOwner<TermId> Owner { get; set; }

        /// <summary>Number of valid entries currently held; the live span is <c>Owner.Memory[..Count]</c>.</summary>
        public int Count { get; private set; }

        /// <summary>
        /// Rents an initial buffer sized to at least
        /// <paramref name="initialCapacity"/>. The pool's exact-size
        /// rental semantics mean the rental's memory length equals the
        /// requested capacity rounded up to the pool's nearest tier.
        /// </summary>
        /// <param name="pool">The pool to rent from. Pass <see cref="VeritasMemoryPool{T}.Shared"/> in production.</param>
        /// <param name="initialCapacity">Initial capacity hint. Must be positive.</param>
        /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="initialCapacity"/> is non-positive.</exception>
        public FrontierBuffer(VeritasMemoryPool<TermId> pool, int initialCapacity)
        {
            ArgumentNullException.ThrowIfNull(pool);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

            Pool = pool;
            Owner = pool.Rent(initialCapacity);
        }

        /// <summary>
        /// Appends <paramref name="item"/> to the buffer; grows the
        /// backing rental by doubling on overflow.
        /// </summary>
        public void Add(TermId item)
        {
            if(Count == Owner.Memory.Length)
            {
                //Double-and-copy growth. The old rental is disposed
                //after the copy; the pool reuses its slab as
                //appropriate.
                IMemoryOwner<TermId> larger = Pool.Rent(Owner.Memory.Length * 2);
                Owner.Memory.Span.CopyTo(larger.Memory.Span);
                Owner.Dispose();
                Owner = larger;
            }

            Owner.Memory.Span[Count++] = item;
        }

        /// <summary>
        /// Returns the live span of the buffer as a
        /// <see cref="ReadOnlyMemory{T}"/> of length <see cref="Count"/>.
        /// </summary>
        public ReadOnlyMemory<TermId> AsMemory()
        {
            return Owner.Memory[..Count];
        }

        /// <summary>
        /// Clears the buffer's logical contents without releasing the
        /// rental. Used between BFS iterations to recycle the buffer
        /// in place.
        /// </summary>
        public void Reset()
        {
            Count = 0;
        }

        /// <summary>Releases the current rental back to the pool.</summary>
        public void Dispose()
        {
            Owner.Dispose();
        }
    }
}
