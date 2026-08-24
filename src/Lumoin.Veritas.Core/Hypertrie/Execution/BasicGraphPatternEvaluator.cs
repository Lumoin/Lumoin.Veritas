using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// Evaluates a <see cref="BasicGraphPattern"/> against a
/// <see cref="HypertrieSnapshot"/> using worst-case-optimal
/// joins. Combines per-pattern <see cref="TriejoinIterator"/>
/// instances with a <see cref="Planner"/> for variable
/// elimination order and
/// <see cref="LeapfrogIntersection"/> for the inner intersection
/// step.
/// </summary>
/// <remarks>
/// <para>
/// <b>Iterative state machine.</b> The evaluator does not
/// recurse. It maintains an explicit stack of "open frames" —
/// one per variable level that has been descended — and
/// dispatches on the planner's decision at each step.
/// Backtracking is handled by popping the topmost frame,
/// advancing the iterators that participated in it via
/// <see cref="TriejoinIterator.Next"/>, and either resuming
/// at the same level (if leapfrog finds another common key)
/// or unwinding further.
/// </para>
/// <para>
/// <b>Access control wiring.</b> When a solution is candidate
/// for yield, the evaluator consults the access-control policy
/// once per pattern's full triple. If every pattern's triple is
/// allowed, the solution is yielded. If any pattern's triple
/// returns <see cref="AccessDecision.Deny"/>, an
/// <see cref="QueryTraceEventKind.AccessDenied"/> event is
/// emitted (the audit channel) and the solution is skipped. If
/// any returns <see cref="AccessDecision.NotFound"/>, the
/// solution is skipped silently — the privacy guarantee. A
/// <c>null</c> access-control delegate is treated as Allow-all
/// with zero per-candidate cost.
/// </para>
/// <para>
/// <b>Pattern partitioning at construction.</b> Patterns with
/// no variables (fully bound) are handled by a fast pre-check
/// via <see cref="HypertrieOps.Match"/> — if any such pattern
/// produces zero matches the query yields nothing. Patterns
/// with variables become iterators. The driver only joins the
/// variable-bearing patterns; fully-bound patterns are
/// constraints, not joins.
/// </para>
/// <para>
/// <b>Per-iterator variable order.</b> Each iterator carries
/// its own variable order — a permutation of that pattern's
/// variables compatible with the global planner-supplied order.
/// "Compatible" means: walking the iterator's order yields the
/// pattern's variables in the same relative sequence as walking
/// the global order. This is computed once at construction by
/// projecting the global first-occurrence order onto each
/// pattern's variable set.
/// </para>
/// <para>
/// <b>Async surface.</b> The evaluator returns
/// <see cref="IAsyncEnumerable{Solution}"/> because the
/// access-control consultation can be asynchronous (remote
/// capability servers, revocation lists). The synchronous
/// pieces of the algorithm — planner, iterator operations,
/// leapfrog — execute synchronously inside the async iterator;
/// only the access-control consultation actually awaits.
/// </para>
/// <para>
/// <b>Self-joins.</b> Patterns with self-joins (the same
/// variable in multiple positions of one pattern) are rejected
/// at construction by the underlying iterator.
/// </para>
/// </remarks>
[DebuggerDisplay("BasicGraphPatternEvaluator Patterns={query.Patterns.Count}")]
public sealed class BasicGraphPatternEvaluator
{
    //Window size for the recent-denials list passed to the
    //planner. Bounded so the list does not grow unboundedly during
    //queries against permissive-but-noisy policies.
    private const int RecentDenialsWindow = 32;

    private readonly HypertrieSnapshot snapshot;

    private readonly BasicGraphPattern query;

    private readonly Planner planner;

    /// <summary>A-priori per-class upper bounds for the planner, or <c>null</c> when the caller supplied none. Stable for the snapshot's generation; handed to the planner unchanged on every consultation.</summary>
    private AprioriCardinalities? Cardinalities { get; }

