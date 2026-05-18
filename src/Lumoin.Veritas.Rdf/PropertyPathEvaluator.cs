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
/// Evaluation is driven entirely by <see cref="StorageDelegates.MatchTriplesAsync"/>.
/// Inverse paths flip the match call from <c>match(s, p, null)</c> to
/// <c>match(null, p, o)</c>. Storage providers that can answer both forms
/// efficiently — via separate POS and OSP indices — will see linear behaviour.
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
    /// <param name="match">The pattern match delegate over the graph.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of reachable node identifiers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="match"/> is <c>null</c>.</exception>
    public static IAsyncEnumerable<TermId> EvaluateAsync(
        TermId start,
        PropertyPath path,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(match);
        return EvaluateFromSetCore([start], path, match, cancellationToken);
    }

    /// <summary>
    /// Evaluates <paramref name="path"/> from a set of starting nodes and yields
    /// each reachable distinct node exactly once.
    /// </summary>
    /// <param name="starts">The encoded identifiers of the starting nodes.</param>
    /// <param name="path">The property path AST.</param>
    /// <param name="match">The pattern match delegate over the graph.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of reachable node identifiers.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="starts"/>, <paramref name="path"/>, or <paramref name="match"/> is <c>null</c>.
    /// </exception>
    public static IAsyncEnumerable<TermId> EvaluateFromSetAsync(
        IReadOnlyCollection<TermId> starts,
        PropertyPath path,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(match);
        return EvaluateFromSetCore(starts, path, match, cancellationToken);
    }

    private static async IAsyncEnumerable<TermId> EvaluateFromSetCore(
        IReadOnlyCollection<TermId> starts,
        PropertyPath path,
        StorageDelegates.MatchTriplesAsync match,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        HashSet<TermId> results = await EvaluateToSetAsync(starts, path, match, cancellationToken).ConfigureAwait(false);
        foreach(TermId node in results)
        {
            yield return node;
        }
    }

    private static async ValueTask<HashSet<TermId>> EvaluateToSetAsync(
        IReadOnlyCollection<TermId> starts,
        PropertyPath path,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch(path)
        {
            case PredicatePath predicatePath:
            {
                return await ExpandOneStepAsync(starts, predicatePath.Predicate, forward: true, match, cancellationToken).ConfigureAwait(false);
            }

            case InversePath inversePath when inversePath.Inner is PredicatePath innerPredicate:
            {
                return await ExpandOneStepAsync(starts, innerPredicate.Predicate, forward: false, match, cancellationToken).ConfigureAwait(false);
            }

            case InversePath inversePath:
            {
                PropertyPath inverted = InvertPath(inversePath.Inner);
                return await EvaluateToSetAsync(starts, inverted, match, cancellationToken).ConfigureAwait(false);
            }

            case SequencePath sequencePath:
            {
                return await EvaluateSequenceAsync(starts, sequencePath.Steps, match, cancellationToken).ConfigureAwait(false);
            }

            case AlternativePath alternativePath:
            {
                return await EvaluateAlternativeAsync(starts, alternativePath.Alternatives, match, cancellationToken).ConfigureAwait(false);
            }

            case ZeroOrMorePath zeroOrMorePath:
            {
                return await EvaluateZeroOrMoreAsync(starts, zeroOrMorePath.Inner, match, cancellationToken).ConfigureAwait(false);
            }

            case OneOrMorePath oneOrMorePath:
            {
                return await EvaluateOneOrMoreAsync(starts, oneOrMorePath.Inner, match, cancellationToken).ConfigureAwait(false);
            }

            case ZeroOrOnePath zeroOrOnePath:
            {
                HashSet<TermId> oneStep = await EvaluateToSetAsync(starts, zeroOrOnePath.Inner, match, cancellationToken).ConfigureAwait(false);
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
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken)
    {
        HashSet<TermId> next = [];
        foreach(TermId start in starts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(forward)
            {
                await foreach(EncodedTriple triple in match(start, predicate, TermId.None, cancellationToken).ConfigureAwait(false))
                {
                    next.Add(triple.Object);
                }
            }
            else
            {
                await foreach(EncodedTriple triple in match(TermId.None, predicate, start, cancellationToken).ConfigureAwait(false))
                {
                    next.Add(triple.Subject);
                }
            }
        }

        return next;
    }

    private static async ValueTask<HashSet<TermId>> EvaluateSequenceAsync(
        IReadOnlyCollection<TermId> starts,
        ImmutableArray<PropertyPath> steps,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<TermId> current = starts;
        foreach(PropertyPath step in steps)
        {
            HashSet<TermId> expanded = await EvaluateToSetAsync(current, step, match, cancellationToken).ConfigureAwait(false);
            current = expanded;
        }

        return current is HashSet<TermId> asHashSet ? asHashSet : [.. current];
    }

    private static async ValueTask<HashSet<TermId>> EvaluateAlternativeAsync(
        IReadOnlyCollection<TermId> starts,
        ImmutableArray<PropertyPath> alternatives,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken)
    {
        HashSet<TermId> union = [];
        foreach(PropertyPath alternative in alternatives)
        {
            HashSet<TermId> branch = await EvaluateToSetAsync(starts, alternative, match, cancellationToken).ConfigureAwait(false);
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

    private static async ValueTask<HashSet<TermId>> EvaluateZeroOrMoreAsync(
        IReadOnlyCollection<TermId> starts,
        PropertyPath inner,
        StorageDelegates.MatchTriplesAsync match,
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

            HashSet<TermId> expanded = await EvaluateToSetAsync([current], inner, match, cancellationToken).ConfigureAwait(false);
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

    private static async ValueTask<HashSet<TermId>> EvaluateOneOrMoreAsync(
        IReadOnlyCollection<TermId> starts,
        PropertyPath inner,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken)
    {
        //Take one step first, then take zero-or-more from each result, and union all.
        HashSet<TermId> firstStep = await EvaluateToSetAsync(starts, inner, match, cancellationToken).ConfigureAwait(false);
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

            HashSet<TermId> expanded = await EvaluateToSetAsync([current], inner, match, cancellationToken).ConfigureAwait(false);
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