    private readonly AccessControlDelegate? accessControl;

    private readonly AccessContext? accessContext;

    private readonly TraceHandler<QueryTraceEvent>? traceHandler;

    private readonly Guid correlationId;

    //Clock used to stamp Ticks on emitted trace events. Injected
    //so tests can pin time deterministically via FakeTimeProvider;
    //production callers pass TimeProvider.System at the
    //composition root.
    private readonly TimeProvider timeProvider;

    //Sequence-number counter for trace events emitted by this
    //evaluator. A field rather than a property because Interlocked
    //requires a ref parameter; reads happen only from the
    //emission helpers below.
    private long traceSequence;

    /// <summary>
    /// Constructs a new evaluator.
    /// </summary>
    /// <param name="snapshot">The snapshot to query.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="planner">The variable-elimination planner.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events. Pass <see cref="TimeProvider.System"/> in production; tests pinning trace timing pass a <c>FakeTimeProvider</c>.</param>
    /// <param name="cardinalities">A-priori per-class upper bounds handed to the planner on every consultation, or <c>null</c> when none are known.</param>
    /// <param name="accessControl">Optional access-control policy. <c>null</c> treats every candidate as allowed.</param>
    /// <param name="accessContext">Caller-supplied access context, threaded into every access-control consultation. Required when <paramref name="accessControl"/> is non-<c>null</c>; ignored when <c>null</c>.</param>
    /// <param name="traceHandler">Optional trace handler for query-execution events.</param>
    /// <param name="correlationId">Correlation id stamped on every emitted trace event.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">An access-control delegate is supplied without an access context.</exception>
    public BasicGraphPatternEvaluator(
        HypertrieSnapshot snapshot,
        BasicGraphPattern query,
        Planner planner,
        TimeProvider timeProvider,
        AprioriCardinalities? cardinalities = null,
        AccessControlDelegate? accessControl = null,
        AccessContext? accessContext = null,
        TraceHandler<QueryTraceEvent>? traceHandler = null,
        Guid correlationId = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if(accessControl is not null && accessContext is null)
        {
            throw new ArgumentException("An access context must be supplied when access control is configured.", nameof(accessContext));
        }

        this.snapshot = snapshot;
        this.query = query;
        this.planner = planner;
        Cardinalities = cardinalities;
        this.timeProvider = timeProvider;
        this.accessControl = accessControl;
        this.accessContext = accessContext;
        this.traceHandler = traceHandler;
        this.correlationId = correlationId;
    }

    /// <summary>
    /// Evaluates the query, yielding solutions one at a time.
    /// Each enumeration of the returned async sequence performs
    /// a fresh evaluation; the evaluator itself is reusable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token threaded into iterator operations and the access-control consultation.</param>
    /// <returns>An async sequence of solutions, in driver-determined order.</returns>
    public async IAsyncEnumerable<Solution> EvaluateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EmitQueryStarted(query.Patterns.Count);

        int yieldedCount = 0;

        //Pre-check fully-bound patterns. If any fails, no
        //solutions exist; bail immediately.
        if(!FullyBoundPatternsMatch())
        {
            EmitQueryCompleted(yieldedCount);

            yield break;
        }

        //A fully-bound pattern is a candidate triple in its own right: the policy must be able to hide a denied
        //triple's existence, or a membership query (ASK { s p o }) observes it past access control. The
        //variable-bearing patterns are consulted per solution below; the constant ones are consulted once here.
        if(accessControl is not null && !await FullyBoundPatternsAllowedAsync(cancellationToken).ConfigureAwait(false))
        {
            EmitQueryCompleted(yieldedCount);

            yield break;
        }

        //Identify the variable-bearing patterns and build per-pattern
        //iterators. If there are no variable-bearing patterns the
        //query has no joins; the empty solution is the answer
        //(provided the fully-bound check above passed).
        List<int> variablePatternIndices = [];

        for(int i = 0; i < query.Patterns.Count; i++)
        {
            if(query.Patterns[i].Variables().GetEnumerator().MoveNext())
            {
                variablePatternIndices.Add(i);
            }
        }

        if(variablePatternIndices.Count == 0)
        {
            yield return new Solution([]);
            yieldedCount++;

            EmitQueryCompleted(yieldedCount);

            yield break;
        }

        //Build iterators. Each iterator's variable order is the
        //projection of the BGP's first-occurrence variable order
        //onto that pattern's variable set.
        TriejoinIterator[] iterators = new TriejoinIterator[variablePatternIndices.Count];

        try
        {
            for(int i = 0; i < variablePatternIndices.Count; i++)
            {
                int patternIndex = variablePatternIndices[i];
                TriplePattern pattern = query.Patterns[patternIndex];
                Variable[] perIteratorOrder = ProjectGlobalOrderOntoPattern(query.Variables, pattern);

                iterators[i] = new TriejoinIterator(
                    snapshot,
                    pattern,
                    perIteratorOrder,
                    timeProvider,
                    patternIndex,
                    correlationId,
                    traceHandler,
                    cancellationToken);
            }

            //Drive the state machine.
            await foreach(Solution solution in DriveAsync(iterators, variablePatternIndices, cancellationToken).ConfigureAwait(false))
            {
                yieldedCount++;

                yield return solution;
            }
        }
        finally
        {
            for(int i = 0; i < iterators.Length; i++)
            {
                iterators[i]?.Dispose();
            }
        }

        EmitQueryCompleted(yieldedCount);
    }

    //The state-machine driver. Maintains the current binding list
    //and the per-level open-frames stack; consults the planner at
    //each step; dispatches on the decision.
    private async IAsyncEnumerable<Solution> DriveAsync(
        TriejoinIterator[] iterators,
        IReadOnlyList<int> variablePatternIndices,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<VariableBinding> bindings = [];
        List<TriejoinIterator[]> openFrames = [];
        List<EncodedTriple> recentDenials = [];

        //needsAdvance set after a yield or after a failed descent —
        //indicates the next loop iteration should advance the
        //topmost frame instead of consulting the planner fresh.
        bool needsAdvance = false;

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(needsAdvance)
            {
                bool advanced = TryAdvance(iterators, bindings, openFrames, cancellationToken);

                if(!advanced)
                {
                    //Fully unwound — no more solutions.
                    yield break;
                }

                needsAdvance = false;
            }

            PlannerContext context = BuildPlannerContext(iterators, bindings, recentDenials);
            PlannerDecision decision = planner(context, cancellationToken);

            EmitPlannerDecision(decision);

            switch(decision.Kind)
            {
                case PlannerDecisionKind.StopQuery:
                {
                    yield break;
                }
                case PlannerDecisionKind.SkipBranch:
                {
                    needsAdvance = true;

                    continue;
                }
                case PlannerDecisionKind.YieldSolution:
                {
                    bool allowed = await CheckAccessControlAsync(iterators, variablePatternIndices, bindings, recentDenials, cancellationToken).ConfigureAwait(false);

                    if(allowed)
                    {
                        VariableBinding[] snapshotBindings = bindings.ToArray();

                        EmitSolutionYielded();

                        yield return new Solution(snapshotBindings);
                    }

                    needsAdvance = true;

                    continue;
                }
                case PlannerDecisionKind.DescendVariable:
                {
                    Variable target = decision.AsDescendVariable();
                    bool descended = TryDescend(iterators, target, bindings, openFrames, cancellationToken);

                    if(!descended)
                    {
                        //No common key at this variable; treat as a skip.
                        needsAdvance = true;
                    }

                    continue;
                }
                default:
                {
                    throw new InvalidOperationException($"Unknown planner decision kind: {decision.Kind}.");
                }
            }
        }
    }

    //Pops the topmost open frame, advances its iterators past
    //their current keys, and tries to find the next common key at
    //the now-current level. Returns true when a new common key was
    //found and the frame was re-pushed; returns false when every
    //frame has been unwound (no more solutions).
    private static bool TryAdvance(
        TriejoinIterator[] iterators,
        List<VariableBinding> bindings,
        List<TriejoinIterator[]> openFrames,
        CancellationToken cancellationToken)
    {
        while(openFrames.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TriejoinIterator[] frame = openFrames[^1];

            //Up out of the level the frame represents and discard
            //its binding, then advance every participant past its
            //current key so the next leapfrog finds the next
            //candidate.
            for(int i = 0; i < frame.Length; i++)
            {
                frame[i].Up();
                frame[i].Next(cancellationToken);
            }

            bindings.RemoveAt(bindings.Count - 1);

            //Try to find the next common key at this level.
            if(LeapfrogIntersection.TryFindNextCommonKey(frame, out TermId commonKey, cancellationToken))
            {
                //Re-open every participant on the new common key
                //and push the frame back. The level was popped from
                //bindings above; we now push the new binding for
                //the variable that was at this level. Recover the
                //variable from the frame's first iterator (every
                //participant has the same CurrentVariable).
                Variable variable = frame[0].CurrentVariable;

                for(int i = 0; i < frame.Length; i++)
                {
                    bool opened = frame[i].Open(commonKey, cancellationToken);

                    if(!opened)
                    {
                        //Should be unreachable: leapfrog confirmed
                        //the key exists for every participant. If
                        //we get here something is structurally
                        //wrong with the iterators.
                        throw new InvalidOperationException("Leapfrog intersection reported a common key that an iterator could not open.");
                    }
                }

                bindings.Add(new VariableBinding(variable, commonKey));

                //Re-binding this level supersedes the binding under which independent iterators (those NOT in this
                //frame) enumerated their own deeper variables. Such an iterator may have unwound during this same
                //TryAdvance pass into one of two stale states: fully exhausted (AtEnd), or — when its own domain for
                //the just-finished join level outruns the joined keys (e.g. a `?s a :Set` iterator whose set lacks
                //the `?s :member` triples the co-iterator requires) — stranded NOT-AtEnd on a key that no longer
                //joins. Either way its deeper enumeration is invalid for the new binding, so the planner would skip
                //the branch (AtEnd case) or the next leapfrog would dead-end on the stranded key. Reset the current
                //level of every such iterator so it presents its first key again under the new binding.
                ReseedIndependentIterators(iterators, frame);

                return true;
            }

            //No more keys at this level — pop the frame and
            //continue unwinding.
            openFrames.RemoveAt(openFrames.Count - 1);
        }

        return false;
    }

    //Resets the current variable level of every non-participant iterator that still has a level left to enumerate, so
    //it re-presents its first key under the just-re-bound binding. Any such iterator is necessarily positioned at a
    //variable strictly later in the elimination order than the re-bound one: the driver binds in global order and the
    //bindings list is exactly that bound prefix, so an iterator's unbound current variable cannot precede the most
    //recently bound variable. Its deeper enumeration was therefore relative to the superseded binding and must
    //restart — whether the iterator exhausted (AtEnd) or merely stranded on a key that no longer joins. An iterator
    //with all variables already bound (no current level) is left untouched: its state is a fully-bound constraint, not
    //an unstarted enumeration. The participants of the re-bound frame were re-opened by the caller and must not be
    //reset. (Restarting an already-first-key iterator is idempotent, so the broad sweep is safe.)
    private static void ReseedIndependentIterators(TriejoinIterator[] iterators, TriejoinIterator[] reboundFrame)
    {
        for(int i = 0; i < iterators.Length; i++)
        {
            TriejoinIterator iterator = iterators[i];

            if(Array.IndexOf(reboundFrame, iterator) >= 0)
            {
                continue;
            }

            if(iterator.DescendedLevels < iterator.VariableOrder.Count)
            {
                iterator.RestartCurrentLevel();
            }
        }
    }

    //Attempts to descend by binding `target`. Identifies which
    //iterators participate (those whose CurrentVariable equals
    //target), runs leapfrog to find a common key, opens each
    //participant on that key, and pushes a frame. Returns true on
    //success; false when no common key exists at this variable
    //(in which case the iterators are not advanced).
    private static bool TryDescend(
        TriejoinIterator[] iterators,
        Variable target,
        List<VariableBinding> bindings,
        List<TriejoinIterator[]> openFrames,
        CancellationToken cancellationToken)
    {
        //Identify participants. Iterators whose entire variable
        //order does not contain `target` cannot participate at
        //this step, but if their CurrentVariable does not equal
        //target we also exclude them — typically because target
        //is bound earlier than this iterator's first variable, so
        //the iterator is unaffected by binding target.
        List<TriejoinIterator> participants = [];

        for(int i = 0; i < iterators.Length; i++)
        {
            TriejoinIterator iterator = iterators[i];

            if(iterator.AtEnd)
            {
                //An at-end iterator cannot participate; the planner
                //should have skipped this branch. Treat the
                //descent as failed.
                return false;
            }

            if(iterator.DescendedLevels < iterator.VariableOrder.Count && iterator.CurrentVariable == target)
            {
                participants.Add(iterator);
            }
        }

        if(participants.Count == 0)
        {
            //Target variable does not appear in any iterator's
            //current level — the planner picked a variable no
            //iterator is positioned to bind. This is a planner
            //error. Treat as failure rather than silently misjoin.
            return false;
        }

        if(!LeapfrogIntersection.TryFindNextCommonKey(participants, out TermId commonKey, cancellationToken))
        {
            return false;
        }

        TriejoinIterator[] frame = participants.ToArray();

        for(int i = 0; i < frame.Length; i++)
        {
            bool opened = frame[i].Open(commonKey, cancellationToken);

            if(!opened)
            {
                throw new InvalidOperationException("Leapfrog intersection reported a common key that an iterator could not open.");
            }
        }

        bindings.Add(new VariableBinding(target, commonKey));
        openFrames.Add(frame);

        return true;
    }

    //Verifies every fully-bound pattern resolves to at least one
    //matching triple in the snapshot. Returns false if any does
    //not — in which case the query yields no solutions.
    private bool FullyBoundPatternsMatch()
    {
        for(int i = 0; i < query.Patterns.Count; i++)
        {
            TriplePattern pattern = query.Patterns[i];

            //A pattern is fully bound when no position is a variable.
            if(pattern.Subject.IsVariable || pattern.Predicate.IsVariable || pattern.Object.IsVariable)
            {
                continue;
            }

            //Match returns the canonical triple if the pattern
            //resolves; we only need existence.
            HypertrieNode root = snapshot.Store.GetByHandle(snapshot.Root);
            IEnumerable<EncodedTriple> matches = HypertrieOps.Match(
                root,
                snapshot.Store,
                pattern.Subject.BoundTerm,
                pattern.Predicate.BoundTerm,
                pattern.Object.BoundTerm);

            using IEnumerator<EncodedTriple> enumerator = matches.GetEnumerator();

            if(!enumerator.MoveNext())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Consults the access-control policy on every fully-bound (constant) pattern, each of which is its own
    /// candidate triple. Returns <see langword="true"/> when all are allowed; <see langword="false"/> — hiding
    /// the match — when any returns <see cref="AccessDecision.Deny"/> (audited via the trace stream) or
    /// <see cref="AccessDecision.NotFound"/> (silent), so a membership query cannot observe a triple the policy
    /// forbids. Only called when a policy is configured; mirrors <see cref="CheckAccessControlAsync"/> for the
    /// variable-bearing patterns. The denial window is a join-planning aid and is not needed here, since a denied
    /// constant pattern yields the whole query empty.
    /// </summary>
    /// <param name="cancellationToken">Threaded into the access-control consultation.</param>
    /// <returns><see langword="true"/> when every fully-bound pattern is allowed.</returns>
    private async ValueTask<bool> FullyBoundPatternsAllowedAsync(CancellationToken cancellationToken)
    {
        for(int i = 0; i < query.Patterns.Count; i++)
        {
            TriplePattern pattern = query.Patterns[i];

            if(pattern.Subject.IsVariable || pattern.Predicate.IsVariable || pattern.Object.IsVariable)
            {
                continue;
            }

            EncodedTriple candidate = EncodedTriple.FromEncoded(pattern.Subject.BoundTerm.Encoded, pattern.Predicate.BoundTerm.Encoded, pattern.Object.BoundTerm.Encoded);
            AccessDecision decision = await accessControl!(new AccessRequest(candidate, accessContext!), cancellationToken).ConfigureAwait(false);

            //A denial is audited; NotFound is silent by design. Both hide the triple, failing the membership.
            if(decision is AccessDecision.Deny)
            {
                EmitAccessDenied(candidate);
            }

            if(!Visible(decision))
            {
                return false;
            }
        }

        return true;

        static bool Visible(AccessDecision decision) => decision switch
        {
            AccessDecision.Allow => true,
            AccessDecision.Deny or AccessDecision.NotFound => false,
            _ => throw new InvalidOperationException($"Unknown access decision: {decision}."),
        };
    }

    //Consults the access-control policy on each variable-bearing
    //pattern's full triple at solution-yield time. Returns true
    //when every pattern's triple is allowed; false (and emits an
    //AccessDenied trace event) when any returns Deny; false
    //silently when any returns NotFound.
    private async ValueTask<bool> CheckAccessControlAsync(
        TriejoinIterator[] iterators,
        IReadOnlyList<int> variablePatternIndices,
        IReadOnlyList<VariableBinding> bindings,
        List<EncodedTriple> recentDenials,
        CancellationToken cancellationToken)
    {
        if(accessControl is null)
        {
            return true;
        }

        for(int i = 0; i < iterators.Length; i++)
        {
            TriplePattern pattern = query.Patterns[variablePatternIndices[i]];
            EncodedTriple candidate = MaterialiseTriple(pattern, bindings, iterators[i]);
            AccessRequest request = new(candidate, accessContext!);
            AccessDecision decision = await accessControl(request, cancellationToken).ConfigureAwait(false);

            switch(decision)
            {
                case AccessDecision.Allow:
                {
                    continue;
                }
                case AccessDecision.Deny:
                {
                    EmitAccessDenied(candidate);
                    AddDenialToWindow(recentDenials, candidate);

                    return false;
                }
                case AccessDecision.NotFound:
                {
                    return false;
                }
                default:
                {
                    throw new InvalidOperationException($"Unknown access decision: {decision}.");
                }
            }
        }

        return true;
    }

    //Resolves a candidate triple by combining the pattern's bound
    //positions with the iterator's current variable bindings.
    private static EncodedTriple MaterialiseTriple(
        TriplePattern pattern,
        IReadOnlyList<VariableBinding> bindings,
        TriejoinIterator iterator)
    {
        uint subject = ResolvePosition(pattern.Subject, bindings, iterator);
        uint predicate = ResolvePosition(pattern.Predicate, bindings, iterator);
        uint obj = ResolvePosition(pattern.Object, bindings, iterator);

        return EncodedTriple.FromEncoded(subject, predicate, obj);
    }

    private static uint ResolvePosition(
        PatternPosition position,
        IReadOnlyList<VariableBinding> bindings,
        TriejoinIterator iterator)
    {
        if(position.IsBound)
        {
            return position.BoundTerm.Encoded;
        }

        Variable variable = position.Variable;

        //Try the global bindings list first.
        for(int i = 0; i < bindings.Count; i++)
        {
            VariableBinding binding = bindings[i];

            if(binding.Variable == variable)
            {
                return binding.Value.Encoded;
            }
        }

        //Fall back to the iterator's own bound values — covers
        //variables that exist only in this pattern and have been
        //bound here but did not yet make it into the global
        //bindings list.
        return iterator.ValueOf(variable).Encoded;
    }

    //Bounded sliding window — append, then trim from the head if
    //we've grown past the window size.
    private static void AddDenialToWindow(List<EncodedTriple> recentDenials, EncodedTriple denial)
    {
        recentDenials.Add(denial);

        if(recentDenials.Count > RecentDenialsWindow)
        {
            recentDenials.RemoveAt(0);
        }
    }

    //Builds an immutable PlannerContext for one consultation.
    //Iterator snapshots are constructed fresh per consultation.
    private PlannerContext BuildPlannerContext(
        TriejoinIterator[] iterators,
        IReadOnlyList<VariableBinding> bindings,
        IReadOnlyList<EncodedTriple> recentDenials)
    {
        IteratorSnapshot[] iteratorSnapshots = new IteratorSnapshot[iterators.Length];

        for(int i = 0; i < iterators.Length; i++)
        {
            TriejoinIterator iterator = iterators[i];
            Variable currentVariable = iterator.DescendedLevels < iterator.VariableOrder.Count
                ? iterator.CurrentVariable
                : default;
            TermId key = iterator.AtEnd ? TermId.None : iterator.Key;

            iteratorSnapshots[i] = new IteratorSnapshot(
                PatternIndex: iterator.PatternIndex,
                CurrentVariable: currentVariable,
                Key: key,
                AtEnd: iterator.AtEnd,
                DescendedLevels: iterator.DescendedLevels);
        }

        return new PlannerContext(query, bindings, iteratorSnapshots, recentDenials, Cardinalities);
    }

    //Projects the global first-occurrence variable order onto a
    //pattern's variable set. The result is the variables present
    //in the pattern, in the same relative order they appear in
    //the global list.
    private static Variable[] ProjectGlobalOrderOntoPattern(IReadOnlyList<Variable> globalOrder, TriplePattern pattern)
    {
        HashSet<Variable> patternVariables = [.. pattern.Variables()];
        List<Variable> projected = [];

        for(int i = 0; i < globalOrder.Count; i++)
        {
            Variable variable = globalOrder[i];

            if(patternVariables.Contains(variable))
            {
                projected.Add(variable);
            }
        }

        return [.. projected];
    }

    private void EmitQueryStarted(int patternCount)
    {
        if(traceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.QueryStarted(sequence, timeProvider.GetUtcNow().UtcTicks, correlationId, patternCount);

        traceHandler(in evt);
    }

    private void EmitQueryCompleted(int solutionCount)
    {
        if(traceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.QueryCompleted(sequence, timeProvider.GetUtcNow().UtcTicks, correlationId, solutionCount);

        traceHandler(in evt);
    }

    private void EmitPlannerDecision(PlannerDecision decision)
    {
        if(traceHandler is null)
        {
            return;
        }

        Variable variable = decision.Kind == PlannerDecisionKind.DescendVariable ? decision.Variable : default;
        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.PlannerDecision(sequence, timeProvider.GetUtcNow().UtcTicks, correlationId, variable);

        traceHandler(in evt);
    }

    private void EmitSolutionYielded()
    {
        if(traceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.SolutionYielded(sequence, timeProvider.GetUtcNow().UtcTicks, correlationId);

        traceHandler(in evt);
    }

    private void EmitAccessDenied(EncodedTriple denied)
    {
        if(traceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.AccessDenied(
            sequence,
            timeProvider.GetUtcNow().UtcTicks,
            correlationId,
            denied.Subject.Encoded,
            denied.Predicate.Encoded,
            denied.Object.Encoded);

        traceHandler(in evt);
    }
}
